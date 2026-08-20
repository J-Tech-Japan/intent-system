using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G307: <c>intent-cli automation publish-lifecycle-repair --repo
/// &lt;owner/repo&gt; [--write] [--format markdown|json]</c> — host-only
/// diagnostic + bounded repair lane that walks every
/// <c>.intent-cli/issues/&lt;unit&gt;/publish.yaml</c>, compares the
/// recorded lifecycle state against the queue-state item state and
/// the GitHub issue labels, and either reports the drift (dry-run by
/// default) or writes the canonical lifecycle state back into
/// publish.yaml on <c>--write</c>.
///
/// The lane only writes deterministic upgrades (rank-monotonic
/// progression: <c>issue-created</c> → <c>published</c> →
/// <c>pr-created</c> → <c>closed-out</c>). Downgrades and ambiguous
/// cases (e.g. issue carries <c>intent-pr-created</c> but queue has no
/// <c>linked_pr</c>) surface as <c>unsafe-stale-lifecycle</c> stops so
/// the operator routes them through G303 publish-recovery first.
/// </summary>
internal static class AutomationPublishLifecycleRepairCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string ModeDryRun = "dry-run";
    private const string ModeWrite = "write";

    private const string UsageLine =
        "Usage: intent-cli automation publish-lifecycle-repair --repo <owner/repo> [--issue <n>|--execution-unit <unit>] [--write|--dry-run] [--format markdown|json]";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }
    public static Func<IGitHubAutomationIssueLookup>? IssueLookupFactory { get; set; }

    /// <summary>Test seam — replaces the default UTC timestamp source.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

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

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(
                args,
                out var repo,
                out var issueScope,
                out var executionUnitScope,
                out var write,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var scope = FormatScope(issueScope, executionUnitScope);

        var queueStatePath = context.GetQueueStatePath();
        QueueState? queueState = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                writer.WriteLine($"queue-state.json could not be parsed: {exception.Message}");
                return 1;
            }
        }

        var issuesDir = Path.Combine(context.RepoRoot, ".intent-cli", "issues");
        if (!Directory.Exists(issuesDir))
        {
            EmitEmpty(writer, format, repo!, scope, write, $"no `.intent-cli/issues/` directory at '{issuesDir}'; nothing to repair.");
            return 0;
        }

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
        var nowIso = ResolveNow().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        var candidates = new List<PublishLifecycleCandidate>();
        foreach (var unitDir in Directory.EnumerateDirectories(issuesDir).OrderBy(p => p, StringComparer.Ordinal))
        {
            var unit = Path.GetFileName(unitDir)!;
            if (executionUnitScope is not null
                && !string.Equals(unit, executionUnitScope, StringComparison.Ordinal))
            {
                continue;
            }

            var artifactPath = Path.Combine(unitDir, "publish.yaml");
            IssuePublishArtifact? artifact = null;
            if (File.Exists(artifactPath))
            {
                try
                {
                    artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
                }
                catch (InvalidOperationException)
                {
                    artifact = null;
                }
            }

            if (issueScope is { } requestedIssue
                && artifact?.CreatedIssueNumber != requestedIssue)
            {
                continue;
            }

            var queueItem = queueState?.Items.FirstOrDefault(i => string.Equals(i.ExecutionUnit, unit, StringComparison.Ordinal));

            int? linkedPrNumber = null;
            string? linkedPrUrl = null;
            if (queueItem?.LinkedPr is { } prRef && !string.IsNullOrWhiteSpace(prRef))
            {
                linkedPrNumber = ExtractTrailingInteger(prRef);
                linkedPrUrl = prRef.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? prRef : null;
            }

            string? issueState = null;
            IReadOnlyCollection<string> issueLabels = Array.Empty<string>();
            if (artifact?.CreatedIssueNumber is { } issueNumber)
            {
                if (!issueByNumber.TryGetValue(issueNumber, out var ghIssue))
                {
                    try
                    {
                        ghIssue = issueLookup.GetIssue(repo!, issueNumber);
                        issueByNumber[issueNumber] = ghIssue;
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException)
                    {
                        ghIssue = null;
                    }
                }

                if (ghIssue is not null)
                {
                    issueState = ghIssue.State;
                    issueLabels = (ghIssue.Labels ?? Array.Empty<GitHubAutomationLabel>())
                        .Select(label => label.Name)
                        .ToArray();
                }
            }

            candidates.Add(new PublishLifecycleCandidate
            {
                ExecutionUnit = unit,
                ArtifactPath = artifactPath,
                Artifact = artifact,
                QueueItemState = queueItem?.State,
                LinkedPrNumber = linkedPrNumber,
                LinkedPrUrl = linkedPrUrl,
                IssueState = issueState,
                IssueLabels = issueLabels,
                NowIso = nowIso
            });
        }

        var analysis = PublishLifecycleAnalyzer.Analyze(candidates);

        var applied = new List<string>();
        var failures = new List<string>();
        if (write)
        {
            foreach (var entry in analysis.Entries)
            {
                if (entry.RecommendedLifecycleState is null
                    || string.Equals(entry.CurrentLifecycleState, entry.RecommendedLifecycleState, StringComparison.Ordinal))
                {
                    continue;
                }
                if (entry.Classification == PublishLifecycleAnalyzer.ClassificationUnsafe
                    || entry.Classification == PublishLifecycleAnalyzer.ClassificationMissingArtifact)
                {
                    continue;
                }
                try
                {
                    ApplyRepair(entry);
                    applied.Add(entry.ExecutionUnit);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    failures.Add($"{entry.ExecutionUnit}: {exception.Message}");
                }
            }
        }

        var result = new PublishLifecycleRepairResult
        {
            Repo = repo!,
            Scope = scope,
            Mode = write ? ModeWrite : ModeDryRun,
            CoherentCount = analysis.CoherentCount,
            DriftCount = analysis.DriftCount,
            UnsafeCount = analysis.UnsafeCount,
            MissingArtifactCount = analysis.MissingArtifactCount,
            AppliedCount = applied.Count,
            AppliedUnits = applied,
            Entries = analysis.Entries,
            Warnings = failures,
            Summary = BuildSummary(analysis, applied.Count, failures.Count, write, scope)
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
        return failures.Count == 0 ? 0 : 1;
    }

    private static void ApplyRepair(PublishLifecycleEntry entry)
    {
        var existing = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(entry.ArtifactPath));
        var updated = existing with
        {
            LifecycleState = entry.RecommendedLifecycleState,
            LinkedPrNumber = entry.RecommendedLinkedPrNumber ?? existing.LinkedPrNumber,
            LinkedPrUrl = entry.RecommendedLinkedPrUrl ?? existing.LinkedPrUrl,
            ClosedOutAt = entry.RecommendedClosedOutAt ?? existing.ClosedOutAt
        };
        File.WriteAllText(entry.ArtifactPath, IssuePublishArtifactYaml.Serialize(updated));
    }

    private static int? ExtractTrailingInteger(string value)
    {
        var trimmed = value.TrimEnd('/').Trim();
        var slashIndex = trimmed.LastIndexOf('/');
        var tail = slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : trimmed;
        return int.TryParse(tail, out var n) && n > 0 ? n : null;
    }

    private static DateTimeOffset ResolveNow() =>
        (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

    private static string BuildSummary(
        PublishLifecycleAnalysis analysis,
        int appliedCount,
        int failureCount,
        bool write,
        string scope)
    {
        var scopeSuffix = string.Equals(scope, "all", StringComparison.Ordinal)
            ? string.Empty
            : $" ({scope})";
        if (write)
        {
            return $"publish-lifecycle-repair{scopeSuffix}: applied {appliedCount} repair(s) ({analysis.CoherentCount} coherent, {analysis.DriftCount} drift, {analysis.UnsafeCount} unsafe, {analysis.MissingArtifactCount} missing-artifact, {failureCount} failure(s)).";
        }
        return string.Equals(scope, "all", StringComparison.Ordinal)
            ? $"publish-lifecycle-repair (dry-run): {analysis.DriftCount} repairable drift(s); {analysis.CoherentCount} coherent, {analysis.UnsafeCount} unsafe, {analysis.MissingArtifactCount} missing-artifact."
            : $"publish-lifecycle-repair (dry-run, {scope}): {analysis.DriftCount} repairable drift(s); {analysis.CoherentCount} coherent, {analysis.UnsafeCount} unsafe, {analysis.MissingArtifactCount} missing-artifact.";
    }

    private static void EmitEmpty(
        TextWriter writer,
        string format,
        string repo,
        string scope,
        bool write,
        string summary)
    {
        var result = new PublishLifecycleRepairResult
        {
            Repo = repo,
            Scope = scope,
            Mode = write ? ModeWrite : ModeDryRun,
            CoherentCount = 0,
            DriftCount = 0,
            UnsafeCount = 0,
            MissingArtifactCount = 0,
            AppliedCount = 0,
            AppliedUnits = Array.Empty<string>(),
            Entries = Array.Empty<PublishLifecycleEntry>(),
            Warnings = Array.Empty<string>(),
            Summary = summary
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

    private static void WriteMarkdown(TextWriter writer, PublishLifecycleRepairResult result)
    {
        var scopeSuffix = string.Equals(result.Scope, "all", StringComparison.Ordinal)
            ? string.Empty
            : $", {result.Scope}";
        writer.WriteLine($"# automation publish-lifecycle-repair (G307) — `{result.Repo}` ({result.Mode}{scopeSuffix})");
        writer.WriteLine();
        writer.WriteLine($"- coherent: {result.CoherentCount}");
        writer.WriteLine($"- drift (repairable): {result.DriftCount}");
        writer.WriteLine($"- unsafe (operator stop): {result.UnsafeCount}");
        writer.WriteLine($"- missing publish artifact: {result.MissingArtifactCount}");
        writer.WriteLine($"- applied: {result.AppliedCount}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.Entries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Entries");
            foreach (var entry in result.Entries)
            {
                writer.WriteLine($"- **`{entry.ExecutionUnit}`** [{entry.Classification}] `{entry.CurrentLifecycleState ?? "(none)"}` → `{entry.RecommendedLifecycleState ?? "(no change)"}`");
                foreach (var ev in entry.Evidence)
                {
                    writer.WriteLine($"  - {ev}");
                }
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int? issueScope,
        out string? executionUnitScope,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        issueScope = null;
        executionUnitScope = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;
        var dryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--issue":
                    if (index + 1 >= args.Length
                        || !int.TryParse(
                            args[++index],
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var requestedIssue)
                        || requestedIssue <= 0)
                    {
                        error = "--issue requires a positive integer.";
                        return false;
                    }
                    if (executionUnitScope is not null)
                    {
                        error = "--issue and --execution-unit are mutually exclusive.";
                        return false;
                    }
                    issueScope = requestedIssue;
                    break;
                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }
                    var requestedUnit = args[++index].Trim();
                    if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(requestedUnit, out var unitError))
                    {
                        error = $"--execution-unit is invalid: {unitError}";
                        return false;
                    }
                    if (issueScope is not null)
                    {
                        error = "--issue and --execution-unit are mutually exclusive.";
                        return false;
                    }
                    executionUnitScope = requestedUnit;
                    break;
                case "--write":
                    if (dryRun)
                    {
                        error = "--write and --dry-run are mutually exclusive.";
                        return false;
                    }
                    write = true;
                    break;
                case "--dry-run":
                    if (write)
                    {
                        error = "--write and --dry-run are mutually exclusive.";
                        return false;
                    }
                    dryRun = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
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
            error = "automation publish-lifecycle-repair requires '--repo <owner/repo>'.";
            return false;
        }
        return true;
    }

    private static string FormatScope(int? issueScope, string? executionUnitScope) =>
        issueScope is { } issue
            ? $"issue:{issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : executionUnitScope is not null
                ? $"execution-unit:{executionUnitScope}"
                : "all";

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation publish-lifecycle-repair");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Repairs deterministic publish.yaml lifecycle drift; scope with one issue or execution unit to leave other domains untouched.");
    }
}

