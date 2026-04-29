namespace IntentSystem.Cli.Commands;

/// <summary>
/// Owner-approved clarification decision parsed from the artifact passed via
/// <c>--from-file</c> to <c>intent-cli clarify record</c> (G182). The CLI never
/// chooses or rewrites the decision; this record is purely the parsed payload.
/// </summary>
internal sealed record ClarifyRecordDecision
{
    public required string Question { get; init; }

    public required string Decision { get; init; }

    public string? Rationale { get; init; }
}
