namespace IntentSystem.Cli.Commands;

internal sealed record ReviewAcceptResult
{
    public required string ExecutionUnit { get; init; }

    public required string MergedPrRef { get; init; }

    public required string ClosedIssueRef { get; init; }
}
