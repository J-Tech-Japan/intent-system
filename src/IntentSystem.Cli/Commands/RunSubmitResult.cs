namespace IntentSystem.Cli.Commands;

internal sealed record RunSubmitResult
{
    public required string ExecutionUnit { get; init; }

    public required string LinkedPr { get; init; }
}
