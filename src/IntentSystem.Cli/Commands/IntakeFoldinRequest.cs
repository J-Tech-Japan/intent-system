namespace IntentSystem.Cli.Commands;

internal sealed record IntakeFoldinRequest
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> AnsweredQuestionIds { get; init; }

    public required IReadOnlyList<string> RecommendedUpdates { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required IReadOnlyList<string> SourceConceptRefs { get; init; }
}
