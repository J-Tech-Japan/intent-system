namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentReconstructionResult
{
    public required string Domain { get; init; }

    public required string ConceptArtifactPath { get; init; }

    public required string InterviewArtifactPath { get; init; }

    public required IReadOnlyList<string> SelectedAltitudes { get; init; }

    public required IReadOnlyList<string> CandidateIntentNodes { get; init; }

    public required IReadOnlyList<string> CandidateExecutionUnits { get; init; }

    public required IReadOnlyList<string> ConfidenceByAltitude { get; init; }

    public required IReadOnlyList<string> SourceConceptRefs { get; init; }

    public required IReadOnlyList<string> RecommendedFollowUpQuestions { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required IReadOnlyList<string> Gaps { get; init; }
}
