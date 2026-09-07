using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G812's project-neutral progress model.  The controller consumes durable
/// evidence; it never treats a pane, model response, delivery flag, or local
/// commit as completion on its own.  All timestamps are injected by callers so
/// deterministic tests can exercise equality and already-overdue boundaries.
/// </summary>
internal sealed record ProgressSupervisionEvidence
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; init; } = ProgressSupervisionConstants.SchemaVersion;
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("project")] public required string Project { get; init; }
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("unit")] public required string Unit { get; init; }
    [JsonPropertyName("phase")] public required string Phase { get; init; }
    [JsonPropertyName("episode")] public required string Episode { get; init; }
    [JsonPropertyName("task_id")] public string? TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("claim_actor")] public string? ClaimActor { get; init; }
    [JsonPropertyName("claim_status")] public string ClaimStatus { get; init; } = "unknown";
    [JsonPropertyName("selected_action")] public string SelectedAction { get; init; } = "unknown";
    [JsonPropertyName("selected_reason")] public string SelectedReason { get; init; } = "unknown";
    [JsonPropertyName("work_root")] public string WorkRoot { get; init; } = "unknown";
    [JsonPropertyName("relevant_commit")] public string RelevantCommit { get; init; } = "unknown";
    [JsonPropertyName("base_commit")] public string BaseCommit { get; init; } = "unknown";
    [JsonPropertyName("remote_pr")] public string RemotePr { get; init; } = "unknown";
    [JsonPropertyName("pr_head")] public string PrHead { get; init; } = "unknown";
    [JsonPropertyName("review_identity")] public string ReviewIdentity { get; init; } = "unknown";
    [JsonPropertyName("report_identity")] public string ReportIdentity { get; init; } = "unknown";
    [JsonPropertyName("ack_identity")] public string AckIdentity { get; init; } = "unknown";
    [JsonPropertyName("closeout_identity")] public string CloseoutIdentity { get; init; } = "unknown";
    [JsonPropertyName("observed_at")] public required DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("freshness_at")] public DateTimeOffset? FreshnessAt { get; init; }
    [JsonPropertyName("eligible_at")] public required DateTimeOffset EligibleAt { get; init; }
    [JsonPropertyName("last_progress_at")] public DateTimeOffset? LastProgressAt { get; init; }
    [JsonPropertyName("deadline_at")] public DateTimeOffset? DeadlineAt { get; init; }
    [JsonPropertyName("floor_seconds")] public int FloorSeconds { get; init; } = ProgressSupervisionConstants.DefaultFloorSeconds;
    [JsonPropertyName("jitter_seconds")] public int JitterSeconds { get; init; }
    [JsonPropertyName("max_sweep_seconds")] public double MaxSweepSeconds { get; init; }
    [JsonPropertyName("recovery_attempts")] public int RecoveryAttempts { get; init; }
    [JsonPropertyName("last_recovery_at")] public DateTimeOffset? LastRecoveryAt { get; init; }
    [JsonPropertyName("attempt_result")] public string AttemptResult { get; init; } = "none";
    [JsonPropertyName("next_action")] public string NextAction { get; init; } = "observe";
    [JsonPropertyName("owner")] public string Owner { get; init; } = "orchestrator";
    [JsonPropertyName("severity")] public string Severity { get; init; } = "unknown";
    [JsonPropertyName("recipient_role")] public string RecipientRole { get; init; } = "unknown";
    [JsonPropertyName("recipient_identity")] public string RecipientIdentity { get; init; } = "unknown";
    [JsonPropertyName("source_failures")] public IReadOnlyList<string> SourceFailures { get; init; } = [];
    [JsonPropertyName("evidence_links")] public IReadOnlyList<string> EvidenceLinks { get; init; } = [];
    [JsonPropertyName("phase_skips")] public IReadOnlyList<ProgressPhaseSkipEvidence> PhaseSkips { get; init; } = [];
    [JsonPropertyName("semantic_fingerprint")] public string SemanticFingerprint { get; init; } = "unknown";

    public string StableFingerprint()
    {
        var material = string.Join("|", Domain, Team, Project, Repo, Unit, Phase, Episode,
            TaskId ?? "", ResultNonce ?? "", ClaimActor ?? "", ClaimStatus, SelectedAction,
            SelectedReason, WorkRoot, RelevantCommit, BaseCommit, RemotePr, PrHead,
            ReviewIdentity, ReportIdentity, AckIdentity, CloseoutIdentity,
            EligibleAt.ToUniversalTime().ToString("O"), LastProgressAt?.ToUniversalTime().ToString("O") ?? "",
            DeadlineAt?.ToUniversalTime().ToString("O") ?? "",
            Owner, Severity, RecipientRole, RecipientIdentity, string.Join(",", SourceFailures),
            string.Join(",", EvidenceLinks), string.Join(",", PhaseSkips.Select(skip => skip.StableKey())));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}

