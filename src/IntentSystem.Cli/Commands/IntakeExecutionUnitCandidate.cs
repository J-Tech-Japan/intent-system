namespace IntentSystem.Cli.Commands;

internal sealed record IntakeExecutionUnitCandidate
{
    public required string ExecutionUnitId { get; init; }

    public required string SourceFilePath { get; init; }

    public required string TargetPart { get; init; }

    public required IReadOnlyList<string> Dependencies { get; init; }

    public required IReadOnlyList<string> ReadinessNotes { get; init; }

    public required IReadOnlyList<string> VerificationHints { get; init; }
}
