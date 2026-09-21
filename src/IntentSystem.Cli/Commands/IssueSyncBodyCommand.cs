using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G825 (#1777): <c>intent-cli issue sync-body &lt;execution-unit&gt; --repo
/// &lt;owner/repo&gt; [--expected-remote-sha256 &lt;hex&gt;] [--dry-run|--write]</c>.
/// Updates the body of a child issue that has already been published, from the
/// packet's <c>github-body.md</c>, after the body passes validation and the
/// unit's issue is bound to <c>--repo</c>.
///
/// GitHub offers no compare-and-swap on an issue body. The write re-reads the
/// remote body, compares, replaces, and reads back. It cannot exclude a
/// concurrent edit between the read and the write, and every result says so.
/// No label, claim, queue-state, publish.yaml, packet file, or issue title is
/// changed.
/// </summary>
internal static class IssueSyncBodyCommand
{
    internal const string Usage =
        "Usage: intent-cli issue sync-body <execution-unit> --repo <owner/repo> [--expected-remote-sha256 <hex>] [--dry-run|--write] [--format markdown|json]";

    internal const string ConcurrencyGuarantee = "read-compare-write-verified; not atomic";

    internal const string OutcomeRefused = "refused";
    internal const string OutcomeDiffers = "differs";
    internal const string OutcomeNoOp = "no-op";
    internal const string OutcomeApplied = "applied";
    internal const string OutcomeConcurrentEdit = "concurrent-edit";
    internal const string OutcomeVerificationFailed = "verification-failed";

    internal const string EventSynced = "issue-body-synced";
    internal const string EventNoOp = "issue-body-sync-noop";
    internal const string EventUnverified = "issue-body-sync-unverified";

    public static Func<IGitHubIssueBodyClient> BodyClientFactory { get; set; } = () => new GhCliGitHubIssueBodyClient();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine("issue sync-body");
            writer.WriteLine(Usage);
            writer.WriteLine($"Updates an already-published child issue body from the packet's github-body.md. Dry-run by default. Guarantee: {ConcurrencyGuarantee} — a concurrent edit between the read and the write cannot be excluded.");
            return 0;
        }

        if (!TryParse(args, out var unit, out var repo, out var expected, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        var result = new IssueSyncBodyResult
        {
            ExecutionUnit = unit!,
            Repo = repo!,
            Mode = write ? "write" : "dry-run",
            ExpectedRemoteSha256 = expected,
            ConcurrencyGuarantee = ConcurrencyGuarantee,
        };

        var artifactRelative = IssuePublishArtifactPathResolver.Resolve(unit!);
        var artifactPath = Path.Combine(context.RepoRoot, artifactRelative);
        var packetDirectory = Path.GetDirectoryName(artifactPath)!;
        var bodyPath = Path.Combine(packetDirectory, "github-body.md");

        // Every local gate precedes the first GitHub call.
        if (!Directory.Exists(packetDirectory))
        {
            return Emit(writer, format, Refuse(result, "packet-missing", $"no packet directory at '{packetDirectory}'."));
        }

        if (!File.Exists(bodyPath))
        {
            return Emit(writer, format, Refuse(result, "body-missing", $"no github-body.md at '{bodyPath}'."));
        }

        var localBytes = File.ReadAllBytes(bodyPath);
        var localSizeBand = IssueBodySizeLimits.GetBand(localBytes.Length);
        var localWarnings = localSizeBand == IssueBodySizeBand.Warning
            ? new[] { "issue-body-size-warning" }
            : Array.Empty<string>();
        result = result with { Warnings = localWarnings };
        if (localSizeBand == IssueBodySizeBand.OverLimit)
        {
            var oversizedLocalSha = IssuePrepareCommand.ComputeSha256Hex(localBytes);
            return Emit(writer, format, Refuse(
                result with
                {
                    LocalSha256 = oversizedLocalSha,
                    LocalBytes = localBytes.Length,
                },
                "body-too-large",
                $"github-body.md is {localBytes.Length} bytes, which exceeds the {IssueBodySizeLimits.HardLimitBytes}-byte limit."));
        }

        var localBody = Encoding.UTF8.GetString(localBytes);
        var validation = IssueValidateBodyValidator.Validate(bodyPath, localBody, requireTargetPathsDeclaration: true);
        if (!validation.IsValid)
        {
            var detail = validation.MissingHeadings.Count > 0
                ? $"missing headings: {string.Join(", ", validation.MissingHeadings)}"
                : "body does not pass issue validate-body";
            return Emit(writer, format, Refuse(result, "body-invalid", $"github-body.md fails issue validate-body ({detail})."));
        }

        if (!File.Exists(artifactPath))
        {
            return Emit(writer, format, Refuse(result, "not-published", $"no publish.yaml at '{artifactRelative}'; the unit has not been published."));
        }

        IssuePublishArtifact artifact;
        try
        {
            artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
        }
        catch (InvalidOperationException exception)
        {
            return Emit(writer, format, Refuse(result, "publish-artifact-unreadable", $"publish.yaml could not be read: {exception.Message}"));
        }

        if (artifact.CreatedIssueNumber is not { } issueNumber)
        {
            return Emit(writer, format, Refuse(result, "not-published", "publish.yaml has no created_issue_number; the unit has not been published."));
        }

        result = result with { IssueNumber = issueNumber, IssueUrl = artifact.CreatedIssueUrl };

        if (!AutomationPublishLifecycleRepairCommand.TryBindIssueRepository(artifact, out var boundRepository))
        {
            return Emit(writer, format, Refuse(result, "issue-repository-unproven", $"created_issue_url '{artifact.CreatedIssueUrl}' does not identify a github.com issue #{issueNumber}."));
        }

        if (!AutomationPublishLifecycleRepairCommand.RepositoryEquals(boundRepository!, repo!))
        {
            return Emit(writer, format, Refuse(result with { BoundRepository = boundRepository }, "foreign-repository", $"the unit's issue belongs to '{boundRepository}', not --repo '{repo}'."));
        }

        var client = BodyClientFactory();
        var localSha = IssuePrepareCommand.ComputeSha256Hex(localBytes);
        result = result with { LocalSha256 = localSha, LocalBytes = localBytes.Length };

        string remoteBody;
        try
        {
            remoteBody = client.ReadBody(repo!, issueNumber);
        }
        catch (Exception exception) when (IsAdapterFailure(exception))
        {
            return Emit(writer, format, Refuse(result, "remote-read-failed", $"could not read issue #{issueNumber} body: {exception.Message}"));
        }

        var remoteBytes = Encoding.UTF8.GetBytes(remoteBody);
        var remoteSha = IssuePrepareCommand.ComputeSha256Hex(remoteBytes);
        var (added, removed) = CountLineChanges(remoteBody, localBody);
        result = result with
        {
            RemoteSha256 = remoteSha,
            RemoteBytes = remoteBytes.Length,
            LinesAdded = added,
            LinesRemoved = removed,
        };

        var equalModuloTrailingNewline = EqualsModuloTrailingNewline(remoteBody, localBody);

        if (!write)
        {
            var dryOutcome = equalModuloTrailingNewline ? OutcomeNoOp : OutcomeDiffers;
            return Emit(writer, format, result with
            {
                Outcome = dryOutcome,
                TrailingNewlineNormalized = equalModuloTrailingNewline && !string.Equals(remoteSha, localSha, StringComparison.Ordinal),
                Summary = dryOutcome == OutcomeNoOp
                    ? $"dry-run: issue #{issueNumber} body already matches github-body.md; nothing to write."
                    : $"dry-run: issue #{issueNumber} body differs (+{added} / -{removed} lines). Re-run with --write --expected-remote-sha256 {remoteSha} to replace it.",
            });
        }

        if (expected is not null && !string.Equals(expected, remoteSha, StringComparison.Ordinal))
        {
            return Emit(writer, format, result with
            {
                Outcome = OutcomeConcurrentEdit,
                ReasonCode = OutcomeConcurrentEdit,
                Summary = $"refused: issue #{issueNumber} body is now {remoteSha}, not the expected {expected}; it changed after it was reviewed. Nothing was written.",
            }, exitCode: 1);
        }

        var runLogPath = context.GetRunLogPath();

        if (equalModuloTrailingNewline)
        {
            AppendRunEvent(runLogPath, BuildEvent(unit!, EventNoOp, repo!, artifact.CreatedIssueUrl, localSha, remoteSha, remoteSha));
            return Emit(writer, format, result with
            {
                Outcome = OutcomeNoOp,
                TrailingNewlineNormalized = !string.Equals(remoteSha, localSha, StringComparison.Ordinal),
                RunsEvent = EventNoOp,
                Summary = $"no-op: issue #{issueNumber} body already matches github-body.md; no GitHub write was made.",
            });
        }

        // Upload the exact bytes that passed validation, not the packet file,
        // so an edit to github-body.md after the gate cannot reach GitHub.
        var uploadPath = Path.Combine(Path.GetTempPath(), $"intent-cli-sync-body-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllBytes(uploadPath, localBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteUpload(uploadPath);
            return Emit(writer, format, Refuse(result, "upload-staging-failed", string.Empty) with
            {
                Summary = $"refused (upload-staging-failed): could not stage the validated body for upload ({exception.Message}). The issue body was read, but no update was sent.",
            });
        }

        try
        {
            client.UpdateBody(repo!, issueNumber, uploadPath);
        }
        catch (Exception exception) when (IsAdapterFailure(exception))
        {
            // The update request may or may not have reached GitHub.
            return Emit(writer, format, Unverified(result, runLogPath, unit!, repo!, artifact.CreatedIssueUrl, localSha, remoteSha, afterSha: null,
                $"the body update reported an error ({exception.Message}); the body may or may not have changed."));
        }
        finally
        {
            TryDeleteUpload(uploadPath);
        }

        string afterBody;
        try
        {
            afterBody = client.ReadBody(repo!, issueNumber);
        }
        catch (Exception exception) when (IsAdapterFailure(exception))
        {
            return Emit(writer, format, Unverified(result, runLogPath, unit!, repo!, artifact.CreatedIssueUrl, localSha, remoteSha, afterSha: null,
                $"the body was sent but could not be read back ({exception.Message})."));
        }

        var afterSha = IssuePrepareCommand.ComputeSha256Hex(Encoding.UTF8.GetBytes(afterBody));
        if (!EqualsModuloTrailingNewline(afterBody, localBody))
        {
            return Emit(writer, format, Unverified(result, runLogPath, unit!, repo!, artifact.CreatedIssueUrl, localSha, remoteSha, afterSha,
                $"read-back {afterSha} does not match github-body.md {localSha}; the issue may have been edited concurrently."));
        }

        AppendRunEvent(runLogPath, BuildEvent(unit!, EventSynced, repo!, artifact.CreatedIssueUrl, localSha, remoteSha, afterSha));
        return Emit(writer, format, result with
        {
            Outcome = OutcomeApplied,
            RemoteAfterSha256 = afterSha,
            TrailingNewlineNormalized = !string.Equals(afterSha, localSha, StringComparison.Ordinal),
            RunsEvent = EventSynced,
            Summary = $"applied: issue #{issueNumber} body replaced ({remoteSha} -> {afterSha}) and verified by read-back.",
        });
    }

    private static void TryDeleteUpload(string uploadPath)
    {
        try
        {
            File.Delete(uploadPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp copy of an already-validated body is harmless and
            // must not turn a sent update into an unreported crash.
        }
    }

    /// <summary>
    /// Failures of the <c>gh</c> adapter: a nonzero exit, an unreadable or
    /// unexpected JSON reply, or a missing executable. After an update was
    /// sent, each of these must still end as <c>verification-failed</c>.
    /// </summary>
    private static bool IsAdapterFailure(Exception exception) =>
        exception is InvalidOperationException
            or IOException
            or JsonException
            or KeyNotFoundException
            or System.ComponentModel.Win32Exception;

    private static IssueSyncBodyResult Unverified(
        IssueSyncBodyResult result,
        string runLogPath,
        string unit,
        string repo,
        string? issueUrl,
        string localSha,
        string remoteSha,
        string? afterSha,
        string detail)
    {
        AppendRunEvent(runLogPath, BuildEvent(unit, EventUnverified, repo, issueUrl, localSha, remoteSha, afterSha));
        return result with
        {
            Outcome = OutcomeVerificationFailed,
            ReasonCode = OutcomeVerificationFailed,
            RemoteAfterSha256 = afterSha,
            MayHaveApplied = true,
            RecoveryCommand = $"gh issue view {result.IssueNumber} --repo {repo} --json body",
            RunsEvent = EventUnverified,
            ExitCode = 1,
            Summary = $"verification-failed: {detail} Read the issue before retrying; this command does not retry.",
        };
    }

    private static IssueSyncBodyResult Refuse(IssueSyncBodyResult result, string reasonCode, string detail) =>
        result with
        {
            Outcome = OutcomeRefused,
            ReasonCode = reasonCode,
            ExitCode = 1,
            Summary = $"refused ({reasonCode}): {detail} No GitHub call was made.",
        };

    private static RunEvent BuildEvent(string unit, string @event, string repo, string? issueUrl, string localSha, string remoteBeforeSha, string? remoteAfterSha) =>
        new()
        {
            Ts = TimestampFactory().ToUniversalTime(),
            ExecutionUnit = unit,
            Event = @event,
            By = "intent-cli issue sync-body",
            Repo = repo,
            LinkedIssue = issueUrl,
            Reason = $"local_sha256={localSha}; remote_before_sha256={remoteBeforeSha}; remote_after_sha256={remoteAfterSha ?? "unknown"}; guarantee={ConcurrencyGuarantee}",
        };

    private static void AppendRunEvent(string runLogPath, RunEvent runEvent)
    {
        var directory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(directory);
        File.AppendAllText(runLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    internal static bool EqualsModuloTrailingNewline(string left, string right) =>
        string.Equals(left.TrimEnd('\r', '\n'), right.TrimEnd('\r', '\n'), StringComparison.Ordinal);

    private static (int Added, int Removed) CountLineChanges(string before, string after)
    {
        static Dictionary<string, int> Count(string text)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n'))
            {
                counts[line] = counts.TryGetValue(line, out var n) ? n + 1 : 1;
            }
            return counts;
        }

        var b = Count(before);
        var a = Count(after);
        var added = a.Sum(kv => Math.Max(0, kv.Value - (b.TryGetValue(kv.Key, out var n) ? n : 0)));
        var removed = b.Sum(kv => Math.Max(0, kv.Value - (a.TryGetValue(kv.Key, out var n) ? n : 0)));
        return (added, removed);
    }

    private static int Emit(TextWriter writer, string format, IssueSyncBodyResult result, int? exitCode = null)
    {
        var code = exitCode ?? result.ExitCode;
        if (string.Equals(format, "json", StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine($"# issue sync-body — `{result.ExecutionUnit}` → `{result.Repo}` ({result.Mode})");
            writer.WriteLine();
            writer.WriteLine($"- outcome: **{result.Outcome}**{(result.ReasonCode is null ? string.Empty : $" ({result.ReasonCode})")}");
            if (result.IssueNumber is not null)
            {
                writer.WriteLine($"- issue: #{result.IssueNumber} {result.IssueUrl}");
            }
            if (result.LocalSha256 is not null)
            {
                writer.WriteLine($"- local github-body.md: {result.LocalSha256} ({result.LocalBytes} bytes)");
            }
            if (result.RemoteSha256 is not null)
            {
                writer.WriteLine($"- remote body before: {result.RemoteSha256} ({result.RemoteBytes} bytes); +{result.LinesAdded} / -{result.LinesRemoved} lines");
            }
            if (result.RemoteAfterSha256 is not null)
            {
                writer.WriteLine($"- remote body after: {result.RemoteAfterSha256}");
            }
            if (result.TrailingNewlineNormalized)
            {
                writer.WriteLine("- GitHub's copy differs from github-body.md only by a trailing newline; treated as matching.");
            }
            if (result.RecoveryCommand is not null)
            {
                writer.WriteLine($"- read before retrying: `{result.RecoveryCommand}`");
            }
            writer.WriteLine($"- guarantee: {result.ConcurrencyGuarantee} — a concurrent edit between the read and the write cannot be excluded.");
            writer.WriteLine("- warnings:");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"  - {warning}");
            }
            writer.WriteLine();
            writer.WriteLine(result.Summary);
        }
        return code;
    }

    private static bool TryParse(
        string[] args,
        out string? unit,
        out string? repo,
        out string? expected,
        out bool write,
        out string format,
        out string? error)
    {
        unit = null;
        repo = null;
        expected = null;
        write = false;
        format = "markdown";
        error = null;
        var sawDryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--expected-remote-sha256":
                    if (index + 1 >= args.Length || !Sha256Hex.IsMatch(args[index + 1].Trim().ToLowerInvariant()))
                    {
                        error = "--expected-remote-sha256 requires a 64-character hex SHA-256.";
                        return false;
                    }
                    expected = args[++index].Trim().ToLowerInvariant();
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    sawDryRun = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length
                        || (args[index + 1] != "markdown" && args[index + 1] != "json"))
                    {
                        error = "--format must be markdown or json.";
                        return false;
                    }
                    format = args[++index];
                    break;
                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal) || unit is not null)
                    {
                        error = $"Unknown argument '{argument}'.";
                        return false;
                    }
                    unit = argument.Trim();
                    break;
            }
        }

        if (write && sawDryRun)
        {
            error = "--write and --dry-run are mutually exclusive.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            error = "issue sync-body requires an <execution-unit>.";
            return false;
        }

        if (unit.Contains('/') || unit.Contains('\\') || unit.Contains(".."))
        {
            error = "<execution-unit> must be a plain unit id.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "issue sync-body requires --repo <owner/repo>.";
            return false;
        }

        return true;
    }
}

