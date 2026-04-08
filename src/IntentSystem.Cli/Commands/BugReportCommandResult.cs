namespace IntentSystem.Cli.Commands;

internal sealed record BugReportCommandResult
{
    public required BugReportArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
