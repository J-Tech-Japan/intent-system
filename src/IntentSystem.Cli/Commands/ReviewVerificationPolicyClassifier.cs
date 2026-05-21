namespace IntentSystem.Cli.Commands;

/// <summary>
/// G383: pure classifier that keeps a host review loop from repeatedly
/// asking the operator a standing A/B/C policy question when a PR is
/// blocked only by a visible / manual / runtime-gated verification
/// acceptance criterion. The review loop is NOT the product-owner
/// interview loop, so for such an AC it must deterministically pick one
/// of three routes instead of polling-and-asking each wake:
///
/// - <see cref="Decisions.StandingPolicyApprove"/>: an encoded standing
///   norm permits approval on source-mapping + documented observation
///   (and the summary never falsely claims runtime/manual verification
///   was executed) — apply it and proceed to approval/merge/closeout.
/// - <see cref="Decisions.ImplementationFinding"/>: the gap is
///   implementation-actionable — the implementer can add the missing
///   evidence on the PR branch. Route it as an intent-cli-managed PR
///   feedback comment + <c>request-update</c>, NOT a chat question.
/// - <see cref="Decisions.ReviewPolicyGap"/>: the gap is host-policy /
///   design-owned — record it ONCE as a durable host clarification /
///   signal (mark it pending) so later wakes do not re-ask. Never post a
///   host-policy gap as an implementer request on the child PR.
///
/// Hard guard: a path that would require falsely claiming runtime/manual
/// verification was performed is NEVER approved, even when a standing
/// policy is encoded.
/// </summary>
internal static class ReviewVerificationPolicyClassifier
{
    public static class Decisions
    {
        public const string StandingPolicyApprove = "standing-policy-approve";
        public const string ImplementationFinding = "implementation-finding";
        public const string ReviewPolicyGap = "review-policy-gap";
    }

    public static class Routes
    {
        /// <summary>Apply the standing norm and continue approval/merge/closeout.</summary>
        public const string ProceedApprove = "proceed-approve";

        /// <summary>Leave an actionable PR comment and apply request-update via intent-cli.</summary>
        public const string PrFeedbackRequestUpdate = "pr-feedback-request-update";

        /// <summary>Record a durable host clarification/signal once; do not re-ask, do not post on the child PR.</summary>
        public const string HostDurableSignalOnce = "host-durable-signal-once";
    }

    public static class Evidence
    {
        public const string SourceMapping = "source-mapping";
        public const string DocumentedObservation = "documented-observation";
        public const string StaticScreenshot = "static-screenshot";
        public const string None = "none";
    }

    private static bool IsAcceptableVisibleEvidence(string evidence) =>
        string.Equals(evidence, Evidence.SourceMapping, StringComparison.Ordinal)
        || string.Equals(evidence, Evidence.DocumentedObservation, StringComparison.Ordinal)
        || string.Equals(evidence, Evidence.StaticScreenshot, StringComparison.Ordinal);

    /// <summary>
    /// Classify a visible/manual/runtime-gated verification AC situation.
    /// </summary>
    /// <param name="standingPolicyEncoded">A wave-wide / repo standing norm for visible-verification ACs is encoded.</param>
    /// <param name="evidence">The strongest available review evidence (see <see cref="Evidence"/>).</param>
    /// <param name="wouldRequireFalseRuntimeClaim">Approving would require claiming runtime/manual verification was actually executed (never allowed).</param>
    /// <param name="implementationActionable">The implementer can add the missing evidence on the PR branch.</param>
    public static ReviewVerificationPolicyDecision Classify(
        bool standingPolicyEncoded,
        string evidence,
        bool wouldRequireFalseRuntimeClaim,
        bool implementationActionable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        var normalizedEvidence = evidence.Trim().ToLowerInvariant();

        // Hard guard: never approve a path that would require falsely
        // claiming runtime/manual verification was executed. Fall through
        // to an honest route (implementation finding, else policy gap).
        if (!wouldRequireFalseRuntimeClaim
            && standingPolicyEncoded
            && IsAcceptableVisibleEvidence(normalizedEvidence))
        {
            return new ReviewVerificationPolicyDecision
            {
                Decision = Decisions.StandingPolicyApprove,
                Route = Routes.ProceedApprove,
                RecordHostGapOnce = false,
                PostPrFeedback = false,
                Reason = "An encoded standing policy permits approval on visible evidence (" + normalizedEvidence
                    + ") with no false runtime/manual-verification claim; apply it and continue approval/merge/closeout. The summary must still state exactly what was verified and what was NOT run.",
            };
        }

        if (implementationActionable)
        {
            return new ReviewVerificationPolicyDecision
            {
                Decision = Decisions.ImplementationFinding,
                Route = Routes.PrFeedbackRequestUpdate,
                RecordHostGapOnce = false,
                PostPrFeedback = true,
                Reason = wouldRequireFalseRuntimeClaim
                    ? "Approval would require a false runtime/manual-verification claim, but the implementer CAN add real evidence on the PR branch; leave an actionable PR comment and apply request-update via intent-cli — do not ask in chat and never fake the verification."
                    : "No standing policy covers this visible-verification AC, but the implementer CAN add the missing evidence on the PR branch; leave an actionable PR comment and apply request-update via intent-cli — do not ask in chat.",
            };
        }

        return new ReviewVerificationPolicyDecision
        {
            Decision = Decisions.ReviewPolicyGap,
            Route = Routes.HostDurableSignalOnce,
            RecordHostGapOnce = true,
            PostPrFeedback = false,
            Reason = "The missing piece is a host-owned policy/design decision, not a PR implementation finding; record it ONCE as a durable host clarification/signal and mark it pending so later wakes do not re-ask the operator. Do not post it on the child PR as an implementer request.",
        };
    }
}

/// <summary>G383: the verdict from <see cref="ReviewVerificationPolicyClassifier"/>.</summary>
internal sealed record ReviewVerificationPolicyDecision
{
    public required string Decision { get; init; }
    public required string Route { get; init; }
    public required bool RecordHostGapOnce { get; init; }
    public required bool PostPrFeedback { get; init; }
    public required string Reason { get; init; }
}
