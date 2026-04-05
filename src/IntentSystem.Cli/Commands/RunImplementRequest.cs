namespace IntentSystem.Cli.Commands;

internal sealed record RunImplementRequest
{
    public required string ExecutionUnit { get; init; }

    public required string State { get; init; }

    public required string ImplementRole { get; init; }

    public required string QueueWorkerRole { get; init; }

    public required string QueueReviewRole { get; init; }

    public required string WorktreePath { get; init; }

    public required string ChildRepoPath { get; init; }

    public required string Branch { get; init; }

    public required string LinkedIssue { get; init; }

    public string? LatestLinkedPr { get; init; }

    public required string PacketRef { get; init; }

    public required string ReviewContextRef { get; init; }

    public required string IssueTitle { get; init; }

    public required string Goal { get; init; }

    public required string TargetPart { get; init; }

    public required string TargetRepo { get; init; }

    public required string TargetPath { get; init; }

    public required IReadOnlyList<string> InScope { get; init; }

    public required IReadOnlyList<string> OutOfScope { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    public required IReadOnlyList<string> DeterministicReviewChecks { get; init; }

    public required IReadOnlyList<string> ExpectedEvidence { get; init; }
}
