namespace IntentSystem.Cli.Commands;

internal sealed record BugExecutionCommandResult
{
    public required BugExecutionArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
