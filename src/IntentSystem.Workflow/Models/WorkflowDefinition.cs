namespace IntentSystem.Workflow.Models;

/// <summary>
/// Defines the workflow for a single queue item / execution unit.
/// Contains the step sequence, roles, dependency snapshot, and entry conditions.
/// The workflow definition artifact is per-execution-unit and supports
/// repair-in-place cycles (review → comment-findings → fix → rereview)
/// on the same execution unit / same PR.
/// </summary>
public sealed record WorkflowDefinition
{
    public required string ExecutionUnit { get; init; }

    public required WorkflowPacketPaths PacketPaths { get; init; }

    public required WorkerRoles WorkerRoles { get; init; }

    public required IReadOnlyList<string> DependencySnapshot { get; init; }

    public required IReadOnlyList<string> EntryConditions { get; init; }

    public required IReadOnlyList<WorkflowStep> Steps { get; init; }

    public required string SuccessSignal { get; init; }

    public required string ReviewMode { get; init; }

    public required string CompletionAction { get; init; }
}
