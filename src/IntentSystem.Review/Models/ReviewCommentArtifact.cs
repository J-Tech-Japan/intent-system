namespace IntentSystem.Review.Models;

public sealed record ReviewCommentArtifact
{
    public required string ExecutionUnit { get; init; }

    public required string ReviewRequestRef { get; init; }

    public required string LinkedPr { get; init; }

    public required string CommentRef { get; init; }

    public required string BodyPath { get; init; }
}
