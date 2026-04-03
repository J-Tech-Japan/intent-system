namespace IntentSystem.Workflow.Models;

/// <summary>
/// Defines the worker and reviewer roles for a workflow.
/// </summary>
public sealed record WorkerRoles
{
    public required string Worker { get; init; }

    public required string Reviewer { get; init; }
}
