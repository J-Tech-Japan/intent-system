namespace IntentSystem.Cli.Commands;

internal sealed record QueueDispatchCommandResult
{
    public required string ExecutionUnit { get; init; }

    public required string? LinkedIssueUrl { get; init; }

    public required bool ReusedExistingIssue { get; init; }
}
