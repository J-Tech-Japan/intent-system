namespace IntentSystem.Cli.Commands;

internal sealed record IntakeLaunchResult
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> LaunchedExecutionUnits { get; init; }

    public required IReadOnlyList<string> CreatedIssueRefs { get; init; }

    public required IReadOnlyList<string> WorktreePaths { get; init; }

    public required IReadOnlyList<string> SkippedUnits { get; init; }
}
