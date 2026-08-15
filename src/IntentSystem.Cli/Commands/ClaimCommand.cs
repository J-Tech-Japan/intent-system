using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G679: serverless, Git-backed ownership for the two scope vocabularies that
/// are safe to coordinate across host clones. A successful plain push is the
/// only acquisition/release/takeover fact; local file creation is never
/// reported as ownership.
/// </summary>
internal static class ClaimCommand
{
    internal const string ClaimsDirectory = ".intent-cli/claims";
    internal const int DefaultMaxAttempts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static int ExecuteAcquire(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, ClaimOperation.Acquire);

    public static int ExecuteRelease(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, ClaimOperation.Release);

    public static int ExecuteTakeover(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, ClaimOperation.Takeover);

    private static int Execute(
        CliContext context,
        string[] args,
        TextWriter writer,
        ClaimOperation operation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage(operation));
            return 0;
        }

        if (!TryParse(args, operation, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage(operation));
            return 1;
        }

        ClaimTransactionResult result;
        try
        {
            result = RunTransaction(context.RepoRoot, request!);
        }
        catch (HostStateGitFailureException exception)
        {
            result = new ClaimTransactionResult(
                "error", request!.Scope, ClaimPath(request.Scope), false, 0,
                null, null, null, exception.Message)
            {
                GitWriteRetry = exception.Evidence,
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            result = new ClaimTransactionResult(
                "error", request!.Scope, ClaimPath(request.Scope), false, 0,
                null, null, null, exception.Message);
        }

        WriteResult(writer, request!.Format, result);
        return result.Status is "acquired" or "released" or "taken-over" or "planned" ? 0 : 1;
    }

    internal static ClaimTransactionResult RunTransaction(string repoRoot, ClaimRequest request)
    {
        var origin = RunGit(repoRoot, ["remote", "get-url", "origin"]);
        EnsureSuccess(origin, "resolve origin");
        var branch = RunGit(repoRoot, ["branch", "--show-current"]);
        EnsureSuccess(branch, "resolve current branch");
        var remote = origin.StandardOutput.Trim();
        var branchName = branch.StandardOutput.Trim();
        if (remote.Length == 0 || branchName.Length == 0)
        {
            throw new InvalidOperationException("claim requires an origin remote and a named current branch");
        }

        if (!request.Write)
        {
            return new ClaimTransactionResult(
                "planned", request.Scope, ClaimPath(request.Scope), false, 0,
                null, null, null,
                "Dry-run only. Re-run with --write; ownership exists only after a successful plain push.");
        }

        HostStateGitRetryEvidence? lastGitWriteRetry = null;
        var hostPull = RunGit(repoRoot, ["pull", "--ff-only", "origin", branchName], hostStateWrite: true);
        CaptureRetry(ref lastGitWriteRetry, hostPull);
        EnsureSuccess(hostPull, "fast-forward host before claim transaction");

        ClaimRecord? lastObserved = null;
        for (var attempt = 1; attempt <= request.MaxAttempts; attempt++)
        {
            var transactionRoot = Path.Combine(
                Path.GetTempPath(), $"intent-cli-claim-{Guid.NewGuid():N}");
            try
            {
                var clone = RunGit(Path.GetTempPath(),
                    ["clone", "--quiet", "--single-branch", "--branch", branchName, remote, transactionRoot]);
                EnsureSuccess(clone, "clone claim transaction workspace");

                // The sequence is deliberately visible and invariant: ff-only
                // pull, create/change, commit, then a non-forced push.
                var pull = RunGit(transactionRoot, ["pull", "--ff-only", "origin", branchName], hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, pull);
                EnsureSuccess(pull, "fast-forward claim base");

                var relativeClaimPath = ClaimPath(request.Scope);
                var absoluteClaimPath = Path.Combine(
                    transactionRoot, relativeClaimPath.Replace('/', Path.DirectorySeparatorChar));
                var current = ReadClaim(absoluteClaimPath);
                lastObserved = current;

                if (request.Operation == ClaimOperation.Acquire && current is not null)
                {
                    return Held(request, relativeClaimPath, attempt, current) with
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
                if (request.Operation != ClaimOperation.Acquire && current is null)
                {
                    return new ClaimTransactionResult(
                        "not-held", request.Scope, relativeClaimPath, false, attempt,
                        null, null, null, "No active claim exists for this scope.")
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
                if (request.Operation == ClaimOperation.Release
                    && (!string.Equals(current!.Actor, request.Actor, StringComparison.Ordinal)
                        || !string.Equals(current.Team, request.Team, StringComparison.Ordinal)))
                {
                    return Held(request, relativeClaimPath, attempt, current,
                        "Only the complete attributed holder identity (actor and team) may release; use explicit takeover otherwise.") with
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
                if (request.Operation == ClaimOperation.Takeover
                    && !string.Equals(current!.Actor, request.DisplacedHolder, StringComparison.Ordinal))
                {
                    return Held(request, relativeClaimPath, attempt, current,
                        $"--displaced-holder must name the current holder '{current.Actor}'.") with
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }

                var now = DateTimeOffset.UtcNow;
                var head = RunGit(transactionRoot, ["rev-parse", "HEAD"]);
                EnsureSuccess(head, "resolve claim base commit");
                string? historyPath = null;

                if (request.Operation == ClaimOperation.Acquire)
                {
                    WriteClaim(absoluteClaimPath, new ClaimRecord(
                        "1", request.Scope, request.Actor, request.Team, now,
                        head.StandardOutput.Trim()));
                }
                else
                {
                    historyPath = WriteHistory(
                        transactionRoot, request, current!, now, head.StandardOutput.Trim());
                    if (request.Operation == ClaimOperation.Release)
                    {
                        File.Delete(absoluteClaimPath);
                    }
                    else
                    {
                        WriteClaim(absoluteClaimPath, new ClaimRecord(
                            "1", request.Scope, request.Actor, request.Team, now,
                            head.StandardOutput.Trim()));
                    }
                }

                var paths = historyPath is null
                    ? new[] { relativeClaimPath }
                    : new[] { relativeClaimPath, historyPath };
                var add = RunGit(transactionRoot, ["add", "--", .. paths], hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, add);
                EnsureSuccess(add, "stage claim transaction");
                var verb = request.Operation switch
                {
                    ClaimOperation.Acquire => "acquire",
                    ClaimOperation.Release => "release",
                    _ => "take over",
                };
                var commit = RunGit(transactionRoot,
                    ["-c", $"user.name={request.Actor}", "-c", $"user.email={SafeEmail(request)}",
                     "commit", "--quiet", "-m", $"claim: {verb} {request.Scope}"],
                    hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, commit);
                EnsureSuccess(commit, "commit claim transaction");

                var push = RunGit(transactionRoot, ["push", "origin", "HEAD"], hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, push);
                if (push.ExitCode != 0 && push.RetryEvidence is not null)
                {
                    throw new HostStateGitFailureException("push claim transaction", push.RetryEvidence);
                }
                if (push.ExitCode == 0)
                {
                    var pushedHead = RunGit(transactionRoot, ["rev-parse", "HEAD"]);
                    EnsureSuccess(pushedHead, "resolve pushed claim commit");
                    var status = request.Operation switch
                    {
                        ClaimOperation.Acquire => "acquired",
                        ClaimOperation.Release => "released",
                        _ => "taken-over",
                    };
                    // Keep the invoking clone on the pushed fact as well. The
                    // ownership result remains true even if this best-effort
                    // refresh is blocked by unrelated local workspace state;
                    // the successful origin push is still authoritative.
                    var localRefresh = RunGit(repoRoot, ["pull", "--ff-only", "origin", branchName], hostStateWrite: true);
                    CaptureRetry(ref lastGitWriteRetry, localRefresh);
                    var detail = localRefresh.ExitCode == 0
                        ? "The plain push succeeded; this is the ownership transition fact."
                        : "The plain push succeeded and is the ownership transition fact, but the invoking clone could not fast-forward: "
                            + localRefresh.StandardError.Trim();
                    return new ClaimTransactionResult(
                        status, request.Scope, relativeClaimPath, true, attempt,
                        request.Operation == ClaimOperation.Release ? null : request.Actor,
                        request.Operation == ClaimOperation.Takeover ? current!.Actor : null,
                        pushedHead.StandardOutput.Trim(),
                        detail,
                        historyPath)
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }

                var fetch = RunGit(transactionRoot, ["fetch", "origin", branchName]);
                EnsureSuccess(fetch, "inspect rejected claim push");
                var remoteClaim = RunGit(transactionRoot,
                    ["show", $"origin/{branchName}:{relativeClaimPath}"]);
                if (remoteClaim.ExitCode == 0)
                {
                    var holder = JsonSerializer.Deserialize<ClaimRecord>(remoteClaim.StandardOutput, JsonOptions)
                        ?? throw new InvalidOperationException("remote claim record was empty");
                    var samePreexistingClaim = current is not null
                        && holder == current;
                    if (request.Operation == ClaimOperation.Acquire || !samePreexistingClaim)
                    {
                        return Held(request, relativeClaimPath, attempt, holder,
                            "The push was rejected and the same scope now exists on origin.") with
                        {
                            GitWriteRetry = lastGitWriteRetry,
                        };
                    }
                }

                // Unrelated remote advance: discard this isolated workspace and
                // reapply from a fresh ff-only base. No force, rebase, or merge.
                if (attempt == request.MaxAttempts)
                {
                    return new ClaimTransactionResult(
                        "retry-exhausted", request.Scope, relativeClaimPath, false, attempt,
                        lastObserved?.Actor, null, null,
                        $"Push was rejected by unrelated remote advance after {attempt} bounded attempt(s).")
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
            }
            finally
            {
                if (Directory.Exists(transactionRoot))
                {
                    Directory.Delete(transactionRoot, recursive: true);
                }
            }
        }

        throw new InvalidOperationException("claim transaction exhausted unexpectedly");
    }

    internal static string ClaimPath(string scope)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))).ToLowerInvariant();
        return $"{ClaimsDirectory}/{digest}.json";
    }

    private static ClaimTransactionResult Held(
        ClaimRequest request,
        string path,
        int attempt,
        ClaimRecord holder,
        string? detail = null) =>
        new ClaimTransactionResult(
            "held", request.Scope, path, false, attempt, holder.Actor, null, null,
            detail ?? $"Scope is held by actor '{holder.Actor}' on team '{holder.Team}'.")
        {
            HolderTeam = holder.Team,
        };

    private static string WriteHistory(
        string transactionRoot,
        ClaimRequest request,
        ClaimRecord current,
        DateTimeOffset now,
        string baseCommit)
    {
        var claimName = Path.GetFileNameWithoutExtension(ClaimPath(request.Scope));
        var operation = request.Operation == ClaimOperation.Release ? "release" : "takeover";
        var relative = $"{ClaimsDirectory}/history/{claimName}/{now:yyyyMMddTHHmmssfffffffZ}-{operation}.json";
        var absolute = Path.Combine(transactionRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var history = new ClaimHistoryRecord(
            "1", operation, request.Scope, request.Actor, request.Team, now,
            request.Reason!, current.Actor, current.Team, current.ClaimedAt, baseCommit);
        File.WriteAllText(absolute, JsonSerializer.Serialize(history, JsonOptions) + Environment.NewLine);
        return relative;
    }

    private static ClaimRecord? ReadClaim(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ClaimRecord>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"claim record '{path}' was empty");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"claim record '{path}' is invalid: {exception.Message}");
        }
    }

    private static void WriteClaim(string path, ClaimRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
    }

    private static string SafeEmail(ClaimRequest request)
    {
        var local = new string(request.Actor.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return $"{(local.Length == 0 ? "intent-cli" : local)}@claims.invalid";
    }

    private static ClaimProcessResult RunGit(
        string workdir,
        IReadOnlyList<string> arguments,
        bool hostStateWrite = false)
    {
        if (hostStateWrite)
        {
            return HostStateGitRetryRunner.Run(
                workdir,
                arguments,
                () => RunGitProcess(workdir, arguments));
        }

        return RunGitProcess(workdir, arguments);
    }

    private static ClaimProcessResult RunGitProcess(string workdir, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start git");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ClaimProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void EnsureSuccess(ClaimProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            if (result.RetryEvidence is not null)
            {
                throw new HostStateGitFailureException(operation, result.RetryEvidence);
            }

            throw new InvalidOperationException(
                $"Could not {operation}: {result.StandardError.Trim()}");
        }
    }

    private static void CaptureRetry(
        ref HostStateGitRetryEvidence? target,
        ClaimProcessResult result)
    {
        if (result.RetryEvidence is not null)
        {
            target = result.RetryEvidence;
        }
    }

    private static bool TryParse(
        string[] args,
        ClaimOperation operation,
        out ClaimRequest? request,
        out string error)
    {
        string? scope = null;
        string? actor = null;
        string? team = null;
        string? reason = null;
        string? displacedHolder = null;
        var format = "json";
        var write = false;
        var maxAttempts = DefaultMaxAttempts;

        for (var i = 0; i < args.Length; i++)
        {
            string NeedValue(string option)
            {
                if (++i >= args.Length) throw new ArgumentException($"{option} requires a value");
                return args[i];
            }

            try
            {
                switch (args[i])
                {
                    case "--scope": scope = NeedValue("--scope"); break;
                    case "--actor": actor = NeedValue("--actor"); break;
                    case "--team": team = NeedValue("--team"); break;
                    case "--reason": reason = NeedValue("--reason"); break;
                    case "--displaced-holder": displacedHolder = NeedValue("--displaced-holder"); break;
                    case "--format": format = NeedValue("--format"); break;
                    case "--max-attempts":
                        if (!int.TryParse(NeedValue("--max-attempts"), out maxAttempts)
                            || maxAttempts is < 1 or > 5)
                            throw new ArgumentException("--max-attempts must be between 1 and 5");
                        break;
                    case "--write": write = true; break;
                    default: throw new ArgumentException($"Unknown option '{args[i]}'");
                }
            }
            catch (ArgumentException exception)
            {
                request = null;
                error = exception.Message;
                return false;
            }
        }

        if (!TryValidateScope(scope, out error))
        {
            request = null;
            return false;
        }
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(team))
        {
            request = null;
            error = "--actor and --team are required attribution";
            return false;
        }
        if (format is not ("json" or "markdown"))
        {
            request = null;
            error = "--format must be json or markdown";
            return false;
        }
        if (operation != ClaimOperation.Acquire && string.IsNullOrWhiteSpace(reason))
        {
            request = null;
            error = "release and takeover require --reason";
            return false;
        }
        if (operation == ClaimOperation.Takeover && string.IsNullOrWhiteSpace(displacedHolder))
        {
            request = null;
            error = "takeover requires --displaced-holder";
            return false;
        }

        request = new ClaimRequest(operation, scope!, actor.Trim(), team.Trim(), reason?.Trim(),
            displacedHolder?.Trim(), write, format, maxAttempts);
        error = string.Empty;
        return true;
    }

    internal static bool TryValidateScope(string? scope, out string error)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            error = "--scope is required";
            return false;
        }

        var parts = scope.Split(':');
        var validToken = static (string value) => value.Length > 0
            && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '+');
        if (parts.Length == 2 && parts[0] == "execution-unit" && validToken(parts[1]))
        {
            error = string.Empty;
            return true;
        }
        if (parts.Length == 3 && parts[0] == "release-prep")
        {
            var repoParts = parts[1].Split('/');
            if (repoParts.Length == 2 && repoParts.All(validToken) && validToken(parts[2]))
            {
                error = string.Empty;
                return true;
            }
        }

        error = "scope must be execution-unit:<EU> or release-prep:<owner/repo>:<version>";
        return false;
    }

    private static string Usage(ClaimOperation operation) => operation switch
    {
        ClaimOperation.Acquire =>
            "Usage: intent-cli claim acquire --scope <execution-unit:EU|release-prep:owner/repo:version> --actor <actor> --team <team> [--max-attempts 2] [--write] [--format json|markdown]",
        ClaimOperation.Release =>
            "Usage: intent-cli claim release --scope <scope> --actor <holder> --team <team> --reason <reason> [--write] [--format json|markdown]",
        _ =>
            "Usage: intent-cli claim takeover --scope <scope> --actor <actor> --team <team> --displaced-holder <actor> --reason <reason> [--max-attempts 2] [--write] [--format json|markdown]",
    };

    private static void WriteResult(TextWriter writer, string format, ClaimTransactionResult result)
    {
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# Claim {result.Status}");
        writer.WriteLine("- preview_status: preview-through-1.x");
        writer.WriteLine($"- scope: `{result.Scope}`");
        writer.WriteLine($"- claim_path: `{result.ClaimPath}`");
        writer.WriteLine($"- push_succeeded: {(result.PushSucceeded ? "true" : "false")}");
        if (result.Holder is not null) writer.WriteLine($"- holder: {result.Holder}");
        if (result.HolderTeam is not null) writer.WriteLine($"- holder_team: {result.HolderTeam}");
        if (result.DisplacedHolder is not null) writer.WriteLine($"- displaced_holder: {result.DisplacedHolder}");
        if (result.Commit is not null) writer.WriteLine($"- commit: `{result.Commit}`");
        if (result.HistoryPath is not null) writer.WriteLine($"- history_path: `{result.HistoryPath}`");
        if (result.GitWriteRetry is not null)
        {
            writer.WriteLine($"- git_write_retry: outcome={result.GitWriteRetry.Outcome}; attempts={result.GitWriteRetry.Attempts}; elapsed_milliseconds={result.GitWriteRetry.ElapsedMilliseconds}; lock_path=`{result.GitWriteRetry.LockPath}`");
            writer.WriteLine($"- manual_remediation: {result.GitWriteRetry.ManualRemediation}");
        }
        writer.WriteLine($"- detail: {result.Detail}");
    }
}

