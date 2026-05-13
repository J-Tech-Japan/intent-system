using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G343 (PR #790 repair): <c>intent-cli automation intent-target-gap-recovery
/// --repo &lt;owner/repo&gt; [--write] [--format markdown|json]</c> —
/// host-side recovery lane that finds GitHub issues created via the
/// publish-flow command which never received the <c>intent-target</c>
/// label (typically because the host loop stalled before the
/// publish-boundary transition), and either reports the gap
/// (<c>--write</c> omitted) or invokes
/// <see cref="AutomationIssuePublishCommand"/> for each safe
/// candidate.
///
/// Inputs captured here (filesystem + GitHub):
/// <list type="bullet">
/// <item>Every <c>.intent-cli/issues/&lt;unit&gt;/publish.yaml</c> in the host repo, classified canonical via <see cref="PublishYamlCanonicalAnalyzer"/>.</item>
/// <item>For each canonical artifact with a <c>created_issue_number</c>, the GitHub issue state and labels.</item>
/// </list>
/// All decisions are made by the pure
/// <see cref="IntentTargetGapAnalyzer"/>; this command only translates
/// the analysis into an exit code and (optionally) writes via the
/// existing <c>automation issue-publish</c> surface so the
/// label-transition contract stays single-sourced.
/// </summary>
internal static class AutomationIntentTargetGapRecoveryCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string ModeDryRun = "dry-run";
    private const string ModeWrite = "write";

    /// <summary>Test seam — replaces the gh-backed candidate lister.</summary>
    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    /// <summary>Test seam — replaces the gh-backed per-issue lookup.</summary>
    public static Func<IGitHubAutomationIssueLookup>? IssueLookupFactory { get; set; }

    /// <summary>
    /// Test seam — replaces the in-process call into
    /// <see cref="AutomationIssuePublishCommand.Execute"/>. Receives the
    /// (context, args, writer) tuple just like the real command and
    /// must return the same exit code. Lets tests verify the safe
    /// repair lane invokes the documented automation surface without
    /// hitting <c>gh</c>.
    /// </summary>
    public static Func<CliContext, string[], TextWriter, int>? IssuePublishExecutor { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repo, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var issuesDir = Path.Combine(context.RepoRoot, ".intent-cli", "issues");
        if (!Directory.Exists(issuesDir))
        {
            EmitEmpty(writer, format, repo!, write,
                $"no `.intent-cli/issues/` directory at '{issuesDir}'; nothing to recover.");
            return 0;
        }

        // List GitHub issues once and index by number so each
        // candidate doesn't trigger its own API call when the
        // bulk-list already has it.
        IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        try
        {
            var lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            issues = lister.ListIssues(repo!, requiredLabels: Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to list GitHub issues for {repo}: {exception.Message}");
            return 1;
        }
        var issueByNumber = issues.ToDictionary(i => i.Number);
        var issueLookup = IssueLookupFactory?.Invoke() ?? new GhCliGitHubAutomationIssueLookup();

        var candidates = new List<IntentTargetGapCandidate>();
        foreach (var unitDir in Directory.EnumerateDirectories(issuesDir).OrderBy(p => p, StringComparer.Ordinal))
        {
            var unit = Path.GetFileName(unitDir)!;
            var artifactPath = Path.Combine(unitDir, "publish.yaml");
            if (!File.Exists(artifactPath))
            {
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(artifactPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Filesystem read failure surfaces as a non-canonical
                // candidate so the analyzer can produce an unsafe stop;
                // we still want to know the artifact exists in this
                // unit dir.
                candidates.Add(new IntentTargetGapCandidate
                {
                    ExecutionUnit = unit,
                    PublishArtifactPath = ToRepoRelative(context.RepoRoot, artifactPath),
                    Artifact = null,
                    ArtifactIsCanonical = false,
                    IssueExistsOnGitHub = false,
                    IssueState = null,
                    IssueLabels = Array.Empty<string>(),
                });
                continue;
            }

            IssuePublishArtifact? artifact = null;
            try
            {
                artifact = IssuePublishArtifactYaml.Deserialize(content);
            }
            catch (InvalidOperationException)
            {
                artifact = null;
            }

            var canonicalResult = PublishYamlCanonicalAnalyzer.Analyze(content, unit);
            var isCanonical = string.Equals(
                canonicalResult.Classification,
                PublishYamlCanonicalAnalyzer.ClassificationCanonical,
                StringComparison.Ordinal);

            string? issueState = null;
            IReadOnlyCollection<string> issueLabels = Array.Empty<string>();
            var issueExists = false;
            if (artifact?.CreatedIssueNumber is { } issueNumber)
            {
                if (!issueByNumber.TryGetValue(issueNumber, out var ghIssue))
                {
                    try
                    {
                        ghIssue = issueLookup.GetIssue(repo!, issueNumber);
                        if (ghIssue is not null)
                        {
                            issueByNumber[issueNumber] = ghIssue;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException)
                    {
                        ghIssue = null;
                    }
                }
                if (ghIssue is not null)
                {
                    issueExists = true;
                    issueState = ghIssue.State;
                    issueLabels = (ghIssue.Labels ?? Array.Empty<GitHubAutomationLabel>())
                        .Select(label => label.Name)
                        .ToArray();
                }
            }

            candidates.Add(new IntentTargetGapCandidate
            {
                ExecutionUnit = unit,
                PublishArtifactPath = ToRepoRelative(context.RepoRoot, artifactPath),
                Artifact = artifact,
                ArtifactIsCanonical = isCanonical,
                IssueExistsOnGitHub = issueExists,
                IssueState = issueState,
                IssueLabels = issueLabels,
            });
        }

        var analysis = IntentTargetGapAnalyzer.Analyze(repo!, candidates);

        var applied = new List<string>();
        var failures = new List<string>();
        if (write)
        {
            foreach (var repair in analysis.SafeRepairs)
            {
                try
                {
                    if (ApplyRepair(context, repo!, repair, writer))
                    {
                        applied.Add($"#{repair.IssueNumber}");
                    }
                    else
                    {
                        failures.Add($"{repair.ExecutionUnit}: automation issue-publish returned non-zero exit code.");
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    failures.Add($"{repair.ExecutionUnit}: {exception.Message}");
                }
            }
        }

        var result = new IntentTargetGapRecoveryCommandResult
        {
            Repo = repo!,
            Mode = write ? ModeWrite : ModeDryRun,
            SafeRepairCount = analysis.SafeRepairs.Count,
            UnsafeStopCount = analysis.UnsafeStops.Count,
            AppliedCount = applied.Count,
            AppliedIssues = applied,
            SafeRepairs = analysis.SafeRepairs,
            UnsafeStops = analysis.UnsafeStops,
            Warnings = failures,
            Summary = BuildSummary(analysis, applied.Count, failures.Count, write),
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        // Non-zero exit when there are unsafe stops or write failures
        // so host-loop callers can detect the gap without parsing JSON.
        if (failures.Count > 0)
        {
            return 1;
        }
        if (analysis.UnsafeStops.Count > 0)
        {
            return 1;
        }
        return 0;
    }

    private static bool ApplyRepair(
        CliContext context,
        string repo,
        IntentTargetGapRepair repair,
        TextWriter parentWriter)
    {
        var args = new[]
        {
            "--repo", repo,
            "--issue", repair.IssueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--write",
            "--format", "json",
        };
        using var buffer = new StringWriter();
        var executor = IssuePublishExecutor ?? AutomationIssuePublishCommand.Execute;
        var exitCode = executor(context, args, buffer);
        var emitted = buffer.ToString();
        if (!string.IsNullOrWhiteSpace(emitted))
        {
            // Surface the per-repair issue-publish output as a single
            // bracketed block on the parent writer so operators can see
            // which transitions actually fired.
            parentWriter.WriteLine($"--- automation issue-publish #{repair.IssueNumber} ---");
            parentWriter.Write(emitted);
            if (!emitted.EndsWith("\n", StringComparison.Ordinal))
            {
                parentWriter.WriteLine();
            }
        }
        return exitCode == 0;
    }

    private static string BuildSummary(
        IntentTargetGapAnalysis analysis,
        int appliedCount,
        int failureCount,
        bool write)
    {
        if (write)
        {
            return $"intent-target-gap-recovery: applied intent-target to {appliedCount} issue(s); "
                + $"{analysis.SafeRepairs.Count} safe repair(s), {analysis.UnsafeStops.Count} unsafe stop(s), {failureCount} failure(s).";
        }
        return $"intent-target-gap-recovery (dry-run): {analysis.SafeRepairs.Count} safe repair(s); "
            + $"{analysis.UnsafeStops.Count} unsafe stop(s).";
    }

    private static string ToRepoRelative(string repoRoot, string fullPath)
    {
        if (string.IsNullOrEmpty(repoRoot)) return fullPath;
        var normalizedRoot = repoRoot.EndsWith(Path.DirectorySeparatorChar) ? repoRoot : repoRoot + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            return fullPath[normalizedRoot.Length..].Replace('\\', '/');
        }
        return fullPath.Replace('\\', '/');
    }

    private static void EmitEmpty(
        TextWriter writer,
        string format,
        string repo,
        bool write,
        string summary)
    {
        var result = new IntentTargetGapRecoveryCommandResult
        {
            Repo = repo,
            Mode = write ? ModeWrite : ModeDryRun,
            SafeRepairCount = 0,
            UnsafeStopCount = 0,
            AppliedCount = 0,
            AppliedIssues = Array.Empty<string>(),
            SafeRepairs = Array.Empty<IntentTargetGapRepair>(),
            UnsafeStops = Array.Empty<IntentTargetGapUnsafeStop>(),
            Warnings = Array.Empty<string>(),
            Summary = summary,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
    }

    private static void WriteMarkdown(TextWriter writer, IntentTargetGapRecoveryCommandResult result)
    {
        writer.WriteLine($"# automation intent-target-gap-recovery (G343)");
        writer.WriteLine();
        writer.WriteLine($"- repo: `{result.Repo}`");
        writer.WriteLine($"- mode: `{result.Mode}`");
        writer.WriteLine($"- safe repairs: {result.SafeRepairCount}");
        writer.WriteLine($"- unsafe stops: {result.UnsafeStopCount}");
        writer.WriteLine($"- applied: {result.AppliedCount}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);

        if (result.SafeRepairs.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Safe repairs");
            foreach (var repair in result.SafeRepairs)
            {
                writer.WriteLine($"- `{repair.ExecutionUnit}` → issue #{repair.IssueNumber}: {repair.Summary}");
                writer.WriteLine($"  - recovery: `{repair.RecoveryCommand}`");
            }
        }
        if (result.UnsafeStops.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Unsafe stops");
            foreach (var stop in result.UnsafeStops)
            {
                writer.WriteLine($"- `{stop.ExecutionUnit}` ({stop.Kind}): {stop.Reason}");
            }
        }
        if (result.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;

                case "--write":
                    write = true;
                    break;

                case "--dry-run":
                    write = false;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;

                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }
        return true;
    }
}

internal sealed record IntentTargetGapRecoveryCommandResult
{
    public required string Repo { get; init; }
    public required string Mode { get; init; }
    public required int SafeRepairCount { get; init; }
    public required int UnsafeStopCount { get; init; }
    public required int AppliedCount { get; init; }
    public required IReadOnlyList<string> AppliedIssues { get; init; }
    public required IReadOnlyList<IntentTargetGapRepair> SafeRepairs { get; init; }
    public required IReadOnlyList<IntentTargetGapUnsafeStop> UnsafeStops { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string Summary { get; init; }
}
