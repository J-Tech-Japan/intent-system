namespace IntentSystem.Clarify.Models;

/// <summary>
/// Represents a clarification artifact following the parent
/// Interview And Clarification Artifact Contract.
/// </summary>
public sealed record ClarificationItem
{
    public string ArtifactKind => ClarificationConstants.ArtifactKind;

    public required string ClarificationSource { get; init; }

    public required string QuestionId { get; init; }

    public required string QuestionText { get; init; }

    public required string Reason { get; init; }

    public required IReadOnlyList<string> AffectedIntents { get; init; }

    public required IReadOnlyList<string> AffectedExecutionUnits { get; init; }

    public required string BlockingOrNonblocking { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public required ClarificationState State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? Answer { get; init; }

    public DateTimeOffset? AnsweredAt { get; init; }
}
