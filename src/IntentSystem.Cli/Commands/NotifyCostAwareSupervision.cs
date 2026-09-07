using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G810's cost-aware contract is the deterministic composition layer over the
/// existing notify supervisor.  It deliberately contains no provider, pane,
/// process, label, or lifecycle mutation.  Callers provide durable
/// observations and this type classifies them, computes the measured bound,
/// and records what remains open.
/// </summary>
internal static class NotifyCostAwareSupervisionContract
{
    public const string SchemaVersion = "g810-cost-aware-supervision/v1";
    public const int IndependentFloorSeconds = 30;
    public const int DefaultJitterSeconds = 5;
    public const int ReceiptAckTimeoutSeconds = 30;
    public const int DelegationExecutionWindowSeconds = 300;
    public const int PublishedStallSeconds = 120;
    public const int ReviewVerdictGapSeconds = 60;
    public const int RecoveryEscalationSeconds = 120;
    public const int EscalationDispatchSeconds = 30;
    public const int OrdinaryWakeLimit = 2;
    public const int OrdinaryWakeWindowSeconds = 3600;
    public const int ReminderBackoffSeconds = 300;
    public const int MinimumCycleRecords = 108_474;
    public const long MinimumCycleBytes = 130L * 1024 * 1024;
    public const long MinimumStallBytes = 63L * 1024 * 1024;

    public static IReadOnlyList<NotifyCostAwareCoverageDefinition> CoverageManifest { get; } =
    [
        new()
        {
            Kind = "delegation-dispatch-delivery",
            AuthoritativeSource = "notify pending/delivery/report-outbox/continuation records",
            Identity = "domain/team + source entity + result nonce/revision",
            Eligibility = "durable pending record is accessible",
            Owner = "orchestrator",
            NextAction = "reconcile delivery, receipt and execution evidence",
        },
        new()
        {
            Kind = "completion",
            AuthoritativeSource = "notify report/outbox/continuation and completion-channel receipt",
            Identity = "task id + result nonce + artifact",
            Eligibility = "canonical report is durably recorded",
            Owner = "orchestrator",
            NextAction = "verify receipt/consumption; retain unresolved edge",
        },
        new()
        {
            Kind = "review-verdict",
            AuthoritativeSource = "durable GitHub review/timeline and workflow evidence",
            Identity = "repository + pull request + exact head + verdict identity",
            Eligibility = "head-bound verdict is observable",
            Owner = "reviewer/orchestrator",
            NextAction = "classify current, superseded, stale or forged verdict",
        },
        new()
        {
            Kind = "blocked-prompt",
            AuthoritativeSource = "recorded prompt identity/state sequence/hash",
            Identity = "pane/reader + prompt sequence + observed text hash",
            Eligibility = "prompt identity and source are durable",
            Owner = "orchestrator",
            NextAction = "apply canonical prompt policy or escalate judgement",
        },
        new()
        {
            Kind = "published-unit-stall",
            AuthoritativeSource = "queue/publish/intake and canonical dispatch evidence",
            Identity = "domain/team + unit + publication/intake revision",
            Eligibility = "published or intake record has no canonical progress",
            Owner = "orchestrator",
            NextAction = "classify owning state without resending intake",
        },
    ];

