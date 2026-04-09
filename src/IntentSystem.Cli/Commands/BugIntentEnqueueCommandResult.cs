namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentEnqueueCommandResult
{
    public required BugIntentEnqueueArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
