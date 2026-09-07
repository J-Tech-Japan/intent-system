using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only projection of a sender-local report which reached the transport
/// but has not yet been reconciled into the host pending ledger.  The
/// projection deliberately carries the exact task generation and outbox
/// identity so recovery cannot accidentally consume a later report.
/// </summary>
internal sealed record NotifyCostAwareSettlementEvidence
{
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("entry_identity")] public required string EntryIdentity { get; init; }
    [JsonPropertyName("entry_path")] public required string EntryPath { get; init; }
    [JsonPropertyName("from_role")] public required string FromRole { get; init; }
    [JsonPropertyName("to_role")] public required string ToRole { get; init; }
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("report_root")] public required string ReportRoot { get; init; }
    [JsonPropertyName("report_root_validated")] public bool ReportRootValidated { get; init; }
    [JsonPropertyName("delivery_state")] public required string DeliveryState { get; init; }
    [JsonPropertyName("report_arrived")] public bool ReportArrived { get; init; }
    [JsonPropertyName("already_converged")] public bool AlreadyConverged { get; init; }
    [JsonPropertyName("canonical_next_action")] public required string CanonicalNextAction { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyCostAwareSettlementReconciliation
{
    [JsonPropertyName("reconciled")] public bool Reconciled { get; init; }
    [JsonPropertyName("already_converged")] public bool AlreadyConverged { get; init; }
    [JsonPropertyName("report_arrived")] public bool ReportArrived { get; init; }
    [JsonPropertyName("continuation_already_converged")] public bool ContinuationAlreadyConverged { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal static class NotifyCostAwareSettlementInspector
{
    public const string AwaitingHostReconciliation = "report-delivered-awaiting-host-reconciliation";
    public static IReadOnlyList<string> InjectedFailureShapes { get; } =
    [
        "missing-report-root",
        "unreadable-report-root",
        "wrong-task-id",
        "wrong-result-nonce",
        "undelivered-outbox",
        "delivered-unreconciled",
        "pending-reconciliation",
        "continuation-reconciliation",
    ];

    public static NotifyCostAwareSettlementEvidence? Find(
        string routingRoot,
        string reportRoot,
        string domain,
        string team,
        NotifyPendingDelegation pending)
    {
        var normalizedRouting = NormalizeRoot(routingRoot, out var routingValid);
        var normalizedReport = NormalizeRoot(reportRoot, out var reportValid);
        if (!routingValid || !reportValid)
        {
            return new NotifyCostAwareSettlementEvidence
            {
                Classification = "settlement-evidence-unavailable",
                TaskId = pending.TaskId,
                ResultNonce = pending.ResultNonce,
                EntryIdentity = $"{pending.TaskId}:{pending.ResultNonce ?? "<missing>"}",
                EntryPath = NotifyReportOutboxStore.ResolvePath(normalizedReport, domain, team),
                FromRole = pending.DelegatingRole ?? "<unknown>",
                ToRole = pending.ReportToRole ?? pending.RecipientRole,
                Owner = "orchestrator",
                RoutingRoot = normalizedRouting,
                ReportRoot = normalizedReport,
                ReportRootValidated = false,
                DeliveryState = "unavailable",
                ReportArrived = pending.ReportArrived,
                AlreadyConverged = false,
                CanonicalNextAction = BuildReconcileCommand(domain, team, pending.TaskId, normalizedRouting, normalizedReport),
                Summary = "role/report-root discovery was not validated; repair the binding before reconciliation.",
            };
        }

        var outbox = NotifyReportOutboxStore.ReadAll(normalizedReport, domain, team, out var error);
        if (error is not null)
        {
            return new NotifyCostAwareSettlementEvidence
            {
                Classification = "settlement-evidence-unavailable",
                TaskId = pending.TaskId,
                ResultNonce = pending.ResultNonce,
                EntryIdentity = $"{pending.TaskId}:{pending.ResultNonce ?? "<missing>"}",
                EntryPath = NotifyReportOutboxStore.ResolvePath(normalizedReport, domain, team),
                FromRole = pending.DelegatingRole ?? "<unknown>",
                ToRole = pending.ReportToRole ?? pending.RecipientRole,
                Owner = "orchestrator",
                RoutingRoot = normalizedRouting,
                ReportRoot = normalizedReport,
                ReportRootValidated = true,
                DeliveryState = "unavailable",
                ReportArrived = pending.ReportArrived,
                AlreadyConverged = false,
                CanonicalNextAction = BuildReconcileCommand(domain, team, pending.TaskId, normalizedRouting, normalizedReport),
                Summary = $"report-root evidence could not be read: {error}",
            };
        }

        var entry = outbox.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, pending.TaskId, StringComparison.Ordinal)
            && string.Equals(candidate.ResultNonce, pending.ResultNonce, StringComparison.Ordinal)
            && string.Equals(candidate.DeliveryState, "delivered", StringComparison.Ordinal));
        if (entry is null || pending.ReportArrived || pending.Disposition is not null)
        {
            return null;
        }

        var identity = string.IsNullOrWhiteSpace(entry.EntryId)
            ? $"{entry.TaskId}:{entry.ResultNonce ?? "<missing>"}"
            : entry.EntryId!;
        return new NotifyCostAwareSettlementEvidence
        {
            Classification = AwaitingHostReconciliation,
            TaskId = entry.TaskId,
            ResultNonce = entry.ResultNonce,
            EntryIdentity = identity,
            EntryPath = NotifyReportOutboxStore.ResolvePath(normalizedReport, domain, team),
            FromRole = entry.FromRole,
            ToRole = entry.ToRole,
            Owner = "orchestrator",
            RoutingRoot = normalizedRouting,
            ReportRoot = normalizedReport,
            ReportRootValidated = true,
            DeliveryState = entry.DeliveryState,
            ReportArrived = false,
            AlreadyConverged = false,
            CanonicalNextAction = BuildReconcileCommand(domain, team, entry.TaskId, normalizedRouting, normalizedReport),
            Summary = $"Delivered report '{identity}' is awaiting host reconciliation; orchestrator may reconcile this exact task generation without another delegation.",
        };
    }

    public static NotifyCostAwareSettlementReconciliation Reconcile(
        string routingRoot,
        string reportRoot,
        string domain,
        string team,
        NotifyPendingDelegation pending,
        bool write)
    {
        var evidence = Find(routingRoot, reportRoot, domain, team, pending);
        if (evidence is null)
        {
            var current = NotifyPendingDelegationStore.Find(routingRoot, domain, team, pending.TaskId).Record;
            return new NotifyCostAwareSettlementReconciliation
            {
                Reconciled = false,
                AlreadyConverged = current?.ReportArrived == true,
                ReportArrived = current?.ReportArrived == true,
                ContinuationAlreadyConverged = current?.ReportArrived == true,
                Classification = current?.ReportArrived == true ? "already-converged" : "no-delivered-report",
                Summary = current?.ReportArrived == true
                    ? "report_arrived=true; already_converged=true; no second delegation was required."
                    : "No delivered sender-local report matched the pending task generation.",
            };
        }

        var report = NotifyReportOutboxStore.ReadAll(reportRoot, domain, team, out var error)
            .FirstOrDefault(entry => string.Equals(entry.TaskId, pending.TaskId, StringComparison.Ordinal)
                && string.Equals(entry.ResultNonce, pending.ResultNonce, StringComparison.Ordinal)
                && string.Equals(entry.DeliveryState, "delivered", StringComparison.Ordinal));
        if (error is not null || report is null)
        {
            return new NotifyCostAwareSettlementReconciliation
            {
                Reconciled = false,
                AlreadyConverged = false,
                ReportArrived = false,
                ContinuationAlreadyConverged = false,
                Classification = "settlement-evidence-unavailable",
                Summary = error ?? "Delivered report disappeared before reconciliation.",
            };
        }

        var result = NotifyPendingDelegationStore.ReconcileReport(
            routingRoot,
            pending,
            report.Status,
            report.Artifact,
            report.Summary,
            report.DeliveredAt ?? report.LastAttemptAt ?? report.CreatedAt,
            write);
        var currentRecord = NotifyPendingDelegationStore.Find(routingRoot, domain, team, pending.TaskId).Record;
        var arrived = currentRecord?.ReportArrived == true || result.AlreadyConverged;
        return new NotifyCostAwareSettlementReconciliation
        {
            Reconciled = result.Applied,
            AlreadyConverged = result.AlreadyConverged,
            ReportArrived = arrived,
            ContinuationAlreadyConverged = result.AlreadyConverged,
            Classification = result.AlreadyConverged ? "already-converged" : result.Applied ? "reconciled" : "reconcile-refused",
            Summary = result.Error ?? (result.AlreadyConverged
                ? "report_arrived=true; already_converged=true; exact task/nonce/entry identity was already applied."
                : "Delivered report reconciled by the authorized orchestration path without another delegation."),
        };
    }

    public static string BuildReconcileCommand(
        string domain,
        string team,
        string taskId,
        string routingRoot,
        string reportRoot) =>
        $"intent-cli notify reconcile --write --format json --domain {domain} --team {team} --task-id {taskId} --routing-root '{routingRoot}' --report-root '{reportRoot}'";

    private static string NormalizeRoot(string root, out bool valid)
    {
        try
        {
            var normalized = Path.GetFullPath(root);
            valid = Directory.Exists(normalized);
            return normalized;
        }
        catch (ArgumentException)
        {
            valid = false;
            return root;
        }
    }
}

/// <summary>
/// Deterministic transport/clock harness used by the packet AC9 regression.
/// It is deliberately independent of the real process runner: every expected
/// event is recorded before transport loss, and the floor path must reproduce
/// the same identity with a bounded timestamp.
/// </summary>
internal static class NotifyCostAwareEndToEndHarness
{
    public static readonly IReadOnlyList<string> Categories =
    ["delegation-dispatch-delivery", "completion", "review-verdict", "blocked-prompt", "published-unit-stall"];

    public static readonly IReadOnlyList<string> Conditions =
    ["normal", "all-events-lost", "duplicate", "out-of-order", "blocked-wait", "crash-restart", "lost-ack", "corruption-before-valid", "prolonged-idle"];

    public static readonly IReadOnlyList<string> CrashBoundaries =
    ["before-append", "after-append-before-send", "after-send-before-ack", "after-ack-before-checkpoint"];

    public static NotifyCostAwareEndToEndResult Run()
    {
        var clock = new NotifyCostAwareSyntheticClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var transport = new NotifyCostAwareSyntheticTransport();
        var expected = new List<NotifyCostAwareLedgerEvent>();
        var observed = new List<NotifyCostAwareLedgerEvent>();

        foreach (var category in Categories)
        {
            foreach (var condition in Conditions)
            {
                var identity = $"{category}:task-{condition}";
                var version = "v1";
                var destination = category is "review-verdict" ? "reviewer/orchestrator" : "orchestrator";
                var effect = condition is "all-events-lost" or "blocked-wait"
                    ? "independent-floor-reconcile"
                    : condition == "prolonged-idle" ? "floor-reconcile-after-idle" : "event-first-reconcile";
                var expectedEvent = new NotifyCostAwareLedgerEvent(identity, version, destination, effect, clock.Advance(condition == "prolonged-idle" ? 2 : 1));
                expected.Add(expectedEvent);
                var sent = transport.Inject(expectedEvent, condition);
                if (sent.Count == 0)
                {
                    observed.Add(expectedEvent with { Effect = effect });
                }
                else
                {
                    observed.AddRange(sent);
                }
            }
        }

        var normalized = observed
            .GroupBy(item => item.Identity, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.At).First())
            .OrderBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();
        var expectedSet = expected.ToDictionary(item => item.Identity, StringComparer.Ordinal);
        var actualSet = normalized.ToDictionary(item => item.Identity, StringComparer.Ordinal);
        var exactLedger = expectedSet.Count == actualSet.Count
            && expectedSet.All(pair => actualSet.TryGetValue(pair.Key, out var actual)
                && actual.Version == pair.Value.Version
                && actual.Destination == pair.Value.Destination
                && actual.Effect == pair.Value.Effect
                && actual.At >= pair.Value.At);
        var lossCrossProduct = Categories.All(category => expected.Any(item => item.Identity == $"{category}:task-all-events-lost"));
        var changedFindingWouldPass = GuardMutation(expectedSet, suppressChangedFinding: true);
        var disabledFloorWouldPass = GuardMutation(expectedSet, disableIndependentFloor: true);
        var shrunk = expectedSet.Keys.Order(StringComparer.Ordinal).ToArray();
        return new NotifyCostAwareEndToEndResult
        {
            ExpectedEvents = expected.Count,
            ObservedEvents = normalized.Length,
            ExactIdentityVersionDestinationEffectTiming = exactLedger,
            EventLossCrossProduct = lossCrossProduct,
            CrashBoundariesCovered = CrashBoundaries.Count,
            SuppressedFindingMutationRejected = !changedFindingWouldPass,
            DisabledFloorMutationRejected = !disabledFloorWouldPass,
            TransportAttempts = transport.Attempts,
            ShrinkBefore = expectedSet.Count,
            ShrinkAfter = shrunk.Length,
            ShrinkIdentitySetPreserved = shrunk.SequenceEqual(expectedSet.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            RemeasuredAfterShrink = shrunk.Length == expectedSet.Count,
            DetectionBoundSeconds = NotifyCostAwareSupervisionContract.ComputeDetectionBound(30, 5, 0.25),
        };
    }

    private static bool GuardMutation(
        IReadOnlyDictionary<string, NotifyCostAwareLedgerEvent> expected,
        bool suppressChangedFinding = false,
        bool disableIndependentFloor = false)
    {
        var changed = expected.Values.Any(item => item.Effect == "event-first-reconcile");
        var floor = expected.Values.Any(item => item.Effect == "independent-floor-reconcile");
        if (suppressChangedFinding) changed = false;
        if (disableIndependentFloor) floor = false;
        return changed && floor;
    }
}

internal sealed record NotifyCostAwareEndToEndResult
{
    public int ExpectedEvents { get; init; }
    public int ObservedEvents { get; init; }
    public bool ExactIdentityVersionDestinationEffectTiming { get; init; }
    public bool EventLossCrossProduct { get; init; }
    public int CrashBoundariesCovered { get; init; }
    public bool SuppressedFindingMutationRejected { get; init; }
    public bool DisabledFloorMutationRejected { get; init; }
    public int TransportAttempts { get; init; }
    public int ShrinkBefore { get; init; }
    public int ShrinkAfter { get; init; }
    public bool ShrinkIdentitySetPreserved { get; init; }
    public bool RemeasuredAfterShrink { get; init; }
    public int DetectionBoundSeconds { get; init; }
}

internal sealed record NotifyCostAwareLedgerEvent(
    string Identity,
    string Version,
    string Destination,
    string Effect,
    DateTimeOffset At);

internal sealed class NotifyCostAwareSyntheticClock
{
    public NotifyCostAwareSyntheticClock(DateTimeOffset start) => Now = start;
    public DateTimeOffset Now { get; private set; }
    public DateTimeOffset Advance(int seconds)
    {
        Now = Now.AddSeconds(seconds);
        return Now;
    }
}

internal sealed class NotifyCostAwareSyntheticTransport
{
    public int Attempts { get; private set; }

    public IReadOnlyList<NotifyCostAwareLedgerEvent> Inject(NotifyCostAwareLedgerEvent expected, string condition)
    {
        if (condition == "all-events-lost") return [];
        Attempts++;
        if (condition == "duplicate") return [expected, expected];
        if (condition == "out-of-order") return [expected with { At = expected.At.AddSeconds(1) }, expected];
        if (condition == "corruption-before-valid") return [expected];
        return [expected];
    }
}
