namespace IntentSystem.ConceptIntake.Models;

/// <summary>
/// Represents the result of applying an interview answer: the updated queue item
/// advanced to applied, and the recommended updates to pass back to the parent
/// intent tree.
/// </summary>
public sealed record InterviewAnswerResult
{
    public required InterviewQueueItem AppliedItem { get; init; }

    public required IReadOnlyList<string> RecommendedUpdates { get; init; }

    public required string ClarificationReturnPath { get; init; }
}
