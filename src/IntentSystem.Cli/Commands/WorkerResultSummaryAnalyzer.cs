namespace IntentSystem.Cli.Commands;

/// <summary>
/// G205: Pure deterministic mapper for <c>intent-cli worker result-summary</c>.
/// Given a (kind, outcome, repo, issue, pr) tuple, returns the canonical
/// <see cref="WorkerResultSummaryResult"/> with derived status, advisory
/// label-cleanup actions, and any policy warnings. No I/O, no Process.Start,
/// no GitHub network, no provider launch — pure data in, pure data out.
/// </summary>
internal static class WorkerResultSummaryAnalyzer
{
    /// <summary>
    /// Build the canonical result for a recognized (kind, outcome) pair.
    /// Caller is expected to have already validated <paramref name="kind"/>
    /// and <paramref name="outcome"/> against the allowlists in
    /// <see cref="WorkerResultSummaryConstants"/>; passing an unrecognized
    /// pair throws <see cref="ArgumentException"/>.
    /// </summary>
    public static WorkerResultSummaryResult Analyze(
        string kind,
        string outcome,
        string repo,
        int? issue,
        int? pr,
        bool? prDraft = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        if (!WorkerResultSummaryConstants.AllKinds.Contains(kind, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"unrecognized kind '{kind}'. Supported: {string.Join(", ", WorkerResultSummaryConstants.AllKinds)}.",
                nameof(kind));
        }

        if (!WorkerResultSummaryConstants.AllOutcomes.Contains(outcome, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"unrecognized outcome '{outcome}'. Supported: {string.Join(", ", WorkerResultSummaryConstants.AllOutcomes)}.",
                nameof(outcome));
        }

        if (!IsSupportedKindOutcome(kind, outcome))
        {
            throw new ArgumentException(
                $"outcome '{outcome}' is not supported for kind '{kind}'.",
                nameof(outcome));
        }

        var status = DeriveStatus(outcome);
        var labelActions = DeriveLabelActions(kind, outcome);
        var warnings = DeriveWarnings(kind, outcome, issue, pr, prDraft);
        var summary = DeriveSummaryText(kind, outcome, repo, issue, pr, prDraft);

        return new WorkerResultSummaryResult
        {
            Kind = kind,
            Repo = repo,
            Issue = issue,
            Pr = pr,
            Outcome = outcome,
            Status = status,
            RecommendedLabelActions = labelActions,
            Warnings = warnings,
            Summary = summary,
            PrDraft = prDraft,
        };
    }

    /// <summary>
    /// G205: which (kind, outcome) combinations are accepted as well-formed.
    /// Public for the focused test that asserts every documented pair is
    /// supported and only those — locks the matrix against accidental
    /// regression. Some outcomes only make sense for one kind (e.g.
    /// <c>repair-pushed</c> belongs to <c>pr-comment-fix</c>; <c>pr-created</c>
    /// belongs to <c>issue-to-pr</c>). Outcomes that can occur in either
    /// context (clarification, failed, label-cleanup-required, already-resolved)
    /// are accepted for both.
    /// </summary>
    internal static bool IsSupportedKindOutcome(string kind, string outcome)
    {
        return (kind, outcome) switch
        {
            // issue-to-pr kind
            (WorkerResultSummaryConstants.Kinds.IssueToPr,
                WorkerResultSummaryConstants.Outcomes.PrCreated) => true,
            (WorkerResultSummaryConstants.Kinds.IssueToPr,
                WorkerResultSummaryConstants.Outcomes.DeclinedContractIncomplete) => true,
            (WorkerResultSummaryConstants.Kinds.IssueToPr,
                WorkerResultSummaryConstants.Outcomes.AlreadyResolved) => true,

            // pr-comment-fix kind
            (WorkerResultSummaryConstants.Kinds.PrCommentFix,
                WorkerResultSummaryConstants.Outcomes.RepairPushed) => true,
            (WorkerResultSummaryConstants.Kinds.PrCommentFix,
                WorkerResultSummaryConstants.Outcomes.NoActionableComments) => true,
            (WorkerResultSummaryConstants.Kinds.PrCommentFix,
                WorkerResultSummaryConstants.Outcomes.AlreadyResolved) => true,

            // shared
            (_, WorkerResultSummaryConstants.Outcomes.ClarificationRequired) => true,
            (_, WorkerResultSummaryConstants.Outcomes.Failed) => true,
            (_, WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired) => true,

            _ => false,
        };
    }

