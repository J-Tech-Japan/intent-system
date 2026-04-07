namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentBridgeResult
{
    public required string Domain { get; init; }

    public required string ConceptArtifactPath { get; init; }

    public required IReadOnlyList<string> InterviewArtifactPaths { get; init; }

    public required IReadOnlyList<string> RecommendedUpdates { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required IReadOnlyList<string> Gaps { get; init; }

    public required IReadOnlyList<string> SkippedBridgeSteps { get; init; }
}
