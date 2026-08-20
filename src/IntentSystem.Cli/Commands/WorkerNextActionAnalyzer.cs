using System.Text.RegularExpressions;

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
        IReadOnlyList<GitHubAutomationIssueCandidate> intentTargetIssues,
        IReadOnlyDictionary<int, ClaimOwnershipVerification>? issueClaims = null)
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
        // G392: a request-update PR is only a child-actionable `pr-comment-fix`
        // when it has a resolvable source issue (a GitHub closing reference or a
        // `Closes/Fixes/Resolves #N` in the body). This mirrors
        // `worker pr-comment-preflight`'s source-issue-missing gate: a PR with
        // `intent-pr-request-update` but no linked source issue is host-owned /
        // not a narrow child PR-branch repair, so the selector must not return it
        // as claimable work (the AIC #3648 next-action↔preflight contradiction).
        bool IsRequestUpdateCandidate(GitHubAutomationPrCandidate pr)
        {
            var labels = LabelNames(pr.Labels);
            return labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal)
                && !labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal)
                && !labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);
        }

        var prRepair = intentTargetPrs
            .Where(pr => IsRequestUpdateCandidate(pr) && HasResolvableSourceIssue(pr))
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
                MustCreatePr = false,
                AllowedTerminalOutcomes = WorkerNextActionConstants.TerminalOutcomes.PrCommentFixAllowed,
                ForbiddenTerminalOutcomes = WorkerNextActionConstants.TerminalOutcomes.PrCommentFixForbidden,
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
        // Eligible: open issue with intent-target, NOT intent-pr-created, and
        // either no in-progress label or a claim-registry observation proving
        // that the in-progress label is stale. The claim registry is the
        // authority: an active/unavailable claim remains a stop even when the
        // label is missing, while an unheld claim explicitly overrides the
        // stale lifecycle shadow.
        var claimBlockedIssues = intentTargetIssues
            .Where(issue =>
            {
                var labels = LabelNames(issue.Labels);
                if (!labels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal)
                    || issueClaims is null
                    || !issueClaims.TryGetValue(issue.Number, out var claim))
                {
                    return false;
                }

                return IsActiveOrUnavailableClaim(claim);
            })
            .OrderBy(issue => issue.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        var issueToPr = intentTargetIssues
            .Where(issue =>
            {
                var labels = LabelNames(issue.Labels);
                if (labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
                {
                    return false;
                }

                if (!labels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal))
                {
                    return true;
                }

                return issueClaims is not null
                    && issueClaims.TryGetValue(issue.Number, out var claim)
                    && IsUnheldClaim(claim);
            })
            .OrderBy(issue => issue.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        if (issueToPr is not null)
        {
            var selectedLabels = LabelNames(issueToPr.Labels);
            var staleLifecycleLabel = selectedLabels.Contains(
                WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal)
                && issueClaims is not null
                && issueClaims.TryGetValue(issueToPr.Number, out var selectedClaim)
                && IsUnheldClaim(selectedClaim);

            return new WorkerNextActionResult
            {
                Action = WorkerNextActionConstants.Actions.IssueToPr,
                Repo = repo,
                Number = issueToPr.Number,
                Url = issueToPr.Url,
                Reason = staleLifecycleLabel
                    ? "oldest open intent-target issue whose claim registry is unheld; the intent-issue-in-progress label is stale shadow state and next-action proceeds to worker claim/preflight"
                    : "oldest open intent-target issue without in-progress or pr-created state",
                RecommendedWorkflow = WorkerNextActionConstants.RecommendedWorkflows.GhIssueToPr,
                Warnings = warnings,
                SourceClassification = WorkerNextActionConstants.SourceClassifications.ReadyToImplement,
                MustCreatePr = true,
                AllowedTerminalOutcomes = WorkerNextActionConstants.TerminalOutcomes.IssueToPrAllowed,
                ForbiddenTerminalOutcomes = WorkerNextActionConstants.TerminalOutcomes.IssueToPrForbidden,
            };
        }

        if (claimBlockedIssues is not null)
        {
            return new WorkerNextActionResult
            {
                Action = WorkerNextActionConstants.Actions.Wait,
                Repo = repo,
                Number = claimBlockedIssues.Number,
                Url = claimBlockedIssues.Url,
                Reason = issueClaims![claimBlockedIssues.Number].Detail
                    + " claim registry is authoritative over the intent-issue-in-progress lifecycle label; active or unavailable claim evidence remains an ownership stop.",
                Warnings = warnings,
                SourceClassification = WorkerNextActionConstants.SourceClassifications.ClaimRefused,
            };
        }

        // Priority 3.5 (G392): surface a request-update PR that is NOT
        // child-actionable (carries intent-pr-request-update but has no
        // resolvable source issue) as a stable `wait` with a
        // request-update-pending-not-child-actionable classification, instead of
        // a bare `none`. This stops the child loop from re-selecting the same PR
        // as claimable `pr-comment-fix` every wake and tells the operator the
        // PR's feedback is host-owned (missing source issue → route to the
        // host/review/design lane, not a child PR-branch repair).
        var notChildActionable = intentTargetPrs
            .Where(pr => IsRequestUpdateCandidate(pr) && !HasResolvableSourceIssue(pr))
            .OrderBy(pr => pr.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        if (notChildActionable is not null)
        {
            return new WorkerNextActionResult
            {
                Action = WorkerNextActionConstants.Actions.Wait,
                Repo = repo,
                Number = notChildActionable.Number,
                Url = notChildActionable.Url,
                Reason = "PR carries intent-pr-request-update but has no resolvable source issue "
                    + "(no closing reference / no Closes/Fixes/Resolves #N in body); the requested update is "
                    + "not a narrow child PR-branch repair. Route to the host/review/design lane — the child "
                    + "implementation loop must not claim it.",
                RecommendedWorkflow = WorkerNextActionConstants.RecommendedWorkflows.PrCommentFix,
                Warnings = warnings,
                SourceClassification = WorkerNextActionConstants.SourceClassifications.RequestUpdateNotChildActionable,
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

    private static readonly Regex BodyClosesIssueRegex = new(
        @"(?i)\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+#\d+\b",
        RegexOptions.Compiled);

    /// <summary>
    /// G392: true when a PR has a resolvable source issue — a GitHub
    /// <c>closingIssuesReferences</c> entry or a <c>Closes/Fixes/Resolves #N</c>
    /// reference in the body. Aligns with <c>worker pr-comment-preflight</c>'s
    /// source-issue resolution so the selector and the preflight agree on
    /// child-actionability.
    /// </summary>
    private static bool HasResolvableSourceIssue(GitHubAutomationPrCandidate pr)
    {
        if (pr.ClosingIssuesReferences is { Count: > 0 })
        {
            return true;
        }

        return !string.IsNullOrEmpty(pr.Body) && BodyClosesIssueRegex.IsMatch(pr.Body);
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

    private static bool IsUnheldClaim(ClaimOwnershipVerification claim) =>
        claim.StoreConfigured
        && (claim.Status == ClaimOwnershipVerification.StatusUnheld
            || claim.Status == ClaimOwnershipVerification.StatusUnheldAvailable);

    private static bool IsActiveOrUnavailableClaim(ClaimOwnershipVerification claim) =>
        claim.StoreConfigured
        && (claim.Status == ClaimOwnershipVerification.StatusOwned
            || claim.Status == ClaimOwnershipVerification.StatusHeldByOtherTeam
            || claim.Status == ClaimOwnershipVerification.StatusTeamRequired
            || claim.Status == ClaimOwnershipVerification.StatusCanonicalUnavailable
            || claim.Status == ClaimOwnershipVerification.StatusInvalid);
}