    public static NotifyCostAwareResult Evaluate(NotifyCostAwareObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var now = observation.Now.ToUniversalTime();
        var samples = observation.SweepSamplesSeconds
            .Where(sample => sample >= 0)
            .ToArray();
        var measuredP = Math.Max(observation.MeasuredMaxSweepSeconds ?? 0, samples.DefaultIfEmpty(0).Max());
        var bound = ComputeDetectionBound(observation.FloorSeconds, observation.JitterSeconds, measuredP);
        var profileQualified = observation.SteadySweepCount >= 100
            && observation.ColdRestartSweepCount >= 3
            && measuredP > 0
            && bound <= 60
            && observation.BaselineCycleRecords >= MinimumCycleRecords
            && observation.BaselineCycleBytes >= MinimumCycleBytes
            && observation.BaselineStallBytes >= MinimumStallBytes
            && observation.DoubledCycleRecords >= observation.BaselineCycleRecords * 2
            && observation.DoubledStallBytes >= observation.BaselineStallBytes * 2
            && observation.DoubledDeltaRecords == observation.BaselineDeltaRecords;

        var timing = new NotifyCostAwareTiming
        {
            FloorSeconds = observation.FloorSeconds,
            JitterSeconds = observation.JitterSeconds,
            MeasuredMaximumSweepSeconds = measuredP,
            DetectionBoundSeconds = bound,
            ImmediateEscalationBoundSeconds = bound + EscalationDispatchSeconds,
            RoutineEscalationBoundSeconds = bound + RecoveryEscalationSeconds + EscalationDispatchSeconds,
            DeclaredBoundSeconds = observation.DeclaredBoundSeconds,
            DeclaredBoundMatchesMeasured = observation.DeclaredBoundSeconds is null || observation.DeclaredBoundSeconds == bound,
            Qualified = profileQualified,
            QualificationReason = profileQualified
                ? $"qualified measured profile D={bound}s (F={observation.FloorSeconds}s,J={observation.JitterSeconds}s,P={measuredP:F6}s)"
                : BuildQualificationReason(observation, measuredP, bound),
        };

        var floorDue = observation.FloorDueAt is { } floorAt && now >= floorAt;
        var eventLost = observation.EventWaitBlocked || !observation.EventStreamAvailable;
        var eventFirst = string.Equals(observation.Trigger, "event", StringComparison.OrdinalIgnoreCase)
            && observation.EventObserved;
        var eventFloor = new NotifyCostAwareEventFloor
        {
            EventObserved = eventFirst,
            EventStreamAvailable = observation.EventStreamAvailable,
            EventWaitBlocked = observation.EventWaitBlocked,
            IndependentFloorScheduled = true,
            FloorDue = floorDue,
            EventLost = eventLost,
            Action = eventLost && floorDue
                ? "independent-floor-reconcile"
                : eventFirst
                    ? "event-first-reconcile"
                    : floorDue
                        ? "independent-floor-reconcile"
                        : "retain-next-floor-deadline",
            Summary = eventLost
                ? "event path is unavailable; the independent floor remains authoritative"
                : eventFirst
                    ? "durable event hint was observed; the independent floor remains scheduled"
                    : "no event observation; the independent floor remains scheduled",
        };

        var observedKinds = observation.ObservedSourceKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coverage = CoverageManifest
            .Select(definition => new NotifyCostAwareCoverageObservation
            {
                Definition = definition,
                Observed = observedKinds.Contains(definition.Kind),
                Status = observedKinds.Contains(definition.Kind) ? "covered" : "pending-source-observation",
            })
            .ToArray();

        var budget = NotifyCostAwareBudgetLedger.Summarize(
            observation.WakeAttempts,
            now,
            OrdinaryWakeLimit,
            OrdinaryWakeWindowSeconds);
        var corruption = NotifyCostAwareCorruption.Evaluate(
            observation.UnreadableRecords,
            observation.PrimaryAlive,
            observation.CursorReadable,
            observation.AckReadable,
            observation.StorageWritable);
        var identity = NotifyCostAwareIdentity.Evaluate(
            observation.CurrentSemanticFields,
            observation.PreviousSemanticFingerprint,
            observation.CurrentSemanticVersion);
        var recovery = new NotifyCostAwareRecovery
        {
            Open = observation.PendingRecovery || corruption.Degraded || eventFloor.FloorDue,
            Owner = "orchestrator",
            RetryScheduleSeconds = [5, 10, 20, 30],
            ReceiptAckTimeoutSeconds = ReceiptAckTimeoutSeconds,
            EscalationAfterSeconds = RecoveryEscalationSeconds,
            EscalationDispatchDeadlineSeconds = EscalationDispatchSeconds,
            Action = corruption.Degraded
                ? "dry-run repair-unreadable then preserve evidence"
                : eventFloor.FloorDue
                    ? eventFloor.Action
                    : observation.PendingRecovery
                        ? "retry-same-identity-or-escalate"
                        : "none",
        };

        var safety = new NotifyCostAwareSafety
        {
            G304PublicationBlocked = observation.G304PublicationBlocked,
            MonitoringContinues = true,
            MutationAttempted = false,
            Action = observation.G304PublicationBlocked
                ? "classify-protected-host-state-and-route-isolated-main-checkout"
                : "none",
            Summary = observation.G304PublicationBlocked
                ? "publication is blocked by protected foreign state; monitoring remains independent"
                : "no protected publication blocker observed",
        };
        var transferability = new NotifyCostAwareTransferability
        {
            TransportMode = observation.TransportMode,
            ExternalReader = observation.ExternalReader,
            TopologyResolved = observation.TopologyResolved,
            ReaderReachable = observation.ReaderReachable,
            WriterIdentityVerified = observation.WriterIdentityVerified,
            Transferable = observation.TopologyResolved && observation.ReaderReachable && observation.WriterIdentityVerified,
            Action = observation.TopologyResolved && observation.ReaderReachable && observation.WriterIdentityVerified
                ? "resume-from-recorded-role-reader-root"
                : "record-topology-or-reader-gap; do-not-claim-transfer",
        };

        var degraded = corruption.Degraded || observation.SourceOutage || observation.CorruptEvidence;
        var state = degraded
            ? "degraded"
            : !profileQualified
                ? "unknown"
                : eventFloor.FloorDue || observation.PendingRecovery
                    ? "open"
                    : "healthy";
        return new NotifyCostAwareResult
        {
            SchemaVersion = SchemaVersion,
            State = state,
            Healthy = state == "healthy",
            Unknown = state == "unknown",
            ReadOnly = true,
            NoModelInvocations = true,
            EventFloor = eventFloor,
            Timing = timing,
            Coverage = coverage,
            CoverageLossless = coverage.All(item => item.Observed) && !degraded,
            Budget = budget,
            Deduplication = identity,
            Corruption = corruption,
            Recovery = recovery,
            Safety = safety,
            Transferability = transferability,
            Summary = state switch
            {
                "healthy" => "measured cost-aware supervision is healthy; all durable sources and independent floor are covered",
                "degraded" => "supervision is degraded; preserve valid evidence and route bounded repair",
                "open" => "supervision remains open with an owner and deadline; no liveness or delivery fact completes work",
                _ => timing.QualificationReason,
            },
        };
    }