internal sealed record ProgressSupervisionOptions
{
    public int DetectionFloorSeconds { get; init; } = ProgressSupervisionConstants.DefaultFloorSeconds;
    public int JitterSeconds { get; init; }
    public double MaxSweepSeconds { get; init; }
    public int RecoveryBackoffSeconds { get; init; } = ProgressSupervisionConstants.RecoveryBackoffSeconds;
    public int MaxRecoveryAttempts { get; init; } = ProgressSupervisionConstants.MaxRecoveryAttempts;
    public bool RequireQualifiedBound { get; init; } = true;
}

internal sealed record ProgressSupervisionEvaluation
{
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("healthy")] public bool Healthy { get; init; }
    [JsonPropertyName("unknown")] public bool Unknown { get; init; }
    [JsonPropertyName("open_count")] public int OpenCount { get; init; }
    [JsonPropertyName("overdue_count")] public int OverdueCount { get; init; }
    [JsonPropertyName("oldest_overdue_seconds")] public long? OldestOverdueSeconds { get; init; }
    [JsonPropertyName("last_successful_floor_at")] public DateTimeOffset? LastSuccessfulFloorAt { get; init; }
    [JsonPropertyName("last_successful_writer")] public string? LastSuccessfulWriter { get; init; }
    [JsonPropertyName("detection_bound_seconds")] public int DetectionBoundSeconds { get; init; }
    [JsonPropertyName("deadline_at")] public DateTimeOffset DeadlineAt { get; init; }
    [JsonPropertyName("detection_at")] public DateTimeOffset DetectionAt { get; init; }
    [JsonPropertyName("recovery_due_at")] public DateTimeOffset RecoveryDueAt { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("next_action")] public required string NextAction { get; init; }
    [JsonPropertyName("attempt")] public int Attempt { get; init; }
    [JsonPropertyName("attempt_result")] public required string AttemptResult { get; init; }
    [JsonPropertyName("semantic_fingerprint")] public required string SemanticFingerprint { get; init; }
    [JsonPropertyName("missing_evidence")] public IReadOnlyList<string> MissingEvidence { get; init; } = [];
    [JsonPropertyName("source_failures")] public IReadOnlyList<string> SourceFailures { get; init; } = [];
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record ProgressRecoveryRequest
{
    public required ProgressSupervisionEvidence Evidence { get; init; }
    public required string Action { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string IdempotencyKey { get; init; }
}

internal sealed record ProgressRecoveryResult
{
    public required bool Applied { get; init; }
    public required bool AlreadyConverged { get; init; }
    public required string Outcome { get; init; }
    public required string Summary { get; init; }
    public string? Error { get; init; }
}

internal interface IProgressRecoveryAdapter
{
    ProgressRecoveryResult Probe(ProgressRecoveryRequest request);
    ProgressRecoveryResult Redispatch(ProgressRecoveryRequest request);
    ProgressRecoveryResult Escalate(ProgressRecoveryRequest request);
}

internal sealed class ProgressSupervisionController
{
    private readonly ProgressSupervisionOptions options;
    private readonly IProgressRecoveryAdapter? recovery;

    public ProgressSupervisionController(
        ProgressSupervisionOptions? options = null,
        IProgressRecoveryAdapter? recovery = null)
    {
        this.options = options ?? new ProgressSupervisionOptions();
        this.recovery = recovery;
    }

    public ProgressSupervisionEvaluation Evaluate(
        ProgressSupervisionEvidence evidence,
        DateTimeOffset now,
        ProgressSupervisionEvaluation? previous = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var normalizedNow = now.ToUniversalTime();
        var bound = ProgressSupervisionConstants.ComputeDetectionBound(
            evidence.FloorSeconds > 0 ? evidence.FloorSeconds : options.DetectionFloorSeconds,
            evidence.JitterSeconds + options.JitterSeconds,
            Math.Max(evidence.MaxSweepSeconds, options.MaxSweepSeconds));
        var deadline = evidence.DeadlineAt ?? evidence.EligibleAt.AddSeconds(
            ProgressSupervisionConstants.PhaseDeadlineSeconds(evidence.Phase));
        var detection = deadline.AddSeconds(bound);
        var recoveryDue = detection.AddSeconds(ProgressSupervisionConstants.RecoveryGraceSeconds);
        var missing = MissingEvidence(evidence);
        if (evidence.SourceFailures.Count > 0)
        {
            missing = [.. missing, .. evidence.SourceFailures.Select(failure => $"source:{failure}")];
        }
        var stale = evidence.FreshnessAt is null
            || normalizedNow - evidence.FreshnessAt.Value > TimeSpan.FromSeconds(bound);
        if (stale && !missing.Contains("freshness", StringComparer.Ordinal))
        {
            missing = [.. missing, "freshness"];
        }

        var fingerprint = evidence.SemanticFingerprint is "" or "unknown"
            ? evidence.StableFingerprint()
            : evidence.SemanticFingerprint;
        var changed = previous is not null
            && !string.Equals(previous.SemanticFingerprint, fingerprint, StringComparison.Ordinal);
        var overdue = normalizedNow >= detection;
        var open = missing.Count > 0 || overdue;
        var action = "none";
        var next = open ? "orchestrator must retain the unit and classify the missing transition" : "observe";
        var attempt = evidence.RecoveryAttempts;
        var result = evidence.AttemptResult;
        var owner = string.IsNullOrWhiteSpace(evidence.Owner) ? "orchestrator" : evidence.Owner;

        if (overdue)
        {
            if (changed)
            {
                attempt = 0;
                result = "evidence-version-changed";
            }

            var backoff = evidence.LastRecoveryAt is { } last
                ? last.AddSeconds(options.RecoveryBackoffSeconds)
                : normalizedNow;
            if (attempt < options.MaxRecoveryAttempts && normalizedNow >= backoff)
            {
                action = attempt == 0 ? "canonical-recipient-status-probe" : "authorized-canonical-recovery";
                next = action;
                if (recovery is not null)
                {
                    var request = new ProgressRecoveryRequest
                    {
                        Evidence = evidence,
                        Action = action,
                        At = normalizedNow,
                        IdempotencyKey = ProgressSupervisionConstants.IdempotencyKey(evidence),
                    };
                    var recoveryResult = attempt == 0
                        ? recovery.Probe(request)
                        : recovery.Redispatch(request);
                    result = recoveryResult.Outcome;
                    if (recoveryResult.Applied || recoveryResult.AlreadyConverged)
                    {
                        attempt++;
                    }
                }
            }
            else if (attempt >= options.MaxRecoveryAttempts)
            {
                action = "infrastructure-escalation";
                next = "escalate unresolved transition to architect with owner, deadline, and attempt evidence";
                if (recovery is not null)
                {
                    var escalation = recovery.Escalate(new ProgressRecoveryRequest
                    {
                        Evidence = evidence,
                        Action = action,
                        At = normalizedNow,
                        IdempotencyKey = ProgressSupervisionConstants.IdempotencyKey(evidence),
                    });
                    result = escalation.Outcome;
                }
            }
            else
            {
                action = "recovery-backoff";
                next = $"wait until {backoff:O}; retain deadline and unresolved evidence";
            }
        }

        var state = missing.Count > 0 ? "unknown" : overdue ? "overdue" : "healthy";
        var healthy = state == "healthy";
        var summary = healthy
            ? "All required evidence is fresh and no transition is overdue."
            : state == "unknown"
                ? $"Progress is unknown; required evidence is missing or stale: {string.Join(", ", missing)}."
                : $"Progress is overdue at the fixed deadline; action={action}, attempt={attempt}, result={result}.";
        return new ProgressSupervisionEvaluation
        {
            State = state,
            Healthy = healthy,
            Unknown = state == "unknown",
            OpenCount = open ? 1 : 0,
            OverdueCount = overdue ? 1 : 0,
            OldestOverdueSeconds = overdue ? Math.Max(0, (long)(normalizedNow - detection).TotalSeconds) : null,
            LastSuccessfulFloorAt = healthy ? normalizedNow : null,
            LastSuccessfulWriter = healthy ? "progress-controller" : null,
            DetectionBoundSeconds = bound,
            DeadlineAt = deadline,
            DetectionAt = detection,
            RecoveryDueAt = recoveryDue,
            Action = action,
            Owner = owner,
            NextAction = next,
            Attempt = attempt,
            AttemptResult = result,
            SemanticFingerprint = fingerprint,
            MissingEvidence = missing,
            SourceFailures = evidence.SourceFailures,
            Summary = summary,
        };
    }

    private static IReadOnlyList<string> MissingEvidence(ProgressSupervisionEvidence evidence)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(evidence.Domain)) missing.Add("domain");
        if (string.IsNullOrWhiteSpace(evidence.Team)) missing.Add("team");
        if (string.IsNullOrWhiteSpace(evidence.Project)) missing.Add("project");
        if (string.IsNullOrWhiteSpace(evidence.Repo)) missing.Add("repo");
        if (string.IsNullOrWhiteSpace(evidence.Unit)) missing.Add("unit");
        if (string.IsNullOrWhiteSpace(evidence.Phase)) missing.Add("phase");
        if (string.IsNullOrWhiteSpace(evidence.Episode)) missing.Add("episode");
        if (string.Equals(evidence.ClaimStatus, "unknown", StringComparison.OrdinalIgnoreCase)) missing.Add("claim");
        if (string.Equals(evidence.WorkRoot, "unknown", StringComparison.OrdinalIgnoreCase)) missing.Add("work_root");
        if (evidence.LastProgressAt is null) missing.Add("last_progress");
        if (string.Equals(evidence.RelevantCommit, "unknown", StringComparison.OrdinalIgnoreCase)
            && string.Equals(evidence.RemotePr, "unknown", StringComparison.OrdinalIgnoreCase)
            && !HasQualifiedAbsence(evidence, "commit_or_pr")) missing.Add("commit_or_pr");
        if (string.Equals(evidence.BaseCommit, "unknown", StringComparison.OrdinalIgnoreCase)
            && !HasQualifiedAbsence(evidence, "base_commit")) missing.Add("base_commit");
        if (string.Equals(evidence.PrHead, "unknown", StringComparison.OrdinalIgnoreCase)
            && !HasQualifiedAbsence(evidence, "pr_head")) missing.Add("pr_head");
        if (string.Equals(evidence.ReviewIdentity, "unknown", StringComparison.OrdinalIgnoreCase)
            && !HasQualifiedAbsence(evidence, "review")) missing.Add("review");
        if (string.Equals(evidence.ReportIdentity, "unknown", StringComparison.OrdinalIgnoreCase)
            && !HasQualifiedAbsence(evidence, "report")) missing.Add("report");
        if (string.Equals(evidence.AckIdentity, "unknown", StringComparison.OrdinalIgnoreCase)
            && !HasQualifiedAbsence(evidence, "ack")) missing.Add("ack");
        if (string.Equals(evidence.CloseoutIdentity, "unknown", StringComparison.OrdinalIgnoreCase)
            && !HasQualifiedAbsence(evidence, "closeout")) missing.Add("closeout");
        return missing;
    }

    private static bool HasQualifiedAbsence(ProgressSupervisionEvidence evidence, string field) =>
        evidence.EvidenceLinks.Any(link =>
            link.StartsWith($"absence:{field}", StringComparison.OrdinalIgnoreCase)
            || link.StartsWith($"source-absent:{field}", StringComparison.OrdinalIgnoreCase));
}

