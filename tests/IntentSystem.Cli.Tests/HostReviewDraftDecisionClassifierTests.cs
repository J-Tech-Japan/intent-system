using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G376: pure unit tests for the draft-aware host review decision
/// classifier. Covers the Zero4Racer PR #203 shape (draft + review-ready
/// + no findings → promote-ready) and the fail-closed cases.
/// </summary>
public sealed class HostReviewDraftDecisionClassifierTests
{
    [Fact]
    public void NotDraft_IsNotApplicable()
    {
        var decision = HostReviewDraftDecisionClassifier.Classify(
            isDraft: false, reviewReady: true, hasFindings: false, operatorIntendedDraft: false);

        Assert.Equal(HostReviewDraftDecisionClassifier.Decisions.NotApplicable, decision.Decision);
        Assert.False(decision.IsPromoteReady);
        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public void Draft_ReviewReady_NoFindings_NotOperatorIntended_PromotesReady()
    {
        // The Zero4Racer PR #203 shape: closeout ready, guide ready, base
        // main, diff passed, no findings, draft state not operator-intended.
        var decision = HostReviewDraftDecisionClassifier.Classify(
            isDraft: true, reviewReady: true, hasFindings: false, operatorIntendedDraft: false);

        Assert.Equal(HostReviewDraftDecisionClassifier.Decisions.PromoteReady, decision.Decision);
        Assert.True(decision.IsPromoteReady);
        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public void Draft_OperatorIntended_IsBlocked_EvenWhenReviewReady()
    {
        var decision = HostReviewDraftDecisionClassifier.Classify(
            isDraft: true, reviewReady: true, hasFindings: false, operatorIntendedDraft: true);

        Assert.Equal(HostReviewDraftDecisionClassifier.Decisions.BlockedOperatorIntended, decision.Decision);
        Assert.True(decision.IsBlocked);
        Assert.False(decision.IsPromoteReady);
    }

    [Fact]
    public void Draft_WithFindings_RequestsUpdate()
    {
        var decision = HostReviewDraftDecisionClassifier.Classify(
            isDraft: true, reviewReady: false, hasFindings: true, operatorIntendedDraft: false);

        Assert.Equal(HostReviewDraftDecisionClassifier.Decisions.RequestUpdate, decision.Decision);
        Assert.True(decision.IsRequestUpdate);
        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public void Draft_ReadinessNotVerified_IsBlockedNeedsVerification()
    {
        // Backward-compatible default: no readiness signal supplied.
        var decision = HostReviewDraftDecisionClassifier.Classify(
            isDraft: true, reviewReady: false, hasFindings: false, operatorIntendedDraft: false);

        Assert.Equal(HostReviewDraftDecisionClassifier.Decisions.BlockedNeedsVerification, decision.Decision);
        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public void Draft_OperatorIntended_TakesPrecedence_OverFindings()
    {
        var decision = HostReviewDraftDecisionClassifier.Classify(
            isDraft: true, reviewReady: false, hasFindings: true, operatorIntendedDraft: true);

        Assert.Equal(HostReviewDraftDecisionClassifier.Decisions.BlockedOperatorIntended, decision.Decision);
    }
}
