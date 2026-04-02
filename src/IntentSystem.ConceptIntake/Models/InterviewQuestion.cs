namespace IntentSystem.ConceptIntake.Models;

/// <summary>
/// Represents a single interview question generated during concept intake.
/// Interview questions explore unknown areas of a new concept, distinct from
/// clarification artifacts which resolve blockers on existing execution units.
/// </summary>
public sealed record InterviewQuestion
{
    public required string QuestionId { get; init; }

    public required string QuestionText { get; init; }

    public required string Reason { get; init; }

    public required IReadOnlyList<string> Affects { get; init; }

    public required string BlockingOrNonblocking { get; init; }
}
