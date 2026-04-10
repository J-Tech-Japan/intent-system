namespace IntentSystem.Cli.Commands;

internal sealed record DirectRunLaunchResult
{
    public required string RequestArtifactPath { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string Transport { get; init; }

    public required string ProviderSessionId { get; init; }

    public required string TransportSummary { get; init; }
}
