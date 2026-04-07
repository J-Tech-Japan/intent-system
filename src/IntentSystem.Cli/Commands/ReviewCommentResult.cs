namespace IntentSystem.Cli.Commands;

internal sealed record ReviewCommentResult
{
    public required string ExecutionUnit { get; init; }

    public required string ArtifactPath { get; init; }

    public required string CommentRef { get; init; }
}
