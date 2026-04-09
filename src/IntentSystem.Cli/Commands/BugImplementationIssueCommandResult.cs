namespace IntentSystem.Cli.Commands;

internal sealed record BugImplementationIssueCommandResult
{
    public required BugImplementationIssueArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
