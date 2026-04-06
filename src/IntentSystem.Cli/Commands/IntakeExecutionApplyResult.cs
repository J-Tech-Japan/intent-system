namespace IntentSystem.Cli.Commands;

internal sealed record IntakeExecutionApplyResult
{
    public required string Domain { get; init; }

    public required IReadOnlyList<string> ChangedFilePaths { get; init; }

    public required int AppliedUnitCount { get; init; }

    public required IReadOnlyList<string> PreservedDependencyRefs { get; init; }
}