internal sealed record ProgressPhaseSkipEvidence
{
    [JsonPropertyName("from_phase")] public required string FromPhase { get; init; }
    [JsonPropertyName("to_phase")] public required string ToPhase { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("approved_by")] public required string ApprovedBy { get; init; }
    [JsonPropertyName("evidence_link")] public required string EvidenceLink { get; init; }
    [JsonPropertyName("at")] public required DateTimeOffset At { get; init; }

    public string StableKey() => string.Join("/", FromPhase, ToPhase, Reason, ApprovedBy, EvidenceLink, At.ToUniversalTime().ToString("O"));

    public bool IsTypedAndAuthorized() =>
        !string.IsNullOrWhiteSpace(FromPhase)
        && !string.IsNullOrWhiteSpace(ToPhase)
        && !string.IsNullOrWhiteSpace(Reason)
        && (string.Equals(ApprovedBy, "architect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ApprovedBy, "orchestrator", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(EvidenceLink);
}

internal static class ProgressSupervisionConstants
{
    public const string SchemaVersion = "g812-progress/v1";
    public const int DefaultFloorSeconds = 30;
    public const int RecoveryGraceSeconds = 30;
    public const int RecoveryBackoffSeconds = 30;
    public const int MaxRecoveryAttempts = 2;

    public static int ComputeDetectionBound(int floorSeconds, int jitterSeconds, double maxSweepSeconds) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, floorSeconds) + Math.Max(0, jitterSeconds) + Math.Max(0, maxSweepSeconds)) + 1);

    public static int PhaseDeadlineSeconds(string phase) => phase.Trim().ToLowerInvariant() switch
    {
        "eligible-dispatch-to-claim" or "dispatch-to-claim" => 60,
        "claim-to-selected" => 60,
        "selected-to-relevant-commit" => 300,
        "commit-to-remote-pr" => 600,
        "pr-ready-to-independent-verdict" => 900,
        "verified-request-update-to-repair-selection" => 60,
        "terminal-disposition-to-closeout-report-receipt" or "terminal-disposition-to-closeout" => 120,
        "merge-ready-to-closeout" => 120,
        "completed-to-next-eligible-work" => 60,
        _ => 120,
    };

    public static string IdempotencyKey(ProgressSupervisionEvidence evidence) =>
        string.Join("/", evidence.Domain, evidence.Team, evidence.Repo, evidence.Unit, evidence.Phase, evidence.TaskId ?? evidence.Unit, evidence.ResultNonce ?? evidence.Episode);

    public static bool IsQualified(ProgressSupervisionOptions options, out string reason)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bound = ComputeDetectionBound(
            options.DetectionFloorSeconds,
            options.JitterSeconds,
            options.MaxSweepSeconds);
        if (bound > 60)
        {
            reason = $"qualified detection bound D={bound}s exceeds the required 60s limit";
            return false;
        }

        foreach (var phase in new[]
                 {
                     "dispatch-to-claim", "claim-to-selected", "selected-to-relevant-commit",
                     "commit-to-remote-pr", "pr-ready-to-independent-verdict",
                     "verified-request-update-to-repair-selection", "terminal-disposition-to-closeout",
                     "merge-ready-to-closeout", "completed-to-next-eligible-work",
                 })
        {
            if (PhaseDeadlineSeconds(phase) <= 0)
            {
                reason = $"phase {phase} has no positive deadline";
                return false;
            }
        }

        reason = $"qualified detection bound D={bound}s (F={options.DetectionFloorSeconds}s,J={options.JitterSeconds}s,P={options.MaxSweepSeconds}s)";
        return true;
    }
}

internal sealed class RecordingProgressRecoveryAdapter : IProgressRecoveryAdapter
{
    private readonly List<ProgressRecoveryRequest> requests = [];
    public IReadOnlyList<ProgressRecoveryRequest> Requests => requests;

    public ProgressRecoveryResult Probe(ProgressRecoveryRequest request)
    {
        requests.Add(request);
        return new ProgressRecoveryResult { Applied = true, AlreadyConverged = false, Outcome = "status-probe-recorded", Summary = "Canonical recipient status probe recorded." };
    }

    public ProgressRecoveryResult Redispatch(ProgressRecoveryRequest request)
    {
        requests.Add(request);
        return new ProgressRecoveryResult { Applied = true, AlreadyConverged = false, Outcome = "authorized-recovery-recorded", Summary = "Authorized canonical recovery recorded." };
    }

    public ProgressRecoveryResult Escalate(ProgressRecoveryRequest request)
    {
        requests.Add(request);
        return new ProgressRecoveryResult { Applied = true, AlreadyConverged = false, Outcome = "infrastructure-escalation-recorded", Summary = "Infrastructure escalation recorded." };
    }
}
