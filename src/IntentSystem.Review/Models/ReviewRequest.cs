namespace IntentSystem.Review.Models;

public sealed record ReviewRequest
{
    public required string ExecutionUnit { get; init; }

    public required string ReviewContextRef { get; init; }

    public required string LinkedPr { get; init; }

    public required IReadOnlyList<string> DeterministicReviewChecks { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    public required IReadOnlyList<string> ExpectedEvidence { get; init; }
}
