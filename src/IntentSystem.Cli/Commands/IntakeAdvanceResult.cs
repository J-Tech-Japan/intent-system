namespace IntentSystem.Cli.Commands;

internal sealed record IntakeAdvanceResult
{
    public required string Domain { get; init; }

    public required string ReadinessStatus { get; init; }

    public required IReadOnlyList<string> UpdatedSourceFilePaths { get; init; }

    public required IReadOnlyList<string> UpdatedExecutionFilePaths { get; init; }

    public required IReadOnlyList<string> RegeneratedArtifactPaths { get; init; }

    public required IReadOnlyList<string> SkippedStages { get; init; }
}
