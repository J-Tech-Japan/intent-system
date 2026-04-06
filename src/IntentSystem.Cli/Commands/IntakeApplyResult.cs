namespace IntentSystem.Cli.Commands;

internal sealed record IntakeApplyResult
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> ChangedFilePaths { get; init; }

    public required int AppliedEditCount { get; init; }

    public required IReadOnlyList<string> SourceConceptRefs { get; init; }
}
