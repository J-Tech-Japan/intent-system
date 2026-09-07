using System.Security.Cryptography;

namespace IntentSystem.Cli.Commands;

internal sealed record ProgressReviewVerdictEvidence
{
    public required string Project { get; init; }
    public required string Repo { get; init; }
    public required string Unit { get; init; }
    public required string PullRequest { get; init; }
    public required string ExpectedHead { get; init; }
    public required string VerdictHead { get; init; }
    public required string TaskId { get; init; }
    public required string ResultNonce { get; init; }
    public required string Status { get; init; }
    public required string ArtifactPath { get; init; }
    public required string ReviewerIdentity { get; init; }
    public required string Findings { get; init; }
    public required string VerdictMarker { get; init; }
    public required DateTimeOffset VerdictAt { get; init; }
    public string SelectorReason { get; init; } = "unknown";
    public bool CanonicalReport { get; init; }
    public bool CommentedReview { get; init; }
    public bool GreenCi { get; init; }
    public bool Superseded { get; init; }
}

internal sealed record ProgressReviewVerdictEvaluation
{
    public required string Classification { get; init; }
    public required bool Open { get; init; }
    public required string Owner { get; init; }
    public required string Action { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset? RepairSelectionDeadlineAt { get; init; }
    public DateTimeOffset? DetectionAt { get; init; }
    public DateTimeOffset? ActionDeadlineAt { get; init; }
    public int DetectionBoundSeconds { get; init; }
    public bool SelectorIndependent { get; init; }
}

internal static class ProgressReviewVerdictEvaluator
{
    public const int VerdictToRepairSeconds = 60;

    public static ProgressReviewVerdictEvaluation Evaluate(
        ProgressReviewVerdictEvidence evidence,
        DateTimeOffset now,
        ProgressSupervisionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        options ??= new ProgressSupervisionOptions();
        var bound = ProgressSupervisionConstants.ComputeDetectionBound(
            options.DetectionFloorSeconds, options.JitterSeconds, options.MaxSweepSeconds);
        var repairDeadline = evidence.VerdictAt.AddSeconds(VerdictToRepairSeconds);
        var detection = repairDeadline.AddSeconds(bound);
        var actionDeadline = detection.AddSeconds(ProgressSupervisionConstants.RecoveryGraceSeconds);

        if (!evidence.CanonicalReport || string.IsNullOrWhiteSpace(evidence.ArtifactPath)
            || string.IsNullOrWhiteSpace(evidence.ReviewerIdentity)
            || string.IsNullOrWhiteSpace(evidence.Findings)
            || !string.Equals(evidence.VerdictMarker, "REQUEST_UPDATE", StringComparison.Ordinal))
        {
            return Refusal("forged-verdict-refusal", "canonical reviewer report, artifact, reviewer, findings, and marker are required", bound, repairDeadline, detection, actionDeadline);
        }

        if (evidence.Superseded)
        {
            return Refusal("superseded-verdict-refusal", "the durable verdict is superseded and cannot authorize repair", bound, repairDeadline, detection, actionDeadline);
        }

        if (!string.Equals(evidence.VerdictHead, evidence.ExpectedHead, StringComparison.Ordinal))
        {
            return Refusal("obsolete-head-refusal", "the canonical verdict is not bound to the current PR head", bound, repairDeadline, detection, actionDeadline);
        }

        if (!string.Equals(evidence.Status, "request-update", StringComparison.OrdinalIgnoreCase))
        {
            return Refusal("unavailable-verdict", "only a current durable request-update verdict selects repair", bound, repairDeadline, detection, actionDeadline);
        }

        var overdue = now.ToUniversalTime() >= actionDeadline;
        return new ProgressReviewVerdictEvaluation
        {
            Classification = "review-selector-drift",
            Open = true,
            Owner = "orchestrator",
            Action = overdue ? "canonical-builder-repair-delegation" : "retain-verdict-deadline",
            Reason = $"current-head request-update is authoritative independent of selector ({evidence.SelectorReason})",
            RepairSelectionDeadlineAt = repairDeadline,
            DetectionAt = detection,
            ActionDeadlineAt = actionDeadline,
            DetectionBoundSeconds = bound,
            SelectorIndependent = true,
        };
    }

