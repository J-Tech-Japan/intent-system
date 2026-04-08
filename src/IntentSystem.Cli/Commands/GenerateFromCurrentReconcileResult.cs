namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentReconcileResult
{
    public required string Domain { get; init; }

    public required string Route { get; init; }

    public required string SourceBundleArtifactPath { get; init; }

    public required IReadOnlyList<string> ReconstructedArtifactPaths { get; init; }

    public required string ReviewArtifactPath { get; init; }

    public required string DeveloperConfirmationArtifactPath { get; init; }

    public string? ConfirmedReconstructionArtifactPath { get; init; }

    public string? ClarificationReturnArtifactPath { get; init; }

    public required IReadOnlyList<string> ConfirmedItems { get; init; }

    public required IReadOnlyList<string> RejectedItems { get; init; }

    public required IReadOnlyList<string> DeferredItems { get; init; }

    public required IReadOnlyList<string> BlockedItems { get; init; }

    public required IReadOnlyList<string> ClarifyItems { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required string DownstreamReadiness { get; init; }
}
