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
}
