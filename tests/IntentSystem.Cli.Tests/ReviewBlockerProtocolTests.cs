using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G394: unit tests for the pure review-blocker classifier — current-PR
/// blocker vs follow-up capability vs host-metadata blocker, including the
/// Zero4Racer PR #406 / Z4R-G201 canonical-flow evidence case.
/// </summary>
public sealed class ReviewBlockerProtocolTests
{
    [Fact]
    public void Classify_HostMetadata_NeverBecomesPrComment()
    {
        // Host-metadata precedence: even if it also "fails an AC", a
        // host-metadata blocker is never an implementation-PR comment (G287).
        var classification = ReviewBlockerProtocol.Classify(new ReviewBlockerProtocol.Signal(
            FailsCurrentPrAcceptanceCriterion: true,
            TargetsHostMetadata: true,
            RequiresBroaderCapability: false,
            ImplementerCanFixOnPrBranch: false));

        Assert.Equal(ReviewBlockerProtocol.Category.HostMetadataBlocker, classification.Category);
        Assert.True(classification.MustNotBePrComment);
        Assert.False(classification.RequiresDurablePrComment);
        Assert.False(classification.RequiresFollowUpIssue);
        Assert.Equal(ReviewBlockerProtocol.OutcomeHostArtifactRepairRequired, classification.RecommendedOutcome);
    }

    [Fact]
    public void Classify_Zero4RacerCanonicalFlowBlocker_IsCurrentPrAcBlocker_ClarificationRequired_WithFollowUp()
    {
        // Zero4Racer PR #406 / Z4R-G201 AC#1: canonical Opening -> RaceSelection
        // -> Race flow evidence could not be captured (scene-override only);
        // operator/runtime-gated AND rooted in a broader capability gap
        // (synthetic touch automation).
        var classification = ReviewBlockerProtocol.Classify(new ReviewBlockerProtocol.Signal(
            FailsCurrentPrAcceptanceCriterion: true,
            TargetsHostMetadata: false,
            RequiresBroaderCapability: true,
            ImplementerCanFixOnPrBranch: false));

        Assert.Equal(ReviewBlockerProtocol.Category.CurrentPrAcBlocker, classification.Category);
        Assert.True(classification.RequiresDurablePrComment);
        Assert.False(classification.MustNotBePrComment);
        Assert.True(classification.RequiresFollowUpIssue);
        Assert.Equal(ReviewBlockerProtocol.OutcomeClarificationRequired, classification.RecommendedOutcome);
    }

    [Fact]
    public void Classify_ImplementerFixableAcFailure_IsCurrentPrAcBlocker_RequestUpdate_NoFollowUp()
    {
        var classification = ReviewBlockerProtocol.Classify(new ReviewBlockerProtocol.Signal(
            FailsCurrentPrAcceptanceCriterion: true,
            TargetsHostMetadata: false,
            RequiresBroaderCapability: false,
            ImplementerCanFixOnPrBranch: true));

        Assert.Equal(ReviewBlockerProtocol.Category.CurrentPrAcBlocker, classification.Category);
        Assert.True(classification.RequiresDurablePrComment);
        Assert.False(classification.RequiresFollowUpIssue);
        Assert.Equal(ReviewBlockerProtocol.OutcomeRequestUpdate, classification.RecommendedOutcome);
    }

    [Fact]
    public void Classify_BroaderCapabilityNotBlockingCurrentAc_IsFollowUpCapabilityGap()
    {
        var classification = ReviewBlockerProtocol.Classify(new ReviewBlockerProtocol.Signal(
            FailsCurrentPrAcceptanceCriterion: false,
            TargetsHostMetadata: false,
            RequiresBroaderCapability: true,
            ImplementerCanFixOnPrBranch: false));

        Assert.Equal(ReviewBlockerProtocol.Category.FollowUpCapabilityGap, classification.Category);
        Assert.True(classification.RequiresFollowUpIssue);
        Assert.False(classification.RequiresDurablePrComment);
        Assert.False(classification.MustNotBePrComment);
        Assert.Equal(ReviewBlockerProtocol.OutcomeNone, classification.RecommendedOutcome);
    }

    [Fact]
    public void Classify_NoBlockerFlags_IsNone()
    {
        var classification = ReviewBlockerProtocol.Classify(new ReviewBlockerProtocol.Signal(
            FailsCurrentPrAcceptanceCriterion: false,
            TargetsHostMetadata: false,
            RequiresBroaderCapability: false,
            ImplementerCanFixOnPrBranch: false));

        Assert.Equal(ReviewBlockerProtocol.Category.None, classification.Category);
        Assert.False(classification.RequiresDurablePrComment);
        Assert.False(classification.RequiresFollowUpIssue);
        Assert.Equal(ReviewBlockerProtocol.OutcomeNone, classification.RecommendedOutcome);
    }

    [Fact]
    public void CanonicalScenarios_CoverAllThreeBlockerBuckets_Deterministically()
    {
        var categories = ReviewBlockerProtocol.CanonicalScenarios
            .Select(scenario => ReviewBlockerProtocol.Classify(scenario.Signal).Category)
            .ToArray();

        Assert.Contains(ReviewBlockerProtocol.Category.CurrentPrAcBlocker, categories);
        Assert.Contains(ReviewBlockerProtocol.Category.FollowUpCapabilityGap, categories);
        Assert.Contains(ReviewBlockerProtocol.Category.HostMetadataBlocker, categories);

        // Determinism: re-classifying the same signals yields identical categories.
        var second = ReviewBlockerProtocol.CanonicalScenarios
            .Select(scenario => ReviewBlockerProtocol.Classify(scenario.Signal).Category)
            .ToArray();
        Assert.Equal(categories, second);
    }
}
