namespace IntentSystem.Cli.Commands;

internal sealed record IntakePatchRequest
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> TargetFilePaths { get; init; }

    public required IReadOnlyList<string> SourceConceptRefs { get; init; }

    public required IReadOnlyList<IntakePatchFileDraft> FileDrafts { get; init; }
}
