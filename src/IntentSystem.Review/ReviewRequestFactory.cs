using IntentSystem.Review.Models;

namespace IntentSystem.Review;

public static class ReviewRequestFactory
{
    public static ReviewRequest Create(
        string executionUnit,
        string reviewContextRef,
        string linkedPr,
        ReviewContextSnapshot reviewContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewContextRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedPr);
        ArgumentNullException.ThrowIfNull(reviewContext);

        return new ReviewRequest
        {
            ExecutionUnit = executionUnit,
            ReviewContextRef = reviewContextRef,
            LinkedPr = linkedPr,
            DeterministicReviewChecks = reviewContext.DeterministicReviewChecks,
            AcceptanceCriteria = reviewContext.AcceptanceCriteria,
            ExpectedEvidence = reviewContext.ExpectedEvidence
        };
    }
}
