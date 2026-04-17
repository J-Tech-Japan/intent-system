namespace IntentSystem.Cli.Commands;

internal sealed record RunSuperviseResult
{
    public required string ExecutionUnit { get; init; }

    public required string SessionArtifactPath { get; init; }

    public required RunSupervisionWorkerEntry WorkerEntry { get; init; }

    public required RunSupervisionSessionStatus SessionStatus { get; init; }

    public required int RetryCount { get; init; }

    public required int RetryBudget { get; init; }

    public required string HandoffArtifactRef { get; init; }

    public string? NextRetryAt { get; init; }

    public bool RetryScheduled { get; init; }

    public bool AutoResumed { get; init; }

    public bool Blocked { get; init; }

    public string? FailureReason { get; init; }

    public bool ReportAsNonRetryableFailure { get; init; }

    public bool RequiresPostFixWorktreeProgressDecision { get; init; }
}
