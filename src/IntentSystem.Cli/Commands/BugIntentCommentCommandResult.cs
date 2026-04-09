namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentCommentCommandResult
{
    public required BugIntentCommentArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