    private static ProgressReviewVerdictEvaluation Refusal(
        string classification,
        string reason,
        int bound,
        DateTimeOffset repairDeadline,
        DateTimeOffset detection,
        DateTimeOffset actionDeadline) => new()
        {
            Classification = classification,
            Open = true,
            Owner = "orchestrator",
            Action = "refuse-repair",
            Reason = reason,
            RepairSelectionDeadlineAt = repairDeadline,
            DetectionAt = detection,
            ActionDeadlineAt = actionDeadline,
            DetectionBoundSeconds = bound,
            SelectorIndependent = true,
        };
}

internal sealed record ProgressRoleObservation
{
    public required string Role { get; init; }
    public bool Healthy { get; init; }
    public bool SemanticChanged { get; init; }
    public bool Overdue { get; init; }
    public bool MonitorAvailable { get; init; } = true;
    public bool FalseCompletion { get; init; }
    public bool RequiredReceiptMissing { get; init; }
    public bool EscalationAlreadySent { get; init; }
}

internal sealed record ProgressRoleWakeDecision
{
    public required string Action { get; init; }
    public required string Reason { get; init; }
    public bool ModelWake { get; init; }
    public bool Refused { get; init; }
    public bool FalseCompletionDetected { get; init; }
}

internal static class ProgressRoleWakePolicy
{
    public static ProgressRoleWakeDecision Evaluate(ProgressRoleObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var role = observation.Role.Trim().ToLowerInvariant();
        if (observation.FalseCompletion)
        {
            return new ProgressRoleWakeDecision
            {
                Action = "refuse-false-completion",
                Reason = "independent receipt/transition evidence is missing",
                Refused = true,
                FalseCompletionDetected = true,
            };
        }

        if (!observation.MonitorAvailable)
        {
            return new ProgressRoleWakeDecision
            {
                Action = "escalate-missing-monitor",
                Reason = "source-qualified monitor is unavailable",
                ModelWake = role is "orchestrator" or "steward",
            };
        }

        if ((role is "architect" or "reviewer") && !observation.SemanticChanged && !observation.Overdue)
        {
            return new ProgressRoleWakeDecision
            {
                Action = "refuse-recurring-poll",
                Reason = "architect/reviewer receive only bounded decisions or evidence-bearing escalations",
                Refused = true,
            };
        }

        if (observation.Healthy && !observation.SemanticChanged && !observation.Overdue && !observation.RequiredReceiptMissing)
        {
            return new ProgressRoleWakeDecision { Action = "none", Reason = "unchanged healthy evidence", ModelWake = false };
        }

        if (observation.EscalationAlreadySent && (observation.SemanticChanged || observation.Overdue || observation.RequiredReceiptMissing))
        {
            return new ProgressRoleWakeDecision { Action = "deduplicate-episode", Reason = "one escalation per semantic episode", ModelWake = false };
        }

        return new ProgressRoleWakeDecision
        {
            Action = observation.RequiredReceiptMissing ? "escalate-missing-receipt" : "escalate-changed-or-due-evidence",
            Reason = "changed semantic evidence or a due deadline requires bounded action",
            ModelWake = role is "orchestrator" or "steward",
        };
    }
}

internal enum ProgressFaultKind
{
    DroppedWake,
    MissingFanoutReceipt,
    DelayedConsumption,
    RestartBetweenReceiptAndAction,
    FailedSenderAlert,
    FailedStewardAlert,
    DuplicateVerdict,
    StaleHead,
    MisroutedReader,
    EventLossHungReader,
    ClaimRefusal,
    LocalCommitWithoutPr,
    WrongRecipient,
    ReviewerArtifactWithoutReceipt,
    CorruptEvidence,
    Restart,
    ExhaustedRecovery,
    ChangedEvidenceDuringBackoff,
    SimultaneousProjects,
    UnavailablePrimaryBootstrap,
}

internal sealed record ProgressFaultEvaluation
{
    public required string Project { get; init; }
    public required ProgressFaultKind Fault { get; init; }
    public required string Action { get; init; }
    public required string Owner { get; init; }
    public required string Reason { get; init; }
    public bool Open { get; init; } = true;
    public bool Completed { get; init; }
    public bool DuplicateDispatch { get; init; }
    public DateTimeOffset RecoveryDeadlineAt { get; init; }
}

internal static class ProgressFaultMatrix
{
    public static IReadOnlyList<ProgressFaultKind> All { get; } = Enum.GetValues<ProgressFaultKind>();

