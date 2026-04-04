namespace IntentSystem.Drift.Models;

public sealed record DriftClassificationReport
{
    public required IReadOnlyList<DriftClassificationItem> Items { get; init; }
}
