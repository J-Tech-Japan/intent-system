namespace IntentSystem.Cli.Commands;

internal sealed record IntakeStartResult
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> StartedExecutionUnits { get; init; }

    public required IReadOnlyList<string> GeneratedArtifactPaths { get; init; }

    public required IReadOnlyList<string> CreatedIssueRefs { get; init; }

    public required IReadOnlyList<string> WorktreePaths { get; init; }

    public required IReadOnlyList<string> SkippedUnits { get; init; }
}
