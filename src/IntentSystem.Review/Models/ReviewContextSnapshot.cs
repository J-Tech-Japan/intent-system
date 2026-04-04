namespace IntentSystem.Review.Models;

public sealed record ReviewContextSnapshot
{
    public required string SourceExecutionUnit { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    public required IReadOnlyList<string> DeterministicReviewChecks { get; init; }

    public required IReadOnlyList<string> ExpectedEvidence { get; init; }
}
