namespace IntentSystem.Cli.Commands;

internal sealed record RunFixResult
{
    public required RunFixRequest Request { get; init; }

    public required string ArtifactPath { get; init; }
}
