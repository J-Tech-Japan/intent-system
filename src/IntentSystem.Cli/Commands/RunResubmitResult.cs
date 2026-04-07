namespace IntentSystem.Cli.Commands;

internal sealed record RunResubmitResult
{
    public required string ExecutionUnit { get; init; }

    public required string Branch { get; init; }

    public required string WorktreePath { get; init; }

    public required string LinkedPr { get; init; }
}