    /// <summary>
    /// G205: outcome → status. Used both as an internal mapping and exposed
    /// for the focused test that asserts every outcome maps to a known
    /// status.
    /// </summary>
    internal static string DeriveStatus(string outcome) => outcome switch
    {
        WorkerResultSummaryConstants.Outcomes.PrCreated => WorkerResultSummaryConstants.Statuses.Completed,
        WorkerResultSummaryConstants.Outcomes.RepairPushed => WorkerResultSummaryConstants.Statuses.Completed,
        WorkerResultSummaryConstants.Outcomes.AlreadyResolved => WorkerResultSummaryConstants.Statuses.Completed,
        WorkerResultSummaryConstants.Outcomes.NoActionableComments => WorkerResultSummaryConstants.Statuses.Completed,
        WorkerResultSummaryConstants.Outcomes.DeclinedContractIncomplete => WorkerResultSummaryConstants.Statuses.Declined,
        WorkerResultSummaryConstants.Outcomes.ClarificationRequired => WorkerResultSummaryConstants.Statuses.ClarificationRequired,
        WorkerResultSummaryConstants.Outcomes.Failed => WorkerResultSummaryConstants.Statuses.Failed,
        WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired => WorkerResultSummaryConstants.Statuses.LabelCleanupRequired,
        _ => throw new ArgumentException($"unrecognized outcome '{outcome}'", nameof(outcome)),
    };

    private static IReadOnlyList<WorkerResultSummaryLabelAction> DeriveLabelActions(
        string kind,
        string outcome)
    {
        var actions = new List<WorkerResultSummaryLabelAction>();

        // Common cleanup: when issue work ended (any terminal outcome on the
        // issue-to-pr kind), recommend removing intent-issue-in-progress.
        if (kind == WorkerResultSummaryConstants.Kinds.IssueToPr)
        {
            actions.Add(MakeAction(
                WorkerResultSummaryConstants.LabelActionVerbs.Remove,
                WorkerResultSummaryConstants.LabelActionTargets.Issue,
                WorkerResultSummaryConstants.Labels.IntentIssueInProgress));
        }

        // Common cleanup: when PR-comment-fix work ended on a non-cleanup
        // terminal outcome, the worker's claim label on the PR
        // (intent-pr-update-in-progress) should be released. The convergent
        // rereview outcomes (repair-pushed, already-resolved) handle that
        // release via the explicit swap below, so they are excluded here to
        // avoid a duplicate remove action (G372).
        if (kind == WorkerResultSummaryConstants.Kinds.PrCommentFix
            && !IsConvergentRereviewOutcome(outcome))
        {
            actions.Add(MakeAction(
                WorkerResultSummaryConstants.LabelActionVerbs.Remove,
                WorkerResultSummaryConstants.LabelActionTargets.Pr,
                WorkerResultSummaryConstants.Labels.IntentPrUpdateInProgress));
        }

        // Outcome-specific advisory actions.
        switch (outcome)
        {
            case WorkerResultSummaryConstants.Outcomes.PrCreated:
                // intent-pr-created belongs on the source ISSUE per existing
                // policy (see AcceptedBaseline in the issue body).
                actions.Add(MakeAction(
                    WorkerResultSummaryConstants.LabelActionVerbs.Add,
                    WorkerResultSummaryConstants.LabelActionTargets.Issue,
                    WorkerResultSummaryConstants.Labels.IntentPrCreated));
                break;

            case WorkerResultSummaryConstants.Outcomes.RepairPushed:
            // G372: already-resolved converges identically to repair-pushed
            // — the requested fix is on the branch, so the PR is handed back
            // to host re-review. Sharing this branch keeps the recommended
            // label actions aligned with the worker-complete transition.
            case WorkerResultSummaryConstants.Outcomes.AlreadyResolved
                when kind == WorkerResultSummaryConstants.Kinds.PrCommentFix:
                // The PR claim label and the old request-update state both
                // clear as the PR becomes rereview-ready. Keep the primary
                // in-progress -> rereview-ready transition as a swap, and
                // make the stale request-update cleanup explicit.
                actions.Add(new WorkerResultSummaryLabelAction
                {
                    Action = WorkerResultSummaryConstants.LabelActionVerbs.Swap,
                    Target = WorkerResultSummaryConstants.LabelActionTargets.Pr,
                    Label = $"{WorkerResultSummaryConstants.Labels.IntentPrUpdateInProgress}->" +
                            WorkerResultSummaryConstants.Labels.IntentPrRereviewReady,
                });
                actions.Add(MakeAction(
                    WorkerResultSummaryConstants.LabelActionVerbs.Remove,
                    WorkerResultSummaryConstants.LabelActionTargets.Pr,
                    WorkerResultSummaryConstants.Labels.IntentPrRequestUpdate));
                break;

            case WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired:
                // No specific label is recommended here — the action is
                // "operator must inspect and clean up". The status itself
                // signals this; the warnings list will spell it out.
                break;
        }

        return actions;
    }

