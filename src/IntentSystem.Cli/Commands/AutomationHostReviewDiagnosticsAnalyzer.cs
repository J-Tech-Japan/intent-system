using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G280: Pure analyzer for the host review/next-slice diagnostics surface.
/// Inspects open PRs and intent-target issues in the target repo and
/// classifies why the host loop did not advance, so an operator can tell true
/// idle apart from stale-CLI, stuck review label, missing target, conflicting
/// review-side labels, WIP-cap blockage, and clarification-required.
/// Read-only: never reads files, never mutates GitHub or local state, and
/// never launches an AI provider. The command layer wraps this with I/O and
/// the installed-CLI surface probe.
/// </summary>
internal static class AutomationHostReviewDiagnosticsAnalyzer
{
    private static readonly Regex ClosesIssueRegex = new(
        @"(?i)\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+(?:(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)\b",
        RegexOptions.Compiled);

    public static AutomationHostReviewDiagnosticsResult Analyze(
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> publishedIntentTargetIssues,
        bool clarificationRequired,
        string? candidateExecutionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(openPrs);
        ArgumentNullException.ThrowIfNull(publishedIntentTargetIssues);

        var details = new List<AutomationHostReviewDiagnosticsDetail>();

        var publishedIssueNumbers = publishedIntentTargetIssues
            .Where(issue => LabelNames(issue.Labels).Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            .Select(issue => issue.Number)
            .ToHashSet();

        AutomationHostReviewDiagnosticsDetail? requestUpdateConflictPr = null;
        AutomationHostReviewDiagnosticsDetail? stuckReviewingPr = null;
        AutomationHostReviewDiagnosticsDetail? missingTargetPr = null;
        AutomationHostReviewDiagnosticsDetail? actionableReviewPr = null;

        foreach (var pr in openPrs)
        {
            var labels = LabelNames(pr.Labels);
            var hasReviewing = labels.Contains(WorkerNextActionConstants.Labels.IntentPrReviewing, StringComparer.Ordinal);
            var hasRequestUpdate = labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal);
            var hasRereviewReady = labels.Contains(WorkerNextActionConstants.Labels.IntentPrRereviewReady, StringComparer.Ordinal);
            var hasUpdateInProgress = labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal);
            var hasApproved = labels.Contains(WorkerNextActionConstants.Labels.IntentPrApproved, StringComparer.Ordinal);
            var hasIntentTarget = labels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal);

