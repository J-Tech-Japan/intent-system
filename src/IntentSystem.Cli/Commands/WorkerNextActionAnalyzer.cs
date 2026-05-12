namespace IntentSystem.Cli.Commands;

/// <summary>
/// G206: Pure deterministic selector for <c>intent-cli worker next-action</c>.
/// Given the open PR and issue candidate lists (already filtered to
/// <c>intent-target</c>) for a repo, returns at most one
/// <see cref="WorkerNextActionResult"/> following the priority order
/// documented in #517.
///
/// Pure: no I/O, no Process.Start, no GitHub network, no provider launch.
/// </summary>
internal static class WorkerNextActionAnalyzer
{
    /// <summary>
    /// Apply the selection rules and produce the canonical result.
    /// </summary>
    public static WorkerNextActionResult Analyze(
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> intentTargetPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> intentTargetIssues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(intentTargetPrs);
        ArgumentNullException.ThrowIfNull(intentTargetIssues);

        var warnings = new List<string>();

        // Policy invariant warnings: any PR carrying intent-pr-created is a
        // misplaced label (intent-pr-created belongs on the source ISSUE).
        foreach (var pr in intentTargetPrs)
        {
            var labels = LabelNames(pr.Labels);
            if (labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
            {
                warnings.Add(
                    $"label policy: PR #{pr.Number} carries '{WorkerNextActionConstants.Labels.IntentPrCreated}' which belongs on the source issue, not the PR. Excluded from selection.");
            }
        }

        // Priority 1: PR comment/update repair target.
        // Eligible: open PR with intent-target + intent-pr-request-update,
        // and NOT intent-pr-update-in-progress (already claimed) and NOT
        // intent-pr-created (misplaced — already warned above).
        var prRepair = intentTargetPrs
            .Where(pr =>
            {
                var labels = LabelNames(pr.Labels);
                return labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal)
                    && !labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal)
                    && !labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);
            })
            .OrderBy(pr => pr.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        if (prRepair is not null)
        {
            return new WorkerNextActionResult
            {
                Action = WorkerNextActionConstants.Actions.PrCommentFix,
                Repo = repo,
                Number = prRepair.Number,
                Url = prRepair.Url,
                Reason = "open PR has actionable repair feedback (intent-pr-request-update without intent-pr-update-in-progress)",
                RecommendedWorkflow = WorkerNextActionConstants.RecommendedWorkflows.PrCommentFix,
                Warnings = warnings,
                SourceClassification = WorkerNextActionConstants.SourceClassifications.RepairRequired,
            };
        }

        // G325: Priority 1.5 — active PR update lease. A PR carrying
        // `intent-pr-update-in-progress` is in flight under another
        // worker's lease. The selector previously fell through to
        // `none`, which masked the real state. Surface an explicit
        // `wait` action so operators / controllers see the held lease,
        // its target PR, and the recommended repair workflow without
        // any silent fallback. Stale-lease recovery remains an
        // explicit operator action (`intent-cli automation
        // pr-transition` etc.); this selector never releases a lease
        // on its own.
        var activeLease = intentTargetPrs
            .Where(pr =>
            {
                var labels = LabelNames(pr.Labels);
                return labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal);
            })
            .OrderBy(pr => string.IsNullOrEmpty(pr.UpdatedAt) ? pr.CreatedAt : pr.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        if (activeLease is not null)
        {
            var leaseLabels = LabelNames(activeLease.Labels);
            var requestUpdatePresent = leaseLabels.Contains(
                WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal);
            var reason = requestUpdatePresent
                ? "active PR update lease (intent-pr-update-in-progress + intent-pr-request-update); another worker is in flight"
                : "active PR update lease (intent-pr-update-in-progress); another worker is in flight";
            return new WorkerNextActionResult
            {
                Action = WorkerNextActionConstants.Actions.Wait,
                Repo = repo,
                Number = activeLease.Number,
                Url = activeLease.Url,
                Reason = reason,
                RecommendedWorkflow = WorkerNextActionConstants.RecommendedWorkflows.PrCommentFix,
                Warnings = warnings,
                SourceClassification = WorkerNextActionConstants.SourceClassifications.PrUpdateInProgress,
            };
        }

        // Priority 2: PR review target — intentionally not implemented for
        // child-side automation. Host-side review/next-slice loop owns
        // review selection per the parent-host contract; an implementation
        // worker calling `worker next-action` should not pick up review
        // work itself. This priority slot is documented for completeness;
        // when host-side ever delegates review here we add a third action
        // value, but for now the loop falls through to priority 3.

        // Priority 3: Issue-to-PR target.
        // Eligible: open issue with intent-target, NOT intent-issue-in-progress,
        // NOT intent-pr-created.
        var issueToPr = intentTargetIssues
            .Where(issue =>
            {
                var labels = LabelNames(issue.Labels);
                return !labels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal)
                    && !labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);
            })
            .OrderBy(issue => issue.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        if (issueToPr is not null)
        {
            return new WorkerNextActionResult
            {
                Action = WorkerNextActionConstants.Actions.IssueToPr,
                Repo = repo,
                Number = issueToPr.Number,
                Url = issueToPr.Url,
                Reason = "oldest open intent-target issue without in-progress or pr-created state",
                RecommendedWorkflow = WorkerNextActionConstants.RecommendedWorkflows.GhIssueToPr,
                Warnings = warnings,
                SourceClassification = WorkerNextActionConstants.SourceClassifications.ReadyToImplement,
            };
        }

        // Priority 4: no actionable target.
        return new WorkerNextActionResult
        {
            Action = WorkerNextActionConstants.Actions.None,
            Repo = repo,
            Reason = "no actionable coding automation target",
            Warnings = warnings,
        };
    }

    private static IReadOnlyCollection<string> LabelNames(IReadOnlyList<GitHubAutomationLabel>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(labels.Count);
        foreach (var label in labels)
        {
            if (!string.IsNullOrEmpty(label.Name))
            {
                names.Add(label.Name);
            }
        }
        return names;
    }
}
