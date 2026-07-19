using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor;

/// <summary>
/// G534 review repair: result of <see cref="QueueManager.Retire"/>. Mirrors
/// <see cref="QueueEnqueueResult"/>'s "may be a safe no-op" shape —
/// <see cref="WasRetired"/> is <see langword="false"/> and <see cref="Event"/>
/// is <see langword="null"/> when the item was already retired, so the
/// caller can skip persisting a no-op state write and never appends a
/// duplicate run event.
/// </summary>
public sealed record QueueRetireResult
{
    public required QueueState UpdatedState { get; init; }

    public required QueueItem QueueItem { get; init; }

    public required bool WasRetired { get; init; }

    public RunEvent? Event { get; init; }
}