            // Conflict: both request-update and rereview-ready (or update-in-progress + rereview-ready)
            if (hasRequestUpdate && hasRereviewReady && requestUpdateConflictPr is null)
            {
                requestUpdateConflictPr = new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.RequestUpdateRereviewConflict,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Description = $"PR #{pr.Number} carries both 'intent-pr-request-update' and 'intent-pr-rereview-ready'; the workflow cannot pick a transition until one is removed.",
                };
            }

            // Stuck reviewing: intent-pr-reviewing without an exit transition label
            if (hasReviewing && !hasRequestUpdate && !hasUpdateInProgress && !hasRereviewReady && !hasApproved && stuckReviewingPr is null)
            {
                stuckReviewingPr = new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.StuckReviewing,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Description = $"PR #{pr.Number} carries 'intent-pr-reviewing' with no exit-transition label. The previous review session may have crashed before completing approve/request-update.",
                };
            }

            // Missing target on a PR that closes a published intent-target issue
            if (!hasIntentTarget)
            {
                var linkedNumbers = ExtractLinkedIssueNumbers(repo, pr);
                var closedPublished = linkedNumbers.FirstOrDefault(publishedIssueNumbers.Contains);
                if (closedPublished > 0 && missingTargetPr is null)
                {
                    missingTargetPr = new AutomationHostReviewDiagnosticsDetail
                    {
                        Kind = AutomationHostReviewDiagnosticsClassifications.MissingTargetOnPr,
                        TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                        TargetNumber = pr.Number,
                        TargetUrl = pr.Url,
                        Description = $"PR #{pr.Number} closes published intent-target issue #{closedPublished} but lacks 'intent-target'; host review preflight will skip it.",
                    };
                }
            }

            // Actionable review PR: intent-target without blocking labels
            if (hasIntentTarget && !hasRequestUpdate && !hasUpdateInProgress && !hasApproved && actionableReviewPr is null)
            {
                actionableReviewPr = new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.ReviewPrActionable,
                    TargetKind = GhCliGitHubLabelMutator.Kinds.Pr,
                    TargetNumber = pr.Number,
                    TargetUrl = pr.Url,
                    Description = $"PR #{pr.Number} carries 'intent-target' with no blocking review-side label; host review preflight should pick it up.",
                };
            }
        }

        // Precedence: stale-host-cli is handled by the command layer because it
        // depends on the surface probe; here we cover the analyzer-side cases
        // in the order the operator should care about.
        if (requestUpdateConflictPr is not null)
        {
            details.Add(requestUpdateConflictPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.RequestUpdateRereviewConflict,
                $"PR #{requestUpdateConflictPr.TargetNumber} carries conflicting review-side labels (request-update + rereview-ready). Resolve before the host loop can pick a transition.",
                recommendedNextCommand: null,
                clarification: new AutomationHostReviewDiagnosticsClarification
                {
                    Background = $"PR #{requestUpdateConflictPr.TargetNumber} carries both 'intent-pr-request-update' and 'intent-pr-rereview-ready', so the host workflow has no canonical next transition.",
                    Question = "Which state is the operator's intent for this PR?",
                    Options =
                    [
                        $"intent-pr-rereview-ready (treat the previous repair as ready for re-review): operator removes intent-pr-request-update, then the host loop runs review-start.",
                        $"intent-pr-request-update (treat the previous repair as still incomplete): operator removes intent-pr-rereview-ready and leaves request-update; child loop will re-claim with worker claim --kind pr."
                    ],
                },
                details);
        }

        if (stuckReviewingPr is not null)
        {
            details.Add(stuckReviewingPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.StuckReviewing,
                $"PR #{stuckReviewingPr.TargetNumber} appears to be stuck mid-review. The host loop will not advance until intent-pr-reviewing is paired with an exit-transition label.",
                recommendedNextCommand: $"intent-cli automation pr-transition --transition request-update --repo {repo} --pr {stuckReviewingPr.TargetNumber} --write --format json (or --transition approved when review is complete)",
                clarification: null,
                details);
        }

        if (missingTargetPr is not null)
        {
            details.Add(missingTargetPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.MissingTargetOnPr,
                $"PR #{missingTargetPr.TargetNumber} links a published intent-target issue but lacks 'intent-target'; host review preflight will skip it until reconcile applies the label.",
                recommendedNextCommand: $"intent-cli automation reconcile --lane host-review --repo {repo} --write --format json",
                clarification: null,
                details);
        }

        if (clarificationRequired)
        {
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.ClarificationRequired,
                "Host review preflight was told a clarification is required; resolve the source clarification before resuming the host loop.",
                recommendedNextCommand: null,
                clarification: null,
                details);
        }

        var inFlightIssues = publishedIntentTargetIssues
            .Where(issue => LabelNames(issue.Labels).Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            .Select(issue => issue.Number)
            .OrderBy(n => n)
            .ToArray();
        var inFlightPrs = openPrs
            .Where(pr => LabelNames(pr.Labels).Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            .Select(pr => pr.Number)
            .OrderBy(n => n)
            .ToArray();

        if (actionableReviewPr is not null)
        {
            details.Add(actionableReviewPr);
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.ReviewPrActionable,
                $"PR #{actionableReviewPr.TargetNumber} is eligible for host review; preflight should not have returned no-actionable.",
                recommendedNextCommand: $"intent-cli automation host-review-preflight --repo {repo} --format json",
                clarification: null,
                details);
        }

        if (inFlightPrs.Length > 0 || inFlightIssues.Length > 0)
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.WipCapBlocked,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"in-flight intent-target items: PRs={string.Join(",", inFlightPrs)} issues={string.Join(",", inFlightIssues)}",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.WipCapBlocked,
                "WIP cap blocks new next-slice publication; an open intent-target issue or PR is still in flight.",
                recommendedNextCommand: null,
                clarification: null,
                details);
        }

        if (!string.IsNullOrWhiteSpace(candidateExecutionUnit))
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = AutomationHostReviewDiagnosticsClassifications.CandidateReady,
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = $"next-slice candidate provided: {candidateExecutionUnit}",
            });
            return Build(
                repo,
                AutomationHostReviewDiagnosticsClassifications.CandidateReady,
                $"No host review work and no WIP; next-slice candidate {candidateExecutionUnit} is ready to publish per the host loop's pre-approval gate.",
                recommendedNextCommand: $"intent-cli packet draft --execution-unit {candidateExecutionUnit} --target-repo {repo} --dry-run --format markdown",
                clarification: null,
                details);
        }

        return Build(
            repo,
            AutomationHostReviewDiagnosticsClassifications.TrueIdle,
            "No host review PR, no in-flight intent-target item, and no next-slice candidate. The host loop is correctly idle.",
            recommendedNextCommand: null,
            clarification: null,
            details);
    }

    private static AutomationHostReviewDiagnosticsResult Build(
        string repo,
        string classification,
        string summary,
        string? recommendedNextCommand,
        AutomationHostReviewDiagnosticsClarification? clarification,
        IReadOnlyList<AutomationHostReviewDiagnosticsDetail> details) =>
        new()
        {
            Repo = repo,
            Classification = classification,
            Summary = summary,
            ReadOnly = true,
            RecommendedNextCommand = recommendedNextCommand,
            StructuredClarification = clarification,
            Details = details,
            Warnings = Array.Empty<string>(),
        };

    private static IReadOnlyList<int> ExtractLinkedIssueNumbers(string repo, GitHubAutomationPrCandidate pr)
    {
        var numbers = new List<int>();

        foreach (var reference in pr.ClosingIssuesReferences)
        {
            if (reference.Number <= 0)
            {
                continue;
            }

            var referenceRepo = repo;
            if (reference.Repository is { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
            {
                referenceRepo = $"{repository.Owner.Login}/{repository.Name}";
            }

            if (string.Equals(referenceRepo, repo, StringComparison.OrdinalIgnoreCase)
                && !numbers.Contains(reference.Number))
            {
                numbers.Add(reference.Number);
            }
        }

        foreach (Match match in ClosesIssueRegex.Matches(pr.Body ?? string.Empty))
        {
            var linkedRepo = match.Groups["repo"].Value;
            if (!string.IsNullOrWhiteSpace(linkedRepo)
                && !string.Equals(linkedRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(
                    match.Groups["number"].Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var issueNumber)
                && !numbers.Contains(issueNumber))
            {
                numbers.Add(issueNumber);
            }
        }

        return numbers;
    }

    private static IReadOnlyCollection<string> LabelNames(IReadOnlyList<GitHubAutomationLabel>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        return labels
            .Select(label => label.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();
    }
}