    public static int ComputeDetectionBound(int floorSeconds, int jitterSeconds, double measuredSweepSeconds) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, floorSeconds) + Math.Max(0, jitterSeconds) + Math.Max(0, measuredSweepSeconds)) + 1);

    public static NotifyCostAwareGuide BuildGuide() => new()
    {
        ContractVersion = SchemaVersion,
        Timing = "F=30s independent floor; J=5s; P is the measured maximum complete sweep; D=ceil(F+J+P)+1; qualify D<=60 only after 100 steady and 3 cold/restart sweeps at baseline and doubled journal scale.",
        Coverage = CoverageManifest,
        Cost = "ordinary supervision-originated specialist wakes are limited to two per role per rolling 60 minutes; changed critical findings and due recovery bypass that ordinary budget with an identity/version/reason exception; no hard global cap is claimed under arbitrary critical load.",
        Recovery = "receipt ack is separate from execution completion; retry 5/10/20/30 seconds, escalate after two failed authorized attempts or 120 seconds unresolved, and dispatch escalation within 30 seconds.",
        Corruption = "bad bytes, cursor/ack corruption, missing heartbeat or source outage are degraded/unknown; preserve valid records and use installed dry-run-first repair, never a read-side rewrite.",
        Safety = "G304 protected foreign state blocks publication only; deterministic monitoring and the independent floor continue, with one attributed isolated-checkout next action.",
        Transferability = "record resident/reader/root/writer identity; Orca external readers and Herdr residents use the same identity-bound durable contract without topology forgery.",
    };

    private static string BuildQualificationReason(NotifyCostAwareObservation observation, double p, int d)
    {
        var reasons = new List<string>();
        if (observation.SteadySweepCount < 100) reasons.Add($"steady sweeps={observation.SteadySweepCount}<100");
        if (observation.ColdRestartSweepCount < 3) reasons.Add($"cold/restart sweeps={observation.ColdRestartSweepCount}<3");
        if (observation.BaselineCycleRecords < MinimumCycleRecords) reasons.Add($"cycle records={observation.BaselineCycleRecords}<{MinimumCycleRecords}");
        if (observation.BaselineCycleBytes < MinimumCycleBytes) reasons.Add($"cycle bytes={observation.BaselineCycleBytes}<{MinimumCycleBytes}");
        if (observation.BaselineStallBytes < MinimumStallBytes) reasons.Add($"stall bytes={observation.BaselineStallBytes}<{MinimumStallBytes}");
        if (observation.DoubledCycleRecords < observation.BaselineCycleRecords * 2) reasons.Add("doubled cycle scale missing");
        if (observation.DoubledStallBytes < observation.BaselineStallBytes * 2) reasons.Add("doubled stall scale missing");
        if (observation.DoubledDeltaRecords != observation.BaselineDeltaRecords) reasons.Add("doubled new-event delta changed");
        if (d > 60) reasons.Add($"D={d}s exceeds 60s target (P={p:F6}s)");
        return "not-qualified: " + string.Join("; ", reasons);
    }
}

