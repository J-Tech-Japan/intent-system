using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G390: tests for rereview-ready preservation when a host-review wake stops on
/// a host metadata blocker. The consumed <c>intent-pr-rereview-ready</c> must
/// be restored (keeping the PR review-actionable) and the blocker must never
/// become an implementation request-update comment.
/// </summary>
public sealed class ReviewLeasePreservationClassifierTests
{
    [Fact]
    public void Classify_MetadataBlockedBeforeVerdict_RestoresRereviewReady_AndSuppressesComment()
    {
        var decision = ReviewLeasePreservationClassifier.Classify(
            reviewStartConsumedRereviewReady: true,
            reviewVerdictProduced: false,
            stoppedOnHostMetadataBlocker: true);

        Assert.True(decision.RestoreRereviewReady);
        Assert.True(decision.SuppressRequestUpdateComment);
        Assert.Contains("review-actionable", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_ReviewVerdictProduced_DoesNotRestore()
    {
        // A real verdict means normal review transitions own the label.
        var decision = ReviewLeasePreservationClassifier.Classify(
            reviewStartConsumedRereviewReady: true,
            reviewVerdictProduced: true,
            stoppedOnHostMetadataBlocker: false);

        Assert.False(decision.RestoreRereviewReady);
        Assert.False(decision.SuppressRequestUpdateComment);
    }

    [Fact]
    public void Classify_ImplementationFindingStop_DoesNotRestoreOrSuppress()
    {
        // Stopped, but NOT on a host metadata blocker — an implementation
        // finding legitimately drives a request-update; do not restore.
        var decision = ReviewLeasePreservationClassifier.Classify(
            reviewStartConsumedRereviewReady: true,
            reviewVerdictProduced: false,
            stoppedOnHostMetadataBlocker: false);

        Assert.False(decision.RestoreRereviewReady);
        Assert.False(decision.SuppressRequestUpdateComment);
    }

    [Fact]
    public void Classify_NeverConsumedLabel_DoesNotRestore_ButStillSuppressesMetadataComment()
    {
        var decision = ReviewLeasePreservationClassifier.Classify(
            reviewStartConsumedRereviewReady: false,
            reviewVerdictProduced: false,
            stoppedOnHostMetadataBlocker: true);

        Assert.False(decision.RestoreRereviewReady);
        // A host metadata blocker still must not become an implementation comment.
        Assert.True(decision.SuppressRequestUpdateComment);
    }
}
