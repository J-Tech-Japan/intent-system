namespace IntentSystem.Workflow.Models;

/// <summary>
/// Represents a single step in a workflow definition.
/// </summary>
public sealed record WorkflowStep
{
    public required WorkflowStepKind Kind { get; init; }

    public required string Role { get; init; }

    public IReadOnlyList<WorkflowStepKind> OnSuccess { get; init; } = [];

    public IReadOnlyList<WorkflowStepKind> OnFailure { get; init; } = [];
}
