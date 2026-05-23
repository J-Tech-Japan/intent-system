using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G303 / G351: <c>intent-cli automation publish-recovery</c> — host-only safe
/// recovery lane that consults
/// <c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c> together with the
/// open PR list to repair queue-state items whose <c>linked_issue</c> AND
/// <c>linked_pr</c> are both null. The repair is applied only when the
/// publish artifact deterministically identifies a created GitHub issue
/// AND exactly one open PR closes that issue. Ambiguity, repo mismatch,
/// or missing artifacts surface as structured unsafe stops; with
/// <c>--write</c> only high-confidence repairs are applied to
/// <c>queue-state.json</c>.
///
/// Pairs with the existing G284 <c>automation reconcile</c> repair lane
/// (which handles items that already have a <c>linked_issue</c>): G303
/// covers the both-null variant that <c>reconcile</c> currently treats as
/// advisory only.
///
/// G351: <c>--pr &lt;n&gt;</c> scopes the analysis to only the queue item
/// that is relevant to the selected PR — the candidate whose
/// <c>linked_issue</c> matches the closing-issue reference of PR #N.
/// Without scoping the broad scan may flood the operator with unrelated
/// unsafe stops. The scoped path returns at most one repair or one unsafe
/// stop and never touches unrelated queue items.
/// </summary>
internal static class AutomationPublishRecoveryCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string ModeDryRun = "dry-run";
    private const string ModeWrite = "write";

    /// <summary>G390: append-only run-log event name recorded when a --write recovery fills a missing linked_pr.</summary>
    internal const string RecoveryRunEventName = "linked-pr-recovered";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

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

        if (!TryParseArguments(args, out var repo, out var selectedPr, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            writer.WriteLine($"queue-state.json not found at '{queueStatePath}'. Cannot run publish-recovery without parent host queue.");
            return 1;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            writer.WriteLine($"queue-state.json could not be parsed: {exception.Message}");
            return 1;
        }

        var candidates = BuildCandidates(context, queueState);

        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        try
        {
            var lister = CandidateListerFactory?.Invoke()
                ?? new GhCliGitHubAutomationCandidateLister();
            openPrs = lister.ListPullRequests(repo!, requiredLabels: Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to list open PRs for {repo}: {exception.Message}");
            return 1;
        }

        // G351: when --pr is given, scope the analysis to only the queue
        // item relevant to the selected PR. This avoids flooding the
        // operator with unrelated unsafe stops from a broad scan.
        PublishRecoveryAnalysis analysis;
        if (selectedPr is int prNumber)
        {
            analysis = PublishRecoveryAnalyzer.AnalyzeScopedToPr(repo!, candidates, openPrs, prNumber);
        }
        else
        {
            analysis = PublishRecoveryAnalyzer.Analyze(repo!, candidates, openPrs);
        }

        var applied = new List<PublishRecoveryRepair>();
        var failures = new List<string>();
        if (write && analysis.SafeRepairs.Count > 0)
        {
            try
            {
                ApplyRepairs(context, queueStatePath, analysis.SafeRepairs);
                applied.AddRange(analysis.SafeRepairs);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
            {
                failures.Add($"failed to write queue-state.json: {exception.Message}");
            }
        }

        var result = new AutomationPublishRecoveryResult
        {
            Repo = repo!,
            Mode = write ? ModeWrite : ModeDryRun,
            SelectedPr = selectedPr,
            QueueStatePath = queueStatePath,
            SafeRepairs = analysis.SafeRepairs,
            UnsafeStops = analysis.UnsafeStops,
            AppliedCount = applied.Count,
            Warnings = failures,
            Summary = BuildSummary(analysis, applied.Count, write),
            SameRepoMetadataLinkageClassification = ClassifySameRepoLinkage(context, analysis),
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

    /// <summary>
    /// G390: classify the recovery for same-repo metadata-branch topology. A
    /// safe (G315 unique-closing-PR, only-linked_pr-missing) repair maps to the
    /// high-confidence repair-ready case; otherwise advisory-only. Returns
    /// <c>not-applicable</c> in single-root topology. The wrong-branch hazard is
    /// a follow-up that requires resolving the recovery target branch against
    /// the configured metadata branch.
    /// </summary>
    private static string ClassifySameRepoLinkage(CliContext context, PublishRecoveryAnalysis analysis)
    {
        var project = context.Config.Project;
        var sameRepoMetadataBranchConfigured = project.SameRepoTopology
            && (!string.IsNullOrWhiteSpace(project.MetadataWriteBranch)
                || !string.IsNullOrWhiteSpace(project.MetadataBranch));
        var hasSafeLinkedPrRepair = analysis.SafeRepairs.Count > 0;

        return SameRepoMetadataLinkageDiagnostics.Classify(
            sameRepoMetadataBranchConfigured: sameRepoMetadataBranchConfigured,
            recoveryTargetsImplementationBranch: false,
            selectedPrInTargetRepo: hasSafeLinkedPrRepair,
            prUniquelyClosesLinkedIssue: hasSafeLinkedPrRepair,
            executionUnitIdentified: hasSafeLinkedPrRepair,
            onlyLinkedPrMissing: hasSafeLinkedPrRepair).Classification;
    }

    private static IReadOnlyList<PublishRecoveryCandidate> BuildCandidates(CliContext context, QueueState queueState)
    {
        var candidates = new List<PublishRecoveryCandidate>();
        foreach (var item in queueState.Items)
        {
            // G315: items with linked_issue populated but linked_pr null
            // are now in scope (queue-state-backed lane). Skip only items
            // that already have a linked_pr — those are fully linked.
            if (!string.IsNullOrWhiteSpace(item.LinkedPr))
            {
                continue;
            }

            var publishPath = Path.Combine(
                context.RepoRoot,
                IssuePublishArtifactPathResolver.Resolve(item.ExecutionUnit).Replace('/', Path.DirectorySeparatorChar));
            IssuePublishArtifact? artifact = null;
            if (File.Exists(publishPath))
            {
                try
                {
                    artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishPath));
                }
                catch (InvalidOperationException)
                {
                    artifact = null;
                }
            }

            candidates.Add(new PublishRecoveryCandidate
            {
                ExecutionUnit = item.ExecutionUnit,
                LinkedIssueRepo = item.LinkedIssue?.Repo,
                LinkedIssueNumber = item.LinkedIssue?.Number,
                LinkedIssueUrl = item.LinkedIssue?.Url,
                LinkedPrUrl = item.LinkedPr,
                PublishArtifact = artifact,
                PublishArtifactExpectedPath = publishPath
            });
        }
        return candidates;
    }

    private static void ApplyRepairs(CliContext context, string queueStatePath, IReadOnlyList<PublishRecoveryRepair> repairs)
    {
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        var items = queueState.Items.ToArray();
        foreach (var repair in repairs)
        {
            var index = -1;
            for (var i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i].ExecutionUnit, repair.ExecutionUnit, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"queue-state has no item with execution_unit '{repair.ExecutionUnit}' for publish-recovery repair.");
            }

            var existing = items[index];
            // G315: when the repair is the linked-issue→closing-PR lane,
            // queue-state already has linked_issue as the durable host
            // source-of-truth. Preserve it verbatim and only fill in
            // linked_pr. The G303 lane (publish-artifact) writes both refs.
            var linkedIssue = string.Equals(repair.Type, PublishRecoveryAnalyzer.RepairTypeLinkedIssueClosingPr, StringComparison.Ordinal)
                ? existing.LinkedIssue ?? new LinkedIssue
                {
                    Repo = repair.LinkedIssueRepo,
                    Number = repair.LinkedIssueNumber,
                    Url = repair.LinkedIssueUrl
                }
                : new LinkedIssue
                {
                    Repo = repair.LinkedIssueRepo,
                    Number = repair.LinkedIssueNumber,
                    Url = repair.LinkedIssueUrl
                };
            items[index] = existing with
            {
                LinkedIssue = linkedIssue,
                LinkedPr = repair.LinkedPrUrl ?? $"https://github.com/{repair.LinkedIssueRepo}/pull/{repair.LinkedPrNumber}"
            };
        }
        var updated = queueState with
        {
            Items = items,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updated));

        // G390 (review follow-up, Finding 1): a --write recovery must record a
        // durable run event so the repair is auditable and closeout-plan can
        // observe the linked_pr fill. Append one append-only `runs.jsonl` event
        // per applied repair, mirroring the queue-seed/closeout run-log pattern.
        AppendRecoveryRunEvents(context, repairs);
    }

    /// <summary>
    /// G390: append an append-only <c>linked-pr-recovered</c> run event to
    /// <c>.intent-cli/runs.jsonl</c> for each applied publish-recovery repair.
    /// </summary>
    private static void AppendRecoveryRunEvents(CliContext context, IReadOnlyList<PublishRecoveryRepair> repairs)
    {
        if (repairs.Count == 0)
        {
            return;
        }

        var runsPath = Path.Combine(context.RepoRoot, ".intent-cli", "runs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);

        var lines = new System.Text.StringBuilder();
        foreach (var repair in repairs)
        {
            var runEvent = new RunEvent
            {
                Ts = DateTimeOffset.UtcNow,
                ExecutionUnit = repair.ExecutionUnit,
                Event = RecoveryRunEventName,
                By = "automation publish-recovery (G390)",
                LinkedIssue = repair.LinkedIssueNumber is { } issueNumber
                    ? $"https://github.com/{repair.LinkedIssueRepo}/issues/{issueNumber}"
                    : repair.LinkedIssueUrl,
                LinkedPr = repair.LinkedPrUrl ?? $"https://github.com/{repair.LinkedIssueRepo}/pull/{repair.LinkedPrNumber}",
                Reason = $"recovered missing linked_pr (#{repair.LinkedPrNumber}) for '{repair.ExecutionUnit}'.",
            };
            lines.Append(RunLogSerializer.SerializeLine(runEvent)).Append('\n');
        }

        File.AppendAllText(runsPath, lines.ToString());
    }

    private static string BuildSummary(PublishRecoveryAnalysis analysis, int appliedCount, bool write)
    {
        if (analysis.SafeRepairs.Count == 0 && analysis.UnsafeStops.Count == 0)
        {
            return $"No queue items in '{analysis.Repo}' need publish-recovery (no items with linked_pr missing).";
        }

        return write
            ? $"publish-recovery applied {appliedCount} repair(s); {analysis.UnsafeStops.Count} unsafe stop(s) require operator clarification."
            : $"publish-recovery dry-run: {analysis.SafeRepairs.Count} high-confidence repair(s) available, {analysis.UnsafeStops.Count} unsafe stop(s).";
    }

    private static void WriteMarkdown(TextWriter writer, AutomationPublishRecoveryResult result)
    {
        writer.WriteLine($"# automation publish-recovery — `{result.Repo}` ({result.Mode})");
        writer.WriteLine();
        writer.WriteLine($"- queue_state_path: `{result.QueueStatePath}`");
        writer.WriteLine($"- safe_repairs: {result.SafeRepairs.Count} (applied: {result.AppliedCount})");
        writer.WriteLine($"- unsafe_stops: {result.UnsafeStops.Count}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();

        if (result.SafeRepairs.Count > 0)
        {
            writer.WriteLine("## High-confidence repairs");
            foreach (var repair in result.SafeRepairs)
            {
                writer.WriteLine($"- **`{repair.ExecutionUnit}`** ({repair.Type}) → linked_issue=#{repair.LinkedIssueNumber}, linked_pr=#{repair.LinkedPrNumber}");
                foreach (var evidence in repair.Evidence)
                {
                    writer.WriteLine($"  - {evidence}");
                }
            }
            writer.WriteLine();
        }

        if (result.UnsafeStops.Count > 0)
        {
            writer.WriteLine("## Unsafe stops");
            foreach (var stop in result.UnsafeStops)
            {
                writer.WriteLine($"- `{stop.Kind}` on `{stop.ExecutionUnit}`: {stop.Reason}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int? selectedPr,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        selectedPr = null;
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
                case "--pr":
                    // G351: optional PR scoping — scope analysis to the queue
                    // item relevant to the selected PR's closing-issue reference.
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a PR number.";
                        return false;
                    }
                    var prRaw = args[++index].Trim().TrimStart('#');
                    if (!int.TryParse(prRaw, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var prParsed) || prParsed <= 0)
                    {
                        error = $"--pr must be a positive integer PR number (got '{args[index]}').";
                        return false;
                    }
                    selectedPr = prParsed;
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
            error = "automation publish-recovery requires '--repo <owner/repo>'.";
            return false;
        }
        return true;
    }
}

internal sealed record AutomationPublishRecoveryResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    /// <summary>G351: when --pr was supplied, the selected PR number. Null for a broad scan.</summary>
    [JsonPropertyName("selected_pr")]
    public int? SelectedPr { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("safe_repairs")]
    public required IReadOnlyList<PublishRecoveryRepair> SafeRepairs { get; init; }

    [JsonPropertyName("unsafe_stops")]
    public required IReadOnlyList<PublishRecoveryUnsafeStop> UnsafeStops { get; init; }

    [JsonPropertyName("applied_count")]
    public required int AppliedCount { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>
    /// G390: same-repo metadata-branch linkage recovery classification
    /// (<c>same-repo-metadata-linkage-repair-ready</c> / <c>advisory-only</c> /
    /// <c>wrong-branch-unsafe</c> / <c>not-applicable</c>). Lets the host review
    /// loop tell a deterministic, writeable metadata-branch repair apart from
    /// advisory-only / unsafe states. <c>not-applicable</c> in single-root
    /// (non-same-repo) topology.
    /// </summary>
    [JsonPropertyName("same_repo_metadata_linkage_classification")]
    public string? SameRepoMetadataLinkageClassification { get; init; }
}
