namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Review outcome categories used by the repair-in-place cycle.
/// </summary>
public enum WorkerReviewDisposition
{
    Pending,
    Approved,
    ChangesRequested,
    ClarificationRequested,
    NotApplicable
}
