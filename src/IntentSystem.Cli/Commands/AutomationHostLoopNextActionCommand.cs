using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G308: <c>intent-cli automation host-loop-next-action --repo
/// &lt;owner/repo&gt; [--format markdown|json]</c> — read-only
/// aggregator that captures the highest-priority host loop signal from
/// the candidate lister (review PR / WIP cap / lease) plus optional
/// preflight inputs, calls
/// <see cref="HostLoopNextActionAnalyzer"/>, and emits ONE primary
/// recommended action with a deterministic next command and structured
/// evidence.
///
/// Tests inject a fake
/// <see cref="IGitHubAutomationCandidateLister"/> through
/// <see cref="CandidateListerFactory"/>; preflight inputs (host-sync,
/// next-slice, publish-recovery / publish-lifecycle) are accepted on
/// the command line as flags so the wrapper stays cheap and
/// composable. The host loop guidance can pre-run the specialized
/// commands and pipe their results into this aggregator.
/// </summary>
internal static class AutomationHostLoopNextActionCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

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

        if (!TryParseArguments(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues;
        try
        {
            var lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            openPrs = lister.ListPullRequests(parsed.Repo, requiredLabels: Array.Empty<string>());
            openIssues = lister.ListIssues(parsed.Repo, requiredLabels: Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to list automation candidates for {parsed.Repo}: {exception.Message}");
            return 1;
        }

        var actionableReviewPr = FindActionableReviewPr(openPrs);
        var openIntentTargetExists = OpenIntentTargetExists(openPrs, openIssues);
        var anyLeaseHeld = AnyChildWorkerLeaseHeld(openPrs, openIssues);

        var input = new HostLoopNextActionInput
        {
            Repo = parsed.Repo,
            Domain = parsed.Domain,
            StaleCli = parsed.StaleCli,
            SyncClassification = parsed.SyncClassification,
            SafeStashRequired = parsed.SafeStashRequired,
            ActionableReviewPr = actionableReviewPr,
            PublishRecoveryRepairsAvailable = parsed.PublishRecoveryRepairsAvailable,
            PublishLifecycleDriftCount = parsed.PublishLifecycleDriftCount,
            NextSliceIssueCutReady = parsed.NextSliceIssueCutReady,
            PublishNextSliceExecutionUnit = parsed.PublishNextSliceExecutionUnit,
            OpenIntentTargetPrOrIssueExists = openIntentTargetExists,
            AnyChildWorkerLeaseHeld = anyLeaseHeld,
            HardClarificationOpen = parsed.HardClarificationOpen
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);
        var emitted = new HostLoopNextActionEmittedResult
        {
            Repo = parsed.Repo,
            Domain = parsed.Domain,
            Classification = result.Classification,
            MutationAllowed = result.MutationAllowed,
            RecommendedCommand = result.RecommendedCommand,
            Evidence = result.Evidence,
            Summary = result.Summary
        };

        if (string.Equals(parsed.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(emitted, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, emitted);
        }
        return 0;
    }

    private static ActionableReviewPr? FindActionableReviewPr(IReadOnlyList<GitHubAutomationPrCandidate> openPrs)
    {
        foreach (var pr in openPrs)
        {
            var labels = (pr.Labels ?? Array.Empty<GitHubAutomationLabel>())
                .Select(label => label.Name)
                .ToHashSet(StringComparer.Ordinal);
            var hasIntentTarget = labels.Contains(WorkerNextActionConstants.Labels.IntentTarget);
            var hasBlockingReviewLabel =
                labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate)
                || labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress)
                || labels.Contains(WorkerNextActionConstants.Labels.IntentPrApproved);
            if (hasIntentTarget && !hasBlockingReviewLabel)
            {
                return new ActionableReviewPr { Number = pr.Number, Url = pr.Url };
            }
        }
        return null;
    }

    private static bool OpenIntentTargetExists(
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues)
    {
        bool HasIntentTarget(IReadOnlyList<GitHubAutomationLabel>? labels) =>
            (labels ?? Array.Empty<GitHubAutomationLabel>())
            .Any(label => string.Equals(label.Name, WorkerNextActionConstants.Labels.IntentTarget, StringComparison.Ordinal));
        return openPrs.Any(pr => HasIntentTarget(pr.Labels))
            || openIssues.Any(issue => HasIntentTarget(issue.Labels));
    }

    private static bool AnyChildWorkerLeaseHeld(
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues)
    {
        bool LabelMatches(IReadOnlyList<GitHubAutomationLabel>? labels, string name) =>
            (labels ?? Array.Empty<GitHubAutomationLabel>())
            .Any(label => string.Equals(label.Name, name, StringComparison.Ordinal));
        var prLease = openPrs.Any(pr =>
            LabelMatches(pr.Labels, WorkerNextActionConstants.Labels.IntentPrUpdateInProgress));
        var issueLease = openIssues.Any(issue =>
            LabelMatches(issue.Labels, WorkerNextActionConstants.Labels.IntentIssueInProgress));
        return prLease || issueLease;
    }

    private static void WriteMarkdown(TextWriter writer, HostLoopNextActionEmittedResult result)
    {
        writer.WriteLine($"# automation host-loop-next-action (G308) — `{result.Repo}`");
        writer.WriteLine();
        writer.WriteLine($"- classification: **{result.Classification}**");
        writer.WriteLine($"- mutation_allowed: {(result.MutationAllowed ? "yes" : "no")}");
        if (!string.IsNullOrEmpty(result.RecommendedCommand))
        {
            writer.WriteLine($"- recommended_command: `{result.RecommendedCommand}`");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.Evidence.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Evidence");
            foreach (var ev in result.Evidence)
            {
                writer.WriteLine($"- {ev}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out ParsedArgs parsed, out string error)
    {
        parsed = default!;
        error = string.Empty;

        string? repo = null;
        string? domain = null;
        var staleCli = false;
        string? syncClassification = null;
        var safeStashRequired = false;
        var publishRecoveryRepairs = 0;
        var publishLifecycleDrift = 0;
        var nextSliceIssueCutReady = false;
        string? publishNextSliceExecutionUnit = null;
        var hardClarificationOpen = false;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo)."; return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value."; return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--stale-cli":
                    staleCli = true;
                    break;
                case "--sync-classification":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--sync-classification requires a value."; return false;
                    }
                    syncClassification = args[++index].Trim();
                    break;
                case "--safe-stash-required":
                    safeStashRequired = true;
                    break;
                case "--publish-recovery-repairs":
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var prr) || prr < 0)
                    {
                        error = "--publish-recovery-repairs requires a non-negative integer."; return false;
                    }
                    publishRecoveryRepairs = prr; index++;
                    break;
                case "--publish-lifecycle-drift":
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pld) || pld < 0)
                    {
                        error = "--publish-lifecycle-drift requires a non-negative integer."; return false;
                    }
                    publishLifecycleDrift = pld; index++;
                    break;
                case "--next-slice-issue-cut-ready":
                    nextSliceIssueCutReady = true;
                    break;
                case "--publish-next-execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--publish-next-execution-unit requires a value."; return false;
                    }
                    publishNextSliceExecutionUnit = args[++index].Trim();
                    break;
                case "--hard-clarification-open":
                    hardClarificationOpen = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json)."; return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}')."; return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'."; return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation host-loop-next-action requires '--repo <owner/repo>'.";
            return false;
        }

        parsed = new ParsedArgs
        {
            Repo = repo!,
            Domain = domain,
            StaleCli = staleCli,
            SyncClassification = syncClassification,
            SafeStashRequired = safeStashRequired,
            PublishRecoveryRepairsAvailable = publishRecoveryRepairs,
            PublishLifecycleDriftCount = publishLifecycleDrift,
            NextSliceIssueCutReady = nextSliceIssueCutReady,
            PublishNextSliceExecutionUnit = publishNextSliceExecutionUnit,
            HardClarificationOpen = hardClarificationOpen,
            Format = format
        };
        return true;
    }

    private sealed record ParsedArgs
    {
        public required string Repo { get; init; }
        public string? Domain { get; init; }
        public required bool StaleCli { get; init; }
        public string? SyncClassification { get; init; }
        public required bool SafeStashRequired { get; init; }
        public required int PublishRecoveryRepairsAvailable { get; init; }
        public required int PublishLifecycleDriftCount { get; init; }
        public required bool NextSliceIssueCutReady { get; init; }
        public string? PublishNextSliceExecutionUnit { get; init; }
        public required bool HardClarificationOpen { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record HostLoopNextActionEmittedResult
{
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("mutation_allowed")] public required bool MutationAllowed { get; init; }
    [JsonPropertyName("recommended_command")] public required string? RecommendedCommand { get; init; }
    [JsonPropertyName("evidence")] public required IReadOnlyList<string> Evidence { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}
