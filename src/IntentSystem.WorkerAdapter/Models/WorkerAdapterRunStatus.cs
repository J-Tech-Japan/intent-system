namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Overall workflow run status returned by the worker adapter.
/// </summary>
public enum WorkerAdapterRunStatus
{
    Running,
    Succeeded,
    ReviewRejected,
    ClarificationRequested,
    Failed
}
