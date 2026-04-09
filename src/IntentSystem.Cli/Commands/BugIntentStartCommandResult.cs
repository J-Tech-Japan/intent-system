namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentStartCommandResult
{
    public required BugIntentStartArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
