namespace IntentSystem.Cli.Commands;

internal sealed record IntakeInterviewResult
{
    public required string Domain { get; init; }

    public required string ConceptArtifactPath { get; init; }

    public required bool WasSkipped { get; init; }

    public required IReadOnlyList<string> GeneratedArtifactPaths { get; init; }

    public required IReadOnlyList<string> ExistingArtifactPaths { get; init; }

    public required IReadOnlyList<string> CreatedQuestionIds { get; init; }
}