internal sealed record IssueSyncBodyResult
{
    [JsonPropertyName("operation")] public string Operation { get; init; } = "issue sync-body";
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("mode")] public required string Mode { get; init; }
    [JsonPropertyName("outcome")] public string Outcome { get; init; } = IssueSyncBodyCommand.OutcomeRefused;
    [JsonPropertyName("reason_code")] public string? ReasonCode { get; init; }
    [JsonPropertyName("issue_number")] public int? IssueNumber { get; init; }
    [JsonPropertyName("issue_url")] public string? IssueUrl { get; init; }
    [JsonPropertyName("bound_repository")] public string? BoundRepository { get; init; }
    [JsonPropertyName("local_sha256")] public string? LocalSha256 { get; init; }
    [JsonPropertyName("local_bytes")] public int? LocalBytes { get; init; }
    [JsonPropertyName("remote_sha256")] public string? RemoteSha256 { get; init; }
    [JsonPropertyName("remote_bytes")] public int? RemoteBytes { get; init; }
    [JsonPropertyName("remote_after_sha256")] public string? RemoteAfterSha256 { get; init; }
    [JsonPropertyName("expected_remote_sha256")] public string? ExpectedRemoteSha256 { get; init; }
    [JsonPropertyName("lines_added")] public int? LinesAdded { get; init; }
    [JsonPropertyName("lines_removed")] public int? LinesRemoved { get; init; }
    [JsonPropertyName("trailing_newline_normalized")] public bool TrailingNewlineNormalized { get; init; }
    [JsonPropertyName("may_have_applied")] public bool MayHaveApplied { get; init; }
    [JsonPropertyName("recovery_command")] public string? RecoveryCommand { get; init; }
    [JsonPropertyName("concurrency_guarantee")] public required string ConcurrencyGuarantee { get; init; }
    [JsonPropertyName("runs_event")] public string? RunsEvent { get; init; }
    [JsonPropertyName("summary")] public string Summary { get; init; } = string.Empty;
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    [JsonIgnore] public int ExitCode { get; init; }
}

/// <summary>G825: the only GitHub operations issue sync-body performs.</summary>
internal interface IGitHubIssueBodyClient
{
    string ReadBody(string repo, int issueNumber);

    void UpdateBody(string repo, int issueNumber, string bodyFilePath);
}

internal sealed class GhCliGitHubIssueBodyClient : IGitHubIssueBodyClient
{
    public string ReadBody(string repo, int issueNumber)
    {
        var output = Run(["issue", "view", issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), "--repo", repo, "--json", "body"]);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("body").GetString() ?? string.Empty;
    }

    public void UpdateBody(string repo, int issueNumber, string bodyFilePath) =>
        Run(["issue", "edit", issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), "--repo", repo, "--body-file", bodyFilePath]);

    private static string Run(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gh process.");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"gh {arguments[0]} {arguments[1]} failed (exit {process.ExitCode}): {stdErr.Trim()}");
        }

        return stdOut;
    }
}