internal sealed record PublishLifecycleRepairResult
{
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
    [JsonPropertyName("mode")] public required string Mode { get; init; }
    [JsonPropertyName("coherent_count")] public required int CoherentCount { get; init; }
    [JsonPropertyName("drift_count")] public required int DriftCount { get; init; }
    [JsonPropertyName("unsafe_count")] public required int UnsafeCount { get; init; }
    [JsonPropertyName("missing_artifact_count")] public required int MissingArtifactCount { get; init; }
    [JsonPropertyName("applied_count")] public required int AppliedCount { get; init; }
    [JsonPropertyName("applied_units")] public required IReadOnlyList<string> AppliedUnits { get; init; }
    [JsonPropertyName("entries")] public required IReadOnlyList<PublishLifecycleEntry> Entries { get; init; }
    [JsonPropertyName("warnings")] public required IReadOnlyList<string> Warnings { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal interface IGitHubAutomationIssueLookup
{
    GitHubAutomationIssueCandidate GetIssue(string repo, int issueNumber);
}

internal sealed class GhCliGitHubAutomationIssueLookup : IGitHubAutomationIssueLookup
{
    public GitHubAutomationIssueCandidate GetIssue(string repo, int issueNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        if (issueNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(issueNumber), "issue number must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
                 {
                     "issue", "view", issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--repo", repo,
                     "--json", GhCliGitHubAutomationCandidateLister.ListJsonFields
                 })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start `gh` process to view issue #{issueNumber} in {repo}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh` failed to view issue #{issueNumber} in {repo} with exit {process.ExitCode}: {errorBody.Trim()}");
        }

        return JsonSerializer.Deserialize<GitHubAutomationIssueCandidate>(stdout)
            ?? throw new InvalidOperationException($"`gh issue view` for #{issueNumber} in {repo} returned empty JSON.");
    }
}
