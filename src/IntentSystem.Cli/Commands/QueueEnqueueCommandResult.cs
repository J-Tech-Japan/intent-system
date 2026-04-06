namespace IntentSystem.Cli.Commands;

internal sealed record QueueEnqueueCommandResult
{
    public required string ExecutionUnit { get; init; }

    public required IReadOnlyList<string> EnqueuedExecutionUnits { get; init; }

    public required IReadOnlyList<string> PacketPaths { get; init; }

    public required IReadOnlyList<string> SkippedUnits { get; init; }
}
