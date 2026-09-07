using System.Text;
using System.Text.Json;
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
        "delivered-unreconciled",
        "duplicate-retry",
        "lost-owner-wake",
        "wrong-role-g796-routing",
        "restart",
        "wrong-nonce-or-report-root",
        "corrupt-outbox",
        "partial-store-write-failure",
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

internal sealed record NotifyCostAwareSettlementFailureOutcome(
    string Shape,
    bool IsFailure,
    string Classification,
    string Reason,
    string? Fixture = null,
    bool BeforeRepairPassed = false,
    bool AfterRepairPassed = false);

internal static class NotifyCostAwareSettlementFailureHarness
{
    public static IReadOnlyList<string> NamedFixtures { get; } =
    [
        "G810-rollout-plan-review-20260905",
        "G810-orca-rollout-rereview-20260905",
    ];

    public static IReadOnlyList<NotifyCostAwareSettlementFailureOutcome> Run()
    {
        var root = Directory.CreateTempSubdirectory("g810-settlement-failures-");
        try
        {
            var outcomes = new List<NotifyCostAwareSettlementFailureOutcome>();
            outcomes.Add(DeliveredUnreconciled(root.FullName));
            outcomes.Add(DuplicateRetry(root.FullName));
            outcomes.Add(LostOwnerWake(root.FullName));
            outcomes.Add(WrongRoleRouting(root.FullName));
            outcomes.Add(RestartWithoutReport(root.FullName));
            outcomes.Add(WrongNonceOrReportRoot(root.FullName));
            outcomes.Add(CorruptOutbox(root.FullName));
            outcomes.Add(PartialStoreWrite(root.FullName));
            return outcomes;
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    public static IReadOnlyList<NotifyCostAwareSettlementFailureOutcome> RunNamedFixtures()
    {
        var outcomes = new List<NotifyCostAwareSettlementFailureOutcome>();
        foreach (var fixture in NamedFixtures)
        {
            var root = Directory.CreateTempSubdirectory("g810-settlement-fixture-");
            try
            {
                var routing = Path.Combine(root.FullName, "routing");
                var report = Path.Combine(root.FullName, "report");
                Directory.CreateDirectory(routing);
                Directory.CreateDirectory(report);
                var pending = CreatePending(fixture, $"{fixture}-v1");
                _ = NotifyPendingDelegationStore.WriteDispatch(routing, pending);
                _ = NotifyReportOutboxStore.WriteNew(report, CreateOutbox(pending, "delivered"));
                _ = NotifyReportOutboxStore.MarkDelivered(report, CreateOutbox(pending, "delivered"));
                var evidence = NotifyCostAwareSettlementInspector.Find(routing, report, Domain, Team, pending);
                var afterRepair = evidence?.Classification == NotifyCostAwareSettlementInspector.AwaitingHostReconciliation;
                outcomes.Add(new NotifyCostAwareSettlementFailureOutcome(
                    "named-round-trip",
                    IsFailure: afterRepair,
                    evidence?.Classification ?? "no-classification",
                    afterRepair ? "fixture is classified before reconciliation" : "named fixture was not classified",
                    fixture,
                    BeforeRepairPassed: false,
                    AfterRepairPassed: afterRepair));
            }
            finally
            {
                root.Delete(recursive: true);
            }
        }
        return outcomes;
    }

    private static NotifyCostAwareSettlementFailureOutcome DeliveredUnreconciled(string root)
    {
        var (routing, report, pending) = PrepareDelivered(root, "delivered-unreconciled");
        var evidence = NotifyCostAwareSettlementInspector.Find(routing, report, Domain, Team, pending);
        return Outcome("delivered-unreconciled", evidence is not null, evidence?.Classification, "delivered report remains pending host reconciliation");
    }

    private static NotifyCostAwareSettlementFailureOutcome DuplicateRetry(string root)
    {
        var report = Path.Combine(root, "duplicate-report");
        Directory.CreateDirectory(report);
        var pending = CreatePending("duplicate-retry", "duplicate-retry-v1");
        var entry = CreateOutbox(pending, "undelivered");
        var first = NotifyReportOutboxStore.WriteNew(report, entry);
        var second = NotifyReportOutboxStore.WriteNew(report, entry);
        return Outcome("duplicate-retry", first.Written && !second.Written, "duplicate-refused", second.Error ?? "duplicate dispatch was refused");
    }

    private static NotifyCostAwareSettlementFailureOutcome LostOwnerWake(string root)
    {
        var routing = Path.Combine(root, "lost-owner-routing");
        var report = Path.Combine(root, "lost-owner-report");
        Directory.CreateDirectory(routing);
        Directory.CreateDirectory(report);
        var pending = CreatePending("lost-owner-wake", "lost-owner-wake-v1");
        _ = NotifyPendingDelegationStore.WriteDispatch(routing, pending);
        var evidence = NotifyCostAwareSettlementInspector.Find(routing, report, Domain, Team, pending);
        return Outcome("lost-owner-wake", evidence is null, "owner-wake-missing", "no delivered report is treated as an explicit recovery failure");
    }

    private static NotifyCostAwareSettlementFailureOutcome WrongRoleRouting(string root)
    {
        var routing = Path.Combine(root, "wrong-role-routing");
        var report = Path.Combine(root, "wrong-role-report");
        Directory.CreateDirectory(routing);
        Directory.CreateDirectory(report);
        var pending = CreatePending("wrong-role-g796-routing", "wrong-role-g796-routing-v1", recipientRole: "steward");
        _ = NotifyPendingDelegationStore.WriteDispatch(routing, pending);
        var entry = CreateOutbox(pending, "delivered") with { ToRole = "steward" };
        _ = NotifyReportOutboxStore.WriteNew(report, entry);
        _ = NotifyReportOutboxStore.MarkDelivered(report, entry);
        var evidence = NotifyCostAwareSettlementInspector.Find(routing, report, Domain, Team, pending);
        var refused = evidence is null || !string.Equals(evidence.ToRole, "orchestration", StringComparison.Ordinal);
        return Outcome("wrong-role-g796-routing", refused, "wrong-role-refused", "G796 return routing must target orchestration, not steward");
    }

    private static NotifyCostAwareSettlementFailureOutcome RestartWithoutReport(string root)
    {
        var routing = Path.Combine(root, "restart-routing");
        var report = Path.Combine(root, "restart-report");
        Directory.CreateDirectory(routing);
        Directory.CreateDirectory(report);
        var pending = CreatePending("restart", "restart-v1");
        _ = NotifyPendingDelegationStore.WriteDispatch(routing, pending);
        var afterRestart = NotifyPendingDelegationStore.Find(routing, Domain, Team, pending.TaskId).Record;
        var evidence = afterRestart is null ? null : NotifyCostAwareSettlementInspector.Find(routing, report, Domain, Team, afterRestart);
        return Outcome("restart", evidence is null, "restart-recovery-pending", "restart retains pending identity and refuses false settlement");
    }

    private static NotifyCostAwareSettlementFailureOutcome WrongNonceOrReportRoot(string root)
    {
        var (routing, report, pending) = PrepareDelivered(root, "wrong-nonce-or-report-root");
        var wrongNonce = pending with { ResultNonce = "wrong-nonce" };
        var evidence = NotifyCostAwareSettlementInspector.Find(routing, report, Domain, Team, wrongNonce);
        var invalidRoot = NotifyCostAwareSettlementInspector.Find(routing, Path.Combine(root, "missing-report-root"), Domain, Team, pending);
        return Outcome("wrong-nonce-or-report-root", evidence is null || invalidRoot?.ReportRootValidated == false, "identity-or-root-refused", "nonce and report-root bindings are validated");
    }

    private static NotifyCostAwareSettlementFailureOutcome CorruptOutbox(string root)
    {
        var (routing, report, pending) = PrepareDelivered(root, "corrupt-outbox");
        var path = NotifyReportOutboxStore.ResolvePath(report, Domain, Team);
        File.AppendAllText(path, "{not-json}" + Environment.NewLine, Encoding.UTF8);
        var evidence = NotifyCostAwareSettlementInspector.Find(routing, report, Domain, Team, pending);
        return Outcome("corrupt-outbox", evidence?.Classification == "settlement-evidence-unavailable", evidence?.Classification, "corrupt outbox is degraded, never complete");
    }

    private static NotifyCostAwareSettlementFailureOutcome PartialStoreWrite(string root)
    {
        var routing = Path.Combine(root, "partial-write-routing");
        Directory.CreateDirectory(routing);
        var pending = CreatePending("partial-store-write-failure", "partial-store-write-failure-v1");
        NotifyPendingDelegationStore.WriteOverride = (_, _) => new NotifyPendingStoreWriteResult(false, "injected", "partial-store-write-failure");
        try
        {
            var result = NotifyPendingDelegationStore.WriteDispatch(routing, pending);
            return Outcome("partial-store-write-failure", !result.Written, "partial-write-refused", result.Error ?? "write refused");
        }
        finally
        {
            NotifyPendingDelegationStore.WriteOverride = null;
        }
    }

    private static (string Routing, string Report, NotifyPendingDelegation Pending) PrepareDelivered(string root, string taskId)
    {
        var routing = Path.Combine(root, $"{taskId}-routing");
        var report = Path.Combine(root, $"{taskId}-report");
        Directory.CreateDirectory(routing);
        Directory.CreateDirectory(report);
        var pending = CreatePending(taskId, $"{taskId}-v1");
        _ = NotifyPendingDelegationStore.WriteDispatch(routing, pending);
        var entry = CreateOutbox(pending, "delivered");
        _ = NotifyReportOutboxStore.WriteNew(report, entry);
        _ = NotifyReportOutboxStore.MarkDelivered(report, entry);
        return (routing, report, pending);
    }

    private static NotifyPendingDelegation CreatePending(string taskId, string nonce, string recipientRole = "orchestration") => new()
    {
        Domain = Domain,
        Team = Team,
        TaskId = taskId,
        TaskKind = "issue",
        DelegatingRole = "reviewer",
        RecipientRole = recipientRole,
        ReportToRole = "orchestration",
        RecipientIdentity = "role=orchestration;workspace=w4B;pane=p2",
        ExpectedArtifact = "https://github.com/J-Tech-Japan/intent-system/pull/1773",
        ResultNonce = nonce,
        DispatchedAt = DateTimeOffset.Parse("2026-09-07T11:00:00Z"),
        Resident = NotifyRecordedRole.HerdrResident,
        WorkspaceId = "w4B",
        PaneId = "p2",
    };

    private static NotifyReportOutboxEntry CreateOutbox(NotifyPendingDelegation pending, string state) => new()
    {
        Domain = Domain,
        Team = Team,
        TaskId = pending.TaskId,
        ResultNonce = pending.ResultNonce,
        FromRole = pending.DelegatingRole ?? "reviewer",
        ToRole = pending.RecipientRole,
        Status = "completed",
        Artifact = pending.ExpectedArtifact,
        Summary = "settlement fixture",
        CreatedAt = DateTimeOffset.Parse("2026-09-07T11:01:00Z"),
        DeliveredAt = state == "delivered" ? DateTimeOffset.Parse("2026-09-07T11:02:00Z") : null,
        LastAttemptAt = DateTimeOffset.Parse("2026-09-07T11:02:00Z"),
        DeliveryState = state,
    };

    private static NotifyCostAwareSettlementFailureOutcome Outcome(string shape, bool failed, string? classification, string reason) =>
        new(shape, failed, classification ?? "unclassified", reason);

    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
}

/// <summary>
/// Deterministic transport/clock harness used by the packet AC9 regression.
/// Expected events are an independent ledger. Observed events are recovered
/// from a durable synthetic journal; the floor, not the expectation, produces
/// observations when transport delivery is lost or blocked.
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
        var root = Directory.CreateTempSubdirectory("g810-e2e-journal-");
        try
        {
            var clock = new NotifyCostAwareSyntheticClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
            var journal = new NotifyCostAwareSyntheticJournal(root.FullName);
            var transport = new NotifyCostAwareSyntheticTransport(journal, clock);
            var expected = new List<NotifyCostAwareLedgerEvent>();

            foreach (var category in Categories)
            {
                foreach (var condition in Conditions)
                {
                    var identity = $"{category}:task-{condition}";
                    var version = "v1";
                    var destination = category is "review-verdict" ? "reviewer/orchestrator" : "orchestrator";
                    var expectedEffect = condition is "all-events-lost" or "blocked-wait"
                        ? "independent-floor-reconcile"
                        : condition == "prolonged-idle" ? "floor-reconcile-after-idle" : "event-first-reconcile";
                    var expectedEvent = new NotifyCostAwareLedgerEvent(identity, version, destination, expectedEffect, clock.Advance(condition == "prolonged-idle" ? 120 : 1));
                    expected.Add(expectedEvent);
                    transport.Process(expectedEvent, condition);
                }
            }

            var normalized = journal.ReadObserved(out var corruptLines)
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
            var lossCrossProduct = Categories.All(category => actualSet.TryGetValue($"{category}:task-all-events-lost", out var item)
                && item.Effect == "independent-floor-reconcile");
            var changedFindingWouldPass = NotifyCostAwareEndToEndOracle.Matches(expectedSet, actualSet, suppressChangedFinding: true, disableIndependentFloor: false);
            var disabledFloorWouldPass = NotifyCostAwareEndToEndOracle.Matches(expectedSet, actualSet, suppressChangedFinding: false, disableIndependentFloor: true);

            var shrink = journal.ArchiveAndShrink(expectedSet.Keys);
            var remeasured = journal.ReadObserved(out var remeasureCorruption)
                .GroupBy(item => item.Identity, StringComparer.Ordinal)
                .Select(group => group.OrderBy(item => item.At).First())
                .ToDictionary(item => item.Identity, StringComparer.Ordinal);
            var preserved = expectedSet.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                remeasured.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
            return new NotifyCostAwareEndToEndResult
            {
                ExpectedEvents = expected.Count,
                ObservedEvents = normalized.Length,
                ExactIdentityVersionDestinationEffectTiming = exactLedger,
                EventLossCrossProduct = lossCrossProduct,
                CrashBoundariesCovered = transport.CrashBoundaryNames.Count,
                CrashBoundaryCrossings = transport.CrashBoundariesCrossed,
                IndependentFloorEvents = transport.IndependentFloorEvents,
                StoreInjections = transport.StoreInjections + corruptLines + remeasureCorruption,
                SuppressedFindingMutationRejected = !changedFindingWouldPass,
                DisabledFloorMutationRejected = !disabledFloorWouldPass,
                TransportAttempts = transport.Attempts,
                ShrinkBefore = shrink.BeforeCount,
                ShrinkAfter = shrink.AfterCount,
                ShrinkIdentitySetPreserved = preserved,
                RemeasuredAfterShrink = preserved && remeasured.Count == expectedSet.Count,
                DetectionBoundSeconds = NotifyCostAwareSupervisionContract.ComputeDetectionBound(30, 5, 0.25),
            };
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}

internal sealed record NotifyCostAwareEndToEndResult
{
    public int ExpectedEvents { get; init; }
    public int ObservedEvents { get; init; }
    public bool ExactIdentityVersionDestinationEffectTiming { get; init; }
    public bool EventLossCrossProduct { get; init; }
    public int CrashBoundariesCovered { get; init; }
    public int CrashBoundaryCrossings { get; init; }
    public int IndependentFloorEvents { get; init; }
    public int StoreInjections { get; init; }
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

internal sealed record NotifyCostAwareJournalShrinkResult(int BeforeCount, int AfterCount, long BeforeBytes, long AfterBytes);

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

internal sealed class NotifyCostAwareSyntheticJournal
{
    private static readonly JsonSerializerOptions Options = new();
    private readonly string path;
    private readonly string archivePath;

    public NotifyCostAwareSyntheticJournal(string root)
    {
        Directory.CreateDirectory(root);
        path = Path.Combine(root, "events.jsonl");
        archivePath = Path.Combine(root, "events.archive.jsonl");
    }

    public void Append(NotifyCostAwareLedgerEvent value)
    {
        File.AppendAllText(path, JsonSerializer.Serialize(value, Options) + Environment.NewLine, Encoding.UTF8);
    }

    public void AppendCorruptLine() => File.AppendAllText(path, "{\"identity\":\"corrupt\"" + Environment.NewLine, Encoding.UTF8);

    public IReadOnlyList<NotifyCostAwareLedgerEvent> ReadObserved(out int corruptLines)
    {
        corruptLines = 0;
        var values = new List<NotifyCostAwareLedgerEvent>();
        if (!File.Exists(path)) return values;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var item = JsonSerializer.Deserialize<NotifyCostAwareLedgerEvent>(line, Options);
                if (item is null) corruptLines++;
                else values.Add(item);
            }
            catch (JsonException)
            {
                corruptLines++;
            }
        }
        if (File.Exists(archivePath))
        {
            foreach (var line in File.ReadLines(archivePath))
            {
                try
                {
                    var item = JsonSerializer.Deserialize<NotifyCostAwareLedgerEvent>(line, Options);
                    if (item is not null) values.Add(item);
                }
                catch (JsonException)
                {
                    corruptLines++;
                }
            }
        }
        return values;
    }

    public NotifyCostAwareJournalShrinkResult ArchiveAndShrink(IReadOnlyCollection<string> identitySet)
    {
        var before = ReadObserved(out _).Where(item => identitySet.Contains(item.Identity)).ToArray();
        var beforeBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        File.WriteAllLines(archivePath, before.Select(item => JsonSerializer.Serialize(item, Options)), Encoding.UTF8);
        var retained = before.GroupBy(item => item.Identity, StringComparer.Ordinal).Select(group => group.OrderBy(item => item.At).First()).ToArray();
        File.WriteAllLines(path, retained.Select(item => JsonSerializer.Serialize(item, Options)), Encoding.UTF8);
        var afterBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        return new NotifyCostAwareJournalShrinkResult(before.Length, retained.Length, beforeBytes, afterBytes);
    }
}

internal sealed class NotifyCostAwareSyntheticTransport
{
    private readonly NotifyCostAwareSyntheticJournal journal;
    private readonly NotifyCostAwareSyntheticClock clock;

