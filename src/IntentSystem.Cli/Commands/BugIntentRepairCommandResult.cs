namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentRepairCommandResult
{
    public required BugIntentRepairArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
