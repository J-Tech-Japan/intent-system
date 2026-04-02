namespace IntentSystem.ConceptIntake.Models;

/// <summary>
/// Represents the output of concept intake processing: intent draft paths,
/// interview questions to explore unknowns, a clarification return path,
/// and recommended updates to the parent intent tree.
/// </summary>
public sealed record ConceptIntakeOutput
{
    public required IReadOnlyList<string> IntentDraftPaths { get; init; }

    public required IReadOnlyList<InterviewQuestion> InterviewQuestions { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public required IReadOnlyList<string> RecommendedUpdates { get; init; }
}
