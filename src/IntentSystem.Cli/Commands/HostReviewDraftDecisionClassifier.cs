namespace IntentSystem.Cli.Commands;

/// <summary>
/// G376: pure decision classifier that makes host review draft-aware
/// without blocking valid approvals. The pre-G376 host loop treated any
/// draft PR as an unconditional <c>draft-merge-blocked</c> and released
/// the review lease — even when the PR was otherwise fully review-ready
/// (closeout-plan ready, guide review ready, base on policy, diff check
/// passed, no implementation findings). That left review-ready draft PRs
/// in a repeated lease-release loop with no outcome.
///
/// This classifier distinguishes the cases so the host loop can converge:
/// - <see cref="Decisions.NotApplicable"/>: PR is not draft — standard
///   review flow applies, no draft handling.
/// - <see cref="Decisions.BlockedOperatorIntended"/>: the draft state is
///   explicitly operator-intended; stay fail-closed (release lease,
///   surface the gap) regardless of review-readiness.
/// - <see cref="Decisions.RequestUpdate"/>: the PR has implementation
///   findings, so it should be marked ready only after the worker
///   addresses them — request a worker update with an actionable reason.
/// - <see cref="Decisions.PromoteReady"/>: the draft is not
///   operator-intended and the review passed with no findings — mark the
///   PR ready and continue approval/merge/closeout instead of releasing
///   the lease.
/// - <see cref="Decisions.BlockedNeedsVerification"/>: draft state with
///   review-readiness not positively verified — fail-closed (this is the
///   backward-compatible default for callers that do not supply the
///   readiness signals).
///
/// Pure: no I/O, no GitHub, no process launch.
/// </summary>
internal static class HostReviewDraftDecisionClassifier
{
    public static class Decisions
    {
        public const string NotApplicable = "not-applicable";
        public const string PromoteReady = "promote-ready";
        public const string RequestUpdate = "request-update";
        public const string BlockedOperatorIntended = "blocked-operator-intended";
        public const string BlockedNeedsVerification = "blocked-needs-verification";
    }

    /// <summary>
    /// Classify the draft review decision.
    /// </summary>
    /// <param name="isDraft">Whether the PR is currently a GitHub draft.</param>
    /// <param name="reviewReady">
    /// Whether the host positively verified the PR is review-ready
    /// (closeout-plan ready AND guide review ready AND base on policy AND
    /// diff check passed AND no implementation findings).
    /// </param>
    /// <param name="hasFindings">
    /// Whether host review surfaced implementation findings that require a
    /// worker update before the PR can be marked ready.
    /// </param>
    /// <param name="operatorIntendedDraft">
    /// Whether the draft state is explicitly operator-intended (must be an
    /// explicit signal, never inferred from draft state alone).
    /// </param>
    public static HostReviewDraftDecision Classify(
        bool isDraft,
        bool reviewReady,
        bool hasFindings,
        bool operatorIntendedDraft)
    {
        if (!isDraft)
        {
            return new HostReviewDraftDecision(
                Decisions.NotApplicable,
                "PR is not draft; the standard host review flow applies.");
        }

        // Operator intent is authoritative and fail-closed: an explicitly
        // operator-held draft is never auto-promoted, even when the review
        // would otherwise pass.
        if (operatorIntendedDraft)
        {
            return new HostReviewDraftDecision(
                Decisions.BlockedOperatorIntended,
                "Draft is operator-intended; host approval/merge/closeout must not proceed. Release the review lease and surface the gap to the operator.");
        }

        // Implementation findings mean the PR is not ready to promote; the
        // worker must address them first, then mark the PR ready.
        if (hasFindings)
        {
            return new HostReviewDraftDecision(
                Decisions.RequestUpdate,
                "Draft PR has implementation findings; request a worker update and have the worker mark the PR ready for review once addressed.");
        }

        // Not operator-intended, review verified ready, no findings: the
        // only thing blocking merge is the draft flag itself — mark ready
        // and continue rather than releasing the lease without outcome.
        if (reviewReady)
        {
            return new HostReviewDraftDecision(
                Decisions.PromoteReady,
                "Draft PR is otherwise review-ready with no findings and the draft state is not operator-intended; mark it ready for review and continue approval/merge/closeout.");
        }

        // Draft with review-readiness not positively verified: stay
        // fail-closed. This is the backward-compatible default.
        return new HostReviewDraftDecision(
            Decisions.BlockedNeedsVerification,
            "Draft PR review-readiness is not verified; host approval/merge/closeout must not proceed. Release the review lease and surface the gap until readiness is confirmed.");
    }
}

/// <summary>
/// G376: the outcome of <see cref="HostReviewDraftDecisionClassifier.Classify"/>.
/// </summary>
internal sealed record HostReviewDraftDecision(string Decision, string Reason)
{
    public bool IsPromoteReady =>
        string.Equals(Decision, HostReviewDraftDecisionClassifier.Decisions.PromoteReady, StringComparison.Ordinal);

    public bool IsRequestUpdate =>
        string.Equals(Decision, HostReviewDraftDecisionClassifier.Decisions.RequestUpdate, StringComparison.Ordinal);

    public bool IsBlocked =>
        string.Equals(Decision, HostReviewDraftDecisionClassifier.Decisions.BlockedOperatorIntended, StringComparison.Ordinal)
        || string.Equals(Decision, HostReviewDraftDecisionClassifier.Decisions.BlockedNeedsVerification, StringComparison.Ordinal);
}
