namespace IntentSystem.ConceptIntake.Models;

/// <summary>
/// Represents the result of applying an interview answer: the updated queue item
/// advanced to answered, and the recommended updates to pass back to the parent
/// intent tree.
/// </summary>
public sealed record InterviewAnswerResult
{
    public required InterviewQueueItem AnsweredItem { get; init; }

    public required IReadOnlyList<string> RecommendedUpdates { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }
}
