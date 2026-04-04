namespace IntentSystem.Drift.Models;

public sealed record ChangedCanonicalRef
{
    public required string CanonicalRef { get; init; }

    public required DriftClassification Classification { get; init; }

    public required IReadOnlyList<string> AffectedExecutionUnits { get; init; }

    public required string DriftSummary { get; init; }
}
