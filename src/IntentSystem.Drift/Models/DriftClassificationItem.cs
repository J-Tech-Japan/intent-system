namespace IntentSystem.Drift.Models;

public sealed record DriftClassificationItem
{
    public required string ExecutionUnit { get; init; }

    public required DriftClassification Classification { get; init; }

    public required IReadOnlyList<string> ChangedCanonicalRefs { get; init; }

    public string? CorrectiveExecutionUnit { get; init; }
}
