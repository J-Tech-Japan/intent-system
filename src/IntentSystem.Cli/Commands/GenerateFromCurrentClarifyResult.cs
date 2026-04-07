namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentClarifyResult
{
    public required string Domain { get; init; }

    public required string SourceBundleArtifactPath { get; init; }

    public required IReadOnlyList<string> ReconstructedArtifactPaths { get; init; }

    public required string ReviewArtifactPath { get; init; }

    public required string DeveloperConfirmationArtifactPath { get; init; }

    public required string ClarificationReturnArtifactPath { get; init; }

    public required IReadOnlyList<string> ClarifyItems { get; init; }

    public required IReadOnlyList<string> AffectedParentRefs { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public required IReadOnlyList<string> Blockingness { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }

    public required string DownstreamReadiness { get; init; }
}
