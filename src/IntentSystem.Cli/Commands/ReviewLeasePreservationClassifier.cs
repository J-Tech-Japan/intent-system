namespace IntentSystem.Cli.Commands;

/// <summary>
/// G390: pure classifier that preserves a review-ready PR's actionability when
/// a host-review wake stops on a HOST METADATA blocker (e.g. a missing
/// same-repo <c>linked_pr</c>) rather than on an implementation finding.
///
/// The hazard: review-start consumes <c>intent-pr-rereview-ready</c> as it
/// takes the review lease, but if the wake then aborts on host metadata before
/// producing an actual review verdict, the PR can be left with neither
/// <c>intent-pr-rereview-ready</c> nor a completed review — invisible to future
/// review wakes. A host metadata blocker is NOT an implementation finding, so:
/// <list type="bullet">
/// <item><description>the consumed <c>intent-pr-rereview-ready</c> must be restored so the PR stays review-actionable; and</description></item>
/// <item><description>the blocker must NOT be turned into an implementation <c>request-update</c> comment (that would wrongly bounce host-owned work to the implementation side).</description></item>
/// </list>
///
/// No I/O — the command layer supplies the facts.
/// </summary>
internal static class ReviewLeasePreservationClassifier
{
    /// <summary>
    /// Decide whether to restore <c>intent-pr-rereview-ready</c> and whether to
    /// suppress an implementation request-update comment.
    /// </summary>
    /// <param name="reviewStartConsumedRereviewReady">Review-start removed <c>intent-pr-rereview-ready</c> when taking the lease.</param>
    /// <param name="reviewVerdictProduced">An actual review verdict (approve / request-update) was produced this wake.</param>
    /// <param name="stoppedOnHostMetadataBlocker">The wake aborted on a host metadata blocker (e.g. missing linked_pr), not an implementation finding.</param>
    public static ReviewLeasePreservationDecision Classify(
        bool reviewStartConsumedRereviewReady,
        bool reviewVerdictProduced,
        bool stoppedOnHostMetadataBlocker)
    {
        // Restore the consumed label only when the wake stopped on a host
        // metadata blocker BEFORE producing a verdict — otherwise the normal
        // review transitions own the label.
        var restore = reviewStartConsumedRereviewReady
            && !reviewVerdictProduced
            && stoppedOnHostMetadataBlocker;

        // A host metadata blocker is host-owned; it must never become an
        // implementation-side request-update comment.
        var suppressRequestUpdateComment = stoppedOnHostMetadataBlocker;

        string reason;
        if (restore)
        {
            reason = "host metadata blocker stopped the wake before a review verdict; restoring "
                + "intent-pr-rereview-ready so the PR stays review-actionable for a future wake.";
        }
        else if (reviewVerdictProduced)
        {
            reason = "a review verdict was produced; the normal review transitions own the label state.";
        }
        else
        {
            reason = "no consumed rereview-ready label to restore, or the stop was not a host metadata blocker.";
        }

        return new ReviewLeasePreservationDecision
        {
            RestoreRereviewReady = restore,
            SuppressRequestUpdateComment = suppressRequestUpdateComment,
            Reason = reason,
        };
    }
}

/// <summary>G390: the verdict from <see cref="ReviewLeasePreservationClassifier.Classify"/>.</summary>
internal sealed record ReviewLeasePreservationDecision
{
    /// <summary>Restore <c>intent-pr-rereview-ready</c> so the PR stays review-actionable.</summary>
    public required bool RestoreRereviewReady { get; init; }

    /// <summary>A host metadata blocker must not be posted as an implementation request-update comment.</summary>
    public required bool SuppressRequestUpdateComment { get; init; }

    public required string Reason { get; init; }
}
