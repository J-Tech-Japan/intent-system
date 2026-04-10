namespace IntentSystem.Cli.Commands;

internal sealed record RunCommandResult
{
    public required string StopReason { get; init; }

    public required IReadOnlyList<RunCommandAction> Actions { get; init; }

    public required IReadOnlyList<string> TouchedExecutionUnits { get; init; }

    public required IReadOnlyList<string> ReusedChildCommandRefs { get; init; }

    public string? ExecutionUnit { get; init; }

    public string? Detail { get; init; }

    public string? ArtifactPath { get; init; }
}
