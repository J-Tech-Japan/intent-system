using System.Text.Json.Serialization;

namespace IntentSystem.ConceptIntake.Models;

/// <summary>
/// Represents a single interview artifact following the parent
/// Interview And Clarification Artifact Contract.
/// Interview artifacts explore unknowns for new concepts, distinct from
/// clarification artifacts which resolve blockers on existing execution units.
/// </summary>
public sealed record InterviewQueueItem
{
    public string ArtifactKind => "interview";

    public required string DomainSlug { get; init; }

    public required string SourceConceptRef { get; init; }

    public required string QuestionId { get; init; }

    public required string QuestionText { get; init; }

    public required string Reason { get; init; }

    public required IReadOnlyList<string> Affects { get; init; }

    public required string BlockingOrNonblocking { get; init; }

    public required InterviewQueueItemStatus Status { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Answer is part of the canonical outward interview artifact contract.
    /// It is always serialized, even when null for open questions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Answer { get; init; }

    public DateTimeOffset? AnsweredAt { get; init; }

    public IReadOnlyList<string>? RecommendedUpdates { get; init; }
}
