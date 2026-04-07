namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentReviewResult
{
    public required string Domain { get; init; }

    public required string SourceBundleArtifactPath { get; init; }

    public required IReadOnlyList<string> ReconstructedArtifactPaths { get; init; }

    public required IReadOnlyList<string> StandardIntakeArtifactPaths { get; init; }

    public required IReadOnlyList<string> UpdatedSourceFilePaths { get; init; }

    public required IReadOnlyList<string> UpdatedExecutionFilePaths { get; init; }

    public required IReadOnlyList<string> GeneratedIssueArtifactPaths { get; init; }

    public required IReadOnlyList<string> CreatedIssueRefs { get; init; }

    public required IReadOnlyList<string> WorktreePaths { get; init; }

    public required IReadOnlyList<string> StartedExecutionUnits { get; init; }

    public required IReadOnlyList<string> ImplementRequestArtifactPaths { get; init; }

    public required IReadOnlyList<string> CreatedPrRefs { get; init; }

    public required IReadOnlyList<string> ReviewExecutionUnits { get; init; }

    public required IReadOnlyList<string> ReviewRequestArtifactPaths { get; init; }

    public required string ReadinessStatus { get; init; }

    public required IReadOnlyList<string> SkippedStages { get; init; }
}
