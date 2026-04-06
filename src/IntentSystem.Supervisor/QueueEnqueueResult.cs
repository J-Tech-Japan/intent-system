using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor;

public sealed record QueueEnqueueResult
{
    public required QueueState UpdatedState { get; init; }

    public required QueueItem QueueItem { get; init; }

    public required bool WasEnqueued { get; init; }

    public RunEvent? Event { get; init; }
}
