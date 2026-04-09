namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentSubmitCommandResult
{
    public required BugIntentSubmitArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