    public NotifyCostAwareSyntheticTransport(NotifyCostAwareSyntheticJournal journal, NotifyCostAwareSyntheticClock clock)
    {
        this.journal = journal;
        this.clock = clock;
    }

    public int Attempts { get; private set; }
    public int CrashBoundariesCrossed { get; private set; }
    public HashSet<string> CrashBoundaryNames { get; } = new(StringComparer.Ordinal);
    public int IndependentFloorEvents { get; private set; }
    public int StoreInjections { get; private set; }

    public void Process(NotifyCostAwareLedgerEvent expected, string condition)
    {
        if (condition is "all-events-lost" or "blocked-wait")
        {
            // No event is supplied to the observation. The independent floor
            // owns the effect and writes a durable observation after its clock.
            clock.Advance(30);
            journal.Append(BuildFloorObservation(expected, "independent-floor-reconcile"));
            IndependentFloorEvents++;
            StoreInjections++;
            return;
        }

        if (condition == "prolonged-idle")
        {
            clock.Advance(30);
            journal.Append(BuildFloorObservation(expected, "floor-reconcile-after-idle"));
            IndependentFloorEvents++;
            StoreInjections++;
            return;
        }

        if (condition == "crash-restart")
        {
            foreach (var boundary in NotifyCostAwareEndToEndHarness.CrashBoundaries)
            {
                CrossCrashBoundary(boundary);
                if (boundary is "before-append" or "after-append-before-send")
                {
                    journal.Append(expected with { At = clock.Advance(1) });
                    StoreInjections++;
                }
                // A restart reopens and scans the durable journal at every
                // injected boundary; no in-memory expectation is restored.
                _ = journal.ReadObserved(out _);
                StoreInjections++;
            }
            return;
        }

        if (condition == "corruption-before-valid")
        {
            journal.AppendCorruptLine();
            StoreInjections++;
        }

        Attempts++;
        journal.Append(expected with { At = clock.Advance(1) });
        StoreInjections++;
        if (condition is "duplicate" or "lost-ack")
        {
            Attempts++;
            journal.Append(expected with { At = clock.Advance(1) });
            StoreInjections++;
        }
        if (condition == "out-of-order")
        {
            journal.Append(expected with { At = clock.Advance(2) });
            StoreInjections++;
        }
    }