internal enum ClaimOperation { Acquire, Release, Takeover }

internal sealed record ClaimRequest(
    ClaimOperation Operation,
    string Scope,
    string Actor,
    string Team,
    string? Reason,
    string? DisplacedHolder,
    bool Write,
    string Format,
    int MaxAttempts);

internal sealed record ClaimRecord(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("claimed_at")] DateTimeOffset ClaimedAt,
    [property: JsonPropertyName("base_commit")] string BaseCommit);

internal sealed record ClaimHistoryRecord(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("displaced_holder")] string DisplacedHolder,
    [property: JsonPropertyName("displaced_team")] string DisplacedTeam,
    [property: JsonPropertyName("displaced_claimed_at")] DateTimeOffset DisplacedClaimedAt,
    [property: JsonPropertyName("base_commit")] string BaseCommit);

internal sealed record ClaimTransactionResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("claim_path")] string ClaimPath,
    [property: JsonPropertyName("push_succeeded")] bool PushSucceeded,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("holder")] string? Holder,
    [property: JsonPropertyName("displaced_holder")] string? DisplacedHolder,
    [property: JsonPropertyName("commit")] string? Commit,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("history_path")] string? HistoryPath = null)
{
    [JsonPropertyName("preview_status")]
    public string PreviewStatus => "preview-through-1.x";

    [JsonPropertyName("holder_team")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HolderTeam { get; init; }

    [JsonPropertyName("git_write_retry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HostStateGitRetryEvidence? GitWriteRetry { get; init; }
}

internal sealed record ClaimProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    HostStateGitRetryEvidence? RetryEvidence = null);
