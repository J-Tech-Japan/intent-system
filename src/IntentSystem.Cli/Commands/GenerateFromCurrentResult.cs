namespace IntentSystem.Cli.Commands;

internal sealed record GenerateFromCurrentResult
{
    public required string Domain { get; init; }

    public required string ArtifactPath { get; init; }

    public required string SourceRoot { get; init; }

    public required string SelectedIssueScope { get; init; }

    public required string SelectedPrScope { get; init; }

    public required IReadOnlyList<string> SelectedAltitudes { get; init; }

    public required IReadOnlyList<string> SelectedPaths { get; init; }

    public required IReadOnlyList<string> SourceRefs { get; init; }

    public required IReadOnlyList<string> SamplingNotes { get; init; }

    public required IReadOnlyList<string> Gaps { get; init; }
}
