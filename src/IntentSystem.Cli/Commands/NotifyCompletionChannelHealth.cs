using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only completion-channel health projection. It reports evidence and
/// qualification separately: missing/corrupt evidence is never presented as
/// healthy, and the bound is derived from configured floor, declared jitter,
/// and the measured complete-sweep sample.
/// </summary>
internal sealed record NotifyCompletionChannelHealth
{
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("last_floor_cycle_at")] public DateTimeOffset? LastFloorCycleAt { get; init; }
    [JsonPropertyName("live_writer")] public string? LiveWriter { get; init; }
    [JsonPropertyName("delivered_unreconciled_count")] public int DeliveredUnreconciledCount { get; init; }
    [JsonPropertyName("oldest_delivered_unreconciled_age_seconds")] public long? OldestDeliveredUnreconciledAgeSeconds { get; init; }
    [JsonPropertyName("last_settlement_at")] public DateTimeOffset? LastSettlementAt { get; init; }
    [JsonPropertyName("missing_return_ack_age_seconds")] public long? MissingReturnAckAgeSeconds { get; init; }
    [JsonPropertyName("resident_consumption_pending_age_seconds")] public long? ResidentConsumptionPendingAgeSeconds { get; init; }
    [JsonPropertyName("failure_owner")] public string? FailureOwner { get; init; }
    [JsonPropertyName("next_action")] public string? NextAction { get; init; }
    [JsonPropertyName("floor_seconds")] public int FloorSeconds { get; init; }
    [JsonPropertyName("jitter_seconds")] public int JitterSeconds { get; init; }
    [JsonPropertyName("max_sweep_seconds")] public double MaxSweepSeconds { get; init; }
    [JsonPropertyName("bound_seconds")] public int BoundSeconds { get; init; }
    [JsonPropertyName("configured_bound_seconds")] public int? ConfiguredBoundSeconds { get; init; }
    [JsonPropertyName("qualified")] public bool Qualified { get; init; }
    [JsonPropertyName("qualification_reason")] public required string QualificationReason { get; init; }
    [JsonPropertyName("measured_sweeps")] public int MeasuredSweeps { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }

    internal static NotifyCompletionChannelHealth Compute(
        string routingRoot,
        string domain,
        string team,
        DateTimeOffset now,
        int floorSeconds = 300,
        int jitterSeconds = 0,
        double maxSweepSeconds = 0,
        int measuredSweeps = 0,
        string? supervisionArtifactRoot = null,
        int? configuredBoundSeconds = null)
    {
        var pending = NotifyPendingDelegationStore.ReadAll(routingRoot, domain, team, out var pendingError);
        var outbox = NotifyReportOutboxStore.ReadAll(routingRoot, domain, team, out var outboxError);
        var acks = NotifyCompletionChannelStore.ReadAllAcks(routingRoot, domain, team, out var ackError);
        var supervision = supervisionArtifactRoot is null
            ? null
            : NotifySupervisionStore.Read(supervisionArtifactRoot, domain, team);
        var supervisionError = supervision is { Resolved: false } ? supervision.Error : null;
        var hasError = pendingError is not null || outboxError is not null || ackError is not null || supervisionError is not null;
        var deliveredUnreconciled = pending
            .Where(record => !record.ReportArrived && record.Disposition is null)
            .Where(record => outbox.Any(entry => string.Equals(entry.TaskId, record.TaskId, StringComparison.Ordinal)
                && string.Equals(entry.ResultNonce, record.ResultNonce, StringComparison.Ordinal)
                && string.Equals(entry.DeliveryState, "delivered", StringComparison.Ordinal)))
            .OrderBy(record => record.DispatchedAt)
            .ToArray();
        var completed = pending.Where(record => record.ReportArrived && record.ReportArtifact is not null).ToArray();
        var missingAck = completed
            .Where(record => string.Equals(record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
            .Where(record => !acks.Any(ack => string.Equals(ack.TaskId, record.TaskId, StringComparison.Ordinal)
                && string.Equals(ack.ResultNonce, record.ResultNonce, StringComparison.Ordinal)
                && string.Equals(ack.Artifact, record.ReportArtifact, StringComparison.Ordinal)))
            .OrderBy(record => record.ReportedAt ?? record.DispatchedAt)
            .FirstOrDefault();
        var pendingConsumption = acks
            .Where(ack => ack.ConsumedAt is null)
            .OrderBy(ack => ack.AvailableAt)
            .FirstOrDefault();
        var settlements = completed.Select(record => record.ReportedAt).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var measuredSweep = Math.Max(0, maxSweepSeconds);
        var bound = (int)Math.Ceiling(floorSeconds + jitterSeconds + measuredSweep) + 1;
        var configuredBound = configuredBoundSeconds is > 0 ? configuredBoundSeconds : null;
        var qualificationReason = hasError
            ? "evidence-unreadable"
            : measuredSweeps < 3
                ? "insufficient-complete-sweeps"
                : configuredBound is null
                    ? "no-configured-bound"
                    : measuredSweep > configuredBound.Value
                        ? "measured-sweep-exceeds-configured-bound"
                        : "measured-sweep-within-configured-bound";
        var qualified = !hasError
            && measuredSweeps >= 3
            && configuredBound is not null
            && measuredSweep <= configuredBound.Value;
        var state = hasError ? "unknown"
            : !qualified ? "degraded"
            : deliveredUnreconciled.Length > 0 || missingAck is not null ? "failed"
            : pendingConsumption is not null ? "degraded"
            : "healthy";
        var nextAction = hasError
            ? "repair unreadable completion-channel evidence before relying on health"
            : deliveredUnreconciled.Length > 0
                ? "orchestration must run notify reconcile --write for the delivered report"
                : missingAck is not null
                    ? "Steward must record the identity-bound return acknowledgement"
                    : pendingConsumption is not null
                        ? "the bound resident must run task-scoped notify collect --write on its next canonical turn"
                        : null;
        return new NotifyCompletionChannelHealth
        {
            State = state,
            LastFloorCycleAt = supervision?.LastIntervalCycle?.CompletedAt,
            LiveWriter = supervision?.InstalledSupervisor?.Writer is { } writer
                ? $"{writer.Host}:{writer.Pid}"
                : supervision?.LastIntervalCycle?.Writer is { } cycleWriter
                    ? $"{cycleWriter.Host}:{cycleWriter.Pid}"
                    : null,
            DeliveredUnreconciledCount = deliveredUnreconciled.Length,
            OldestDeliveredUnreconciledAgeSeconds = deliveredUnreconciled.Length == 0 ? null : Age(now, deliveredUnreconciled[0].DispatchedAt),
            LastSettlementAt = settlements.Length == 0 ? null : settlements.Max(),
            MissingReturnAckAgeSeconds = missingAck is null ? null : Age(now, missingAck.ReportedAt ?? missingAck.DispatchedAt),
            ResidentConsumptionPendingAgeSeconds = pendingConsumption is null ? null : Age(now, pendingConsumption.AvailableAt),
            FailureOwner = nextAction is null ? null : nextAction.StartsWith("Steward", StringComparison.Ordinal) ? "steward" : nextAction.StartsWith("the bound", StringComparison.Ordinal) ? "resident" : "orchestration",
            NextAction = nextAction,
            FloorSeconds = floorSeconds,
            JitterSeconds = jitterSeconds,
            MaxSweepSeconds = maxSweepSeconds,
            BoundSeconds = bound,
            ConfiguredBoundSeconds = configuredBound,
            Qualified = qualified,
            QualificationReason = qualificationReason,
            MeasuredSweeps = measuredSweeps,
            Summary = hasError
                ? $"Completion-channel health is unknown because durable evidence could not be read: {pendingError ?? outboxError ?? ackError ?? supervisionError}"
                : $"Completion-channel health is {state}; B=ceil(F+J+P)+1 = ceil({floorSeconds}+{jitterSeconds}+{measuredSweep:0.###})+1 = {bound}s from {measuredSweeps} measured sweep(s); qualification={qualificationReason}; configured_bound={configuredBound?.ToString() ?? "<none>"}s.",
        };
    }

    private static long Age(DateTimeOffset now, DateTimeOffset then) => Math.Max(0, (long)(now - then).TotalSeconds);
}
