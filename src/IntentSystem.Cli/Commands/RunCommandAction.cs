namespace IntentSystem.Cli.Commands;

internal sealed record RunCommandAction
{
    public required string Name { get; init; }

    public required string ExecutionUnit { get; init; }
}
