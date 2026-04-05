using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal sealed record InterviewResumeResult
{
    public required string Domain { get; init; }

    public required bool HasArtifacts { get; init; }

    public InterviewQueueItem? NextQuestion { get; init; }

    public required IReadOnlyList<string> AnsweredQuestionIds { get; init; }

    public required IReadOnlyList<string> RecommendedUpdates { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }
}
