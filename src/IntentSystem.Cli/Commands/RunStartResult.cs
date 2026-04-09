namespace IntentSystem.Cli.Commands;

internal sealed record RunStartResult
{
    public required string ExecutionUnit { get; init; }

    public required string WorktreePath { get; init; }

    public required string BranchName { get; init; }
}
