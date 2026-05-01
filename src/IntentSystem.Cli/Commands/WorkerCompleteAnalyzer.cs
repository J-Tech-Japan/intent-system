namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211: Pure deterministic mapper for <c>intent-cli worker complete</c>.
/// Given the kind, the originating outcome (one of the eight G205
/// outcomes), and the issue/PR's current label set, computes the
/// completion transition (add / remove) plus stale-selection refusals
/// and policy warnings.
///
/// The transition mapping mirrors
/// <see cref="WorkerResultSummaryAnalyzer"/>'s recommended label
/// actions, but this analyzer adds the stale-state pre-check and the
/// "intent-pr-created is issue-only" defensive guard so the writer
/// boundary is enforced once, here.
///
/// Pure: no I/O, no Process.Start, no GitHub network, no provider
/// launch.
/// </summary>
internal static class WorkerCompleteAnalyzer
{
    /// <summary>
    /// Plan the completion transition for a given target/outcome.
    /// </summary>
    public static CompleteDecision Analyze(
        string kind,
        string outcome,
        IReadOnlyList<string> currentLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentNullException.ThrowIfNull(currentLabels);

        if (!string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal)
            && !string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal))
        {
            return Refuse($"unrecognized kind '{kind}'.",
                $"unrecognized kind '{kind}'.");
        }

        if (!WorkerResultSummaryAnalyzer.IsSupportedKindOutcome(MapKindForResultSummary(kind), outcome))
        {
            return Refuse(
                $"{WorkerClaimCompleteConstants.ErrorCodes.UnsupportedKindOutcome}: outcome '{outcome}' is not supported for kind '{kind}'.",
                $"Refusing to complete: outcome '{outcome}' not supported for kind '{kind}'.");
        }

        if (string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal))
        {
            return AnalyzeIssue(outcome, currentLabels);
        }
        return AnalyzePr(outcome, currentLabels);
    }

    private static CompleteDecision AnalyzeIssue(
        string outcome,
        IReadOnlyList<string> currentLabels)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var addLabels = new List<string>();
        var removeLabels = new List<string>();

        var hasInProgress = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal);
        var hasPrCreated = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);

        // Stale: an issue must have been claimed (intent-issue-in-progress
        // present) before completion is meaningful.
        if (!hasInProgress)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.CompleteNotClaimed}: issue does not carry '{WorkerNextActionConstants.Labels.IntentIssueInProgress}'.");
        }

        // Stale: pr-created already present means the issue was already
        // completed; refuse to re-add.
        if (hasPrCreated && string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.PrCreated, StringComparison.Ordinal))
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted}: issue already carries '{WorkerNextActionConstants.Labels.IntentPrCreated}'.");
        }

        if (errors.Count == 0)
        {
            // All terminal issue-to-pr outcomes release the in-progress claim.
            removeLabels.Add(WorkerNextActionConstants.Labels.IntentIssueInProgress);

            // pr-created success path also marks the issue as completed.
            if (string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.PrCreated, StringComparison.Ordinal))
            {
                addLabels.Add(WorkerNextActionConstants.Labels.IntentPrCreated);
                warnings.Add(
                    $"label policy: '{WorkerNextActionConstants.Labels.IntentPrCreated}' is being added to the source ISSUE, never to the PR.");
            }
        }

        var proceed = errors.Count == 0;
        return new CompleteDecision
        {
            Proceed = proceed,
            AddLabels = addLabels,
            RemoveLabels = removeLabels,
            Errors = errors,
            Warnings = warnings,
            Summary = proceed
                ? BuildIssueSummary(outcome, addLabels, removeLabels)
                : "Refusing to complete issue: stale or already-completed target.",
        };
    }

    private static CompleteDecision AnalyzePr(
        string outcome,
        IReadOnlyList<string> currentLabels)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var addLabels = new List<string>();
        var removeLabels = new List<string>();

        // Defensive policy guard: intent-pr-created must never be
        // applied to a PR. The mapping below never produces it for
        // kind=pr, but we surface a guard error if a future caller
        // tries to bypass via an unrecognized outcome path.
        // (No-op here in practice — the mapping enforces it.)

        var hasUpdateInProgress = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal);
        var hasRereviewReady = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrRereviewReady, StringComparer.Ordinal);
        var hasMisplacedPrCreated = currentLabels.Contains(
            WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);

        // Stale: a PR must have been claimed (intent-pr-update-in-progress
        // present) before completion is meaningful.
        if (!hasUpdateInProgress)
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.CompleteNotClaimed}: PR does not carry '{WorkerNextActionConstants.Labels.IntentPrUpdateInProgress}'.");
        }

        // Stale: rereview-ready already present means the PR's repair
        // was already completed; refuse to re-add for a successful
        // repair-pushed completion.
        if (hasRereviewReady
            && string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.RepairPushed, StringComparison.Ordinal))
        {
            errors.Add(
                $"{WorkerClaimCompleteConstants.ErrorCodes.CompleteAlreadyCompleted}: PR already carries '{WorkerNextActionConstants.Labels.IntentPrRereviewReady}'.");
        }

        if (hasMisplacedPrCreated)
        {
            warnings.Add(
                $"label policy: PR carries misplaced '{WorkerNextActionConstants.Labels.IntentPrCreated}' which belongs on the source issue, not the PR.");
        }

        if (errors.Count == 0)
        {
            // All terminal pr-comment-fix outcomes release the in-progress claim.
            removeLabels.Add(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress);

            // repair-pushed success path clears the old request-update state
            // and marks the PR as ready for re-review.
            if (string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.RepairPushed, StringComparison.Ordinal))
            {
                removeLabels.Add(WorkerNextActionConstants.Labels.IntentPrRequestUpdate);
                addLabels.Add(WorkerNextActionConstants.Labels.IntentPrRereviewReady);
            }
        }

        var proceed = errors.Count == 0;
        return new CompleteDecision
        {
            Proceed = proceed,
            AddLabels = addLabels,
            RemoveLabels = removeLabels,
            Errors = errors,
            Warnings = warnings,
            Summary = proceed
                ? BuildPrSummary(outcome, addLabels, removeLabels)
                : "Refusing to complete PR: stale or already-completed target.",
        };
    }

    private static CompleteDecision Refuse(string errorMessage, string summary)
    {
        return new CompleteDecision
        {
            Proceed = false,
            AddLabels = Array.Empty<string>(),
            RemoveLabels = Array.Empty<string>(),
            Errors = new[] { errorMessage },
            Warnings = Array.Empty<string>(),
            Summary = summary,
        };
    }

    private static string MapKindForResultSummary(string kind)
    {
        // worker complete uses the GhCli kind tokens ("issue" / "pr"),
        // while WorkerResultSummary uses workflow kind tokens
        // ("issue-to-pr" / "pr-comment-fix"). Map between the two so
        // the supported (kind, outcome) matrix can be reused without
        // duplication.
        return kind switch
        {
            GhCliGitHubLabelMutator.Kinds.Issue => WorkerResultSummaryConstants.Kinds.IssueToPr,
            GhCliGitHubLabelMutator.Kinds.Pr => WorkerResultSummaryConstants.Kinds.PrCommentFix,
            _ => kind,
        };
    }

    private static string BuildIssueSummary(
        string outcome,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels)
    {
        var addPart = addLabels.Count == 0 ? "(none)" : string.Join(", ", addLabels);
        var removePart = removeLabels.Count == 0 ? "(none)" : string.Join(", ", removeLabels);
        return $"Would add: {addPart}; remove: {removePart} on the issue (outcome '{outcome}').";
    }

    private static string BuildPrSummary(
        string outcome,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels)
    {
        var addPart = addLabels.Count == 0 ? "(none)" : string.Join(", ", addLabels);
        var removePart = removeLabels.Count == 0 ? "(none)" : string.Join(", ", removeLabels);
        return $"Would add: {addPart}; remove: {removePart} on the PR (outcome '{outcome}').";
    }

    /// <summary>
    /// G211: Pure data record returned by
    /// <see cref="WorkerCompleteAnalyzer.Analyze"/>. The command layer
    /// projects this into <see cref="WorkerCompleteResult"/>.
    /// </summary>
    internal sealed record CompleteDecision
    {
        public required bool Proceed { get; init; }
        public required IReadOnlyList<string> AddLabels { get; init; }
        public required IReadOnlyList<string> RemoveLabels { get; init; }
        public required IReadOnlyList<string> Errors { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
        public required string Summary { get; init; }
    }
}