internal sealed record NotifyCostAwareCoverageDefinition
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("authoritative_source")] public required string AuthoritativeSource { get; init; }
    [JsonPropertyName("identity")] public required string Identity { get; init; }
    [JsonPropertyName("eligibility")] public required string Eligibility { get; init; }
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("next_action")] public required string NextAction { get; init; }
}

internal sealed record NotifyCostAwareCoverageObservation
{
    [JsonPropertyName("definition")] public required NotifyCostAwareCoverageDefinition Definition { get; init; }
    [JsonPropertyName("observed")] public bool Observed { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
}

internal sealed record NotifyCostAwareObservation
{
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
    public string Trigger { get; init; } = "interval";
    public int FloorSeconds { get; init; } = NotifyCostAwareSupervisionContract.IndependentFloorSeconds;
    public int JitterSeconds { get; init; } = NotifyCostAwareSupervisionContract.DefaultJitterSeconds;
    public double? MeasuredMaxSweepSeconds { get; init; }
    public int SteadySweepCount { get; init; }
    public int ColdRestartSweepCount { get; init; }
    public long BaselineCycleRecords { get; init; }
    public long BaselineCycleBytes { get; init; }
    public long BaselineStallBytes { get; init; }
    public long DoubledCycleRecords { get; init; }
    public long DoubledStallBytes { get; init; }
    public long BaselineDeltaRecords { get; init; }
    public long DoubledDeltaRecords { get; init; }
    public int? DeclaredBoundSeconds { get; init; }
    public IReadOnlyList<double> SweepSamplesSeconds { get; init; } = [];
    public DateTimeOffset? FloorDueAt { get; init; }
    public bool EventObserved { get; init; }
    public bool EventStreamAvailable { get; init; } = true;
    public bool EventWaitBlocked { get; init; }
    public bool PendingRecovery { get; init; }
    public bool PrimaryAlive { get; init; } = true;
    public bool CursorReadable { get; init; } = true;
    public bool AckReadable { get; init; } = true;
    public bool StorageWritable { get; init; } = true;
    public bool SourceOutage { get; init; }
    public bool CorruptEvidence { get; init; }
    public bool G304PublicationBlocked { get; init; }
    public string TransportMode { get; init; } = "herdr-only";
    public bool ExternalReader { get; init; }
    public bool TopologyResolved { get; init; } = true;
    public bool ReaderReachable { get; init; } = true;
    public bool WriterIdentityVerified { get; init; } = true;
    public IReadOnlySet<string> ObservedSourceKinds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<NotifyCostAwareWakeAttempt> WakeAttempts { get; init; } = [];
    public IReadOnlyList<NotifySupervisionUnreadableRecord> UnreadableRecords { get; init; } = [];
    public IReadOnlyDictionary<string, string> CurrentSemanticFields { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public string? PreviousSemanticFingerprint { get; init; }
    public string CurrentSemanticVersion { get; init; } = "version-1";
}

internal sealed record NotifyCostAwareTiming
{
    [JsonPropertyName("floor_seconds")] public required int FloorSeconds { get; init; }
    [JsonPropertyName("jitter_seconds")] public required int JitterSeconds { get; init; }
    [JsonPropertyName("measured_max_sweep_seconds")] public required double MeasuredMaximumSweepSeconds { get; init; }
    [JsonPropertyName("detection_bound_seconds")] public required int DetectionBoundSeconds { get; init; }
    [JsonPropertyName("immediate_escalation_bound_seconds")] public required int ImmediateEscalationBoundSeconds { get; init; }
    [JsonPropertyName("routine_escalation_bound_seconds")] public required int RoutineEscalationBoundSeconds { get; init; }
    [JsonPropertyName("declared_bound_seconds")] public int? DeclaredBoundSeconds { get; init; }
    [JsonPropertyName("declared_bound_matches_measured")] public bool DeclaredBoundMatchesMeasured { get; init; }
    [JsonPropertyName("qualified")] public bool Qualified { get; init; }
    [JsonPropertyName("qualification_reason")] public required string QualificationReason { get; init; }
}

internal sealed record NotifyCostAwareEventFloor
{
    [JsonPropertyName("event_observed")] public bool EventObserved { get; init; }
    [JsonPropertyName("event_stream_available")] public bool EventStreamAvailable { get; init; }
    [JsonPropertyName("event_wait_blocked")] public bool EventWaitBlocked { get; init; }
    [JsonPropertyName("independent_floor_scheduled")] public bool IndependentFloorScheduled { get; init; }
    [JsonPropertyName("floor_due")] public bool FloorDue { get; init; }
    [JsonPropertyName("event_lost")] public bool EventLost { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyCostAwareWakeAttempt
{
    public required string Role { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string SemanticVersion { get; init; }
    public bool Critical { get; init; }
    public bool Failed { get; init; }
}

internal sealed record NotifyCostAwareBudgetSummary
{
    [JsonPropertyName("ordinary_limit")] public int OrdinaryLimit { get; init; }
    [JsonPropertyName("window_seconds")] public int WindowSeconds { get; init; }
    [JsonPropertyName("ordinary_attempts_by_role")] public IReadOnlyDictionary<string, int> OrdinaryAttemptsByRole { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    [JsonPropertyName("failed_attempts_by_role")] public IReadOnlyDictionary<string, int> FailedAttemptsByRole { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    [JsonPropertyName("critical_exceptions")] public int CriticalExceptions { get; init; }
    [JsonPropertyName("ordinary_budget_exhausted_roles")] public IReadOnlyList<string> OrdinaryBudgetExhaustedRoles { get; init; } = [];
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal static class NotifyCostAwareBudgetLedger
{
    public static NotifyCostAwareBudgetSummary Summarize(
        IReadOnlyList<NotifyCostAwareWakeAttempt> attempts,
        DateTimeOffset now,
        int ordinaryLimit,
        int windowSeconds)
    {
        var cutoff = now.ToUniversalTime().AddSeconds(-windowSeconds);
        var recent = attempts.Where(attempt => attempt.At.ToUniversalTime() >= cutoff).ToArray();
        var ordinary = recent.Where(attempt => !attempt.Critical)
            .GroupBy(attempt => attempt.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var failed = recent.Where(attempt => attempt.Failed)
            .GroupBy(attempt => attempt.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var exceptions = recent.Count(attempt => attempt.Critical);
        var exhausted = ordinary.Where(pair => pair.Value >= ordinaryLimit).Select(pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return new NotifyCostAwareBudgetSummary
        {
            OrdinaryLimit = ordinaryLimit,
            WindowSeconds = windowSeconds,
            OrdinaryAttemptsByRole = ordinary,
            FailedAttemptsByRole = failed,
            CriticalExceptions = exceptions,
            OrdinaryBudgetExhaustedRoles = exhausted,
            Summary = exhausted.Length == 0
                ? "ordinary wake budget remains available; critical exceptions are counted separately"
                : $"ordinary budget exhausted for {string.Join(", ", exhausted)}; retain/coalesce routine work and allow only identity-bound critical exceptions",
        };
    }
}

internal sealed record NotifyCostAwareDeduplication
{
    [JsonPropertyName("semantic_fingerprint")] public required string SemanticFingerprint { get; init; }
    [JsonPropertyName("semantic_version")] public required string SemanticVersion { get; init; }
    [JsonPropertyName("changed")] public bool Changed { get; init; }
    [JsonPropertyName("poll_time_excluded")] public bool PollTimeExcluded { get; init; }
    [JsonPropertyName("reminder_backoff_seconds")] public int ReminderBackoffSeconds { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal static class NotifyCostAwareIdentity
{
    public static NotifyCostAwareDeduplication Evaluate(
        IReadOnlyDictionary<string, string> fields,
        string? previousFingerprint,
        string semanticVersion)
    {
        var material = string.Join("\n", fields
            .Where(pair => !pair.Key.Contains("poll", StringComparison.OrdinalIgnoreCase)
                && !pair.Key.Contains("observed_at", StringComparison.OrdinalIgnoreCase)
                && !pair.Key.Contains("age", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        var changed = previousFingerprint is not null
            && !string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal);
        return new NotifyCostAwareDeduplication
        {
            SemanticFingerprint = fingerprint,
            SemanticVersion = semanticVersion,
            Changed = changed,
            PollTimeExcluded = true,
            ReminderBackoffSeconds = NotifyCostAwareSupervisionContract.ReminderBackoffSeconds,
            Summary = changed
                ? "changed semantic fields create a new actionable version despite the same coarse key"
                : "unchanged semantic fields remain one deduplicated episode",
        };
    }
}

internal sealed record NotifyCostAwareCorruption
{
    [JsonPropertyName("degraded")] public bool Degraded { get; init; }
    [JsonPropertyName("valid_records_preserved")] public bool ValidRecordsPreserved { get; init; }
    [JsonPropertyName("unreadable_count")] public int UnreadableCount { get; init; }
    [JsonPropertyName("primary_alive")] public bool PrimaryAlive { get; init; }
    [JsonPropertyName("cursor_readable")] public bool CursorReadable { get; init; }
    [JsonPropertyName("ack_readable")] public bool AckReadable { get; init; }
    [JsonPropertyName("storage_writable")] public bool StorageWritable { get; init; }
    [JsonPropertyName("repair_action")] public required string RepairAction { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }

    public static NotifyCostAwareCorruption Evaluate(
        IReadOnlyList<NotifySupervisionUnreadableRecord> unreadable,
        bool primaryAlive,
        bool cursorReadable,
        bool ackReadable,
        bool storageWritable)
    {
        var degraded = unreadable.Count > 0 || !primaryAlive || !cursorReadable || !ackReadable || !storageWritable;
        return new NotifyCostAwareCorruption
        {
            Degraded = degraded,
            ValidRecordsPreserved = true,
            UnreadableCount = unreadable.Count,
            PrimaryAlive = primaryAlive,
            CursorReadable = cursorReadable,
            AckReadable = ackReadable,
            StorageWritable = storageWritable,
            RepairAction = degraded ? "notify supervise repair-unreadable --dry-run then authorized write" : "none",
            Summary = degraded
                ? "corrupt, absent or unavailable evidence is degraded/unknown; valid records remain observable and no cursor is advanced past unseen data"
                : "no corruption or source/storage failure observed",
        };
    }
}

internal sealed record NotifyCostAwareRecovery
{
    [JsonPropertyName("open")] public bool Open { get; init; }
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("retry_schedule_seconds")] public IReadOnlyList<int> RetryScheduleSeconds { get; init; } = [];
    [JsonPropertyName("receipt_ack_timeout_seconds")] public int ReceiptAckTimeoutSeconds { get; init; }
    [JsonPropertyName("escalation_after_seconds")] public int EscalationAfterSeconds { get; init; }
    [JsonPropertyName("escalation_dispatch_deadline_seconds")] public int EscalationDispatchDeadlineSeconds { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
}

internal sealed record NotifyCostAwareSafety
{
    [JsonPropertyName("g304_publication_blocked")] public bool G304PublicationBlocked { get; init; }
    [JsonPropertyName("monitoring_continues")] public bool MonitoringContinues { get; init; }
    [JsonPropertyName("mutation_attempted")] public bool MutationAttempted { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyCostAwareTransferability
{
    [JsonPropertyName("transport_mode")] public required string TransportMode { get; init; }
    [JsonPropertyName("external_reader")] public bool ExternalReader { get; init; }
    [JsonPropertyName("topology_resolved")] public bool TopologyResolved { get; init; }
    [JsonPropertyName("reader_reachable")] public bool ReaderReachable { get; init; }
    [JsonPropertyName("writer_identity_verified")] public bool WriterIdentityVerified { get; init; }
    [JsonPropertyName("transferable")] public bool Transferable { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
}

internal sealed record NotifyCostAwareResult
{
    [JsonPropertyName("schema_version")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("healthy")] public bool Healthy { get; init; }
    [JsonPropertyName("unknown")] public bool Unknown { get; init; }
    [JsonPropertyName("read_only")] public bool ReadOnly { get; init; }
    [JsonPropertyName("no_model_invocations")] public bool NoModelInvocations { get; init; }
    [JsonPropertyName("event_floor")] public required NotifyCostAwareEventFloor EventFloor { get; init; }
    [JsonPropertyName("timing")] public required NotifyCostAwareTiming Timing { get; init; }
    [JsonPropertyName("coverage")] public IReadOnlyList<NotifyCostAwareCoverageObservation> Coverage { get; init; } = [];
    [JsonPropertyName("coverage_lossless")] public bool CoverageLossless { get; init; }
    [JsonPropertyName("budget")] public required NotifyCostAwareBudgetSummary Budget { get; init; }
    [JsonPropertyName("deduplication")] public required NotifyCostAwareDeduplication Deduplication { get; init; }
    [JsonPropertyName("corruption")] public required NotifyCostAwareCorruption Corruption { get; init; }
    [JsonPropertyName("recovery")] public required NotifyCostAwareRecovery Recovery { get; init; }
    [JsonPropertyName("safety")] public required NotifyCostAwareSafety Safety { get; init; }
    [JsonPropertyName("transferability")] public required NotifyCostAwareTransferability Transferability { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyCostAwareGuide
{
    [JsonPropertyName("contract_version")] public required string ContractVersion { get; init; }
    [JsonPropertyName("timing")] public required string Timing { get; init; }
    [JsonPropertyName("coverage")] public IReadOnlyList<NotifyCostAwareCoverageDefinition> Coverage { get; init; } = [];
    [JsonPropertyName("cost")] public required string Cost { get; init; }
    [JsonPropertyName("recovery")] public required string Recovery { get; init; }
    [JsonPropertyName("corruption")] public required string Corruption { get; init; }
    [JsonPropertyName("safety")] public required string Safety { get; init; }
    [JsonPropertyName("transferability")] public required string Transferability { get; init; }
}

internal sealed record NotifyCostAwareBenchmarkResult
{
    public required ProgressJournalBenchmarkResult Baseline { get; init; }
    public required ProgressJournalBenchmarkResult Doubled { get; init; }
    public bool SameAppendedDelta { get; init; }
    public bool IncrementalReadsBounded { get; init; }
    public bool Qualified { get; init; }
    public int DetectionBoundSeconds { get; init; }
    public double MeasuredPSeconds { get; init; }
}

internal static class NotifyCostAwareBenchmark
{
    public static NotifyCostAwareBenchmarkResult Run(string root)
    {
        var baseline = ProgressJournalBenchmark.Run(Path.Combine(root, "baseline"));
        var doubled = ProgressJournalBenchmark.Run(
            Path.Combine(root, "doubled"),
            new ProgressJournalBenchmarkOptions { RecordsPerHistory = 216_948, AppendedDeltaRecords = 108_474 });
        var sameDelta = baseline.DeltaRecords == doubled.DeltaRecords;
        var bounded = baseline.SteadyReadBoundedByDelta && doubled.SteadyReadBoundedByDelta;
        var p = Math.Max(baseline.MaxPSSeconds, doubled.MaxPSSeconds);
        var d = NotifyCostAwareSupervisionContract.ComputeDetectionBound(30, 5, p);
        return new NotifyCostAwareBenchmarkResult
        {
            Baseline = baseline,
            Doubled = doubled,
            SameAppendedDelta = sameDelta,
            IncrementalReadsBounded = bounded,
            Qualified = baseline.CycleRecords >= NotifyCostAwareSupervisionContract.MinimumCycleRecords
                && baseline.CycleBytes >= NotifyCostAwareSupervisionContract.MinimumCycleBytes
                && baseline.StallBytes >= NotifyCostAwareSupervisionContract.MinimumStallBytes
                && doubled.CycleRecords >= baseline.CycleRecords * 2
                && doubled.CycleBytes >= baseline.CycleBytes * 2
                && doubled.StallBytes >= baseline.StallBytes * 2
                && sameDelta
                && bounded
                && d <= 60,
            DetectionBoundSeconds = d,
            MeasuredPSeconds = p,
        };
    }
}

internal sealed record NotifyCostAwarePhaseProgress
{
    public required ProgressSupervisionEvaluation Evaluation { get; init; }
    public bool ReusableAcrossPhases { get; init; } = true;
}

internal static class NotifyCostAwarePhaseProgressSupervisor
{
    public static NotifyCostAwarePhaseProgress Evaluate(
        ProgressSupervisionEvidence evidence,
        DateTimeOffset now,
        ProgressSupervisionEvaluation? previous = null) => new()
        {
            Evaluation = new ProgressSupervisionController().Evaluate(evidence, now, previous),
        };
}
