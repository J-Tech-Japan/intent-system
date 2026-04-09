namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentReviewCommandResult
{
    public required BugIntentReviewArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
