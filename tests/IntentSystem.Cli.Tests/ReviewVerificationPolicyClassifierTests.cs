using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G383: pure tests for the visible/manual/runtime-gated verification
/// policy classifier — the three deterministic routes and the hard
/// no-false-runtime-claim guard.
/// </summary>
public sealed class ReviewVerificationPolicyClassifierTests
{
    [Fact]
    public void Classify_StandingPolicyWithVisibleEvidence_NoFalseClaim_Approves()
    {
        var decision = ReviewVerificationPolicyClassifier.Classify(
            standingPolicyEncoded: true,
            evidence: ReviewVerificationPolicyClassifier.Evidence.SourceMapping,
            wouldRequireFalseRuntimeClaim: false,
            implementationActionable: false);

        Assert.Equal(ReviewVerificationPolicyClassifier.Decisions.StandingPolicyApprove, decision.Decision);
        Assert.Equal(ReviewVerificationPolicyClassifier.Routes.ProceedApprove, decision.Route);
        Assert.False(decision.RecordHostGapOnce);
        Assert.False(decision.PostPrFeedback);
    }

    [Fact]
    public void Classify_WouldRequireFalseRuntimeClaim_NeverApproves_EvenWithStandingPolicy()
    {
        // The hard guard: a path that needs a false runtime/manual claim is
        // never approved. With the implementer able to add real evidence it
        // becomes an implementation finding routed to PR feedback.
        var decision = ReviewVerificationPolicyClassifier.Classify(
            standingPolicyEncoded: true,
            evidence: ReviewVerificationPolicyClassifier.Evidence.SourceMapping,
            wouldRequireFalseRuntimeClaim: true,
            implementationActionable: true);

        Assert.NotEqual(ReviewVerificationPolicyClassifier.Decisions.StandingPolicyApprove, decision.Decision);
        Assert.Equal(ReviewVerificationPolicyClassifier.Decisions.ImplementationFinding, decision.Decision);
        Assert.Equal(ReviewVerificationPolicyClassifier.Routes.PrFeedbackRequestUpdate, decision.Route);
        Assert.True(decision.PostPrFeedback);
    }

    [Fact]
    public void Classify_NoStandingPolicy_ImplementationActionable_IsImplementationFinding_RoutedToPrFeedback()
    {
        var decision = ReviewVerificationPolicyClassifier.Classify(
            standingPolicyEncoded: false,
            evidence: ReviewVerificationPolicyClassifier.Evidence.DocumentedObservation,
            wouldRequireFalseRuntimeClaim: false,
            implementationActionable: true);

        Assert.Equal(ReviewVerificationPolicyClassifier.Decisions.ImplementationFinding, decision.Decision);
        Assert.Equal(ReviewVerificationPolicyClassifier.Routes.PrFeedbackRequestUpdate, decision.Route);
        Assert.True(decision.PostPrFeedback);
        Assert.False(decision.RecordHostGapOnce);
    }

    [Fact]
    public void Classify_NoStandingPolicy_NotActionable_IsReviewPolicyGap_RecordedOnce_NotOnPr()
    {
        var decision = ReviewVerificationPolicyClassifier.Classify(
            standingPolicyEncoded: false,
            evidence: ReviewVerificationPolicyClassifier.Evidence.None,
            wouldRequireFalseRuntimeClaim: false,
            implementationActionable: false);

        Assert.Equal(ReviewVerificationPolicyClassifier.Decisions.ReviewPolicyGap, decision.Decision);
        Assert.Equal(ReviewVerificationPolicyClassifier.Routes.HostDurableSignalOnce, decision.Route);
        Assert.True(decision.RecordHostGapOnce);
        // A host-policy gap must NOT be posted on the child PR as an implementer request.
        Assert.False(decision.PostPrFeedback);
    }

    [Fact]
    public void Classify_StandingPolicyButEvidenceNone_IsNotApproved()
    {
        // A standing policy still needs acceptable visible evidence; with
        // none and no actionable implementer fix, it is a host policy gap.
        var decision = ReviewVerificationPolicyClassifier.Classify(
            standingPolicyEncoded: true,
            evidence: ReviewVerificationPolicyClassifier.Evidence.None,
            wouldRequireFalseRuntimeClaim: false,
            implementationActionable: false);

        Assert.NotEqual(ReviewVerificationPolicyClassifier.Decisions.StandingPolicyApprove, decision.Decision);
        Assert.Equal(ReviewVerificationPolicyClassifier.Decisions.ReviewPolicyGap, decision.Decision);
    }
}
