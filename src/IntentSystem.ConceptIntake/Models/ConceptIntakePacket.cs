namespace IntentSystem.ConceptIntake.Models;

/// <summary>
/// Represents the input packet for concept intake: a new concept to be
/// processed into intent drafts and interview questions.
/// </summary>
public sealed record ConceptIntakePacket
{
    public required string DomainSlug { get; init; }

    public required string ConceptSource { get; init; }

    public required string ConceptText { get; init; }

    public required IReadOnlyList<string> UpstreamPaths { get; init; }

    public required string InitialGoal { get; init; }

    public required IReadOnlyList<string> Constraints { get; init; }

    public required IReadOnlyList<string> KnownUnknowns { get; init; }
}
