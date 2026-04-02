namespace IntentSystem.Projection.Models;

public sealed record ImplementationIssuePacket
{
    public required string IssueTitle { get; init; }

    public required IssueKind IssueKind { get; init; }

    public required string SourceExecutionUnit { get; init; }

    public required string Goal { get; init; }

    public required IReadOnlyList<string> InScope { get; init; }

    public required IReadOnlyList<string> OutOfScope { get; init; }

    public required string TargetRepo { get; init; }

    public required string TargetPath { get; init; }

    public required string TargetPart { get; init; }

    public required IReadOnlyList<string> Dependencies { get; init; }

    public required IReadOnlyList<string> IntentReferences { get; init; }

    public required IReadOnlyList<string> RulesAndSpecs { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    public required IReadOnlyList<string> VerificationEvidence { get; init; }

    public required string ReviewMode { get; init; }

    public required string CompletionAction { get; init; }

    public required string LandingPolicy { get; init; }
}
