namespace IntentSystem.Cli.Commands;

internal sealed record RunSupervisionSession
{
    public required string ExecutionUnit { get; init; }

    public required RunSupervisionWorkerEntry WorkerEntry { get; init; }

    public required RunSupervisionSessionStatus Status { get; init; }

    public required string QueueState { get; init; }

    public required string WorktreePath { get; init; }

    public required string ChildRepoPath { get; init; }

    public required string Branch { get; init; }

    public required string LinkedIssue { get; init; }

    public string? LinkedPr { get; init; }

    public string? CommentRef { get; init; }

    public required string HandoffArtifactRef { get; init; }

    public required int RetryCount { get; init; }

    public required int RetryBudget { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required DateTimeOffset LastHeartbeatAt { get; init; }

    public DateTimeOffset? NextRetryAt { get; init; }

    public string? LastInterruptionReason { get; init; }

    public bool RequiresPostFixWorktreeProgressDecision { get; init; }
}
