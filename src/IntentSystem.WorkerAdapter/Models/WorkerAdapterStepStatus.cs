using IntentSystem.Workflow.Models;

namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Execution status for an individual workflow step.
/// </summary>
public sealed record WorkerAdapterStepStatus
{
    public required WorkflowStepKind Step { get; init; }

    public required WorkerAdapterStepState Status { get; init; }

    public string? Detail { get; init; }
}
