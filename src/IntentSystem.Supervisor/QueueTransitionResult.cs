using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor;

/// <summary>
/// Represents the result of a queue state transition: a new snapshot and the
/// corresponding run event to append to the log.
/// </summary>
public sealed record QueueTransitionResult
{
    public required QueueState UpdatedState { get; init; }
    public required RunEvent Event { get; init; }
}
