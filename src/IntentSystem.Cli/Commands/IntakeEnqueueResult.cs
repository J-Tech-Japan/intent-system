namespace IntentSystem.Cli.Commands;

internal sealed record IntakeEnqueueResult
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> EnqueuedExecutionUnits { get; init; }

    public required IReadOnlyList<string> PacketPaths { get; init; }

    public required IReadOnlyList<string> SkippedUnits { get; init; }
}
