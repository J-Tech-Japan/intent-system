namespace IntentSystem.Cli.Commands;

internal sealed record RunImplementResult
{
    public required RunImplementRequest Request { get; init; }

    public required string ArtifactPath { get; init; }

    public DirectRunLaunchResult? DirectRun { get; init; }
}