    private static IReadOnlyList<string> DeriveWarnings(
        string kind,
        string outcome,
        int? issue,
        int? pr,
        bool? prDraft)
    {
        var warnings = new List<string>();

        // Policy invariant: intent-pr-created belongs on the source ISSUE,
        // never on the PR. Surface that as a warning whenever the outcome
        // could conceivably be misapplied to a PR position.
        if (outcome == WorkerResultSummaryConstants.Outcomes.PrCreated)
        {
            warnings.Add(
                $"label policy: '{WorkerResultSummaryConstants.Labels.IntentPrCreated}' must be added to the source issue, not the PR.");
        }

        if (outcome == WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired)
        {
            warnings.Add(
                "label-cleanup-required: an in-progress label or a stale review-state label was left behind; operator inspection required.");
        }

        // G296: a draft PR completing the issue-to-pr outcome breaks host
        // review/merge — host review can approve, but GitHub will reject the
        // merge with `Pull Request is still a draft`. Always surface this
        // explicitly so the host loop can ready-for-review or stop the
        // closeout. Repaired-via-pr-comment-fix on a still-draft PR is also
        // host-merge-blocked; surface the same warning for that path.
        // G372: already-resolved on a pr-comment-fix now converges to
        // rereview-ready (like repair-pushed), so a still-draft PR carries
        // the same host-merge-blocking risk and must surface the warning.
        if (prDraft == true
            && (outcome == WorkerResultSummaryConstants.Outcomes.PrCreated
                || outcome == WorkerResultSummaryConstants.Outcomes.RepairPushed
                || (outcome == WorkerResultSummaryConstants.Outcomes.AlreadyResolved
                    && kind == WorkerResultSummaryConstants.Kinds.PrCommentFix)))
        {
            warnings.Add(
                "draft PR: automation-created PRs default to ready-for-review (G296). A draft PR will block host merge — ready it for review or document why draft is intentional.");
        }

        // Coherence warnings: an issue-to-pr outcome should usually have an
        // issue number; a pr-comment-fix outcome should have a pr number.
        if (kind == WorkerResultSummaryConstants.Kinds.IssueToPr && issue is null)
        {
            warnings.Add("kind 'issue-to-pr' typically requires --issue; none was provided.");
        }

        if (kind == WorkerResultSummaryConstants.Kinds.PrCommentFix && pr is null)
        {
            warnings.Add("kind 'pr-comment-fix' typically requires --pr; none was provided.");
        }

        return warnings;
    }

    private static string DeriveSummaryText(
        string kind,
        string outcome,
        string repo,
        int? issue,
        int? pr,
        bool? prDraft)
    {
        var draftSuffix = prDraft switch
        {
            true => " (draft)",
            false => " (ready for review)",
            _ => string.Empty
        };

        return outcome switch
        {
            WorkerResultSummaryConstants.Outcomes.PrCreated =>
                pr is { } prNum
                    ? $"PR #{prNum} created for {Identifier(repo, issue, "#")}{draftSuffix}."
                    : $"PR created for {Identifier(repo, issue, "#")}{draftSuffix}.",

            WorkerResultSummaryConstants.Outcomes.RepairPushed =>
                $"Repair commit pushed to {Identifier(repo, pr, "#")}{draftSuffix}.",

            WorkerResultSummaryConstants.Outcomes.DeclinedContractIncomplete =>
                $"Declined before implementation — issue body was not a sufficient standalone contract ({Identifier(repo, issue, "#")}).",

            WorkerResultSummaryConstants.Outcomes.ClarificationRequired =>
                $"Clarification required — worker stopped without changing scope ({Identifier(repo, issue ?? pr, "#")}).",

            WorkerResultSummaryConstants.Outcomes.AlreadyResolved =>
                kind == WorkerResultSummaryConstants.Kinds.PrCommentFix
                    ? $"Requested fix already present on the PR branch — marked ready for re-review{draftSuffix} ({Identifier(repo, pr, "#")})."
                    : $"Already resolved — no further action needed ({Identifier(repo, issue ?? pr, "#")}).",

            WorkerResultSummaryConstants.Outcomes.NoActionableComments =>
                $"No actionable review comments remain on PR ({Identifier(repo, pr, "#")}).",

            WorkerResultSummaryConstants.Outcomes.Failed =>
                $"Worker failed — recommended labels may need cleanup ({Identifier(repo, issue ?? pr, "#")}).",

            WorkerResultSummaryConstants.Outcomes.LabelCleanupRequired =>
                $"Label cleanup required ({Identifier(repo, issue ?? pr, "#")}).",

            _ => $"{kind} run completed: {outcome}.",
        };
    }

    private static string Identifier(string repo, int? number, string sep) =>
        number is { } n ? $"{repo}{sep}{n}" : repo;

    private static WorkerResultSummaryLabelAction MakeAction(string action, string target, string label) =>
        new()
        {
            Action = action,
            Target = target,
            Label = label,
        };

    /// <summary>
    /// G372: pr-comment-fix outcomes that converge a PR to the
    /// host-reviewable <c>intent-pr-rereview-ready</c> state. Both
    /// <c>repair-pushed</c> and <c>already-resolved</c> hand the PR back to
    /// the host review loop and clear the child-actionable update labels;
    /// keeping them in one set keeps the recommended label actions aligned
    /// with <see cref="WorkerCompleteAnalyzer"/>.
    /// </summary>
    private static bool IsConvergentRereviewOutcome(string outcome) =>
        string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.RepairPushed, StringComparison.Ordinal)
        || string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.AlreadyResolved, StringComparison.Ordinal);
}
