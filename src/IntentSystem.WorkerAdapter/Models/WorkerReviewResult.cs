namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Review outcome summary for the current workflow run.
/// </summary>
public sealed record WorkerReviewResult
{
    public required WorkerReviewDisposition Disposition { get; init; }

    public string? ReviewedBy { get; init; }
}
