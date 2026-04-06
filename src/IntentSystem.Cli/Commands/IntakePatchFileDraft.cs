namespace IntentSystem.Cli.Commands;

internal sealed record IntakePatchFileDraft
{
    public required string TargetFilePath { get; init; }

    public required string CurrentFileState { get; init; }

    public required IReadOnlyList<string> ProposedEdits { get; init; }

    public required IReadOnlyList<string> Rationale { get; init; }

    public required IReadOnlyList<string> SourceConceptRefs { get; init; }

    public required IReadOnlyList<string> FoldinAnchors { get; init; }

    public required string CurrentFileExcerpt { get; init; }
}
