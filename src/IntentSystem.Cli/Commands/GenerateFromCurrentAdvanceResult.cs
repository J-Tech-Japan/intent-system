namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentAdvanceResult
{
    public required string Domain { get; init; }

    public required string SourceBundleArtifactPath { get; init; }

    public required IReadOnlyList<string> ReconstructedArtifactPaths { get; init; }

    public required IReadOnlyList<string> StandardIntakeArtifactPaths { get; init; }

    public required IReadOnlyList<string> UpdatedSourceFilePaths { get; init; }

    public required IReadOnlyList<string> UpdatedExecutionFilePaths { get; init; }

    public required string ReadinessStatus { get; init; }

    public required IReadOnlyList<string> SkippedStages { get; init; }
}
