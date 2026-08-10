namespace IntentSystem.Cli.Commands;

internal sealed record IntentInitResult
{
    public required string Domain { get; init; }

    public required string? TargetRepo { get; init; }

    public required string HostRepoRoot { get; init; }

    public required bool WriteApplied { get; init; }

    public required IReadOnlyList<string> PlannedPaths { get; init; }

    public required IReadOnlyList<string> WrittenPaths { get; init; }

    public required IReadOnlyList<string> ExistingPaths { get; init; }

    public required IReadOnlyList<string> NextSteps { get; init; }

    public required bool FreshHost { get; init; }

    public required IReadOnlyList<string> GitAttributesLines { get; init; }

    public required IReadOnlyList<string> GitIgnoreLines { get; init; }

    public required string ExistingHostGuidance { get; init; }
}
