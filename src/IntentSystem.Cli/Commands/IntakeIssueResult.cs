namespace IntentSystem.Cli.Commands;

internal sealed record IntakeIssueResult
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> GeneratedExecutionUnits { get; init; }

    public required IReadOnlyList<string> ArtifactPaths { get; init; }

    public required IReadOnlyList<string> SkippedUnits { get; init; }
}
