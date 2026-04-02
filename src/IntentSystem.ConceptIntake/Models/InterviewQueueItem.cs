namespace IntentSystem.ConceptIntake.Models;

/// <summary>
/// Represents a single item in the interview queue. Each item wraps an
/// InterviewQuestion with queue state and answer metadata.
/// Interview queue items are distinct from clarification artifacts:
/// interview explores unknowns for new concepts, clarification resolves
/// blockers on existing execution units.
/// </summary>
public sealed record InterviewQueueItem
{
    public required string QuestionId { get; init; }

    public required string QuestionText { get; init; }

    public required string Reason { get; init; }

    public required IReadOnlyList<string> Affects { get; init; }

    public required string BlockingOrNonblocking { get; init; }

    public required InterviewQueueItemStatus Status { get; init; }

    public required string DomainSlug { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? Answer { get; init; }

    public DateTimeOffset? AnsweredAt { get; init; }

    public IReadOnlyList<string>? RecommendedUpdates { get; init; }
}
