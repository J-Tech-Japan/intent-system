namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentBestPracticeResult
{
    public required string Domain { get; init; }

    public required string SourceBundleArtifactPath { get; init; }

    public required IReadOnlyList<string> ReconstructedArtifactPaths { get; init; }

    public required string ReviewArtifactPath { get; init; }

    public required IReadOnlyList<string> ReviewedDimensions { get; init; }

    public required IReadOnlyList<string> ModelRefs { get; init; }

    public required IReadOnlyList<string> KnowledgeRefs { get; init; }

    public required IReadOnlyList<string> RecommendedIntentAdditions { get; init; }

    public required IReadOnlyList<string> RecommendedClarifications { get; init; }

    public required IReadOnlyList<string> DeveloperConfirmationItems { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required IReadOnlyList<string> ConfidenceDeltas { get; init; }

    public required string ReadinessStatus { get; init; }

    public required IReadOnlyList<string> SkippedStages { get; init; }
}
