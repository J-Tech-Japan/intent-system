namespace IntentSystem.Clarify.Models;

public sealed record ClarificationItem
{
    public string ArtifactKind => ClarificationConstants.ArtifactKind;

    public required string Id { get; init; }

    public required string ExecutionUnit { get; init; }

    public required ClarificationState State { get; init; }

    public required string Question { get; init; }

    public required string Context { get; init; }

    public string? Answer { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? AnsweredAt { get; init; }
}
