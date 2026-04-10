namespace IntentSystem.Cli.Commands;

internal sealed record RunCommandResult
{
    public required string StopReason { get; init; }

    public required IReadOnlyList<RunCommandAction> Actions { get; init; }

    public string? ExecutionUnit { get; init; }

    public string? Detail { get; init; }
}
