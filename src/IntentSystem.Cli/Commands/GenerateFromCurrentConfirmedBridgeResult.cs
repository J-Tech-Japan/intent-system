namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentConfirmedBridgeResult
{
    public required string Domain { get; init; }

    public required string Route { get; init; }

    public string? ConceptArtifactPath { get; init; }

    public required IReadOnlyList<string> InterviewArtifactPaths { get; init; }

    public string? ClarificationReturnArtifactPath { get; init; }

    public string? ConfirmedReconstructionArtifactPath { get; init; }

    public required IReadOnlyList<string> RegeneratedArtifactPaths { get; init; }

    public required IReadOnlyList<string> ConfirmedItems { get; init; }

    public required IReadOnlyList<string> BlockedItems { get; init; }

    public required string DownstreamReadiness { get; init; }
}
