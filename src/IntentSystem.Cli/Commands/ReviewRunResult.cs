namespace IntentSystem.Cli.Commands;

internal sealed record ReviewRunResult
{
    public required string ExecutionUnit { get; init; }

    public required string ArtifactPath { get; init; }
}
