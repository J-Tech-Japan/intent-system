using IntentSystem.Drift.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Drift;

public sealed record DriftProcessingResult
{
    public required DriftClassificationReport Report { get; init; }

    public required QueueState UpdatedQueueState { get; init; }

    public required IReadOnlyList<RunEvent> AppendedEvents { get; init; }
}
