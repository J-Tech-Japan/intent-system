namespace IntentSystem.Projection.Models;

public sealed record ProjectionContext
{
    public required string IssueTitle { get; init; }

    public required IssueKind IssueKind { get; init; }

    public required string ParentIntentRoot { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public IReadOnlyList<string> DeterministicReviewChecks { get; init; } = [];

    public IReadOnlyList<string> VerificationEvidence { get; init; } = [];

    public IReadOnlyList<string> AdditionalInScope { get; init; } = [];

    public IReadOnlyList<string> OutOfScope { get; init; } = [];
}
