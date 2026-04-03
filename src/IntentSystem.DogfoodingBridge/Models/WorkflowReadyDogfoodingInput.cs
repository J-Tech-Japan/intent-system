using IntentSystem.Workflow.Models;

namespace IntentSystem.DogfoodingBridge.Models;

/// <summary>
/// Workflow-ready start input assembled for an execution unit without redefining the workflow artifact.
/// </summary>
public sealed record WorkflowReadyDogfoodingInput
{
    public required string ExecutionUnit { get; init; }

    public required WorkflowPacketPaths PacketPaths { get; init; }

    public required IReadOnlyList<string> DependencySnapshot { get; init; }

    public required WorkerRoles WorkerRoles { get; init; }

    public required IReadOnlyList<string> EntryConditions { get; init; }

    public required string ReviewMode { get; init; }

    public required string CompletionAction { get; init; }
}
