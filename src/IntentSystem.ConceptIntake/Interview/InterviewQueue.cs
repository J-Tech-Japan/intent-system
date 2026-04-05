using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.ConceptIntake.Interview;

/// <summary>
/// Manages interview queue items: filtering by status and blocking behavior,
/// applying answers, and determining queue progression.
/// Interview queue is distinct from clarification inbox: interview explores
/// unknowns in new concepts, clarification resolves blockers on existing
/// execution units.
/// </summary>
public static class InterviewQueue
{
    private const string BlockingValue = "blocking";

    /// <summary>
    /// Returns open interview questions that are blocking the interview flow.
    /// Only blocking questions cause the interview to wait.
    /// </summary>
    public static IReadOnlyList<InterviewQueueItem> GetBlockingPending(
        IReadOnlyList<InterviewQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(i => i.Status == InterviewQueueItemStatus.Open && IsBlocking(i)).ToArray();
    }

    /// <summary>
    /// Returns all open interview questions (blocking and nonblocking).
    /// </summary>
    public static IReadOnlyList<InterviewQueueItem> GetAllPending(
        IReadOnlyList<InterviewQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(i => i.Status == InterviewQueueItemStatus.Open).ToArray();
    }

    /// <summary>
    /// Returns true if the interview flow is blocked by any open blocking question.
    /// </summary>
    public static bool IsFlowBlocked(IReadOnlyList<InterviewQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Any(i => i.Status == InterviewQueueItemStatus.Open && IsBlocking(i));
    }

    /// <summary>
    /// Applies an answer to an open interview queue item and returns the result
    /// with recommended updates for the parent intent tree.
    /// </summary>
    public static InterviewAnswerResult ApplyAnswer(
        InterviewQueueItem item,
        string answer,
        IReadOnlyList<string> recommendedUpdates,
        DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentNullException.ThrowIfNull(recommendedUpdates);

        if (item.Status != InterviewQueueItemStatus.Open)
        {
            throw new InvalidOperationException(
                $"Interview question '{item.QuestionId}' must be in 'Open' status to answer, " +
                $"but found '{item.Status}'.");
        }

        var answeredItem = item with
        {
            Status = InterviewQueueItemStatus.Answered,
            Answer = answer,
            AnsweredAt = answeredAt,
            RecommendedUpdates = recommendedUpdates.Count == 0 ? null : recommendedUpdates
        };

        return new InterviewAnswerResult
        {
            AnsweredItem = answeredItem,
            RecommendedUpdates = recommendedUpdates,
            ReturnToIntentPaths = item.ReturnToIntentPaths
        };
    }

    /// <summary>
    /// Filters interview queue items by domain slug.
    /// </summary>
    public static IReadOnlyList<InterviewQueueItem> GetForDomain(
        IReadOnlyList<InterviewQueueItem> items,
        string domainSlug)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainSlug);
        return items.Where(i => string.Equals(i.DomainSlug, domainSlug, StringComparison.Ordinal)).ToArray();
    }

    /// <summary>
    /// Selects the next open interview question for a domain using the current
    /// E2 baseline: blocking questions take precedence, then oldest created_at,
    /// then question_id for deterministic tie-breaking.
    /// </summary>
    public static InterviewQueueItem? GetNextPendingForDomain(
        IReadOnlyList<InterviewQueueItem> items,
        string domainSlug)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainSlug);

        return GetForDomain(items, domainSlug)
            .Where(item => item.Status == InterviewQueueItemStatus.Open)
            .OrderBy(item => IsBlocking(item) ? 0 : 1)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.QuestionId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool IsBlocking(InterviewQueueItem item)
    {
        return string.Equals(item.BlockingOrNonblocking, BlockingValue, StringComparison.Ordinal);
    }
}
