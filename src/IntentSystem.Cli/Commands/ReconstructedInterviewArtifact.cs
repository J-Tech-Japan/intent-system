namespace IntentSystem.Cli.Commands;

internal sealed record ReconstructedInterviewArtifact
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> SelectedAltitudes { get; init; }

    public required IReadOnlyList<string> RootNearIntentCandidates { get; init; }

    public required IReadOnlyList<string> ExecutionNearUpdateCandidates { get; init; }

    public required IReadOnlyList<string> ConfidenceByAltitude { get; init; }

    public required IReadOnlyList<string> SourceConceptRefs { get; init; }

    public required IReadOnlyList<string> RecommendedFollowUpQuestions { get; init; }

    public required IReadOnlyList<ReconstructedBridgeQuestion> BridgeQuestions { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required IReadOnlyList<string> Gaps { get; init; }
}
