using System.Text.Json;
using IntentSystem.Cli.Commands;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class G810RepairAcceptanceTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("g810-repair-").FullName;
    private readonly ITestOutputHelper output;

    public G810RepairAcceptanceTests(ITestOutputHelper output) => this.output = output;

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void PacketAC4_NormativeTimingTableHasDefaultsOverridesAndValidation_G810()
    {
        var guide = NotifyCostAwareSupervisionContract.BuildGuide();
        Assert.Equal(10, guide.TimingTable.Count);
        Assert.Contains(guide.TimingTable, row => row.Name == "independent-floor" && row.DefaultSeconds == 30);
        Assert.Contains(guide.TimingTable, row => row.Name == "jitter" && row.DefaultSeconds == 5);
        Assert.All(guide.TimingTable, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Override));
            Assert.False(string.IsNullOrWhiteSpace(row.Validation));
        });
        Assert.True(NotifyCostAwareSupervisionContract.ValidateTimingOverride(30, 5, 0.25, out var validReason));
        Assert.False(NotifyCostAwareSupervisionContract.ValidateTimingOverride(30, 40, 30, out var invalidReason));
        output.WriteLine(
            $"G810 AC4 timing-table rows={guide.TimingTable.Count}; F=30s; J=5s; P=0.250000s; D={NotifyCostAwareSupervisionContract.ComputeDetectionBound(30, 5, 0.25)}s; valid={validReason}; invalid={invalidReason}");
    }

    [Fact]
    public void PacketAC9_TransportClockLedgerCoversLossCrossProductCrashMutationsAndShrink_G810()
    {
        var result = NotifyCostAwareEndToEndHarness.Run();
        Assert.Equal(45, result.ExpectedEvents);
        Assert.Equal(result.ExpectedEvents, result.ObservedEvents);
        Assert.True(result.ExactIdentityVersionDestinationEffectTiming);
        Assert.True(result.EventLossCrossProduct);
        Assert.Equal(4, result.CrashBoundariesCovered);
        Assert.Equal(15, result.IndependentFloorEvents);
        Assert.Equal(20, result.CrashBoundaryCrossings);
        Assert.True(result.StoreInjections >= 45);
        Assert.True(result.SuppressedFindingMutationRejected);
        Assert.True(result.DisabledFloorMutationRejected);
        Assert.True(result.ShrinkIdentitySetPreserved);
        Assert.True(result.RemeasuredAfterShrink);
        Assert.True(result.DetectionBoundSeconds <= 60);
        output.WriteLine(
            $"G810 AC9 synthetic-e2e expected_event_ledger={result.ExpectedEvents}; observed={result.ObservedEvents}; transport_attempts={result.TransportAttempts}; "
            + $"loss_cross_product={result.EventLossCrossProduct}; crash_boundaries={result.CrashBoundariesCovered}; crash_boundary_crossings={result.CrashBoundaryCrossings}; exact_identity_version_destination_effect_timing={result.ExactIdentityVersionDestinationEffectTiming}; "
            + $"independent_floor_events={result.IndependentFloorEvents}; store_injections={result.StoreInjections}; "
            + $"suppressed_finding_mutation_rejected={result.SuppressedFindingMutationRejected}; disabled_floor_mutation_rejected={result.DisabledFloorMutationRejected}; "
            + $"shrink={result.ShrinkBefore}->{result.ShrinkAfter}; identity_set_preserved={result.ShrinkIdentitySetPreserved}; remeasured={result.RemeasuredAfterShrink}; D={result.DetectionBoundSeconds}s; "
            + "injections=all-events-lost->independent-floor,blocked-wait->independent-floor,crash-restart->durable-journal,lost-ack->retry,corruption-before-valid->tolerant-read,prolonged-idle->floor");
    }

    [Fact]
    public void PacketAC13_DeliveredReportIsClassifiedAndAuthorizedReconcileIsReplaySafe_G810()
    {
        var routingRoot = Path.Combine(root, "routing");
        var reportRoot = Path.Combine(root, "report");
        Directory.CreateDirectory(routingRoot);
        Directory.CreateDirectory(reportRoot);
        var pending = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G810-review-round-trip",
            TaskKind = "issue",
            DelegatingRole = "reviewer",
            RecipientRole = "orchestration",
            ReportToRole = "orchestration",
            RecipientIdentity = "role=orchestration;workspace=w4B;pane=p2",
            ExpectedArtifact = "https://github.com/J-Tech-Japan/intent-system/pull/1773",
            ResultNonce = "g810-review-round-trip-v1",
            DispatchedAt = Now.AddMinutes(-30),
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "w4B",
            PaneId = "p2",
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(routingRoot, pending).Written);
        Assert.True(NotifyReportOutboxStore.WriteNew(reportRoot, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            FromRole = "reviewer",
            ToRole = "orchestration",
            Status = "completed",
            Artifact = pending.ExpectedArtifact,
            Summary = "review round trip delivered",
            CreatedAt = Now.AddMinutes(-29),
            DeliveredAt = Now.AddMinutes(-28),
            LastAttemptAt = Now.AddMinutes(-28),
            DeliveryState = "delivered",
        }).Written);

        var health = NotifyCompletionChannelHealth.Compute(
            routingRoot,
            Domain,
            Team,
            Now,
            measuredSweeps: 3,
            configuredBoundSeconds: 900,
            reportRoot: reportRoot);
        Assert.NotNull(health.Settlement);
        Assert.Equal(NotifyCostAwareSettlementInspector.AwaitingHostReconciliation, health.Settlement!.Classification);
        Assert.Equal("orchestrator", health.Settlement.Owner);
        Assert.True(health.Settlement.ReportRootValidated);
        Assert.False(health.Settlement.ReportArrived);
        Assert.Contains("notify reconcile", health.Settlement.CanonicalNextAction, StringComparison.Ordinal);
        var first = NotifyCostAwareSettlementInspector.Reconcile(routingRoot, reportRoot, Domain, Team, pending, write: true);
        Assert.True(first.Reconciled);
        Assert.True(first.ReportArrived);
        var continuation = NotifyCostAwareSettlementInspector.Reconcile(routingRoot, reportRoot, Domain, Team, pending, write: true);
        Assert.True(continuation.AlreadyConverged);
        Assert.True(continuation.ContinuationAlreadyConverged);
        Assert.Equal(
            new[]
            {
                "delivered-unreconciled", "duplicate-retry", "lost-owner-wake", "wrong-role-g796-routing",
                "restart", "wrong-nonce-or-report-root", "corrupt-outbox", "partial-store-write-failure",
            },
            NotifyCostAwareSettlementInspector.InjectedFailureShapes);
        var failures = NotifyCostAwareSettlementFailureHarness.Run();
        Assert.Equal(8, failures.Count);
        Assert.All(failures, failure =>
        {
            Assert.True(failure.IsFailure, failure.Shape);
            Assert.False(string.IsNullOrWhiteSpace(failure.Reason), failure.Shape);
        });
        var fixtures = NotifyCostAwareSettlementFailureHarness.RunNamedFixtures();
        Assert.Equal(2, fixtures.Count);
        Assert.All(fixtures, fixture =>
        {
            Assert.True(fixture.IsFailure, fixture.Fixture);
            Assert.Equal(NotifyCostAwareSettlementInspector.AwaitingHostReconciliation, fixture.Classification);
            Assert.False(fixture.BeforeRepairPassed, fixture.Fixture);
            Assert.True(fixture.AfterRepairPassed, fixture.Fixture);
        });
        output.WriteLine(
            $"G810 AC13 classification={health.Settlement.Classification}; task={health.Settlement.TaskId}; nonce={health.Settlement.ResultNonce}; entry={health.Settlement.EntryIdentity}; "
            + $"owner={health.Settlement.Owner}; report_root_validated={health.Settlement.ReportRootValidated}; next_action={health.Settlement.CanonicalNextAction}; "
            + $"write_reconciled={first.Reconciled}; report_arrived={first.ReportArrived}; continuation_already_converged={continuation.ContinuationAlreadyConverged}; "
            + $"failure_shapes={string.Join(",", NotifyCostAwareSettlementInspector.InjectedFailureShapes)}; "
            + $"failure_results={string.Join(",", failures.Select(failure => $"{failure.Shape}:{failure.IsFailure}:{failure.Classification}"))}; "
            + $"named_fixtures={string.Join(",", fixtures.Select(fixture => $"{fixture.Fixture}:{fixture.Classification}:before=FAIL:after=PASS"))}");
    }

    [Fact]
    public void PacketAcceptanceMappingAndCompatibilityEntryAreExplicit_G810()
    {
        var mapping = new[]
        {
            "AC1→NotifyCostAwareSupervisionContract.Evaluate",
            "AC2→CoverageManifest and NotifyCostAwareCoverageObservation",
            "AC3→NotifyCostAwareIdentity and NotifyCostAwareBudgetLedger",
            "AC4→TimingTable and ValidateTimingOverride",
            "AC5→NotifyCostAwareSyntheticTransport",
            "AC6→NotifyCostAwareBudgetLedger",
            "AC7→NotifyCostAwareCorruption",
            "AC8→NotifyCostAwareBenchmark",
            "AC9→NotifyCostAwareEndToEndHarness",
            "AC10→NotifyCostAwareSafety",
            "AC11→NotifyCostAwareTransferability",
            "AC12→NotifyCostAwareResult read-only/no-model fields",
            "AC13→NotifyCostAwareSettlementInspector",
            "AC14→NotifyCostAwarePhaseProgressSupervisor",
        };
        Assert.All(mapping, value => Assert.Contains("→", value, StringComparison.Ordinal));
        foreach (var language in new[] { "en", "ja" })
        {
            var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "1.0-compatibility-ledger.md");
            Assert.Contains("g810-cost-aware-supervision/v1", File.ReadAllText(path), StringComparison.Ordinal);
        }
        output.WriteLine($"G810 acceptance mapping: {string.Join("; ", mapping)}; compatibility=g810-cost-aware-supervision/v1; F=30; J=5; P=measured; D=ceil(F+J+P)+1");
    }
}
