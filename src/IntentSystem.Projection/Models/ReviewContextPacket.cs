namespace IntentSystem.Projection.Models;

public sealed record ReviewContextPacket
{
    public required string SourceExecutionUnit { get; init; }

    public required string ParentIntentRoot { get; init; }

    public required IReadOnlyList<string> IntentReferences { get; init; }

    public required IReadOnlyList<string> RulesAndSpecs { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    public required IReadOnlyList<string> DeterministicReviewChecks { get; init; }

    public required string ClarificationReturnPath { get; init; }
}
