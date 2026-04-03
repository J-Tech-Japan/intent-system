namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Lifecycle state for an individual workflow step execution.
/// </summary>
public enum WorkerAdapterStepState
{
    Pending,
    Running,
    Completed,
    Failed,
    Blocked,
    Skipped
}
