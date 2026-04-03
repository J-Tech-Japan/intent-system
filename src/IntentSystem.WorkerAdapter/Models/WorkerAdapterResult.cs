using IntentSystem.Clarify.Models;

namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Worker adapter -> supervisor output contract for a single workflow run.
/// </summary>
public sealed record WorkerAdapterResult
{
    public required WorkerAdapterRunStatus RunStatus { get; init; }

    public required IReadOnlyList<WorkerAdapterStepStatus> StepStatuses { get; init; }

    public required WorkerReviewResult ReviewResult { get; init; }

    public required IReadOnlyList<string> ReviewCommentRefs { get; init; }

    public required IReadOnlyList<ClarificationItem> ClarificationRequests { get; init; }

    public required string ResultSummary { get; init; }

    public required IReadOnlyList<string> RunLogRefs { get; init; }
}
