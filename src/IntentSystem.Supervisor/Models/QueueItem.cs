namespace IntentSystem.Supervisor.Models;

public sealed record QueueItem
{
    public required string ExecutionUnit { get; init; }

    public required string Title { get; init; }

    public required QueueItemState State { get; init; }

    public required IReadOnlyList<string> Dependencies { get; init; }

    public required IReadOnlyList<string> BlockedBy { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public required PacketPaths PacketPaths { get; init; }

    public LinkedIssue? LinkedIssue { get; init; }

    public required string WorkerRole { get; init; }

    public required string ReviewRole { get; init; }

    public required string Priority { get; init; }
}