    public static ProgressFaultEvaluation Evaluate(string project, ProgressFaultKind fault, DateTimeOffset observedAt)
    {
        var action = fault switch
        {
            ProgressFaultKind.DuplicateVerdict or ProgressFaultKind.StaleHead => "refuse-stale-or-duplicate-verdict",
            ProgressFaultKind.MisroutedReader or ProgressFaultKind.WrongRecipient => "refuse-recipient-drift",
            ProgressFaultKind.CorruptEvidence => "escalate-corrupt-evidence",
            ProgressFaultKind.UnavailablePrimaryBootstrap => "canonical-bootstrap-or-infrastructure-escalation",
            ProgressFaultKind.ClaimRefusal => "retain-claim-refusal-and-escalate",
            ProgressFaultKind.LocalCommitWithoutPr => "retain-commit-without-pr-open",
            _ => "retain-handoff-pending-and-escalate",
        };
        var reason = fault switch
        {
            ProgressFaultKind.DroppedWake => "event lost; floor remains independent",
            ProgressFaultKind.MissingFanoutReceipt => "one recipient receipt missing; successful edges remain",
            ProgressFaultKind.DelayedConsumption => "receipt is distinct from consumption",
            ProgressFaultKind.RestartBetweenReceiptAndAction => "idempotency key resumes pending edge",
            ProgressFaultKind.FailedSenderAlert or ProgressFaultKind.FailedStewardAlert => "undelivered alert remains durable",
            ProgressFaultKind.DuplicateVerdict => "duplicate verdict cannot complete the phase",
            ProgressFaultKind.StaleHead => "verdict head is obsolete",
            ProgressFaultKind.MisroutedReader or ProgressFaultKind.WrongRecipient => "recorded recipient identity does not match",
            ProgressFaultKind.EventLossHungReader => "hung reader is a source failure",
            ProgressFaultKind.ClaimRefusal => "claim gate refused the transition",
            ProgressFaultKind.LocalCommitWithoutPr => "local commit is partial progress only",
            ProgressFaultKind.ReviewerArtifactWithoutReceipt => "review artifact without receipt is incomplete",
            ProgressFaultKind.CorruptEvidence => "corrupt evidence is unknown",
            ProgressFaultKind.Restart => "restart must replay idempotently",
            ProgressFaultKind.ExhaustedRecovery => "recovery attempts exhausted and remain materially open",
            ProgressFaultKind.ChangedEvidenceDuringBackoff => "changed evidence invalidates suppression",
            ProgressFaultKind.SimultaneousProjects => "project binding prevents cross-project completion",
            ProgressFaultKind.UnavailablePrimaryBootstrap => "primary and bootstrap routes are unavailable",
            _ => "fault remains open",
        };
        return new ProgressFaultEvaluation
        {
            Project = project,
            Fault = fault,
            Action = action,
            Owner = "orchestrator",
            Reason = reason,
            Open = true,
            Completed = false,
            DuplicateDispatch = false,
            RecoveryDeadlineAt = observedAt.AddSeconds(ProgressSupervisionConstants.RecoveryGraceSeconds),
        };
    }
}

internal sealed record ProgressAcceptanceOracleResult
{
    public bool Pass { get; init; }
    public required string Reason { get; init; }
}

internal static class ProgressAcceptanceOracle
{
    public static ProgressAcceptanceOracleResult Evaluate(
        ProgressSupervisionEvidence evidence,
        ProgressJournalBenchmarkResult? benchmark)
    {
        var livenessOnly = string.Equals(evidence.RelevantCommit, "unknown", StringComparison.OrdinalIgnoreCase)
            && string.Equals(evidence.RemotePr, "unknown", StringComparison.OrdinalIgnoreCase)
            && string.Equals(evidence.ReportIdentity, "unknown", StringComparison.OrdinalIgnoreCase);
        if (livenessOnly)
        {
            return new ProgressAcceptanceOracleResult { Pass = false, Reason = "liveness-only evidence cannot satisfy the transition oracle" };
        }
        if (benchmark is null)
        {
            return new ProgressAcceptanceOracleResult { Pass = false, Reason = "journal benchmark evidence is required" };
        }
        if (benchmark.CycleBytes < 130L * 1024 * 1024 || benchmark.StallBytes < 63L * 1024 * 1024 || !benchmark.IdenticalDelta)
        {
            return new ProgressAcceptanceOracleResult { Pass = false, Reason = "journal envelope or doubled-history delta is below the measured contract" };
        }
        return new ProgressAcceptanceOracleResult { Pass = true, Reason = "transition and measured journal evidence are present" };
    }
}
