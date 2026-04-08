namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentConfirmedReacceptResult
{
    public required string Domain { get; init; }

    public required string Route { get; init; }

    public string? ClarificationReturnArtifactPath { get; init; }

    public string? ConfirmedReconstructionArtifactPath { get; init; }

    public required IReadOnlyList<string> UpdatedSourceFilePaths { get; init; }

    public required IReadOnlyList<string> UpdatedExecutionFilePaths { get; init; }

    public required IReadOnlyList<string> RegeneratedArtifactPaths { get; init; }

    public required IReadOnlyList<string> StartedExecutionUnits { get; init; }

    public required IReadOnlyList<string> CreatedIssueRefs { get; init; }

    public required IReadOnlyList<string> WorktreePaths { get; init; }

    public required IReadOnlyList<string> ImplementRequestArtifactPaths { get; init; }

    public required IReadOnlyList<string> CreatedPrRefs { get; init; }

    public required IReadOnlyList<string> ReviewExecutionUnits { get; init; }

    public required IReadOnlyList<string> ReviewRequestArtifactPaths { get; init; }

    public required IReadOnlyList<string> PostedCommentArtifactPaths { get; init; }

    public required IReadOnlyList<string> CommentRefs { get; init; }

    public required IReadOnlyList<string> FixingExecutionUnits { get; init; }

    public required IReadOnlyList<string> FixRequestArtifactPaths { get; init; }

    public required IReadOnlyList<string> ResubmittedExecutionUnits { get; init; }

    public required IReadOnlyList<string> ResubmittedPrRefs { get; init; }

    public required IReadOnlyList<string> RereviewedExecutionUnits { get; init; }

    public required IReadOnlyList<string> RereviewedPrRefs { get; init; }

    public required IReadOnlyList<string> CompletedExecutionUnits { get; init; }

    public required IReadOnlyList<string> ClosedIssueRefs { get; init; }

    public required IReadOnlyList<string> MergedPrRefs { get; init; }

    public required IReadOnlyList<string> ConfirmedItems { get; init; }

    public required IReadOnlyList<string> BlockedItems { get; init; }

    public required string DownstreamReadiness { get; init; }
}
