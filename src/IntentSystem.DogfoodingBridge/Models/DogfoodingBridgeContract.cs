using IntentSystem.DomainBinding.Models;

namespace IntentSystem.DogfoodingBridge.Models;

/// <summary>
/// Aggregates existing binding, queue, workflow, clarify, and interview contracts
/// into a thin dogfooding bridge boundary.
/// </summary>
public sealed record DogfoodingBridgeContract
{
    public required ProjectionReadySlice Binding { get; init; }

    public required QueueReadyDogfoodingInput QueueInput { get; init; }

    public required WorkflowReadyDogfoodingInput WorkflowInput { get; init; }

    public required DogfoodingReturnRoutes ReturnRoutes { get; init; }
}
