using IntentSystem.Clarify.Models;
using IntentSystem.Projection;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Clarify;

/// <summary>
/// Represents the result of applying a clarification answer and resuming
/// the queue item. Contains the updated clarification artifact, the updated
/// queue state, the events to append to the run log, and the optionally
/// regenerated packet.
/// </summary>
public sealed record ClarifyResumeResult
{
    public required ClarificationItem AppliedClarification { get; init; }
    public required QueueState UpdatedQueueState { get; init; }
    public required IReadOnlyList<RunEvent> Events { get; init; }
    public GeneratedPacket? RegeneratedPacket { get; init; }
}
