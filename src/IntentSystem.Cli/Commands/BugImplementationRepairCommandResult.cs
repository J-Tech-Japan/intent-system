namespace IntentSystem.Cli.Commands;

internal sealed record BugImplementationRepairCommandResult
{
    public required BugImplementationRepairArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }

    public BugImplementationRepairArtifact? PreviousArtifact { get; init; }
}
