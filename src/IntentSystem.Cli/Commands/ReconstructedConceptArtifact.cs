namespace IntentSystem.Cli.Commands;

internal sealed record ReconstructedConceptArtifact
{
    public required string DomainSlug { get; init; }

    public required string InitialGoal { get; init; }

    public required IReadOnlyList<string> CandidateIntentNodes { get; init; }

    public required IReadOnlyList<string> CandidateUserContext { get; init; }

    public required IReadOnlyList<string> CandidateMeans { get; init; }

    public required IReadOnlyList<string> CandidateRules { get; init; }

    public required IReadOnlyList<string> CandidateSpecs { get; init; }

    public required IReadOnlyList<string> CandidateExecutionUnits { get; init; }

    public required IReadOnlyList<string> ConfidenceByAltitude { get; init; }

    public required IReadOnlyList<string> SourceConceptRefs { get; init; }
}
