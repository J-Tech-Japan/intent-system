namespace IntentSystem.Cli.Commands;

internal sealed record BugTriageCommandResult
{
    public required BugTriageArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
