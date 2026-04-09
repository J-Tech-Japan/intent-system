namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentIssueCommandResult
{
    public required BugIntentIssueArtifact Artifact { get; init; }

    public required string ArtifactPath { get; init; }
}