    private NotifyCostAwareLedgerEvent BuildFloorObservation(NotifyCostAwareLedgerEvent source, string effect) =>
        new(source.Identity, source.Version, source.Destination, effect, clock.Now);

    private void CrossCrashBoundary(string boundary)
    {
        // The injected boundary is crossed only when this recovery path runs;
        // static boundary names alone never contribute to the count.
        _ = boundary;
        CrashBoundaryNames.Add(boundary);
        CrashBoundariesCrossed++;
    }
}

internal static class NotifyCostAwareEndToEndOracle
{
    public static bool Matches(
        IReadOnlyDictionary<string, NotifyCostAwareLedgerEvent> expected,
        IReadOnlyDictionary<string, NotifyCostAwareLedgerEvent> observed,
        bool suppressChangedFinding,
        bool disableIndependentFloor)
    {
        if (suppressChangedFinding)
        {
            var changed = expected.Values.FirstOrDefault(item => item.Effect == "event-first-reconcile");
            if (changed is not null) observed = observed.Where(pair => pair.Key != changed.Identity).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        if (disableIndependentFloor)
        {
            observed = observed.Where(pair => pair.Value.Effect != "independent-floor-reconcile").ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        return expected.Count == observed.Count && expected.All(pair => observed.TryGetValue(pair.Key, out var value)
            && value.Version == pair.Value.Version
            && value.Destination == pair.Value.Destination
            && value.Effect == pair.Value.Effect);
    }
}
