namespace IntentSystem.Cli.Commands;

internal sealed record IntakeExecutionRequest
{
    public required string Domain { get; init; }

    public required IReadOnlyList<IntakeExecutionUnitCandidate> ProposedExecutionUnits { get; init; }
}
