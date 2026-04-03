using IntentSystem.Supervisor.Models;

namespace IntentSystem.DogfoodingBridge.Models;

/// <summary>
/// Thin queue-ready input prepared by the dogfooding bridge without selecting state policy.
/// </summary>
public sealed record QueueReadyDogfoodingInput
{
    public required string ExecutionUnit { get; init; }

    public required PacketPaths PacketPaths { get; init; }

    public required IReadOnlyList<string> Dependencies { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public required string WorkerRole { get; init; }

    public required string ReviewRole { get; init; }
}
