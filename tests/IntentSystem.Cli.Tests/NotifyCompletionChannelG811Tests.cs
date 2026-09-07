using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G811's completion channel is an identity-bound, append-only handshake:
/// external consumption creates one receipt, Steward publishes one return
/// acknowledgement, and the originating resident consumes it on its next
/// task-scoped turn. These tests intentionally use synthetic stores only.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifyCompletionChannelG811Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 18, 0, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("notify-g811-").FullName;
    private readonly ITestOutputHelper output;

    public NotifyCompletionChannelG811Tests(ITestOutputHelper output) => this.output = output;

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void CompletionIdentityReceiptAndReturnAckAreReplaySafeAndPreserveResidentBinding_G811()
    {
        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);

        var receipt = Receipt("cursor-1", Now);
        var firstReceipt = NotifyCompletionChannelStore.WriteReceipt(root, receipt, write: true);
        Assert.True(firstReceipt.Written);

        // A replay observed on a later cursor/time is the same completion
        // identity, not a conflicting second consumption.
        var replayReceipt = NotifyCompletionChannelStore.WriteReceipt(
            root,
            receipt with { Cursor = "cursor-2", ConsumedAt = Now.AddSeconds(2) },
            write: true);
        Assert.True(replayReceipt.AlreadyConverged);
        Assert.Null(replayReceipt.Error);

        var ack = new NotifyReturnAck
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            Artifact = pending.ExpectedArtifact,
            StewardRole = LogicalRoleNormalizer.Steward,
            RecipientRole = pending.RecipientRole,
            RecipientIdentity = pending.RecipientIdentity,
            Resident = pending.Resident!,
            WorkspaceId = pending.WorkspaceId,
            PaneId = pending.PaneId,
            ConsumptionReceiptId = receipt.ReceiptId,
            ConsumptionCursor = receipt.Cursor,
            AvailableAt = Now,
        };
        var firstAck = NotifyCompletionChannelStore.WriteAck(root, ack, write: true);
        Assert.True(firstAck.Written);
        Assert.True(NotifyCompletionChannelStore.WriteAck(root, ack with { AvailableAt = Now.AddSeconds(1) }, write: true).AlreadyConverged);

        var dryRun = NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(2), write: false);
        Assert.False(dryRun.Written);
        Assert.Null(dryRun.Error);
        Assert.Null(NotifyCompletionChannelStore.FindAck(root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact).Ack!.ConsumedAt);

        var consumed = NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(3), write: true);
        Assert.True(consumed.Written);
        var final = NotifyCompletionChannelStore.FindAck(root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact).Ack;
        Assert.Equal(Now.AddSeconds(3), final!.ConsumedAt);
        Assert.Equal(pending.RecipientRole, final.RecipientRole);
        Assert.Equal(pending.RecipientIdentity, final.RecipientIdentity);
        Assert.Equal(pending.WorkspaceId, final.WorkspaceId);
        Assert.Equal(pending.PaneId, final.PaneId);
        Assert.True(NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(4), write: true).AlreadyConverged);
    }

    [Fact]
    public void IdentityMismatchesAndUnknownTasksRefuseWithoutAdvancingReceiptOrAck_G811()
    {
        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);
        Assert.True(NotifyCompletionChannelStore.WriteReceipt(root, Receipt("cursor-1", Now), write: true).Written);

        var unknown = NotifyCompletionChannelStore.FindAck(root, Domain, Team, "G811-unknown", "nonce", pending.ExpectedArtifact);
        Assert.Null(unknown.Ack);
        var wrongAck = new NotifyReturnAck
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = "wrong-nonce",
            Artifact = pending.ExpectedArtifact,
            StewardRole = LogicalRoleNormalizer.Steward,
            RecipientRole = pending.RecipientRole,
            RecipientIdentity = pending.RecipientIdentity,
            Resident = pending.Resident!,
            WorkspaceId = pending.WorkspaceId,
            PaneId = pending.PaneId,
            ConsumptionReceiptId = "not-recorded",
            ConsumptionCursor = "cursor-1",
            AvailableAt = Now,
        };
        Assert.True(NotifyCompletionChannelStore.WriteAck(root, wrongAck, write: false).Error is null);
        Assert.Null(NotifyCompletionChannelStore.FindAck(root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact).Ack);
        var missingReceipt = NotifyCompletionChannelStore.FindReceipt(
            root, Domain, Team, pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact,
            LogicalRoleNormalizer.Steward, "unseen-cursor");
        Assert.Null(missingReceipt.Receipt);
    }

    [Fact]
    public void HealthSeparatesDeliveredUnreconciledMissingAckAndResidentPendingStates_G811()
    {
        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);
        Assert.True(NotifyReportOutboxStore.WriteNew(root, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            FromRole = "implementation",
            ToRole = "steward",
            Status = "completed",
            Artifact = pending.ExpectedArtifact,
            Summary = "completed",
            CreatedAt = Now,
            DeliveryState = "delivered",
        }).Written);

        var unreconciled = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now, measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal("failed", unreconciled.State);
        Assert.Equal(1, unreconciled.DeliveredUnreconciledCount);
        Assert.Null(unreconciled.LastFloorCycleAt);
        Assert.False(unreconciled.Qualified is false && unreconciled.State == "healthy");

        Assert.True(NotifyPendingDelegationStore.WriteReport(root, pending, "completed", pending.ExpectedArtifact, "done", Now).Written);
        var missingAck = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now, measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal("failed", missingAck.State);
        Assert.NotNull(missingAck.MissingReturnAckAgeSeconds);

        var receipt = Receipt("cursor-1", Now);
        Assert.True(NotifyCompletionChannelStore.WriteReceipt(root, receipt, write: true).Written);
        var ack = new NotifyReturnAck
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            Artifact = pending.ExpectedArtifact,
            StewardRole = LogicalRoleNormalizer.Steward,
            RecipientRole = pending.RecipientRole,
            RecipientIdentity = pending.RecipientIdentity,
            Resident = pending.Resident!,
            WorkspaceId = pending.WorkspaceId,
            PaneId = pending.PaneId,
            ConsumptionReceiptId = receipt.ReceiptId,
            ConsumptionCursor = receipt.Cursor,
            AvailableAt = Now,
        };
        Assert.True(NotifyCompletionChannelStore.WriteAck(root, ack, write: true).Written);
        var pendingResident = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now, measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal("degraded", pendingResident.State);
        Assert.NotNull(pendingResident.ResidentConsumptionPendingAgeSeconds);

        Assert.True(NotifyCompletionChannelStore.ConsumeAck(root, ack, Now.AddSeconds(1), write: true).Written);
        var settled = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now.AddSeconds(1), measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal("healthy", settled.State);
        Assert.Equal(301, settled.BoundSeconds);
        Assert.Equal(900, settled.ConfiguredBoundSeconds);
        Assert.Equal("measured-sweep-within-configured-bound", settled.QualificationReason);
        Assert.Contains("ceil(300+0+0)+1", settled.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthQualificationUsesMeasuredSweepAgainstConfiguredBound_G811()
    {
        var within = NotifyCompletionChannelHealth.Compute(
            root,
            Domain,
            Team,
            Now,
            floorSeconds: 300,
            maxSweepSeconds: 4.5,
            measuredSweeps: 3,
            configuredBoundSeconds: 900);
        Assert.True(within.Qualified);
        Assert.Equal(306, within.BoundSeconds);
        Assert.Equal("measured-sweep-within-configured-bound", within.QualificationReason);

        var over = NotifyCompletionChannelHealth.Compute(
            root,
            Domain,
            Team,
            Now,
            floorSeconds: 300,
            maxSweepSeconds: 901,
            measuredSweeps: 3,
            configuredBoundSeconds: 900);
        Assert.False(over.Qualified);
        Assert.Equal("degraded", over.State);
        Assert.Equal(1202, over.BoundSeconds);
        Assert.Equal("measured-sweep-exceeds-configured-bound", over.QualificationReason);

        var noBound = NotifyCompletionChannelHealth.Compute(
            root,
            Domain,
            Team,
            Now,
            floorSeconds: 300,
            maxSweepSeconds: 0,
            measuredSweeps: 3);
        Assert.False(noBound.Qualified);
        Assert.Equal("no-configured-bound", noBound.QualificationReason);
        output.WriteLine(
            $"G811 AC4 health qualification: within_P={within.MaxSweepSeconds}; within_bound={within.ConfiguredBoundSeconds}; within_qualified={within.Qualified}; within_B={within.BoundSeconds}; within_reason={within.QualificationReason}; "
            + $"over_P={over.MaxSweepSeconds}; over_bound={over.ConfiguredBoundSeconds}; over_qualified={over.Qualified}; over_B={over.BoundSeconds}; over_reason={over.QualificationReason}; "
            + $"no_configured_bound_qualified={noBound.Qualified}; no_configured_bound_reason={noBound.QualificationReason}");
    }

    [Fact]
    public void JournalEnvelopeMeasuresRequiredScaleAndIncrementalDelta_G811()
    {
        const int requiredCycleRecords = 108_474;
        const long requiredCycleBytes = 130L * 1024 * 1024;
        const long requiredStallBytes = 63L * 1024 * 1024;
        var cyclePath = Path.Combine(root, "cycles.jsonl");
        var stallPath = Path.Combine(root, "stalls.jsonl");

        var cycleHistory = WriteRecords(cyclePath, requiredCycleRecords, requiredCycleBytes, "cycle", 1_280);
        var stallHistory = WriteRecords(stallPath, 1, requiredStallBytes, "stall", 1_280);
        var coldSweeps = Enumerable.Range(0, 3)
            .Select(_ => (Cycle: NotifyCompletionChannelJournalMeasurement.Scan(cyclePath), Stall: NotifyCompletionChannelJournalMeasurement.Scan(stallPath)))
            .ToArray();

        var cursor = cycleHistory.Bytes;
        var steadySweeps = new List<NotifyCompletionJournalScan>();
        for (var index = 0; index < 100; index++)
        {
            AppendRecords(cyclePath, 1, "cycle", 1_280, cycleHistory.Records + index);
            var sweep = NotifyCompletionChannelJournalMeasurement.Scan(cyclePath, cursor);
            Assert.True(sweep.CompleteRecords >= 1, $"steady sweep {index} did not observe its delta record");
            steadySweeps.Add(sweep);
            cursor = sweep.EndOffset;
        }

        AppendRecords(cyclePath, cycleHistory.Records, "cycle", 1_280, cycleHistory.Records);
        AppendRecords(stallPath, stallHistory.Records, "stall", 1_280, stallHistory.Records);
        var doubledCycle = NotifyCompletionChannelJournalMeasurement.Scan(cyclePath);
        var doubledStalls = NotifyCompletionChannelJournalMeasurement.Scan(stallPath);
        var maxSweepSeconds = coldSweeps
            .SelectMany(pair => new[] { pair.Cycle.ElapsedSeconds, pair.Stall.ElapsedSeconds })
            .Concat(steadySweeps.Select(item => item.ElapsedSeconds))
            .Max();

        Assert.True(cycleHistory.Records >= requiredCycleRecords);
        Assert.True(cycleHistory.Bytes >= requiredCycleBytes);
        Assert.True(stallHistory.Bytes >= requiredStallBytes);
        Assert.True(doubledCycle.CompleteRecords >= cycleHistory.Records * 2);
        Assert.True(doubledStalls.CompleteRecords >= stallHistory.Records * 2);
        Assert.Equal(3, coldSweeps.Length);
        Assert.Equal(100, steadySweeps.Count);
        Assert.True(steadySweeps.Max(item => item.BytesScanned) <= NotifyCompletionChannelJournalMeasurement.FixedOverlapBytes + 8_192);
        output.WriteLine(
            $"G811 AC6 journal envelope: cycle_records={cycleHistory.Records}; cycle_bytes={cycleHistory.Bytes}; "
            + $"stall_records={stallHistory.Records}; stall_bytes={stallHistory.Bytes}; cold_sweeps={coldSweeps.Length}; "
            + $"steady_sweeps={steadySweeps.Count}; max_p_seconds={maxSweepSeconds:F6}; "
            + $"steady_max_bytes={steadySweeps.Max(item => item.BytesScanned)}; doubled_cycle_records={doubledCycle.CompleteRecords}; "
            + $"doubled_stall_records={doubledStalls.CompleteRecords}; overlap_bytes={NotifyCompletionChannelJournalMeasurement.FixedOverlapBytes}");
    }

    [Fact]
    public void StatusReportsSettlementAndRepeatedReconcileIsAlreadyConverged_G811()
    {
        var scenarioRoot = Path.Combine(root, "settlement");
        Directory.CreateDirectory(scenarioRoot);
        var pending = Pending() with
        {
            TaskId = "G811-settlement",
            ResultNonce = "g811-settlement-nonce",
            ExpectedArtifact = "https://example.test/pr/1770",
            Resident = NotifyRecordedRole.ExternalResident,
            WorkspaceId = null,
            PaneId = null,
            Reader = ".intent-cli/events/intent-cli/intent-cli-dev.jsonl",
            RecipientIdentity = "external-reader=.intent-cli/events/intent-cli/intent-cli-dev.jsonl",
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(scenarioRoot, pending).Written);
        Assert.True(NotifyReportOutboxStore.WriteNew(scenarioRoot, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = pending.TaskId,
            ResultNonce = pending.ResultNonce,
            FromRole = "reviewer",
            ToRole = "orchestration",
            Status = "completed",
            Artifact = pending.ExpectedArtifact,
            Summary = "G811 settled completion",
            CreatedAt = Now,
            DeliveredAt = Now,
            LastAttemptAt = Now,
            DeliveryState = "delivered",
        }).Written);

        var context = ContextFor(scenarioRoot);
        var dryArgs = new[] {
            "notify", "reconcile", "--domain", Domain, "--team", Team, "--task-id", pending.TaskId,
            "--routing-root", scenarioRoot, "--report-root", scenarioRoot, "--dry-run", "--format", "json",
        };
        var firstDry = Run(context, dryArgs);
        Assert.Equal(0, firstDry.ExitCode);
        Assert.True(firstDry.Result.GetProperty("would_reconcile").GetBoolean());
        Assert.False(firstDry.Result.GetProperty("already_converged").GetBoolean());

        var write = Run(context, [.. dryArgs.Select(argument => argument == "--dry-run" ? "--write" : argument)]);
        Assert.Equal(0, write.ExitCode);
        Assert.True(write.Result.GetProperty("reconciled").GetBoolean());
        var status = Run(context, [
            "notify", "status", "--domain", Domain, "--team", Team, "--task-id", pending.TaskId,
            "--routing-root", scenarioRoot, "--format", "json",
        ]);
        Assert.Equal(0, status.ExitCode);
        Assert.True(status.Result.GetProperty("report_arrived").GetBoolean());
        Assert.Equal("settled", status.Result.GetProperty("verdict").GetString());

        var repeatedDry = Run(context, dryArgs);
        Assert.Equal(0, repeatedDry.ExitCode);
        Assert.True(repeatedDry.Result.GetProperty("already_converged").GetBoolean());
        Assert.False(repeatedDry.Result.GetProperty("would_reconcile").GetBoolean());
        output.WriteLine(
            $"G811 AC3 settlement: first_dry_would_reconcile={firstDry.Result.GetProperty("would_reconcile").GetBoolean()}; "
            + $"write_reconciled={write.Result.GetProperty("reconciled").GetBoolean()}; status_report_arrived={status.Result.GetProperty("report_arrived").GetBoolean()}; "
            + $"repeated_dry_already_converged={repeatedDry.Result.GetProperty("already_converged").GetBoolean()}; "
            + $"continuation_already_converged={repeatedDry.Result.GetProperty("continuation_already_converged").GetBoolean()}");
    }

    [Fact]
    public void FailureHarnessCoversBaselineLossWaitAckRestartCrashCorruptIdleWrongRootAndForeignState_G811()
    {
        // Baseline failure: a delivered reviewer outbox is present while the
        // host pending record still says report_arrived=false.
        var baselineRoot = Path.Combine(root, "baseline");
        Directory.CreateDirectory(baselineRoot);
        var baseline = Pending() with
        {
            TaskId = "G811-baseline-reviewer",
            RecipientRole = "reviewer",
            DelegatingRole = "architect",
            ReportToRole = "orchestration",
            DispatchedAt = Now.AddMinutes(-5),
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(baselineRoot, baseline).Written);
        Assert.True(NotifyReportOutboxStore.WriteNew(baselineRoot, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = baseline.TaskId,
            ResultNonce = baseline.ResultNonce,
            FromRole = "reviewer",
            ToRole = "orchestration",
            Status = "completed",
            Artifact = baseline.ExpectedArtifact,
            Summary = "reviewer report arrived at sender",
            CreatedAt = Now.AddMinutes(-4),
            DeliveredAt = Now.AddMinutes(-4),
            LastAttemptAt = Now.AddMinutes(-4),
            DeliveryState = "delivered",
        }).Written);
        var baselineHealth = NotifyCompletionChannelHealth.Compute(
            baselineRoot, Domain, Team, Now, measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal("failed", baselineHealth.State);
        Assert.Equal(1, baselineHealth.DeliveredUnreconciledCount);
        Assert.Contains("notify reconcile --write", baselineHealth.NextAction!, StringComparison.Ordinal);
        output.WriteLine(
            $"G811 AC5 baseline-failure: state={baselineHealth.State}; delivered_unreconciled={baselineHealth.DeliveredUnreconciledCount}; "
            + $"report_arrived=false; orchestration_action={baselineHealth.NextAction}");

        // Event loss and blocked wait remain low-cost, bounded outcomes.
        var lossRoot = Path.Combine(root, "event-loss");
        Directory.CreateDirectory(lossRoot);
        WriteExternalTopology(lossRoot, ".intent-cli/events/intent-cli/intent-cli-dev.jsonl");
        var lossContext = ContextFor(lossRoot);
        var loss = Run(lossContext, [
            "notify", "collect", "--domain", Domain, "--team", Team, "--role", "steward", "--format", "json",
        ]);
        Assert.Equal(0, loss.ExitCode);
        Assert.Equal("no-events", loss.Result.GetProperty("outcome").GetString());
        Assert.Equal("no-events", loss.Result.GetProperty("cause").GetString());
        Assert.Empty(loss.Result.GetProperty("consumption_receipts").EnumerateArray());
        var blockedWait = Run(lossContext, [
            "notify", "collect", "--domain", Domain, "--team", Team, "--role", "steward",
            "--wait", "--timeout-ms", "1", "--format", "json",
        ]);
        Assert.Equal(0, blockedWait.ExitCode);
        Assert.True(blockedWait.Result.GetProperty("timed_out").GetBoolean());
        Assert.Equal("no-new-events", blockedWait.Result.GetProperty("outcome").GetString());
        output.WriteLine(
            $"G811 AC5 event-loss-blocked-wait: loss_outcome={loss.Result.GetProperty("outcome").GetString()}; "
            + $"loss_receipts=0; blocked_wait_timed_out={blockedWait.Result.GetProperty("timed_out").GetBoolean()}; "
            + "provider_invocations=0; pane_wakes=0");

        // Missing return ack, restart persistence, and intentionally idle
        // resident are separate states, not an automatic wake.
        var ackRoot = Path.Combine(root, "lost-ack");
        Directory.CreateDirectory(ackRoot);
        var ackPending = Pending() with { TaskId = "G811-lost-ack", ExpectedArtifact = "https://example.test/pr/1770-ack" };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(ackRoot, ackPending).Written);
        Assert.True(NotifyPendingDelegationStore.WriteReport(
            ackRoot, ackPending, "completed", ackPending.ExpectedArtifact, "done", Now.AddMinutes(-2)).Written);
        var missingAck = NotifyCompletionChannelHealth.Compute(ackRoot, Domain, Team, Now, measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal("failed", missingAck.State);
        Assert.NotNull(missingAck.MissingReturnAckAgeSeconds);
        var receipt = ReceiptFor(ackPending, "g811-ack-cursor", Now.AddMinutes(-1), ackRoot);
        Assert.True(NotifyCompletionChannelStore.WriteReceipt(ackRoot, receipt, write: true).Written);
        var ack = AckFor(ackPending, receipt);
        Assert.True(NotifyCompletionChannelStore.WriteAck(ackRoot, ack, write: true).Written);
        var idle = NotifyCompletionChannelHealth.Compute(ackRoot, Domain, Team, Now, measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal("degraded", idle.State);
        Assert.NotNull(idle.ResidentConsumptionPendingAgeSeconds);
        var restarted = NotifyCompletionChannelHealth.Compute(ackRoot, Domain, Team, Now.AddSeconds(1), measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.True(restarted.ResidentConsumptionPendingAgeSeconds >= idle.ResidentConsumptionPendingAgeSeconds);
        output.WriteLine(
            $"G811 AC5 lost-ack-restart-idle: missing_ack_state={missingAck.State}; missing_ack_age={missingAck.MissingReturnAckAgeSeconds}; "
            + $"idle_state={idle.State}; consumption_pending_age={idle.ResidentConsumptionPendingAgeSeconds}; restart_state={restarted.State}; "
            + "ack_publication_wakes=0; resident_collect_write_required=true");

        // Crash between pending and continuation writes leaves the report
        // visible but does not invent a continuation row.
        var crashRoot = Path.Combine(root, "crash");
        Directory.CreateDirectory(crashRoot);
        var crashPending = Pending() with { TaskId = "G811-crash", ExpectedArtifact = "https://example.test/pr/1770-crash" };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(crashRoot, crashPending).Written);
        Assert.True(NotifyPendingDelegationStore.WriteReport(
            crashRoot, crashPending, "completed", crashPending.ExpectedArtifact, "crash-window", Now).Written);
        Assert.True(File.Exists(NotifyPendingDelegationStore.ResolvePath(crashRoot, Domain, Team)));
        Assert.False(File.Exists(ContinuationChainStore.ResolvePath(crashRoot, Domain, Team)));
        output.WriteLine(
            $"G811 AC5 crash-pending-continuation: pending_report_arrived={NotifyPendingDelegationStore.Find(crashRoot, Domain, Team, crashPending.TaskId).Record!.ReportArrived}; "
            + "continuation_present=false; retry_owner=orchestration");

        // A corrupt middle record refuses the cursor rather than skipping it.
        var corruptRoot = Path.Combine(root, "corrupt");
        Directory.CreateDirectory(corruptRoot);
        var corruptReader = ".intent-cli/events/intent-cli/intent-cli-dev.jsonl";
        WriteExternalTopology(corruptRoot, corruptReader);
        var corruptPath = Path.Combine(corruptRoot, corruptReader.Replace('/', Path.DirectorySeparatorChar));
        NotifyEventWriter.Append(corruptPath, Event("G811-corrupt-before"));
        File.AppendAllText(corruptPath, "{this-is-not-json}\n");
        NotifyEventWriter.Append(corruptPath, Event("G811-corrupt-after"));
        var corrupt = Run(ContextFor(corruptRoot), [
            "notify", "collect", "--domain", Domain, "--team", Team, "--role", "steward", "--format", "json",
        ]);
        Assert.Equal(1, corrupt.ExitCode);
        Assert.Equal("reader-invalid", corrupt.Result.GetProperty("cause").GetString());
        output.WriteLine(
            $"G811 AC5 corrupt-middle-log: exit={corrupt.ExitCode}; cause={corrupt.Result.GetProperty("cause").GetString()}; "
            + "valid-after-corrupt-record-not-skipped=true");

        // Wrong routing root and an unrelated foreign-submodule-like tree are
        // negative controls; neither may be mutated by a read-only health pass.
        var wrongRoot = Path.Combine(root, "wrong-root");
        var wrong = Run(ContextFor(wrongRoot), [
            "notify", "collect", "--domain", Domain, "--team", Team, "--role", "steward", "--format", "json",
        ]);
        Assert.Equal(1, wrong.ExitCode);
        Assert.Equal("topology-missing", wrong.Result.GetProperty("cause").GetString());
        var foreignRoot = Path.Combine(root, "foreign-submodule", ".git");
        Directory.CreateDirectory(foreignRoot);
        var foreignSentinel = Path.Combine(foreignRoot, "sentinel");
        File.WriteAllText(foreignSentinel, "foreign-tree-unchanged\n");
        var foreignBefore = File.ReadAllText(foreignSentinel);
        _ = NotifyCompletionChannelHealth.Compute(root, Domain, Team, Now, measuredSweeps: 3, configuredBoundSeconds: 900);
        Assert.Equal(foreignBefore, File.ReadAllText(foreignSentinel));
        output.WriteLine(
            $"G811 AC5 wrong-root-foreign-submodule: wrong_root_cause={wrong.Result.GetProperty("cause").GetString()}; "
            + $"foreign_sentinel_unchanged={string.Equals(foreignBefore, File.ReadAllText(foreignSentinel), StringComparison.Ordinal)}; "
            + "publication_safety_preserved=true");
    }

    private void WriteExternalTopology(string scenarioRoot, string readerRelative)
    {
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(scenarioRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "w1",
            roles = new Dictionary<string, object>
            {
                ["steward"] = new { resident = NotifyRecordedRole.ExternalResident, reader = readerRelative },
                ["builder"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "w1", pane_id = "w1:p2" },
            },
        }));
    }

    private static CliContext ContextFor(string scenarioRoot) => new()
    {
        RepoRoot = scenarioRoot,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private NotifyDesignEvent Event(string taskId) => new()
    {
        Timestamp = Now,
        Team = Team,
        Kind = "completion",
        Unit = taskId,
        Summary = "completion event",
        Artifact = $"https://example.test/{taskId}",
        ResultNonce = $"{taskId}-nonce",
    };

    [Fact]
    public void GuidanceExposesCanonicalCollectAcknowledgeReconcileHealthAndNoWakeBoundary_G811()
    {
        var markdown = CompletionChannelGuidance.AppendMarkdown("base");
        Assert.Contains("G811 completion channel", markdown, StringComparison.Ordinal);
        Assert.Contains("notify acknowledge", markdown, StringComparison.Ordinal);
        Assert.Contains("no pane/model wake", markdown, StringComparison.Ordinal);

        var json = CompletionChannelGuidance.AppendJson("{\"route\":\"guide\"}");
        using var document = JsonDocument.Parse(json);
        var channel = document.RootElement.GetProperty("completion_channel");
        Assert.Contains("notify collect", channel.GetProperty("StewardCollectCommand").GetString()!, StringComparison.Ordinal);
        Assert.Contains("--receipt-cursor", channel.GetProperty("StewardAcknowledgeCommand").GetString()!, StringComparison.Ordinal);
        Assert.Contains("notify reconcile", channel.GetProperty("ReconcileCommand").GetString()!, StringComparison.Ordinal);
        Assert.Contains("measured complete sweeps", channel.GetProperty("BoundRule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("No provider, pane, focus", channel.GetProperty("Boundary").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalStewardCollectThenIdentityBoundResidentCollectCompletesTheCanonicalHandshake_G811()
    {
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            },
        };
        var readerRelative = $".intent-cli/events/{Domain}/{Team}.jsonl";
        var readerPath = Path.Combine(root, readerRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(readerPath)!);
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "w1",
            roles = new Dictionary<string, object>
            {
                ["steward"] = new { resident = NotifyRecordedRole.ExternalResident, reader = readerRelative },
                ["builder"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "w1", pane_id = "w1:p2" },
            },
        }));

        var pending = Pending();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, pending).Written);
        NotifyEventWriter.Append(readerPath, new NotifyDesignEvent
        {
            Timestamp = Now,
            Team = Team,
            Kind = "completion",
            Unit = pending.TaskId,
            Summary = "task completed",
            Artifact = pending.ExpectedArtifact,
            ResultNonce = pending.ResultNonce,
            CompletionIdentity = NotifyCompletionChannelStore.CompletionIdentity(pending.TaskId, pending.ResultNonce, pending.ExpectedArtifact),
        });

        using (var writer = new StringWriter())
        {
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        var collect = Run(context, ["notify", "collect", "--domain", Domain, "--team", Team, "--role", "steward", "--write", "--format", "json"]);
        Assert.Equal(0, collect.ExitCode);
        Assert.Equal(1, collect.Result.GetProperty("events").GetArrayLength());
        var receipt = Assert.Single(collect.Result.GetProperty("consumption_receipts").EnumerateArray());
        Assert.True(receipt.GetProperty("written").GetBoolean());
        var cursor = collect.Result.GetProperty("next_cursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        var ack = Run(context, ["notify", "acknowledge", "--domain", Domain, "--team", Team, "--from", "steward", "--task-id", pending.TaskId, "--result-nonce", pending.ResultNonce!, "--artifact", pending.ExpectedArtifact, "--receipt-cursor", cursor!, "--write", "--format", "json"]);
        Assert.Equal(0, ack.ExitCode);
        Assert.True(ack.Result.GetProperty("written").GetBoolean());
        Assert.Equal(pending.RecipientIdentity, ack.Result.GetProperty("ack").GetProperty("recipient_identity").GetString());

        Assert.True(NotifyPendingDelegationStore.WriteReport(root, pending, "completed", pending.ExpectedArtifact, "done", Now).Written);
        var resident = Run(context, ["notify", "collect", "--domain", Domain, "--team", Team, "--task-id", pending.TaskId, "--role", "builder", "--write", "--format", "json"]);
        Assert.Equal(0, resident.ExitCode);
        Assert.True(resident.Result.GetProperty("consumed").GetBoolean());
        Assert.True(resident.Result.GetProperty("ack").GetProperty("consumed_at").ValueKind != JsonValueKind.Null);
        Assert.DoesNotContain(NotifyCompletionChannelStore.ReadAllAcks(root, Domain, Team, out var readError), item => item.ConsumedAt is null);
        Assert.Null(readError);
    }

    private static (int ExitCode, JsonElement Result) Run(CliContext context, string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, context, writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
    }

    private static JournalHistory WriteRecords(
        string path,
        long minimumRecords,
        long minimumBytes,
        string kind,
        int paddingLength)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var records = 0L;
        var bytes = 0L;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024);
        while (records < minimumRecords || bytes < minimumBytes)
        {
            var line = Encoding.UTF8.GetBytes($"{{\"kind\":\"{kind}\",\"index\":{records},\"padding\":\"{new string('x', paddingLength)}\"}}\n");
            stream.Write(line);
            records++;
            bytes += line.Length;
        }

        stream.Flush(true);
        return new JournalHistory(records, bytes);
    }

    private static void AppendRecords(
        string path,
        long count,
        string kind,
        int paddingLength,
        long startIndex)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024);
        for (var index = 0L; index < count; index++)
        {
            var line = Encoding.UTF8.GetBytes($"{{\"kind\":\"{kind}\",\"index\":{startIndex + index},\"padding\":\"{new string('x', paddingLength)}\"}}\n");
            stream.Write(line);
        }

        stream.Flush(true);
    }

    private sealed record JournalHistory(long Records, long Bytes);

    private NotifyConsumptionReceipt ReceiptFor(
        NotifyPendingDelegation pending,
        string cursor,
        DateTimeOffset consumedAt,
        string scenarioRoot) => new()
    {
        ReceiptId = NotifyCompletionChannelStore.BuildReceiptId(
            pending.TaskId,
            pending.ResultNonce,
            pending.ExpectedArtifact,
            LogicalRoleNormalizer.Steward,
            cursor),
        Domain = pending.Domain,
        Team = pending.Team,
        Role = LogicalRoleNormalizer.Steward,
        TaskId = pending.TaskId,
        ResultNonce = pending.ResultNonce!,
        Artifact = pending.ExpectedArtifact,
        Cursor = cursor,
        ReaderPath = Path.Combine(scenarioRoot, "reader.jsonl"),
        ConsumedAt = consumedAt,
    };

    private static NotifyReturnAck AckFor(NotifyPendingDelegation pending, NotifyConsumptionReceipt receipt) => new()
    {
        Domain = pending.Domain,
        Team = pending.Team,
        TaskId = pending.TaskId,
        ResultNonce = pending.ResultNonce!,
        Artifact = pending.ExpectedArtifact,
        StewardRole = LogicalRoleNormalizer.Steward,
        RecipientRole = pending.RecipientRole,
        RecipientIdentity = pending.RecipientIdentity,
        Resident = pending.Resident!,
        WorkspaceId = pending.WorkspaceId,
        PaneId = pending.PaneId,
        ConsumptionReceiptId = receipt.ReceiptId,
        ConsumptionCursor = receipt.Cursor,
        AvailableAt = Now,
    };

    private NotifyPendingDelegation Pending() => new()
    {
        Domain = Domain,
        Team = Team,
        TaskId = "G811-demo",
        TaskKind = "implementation",
        DelegatingRole = "orchestration",
        RecipientRole = "builder",
        ReportToRole = "orchestration",
        RecipientIdentity = "resident=herdr;workspace=w1;pane=w1:p2",
        ExpectedArtifact = "https://example.test/pr/1762",
        ResultNonce = "g811-nonce",
        DispatchedAt = Now,
        Resident = NotifyRecordedRole.HerdrResident,
        WorkspaceId = "w1",
        PaneId = "w1:p2",
        Reader = ".intent-cli/events/intent-cli/intent-cli-dev.jsonl",
        Cwd = "/tmp/g811-child",
        Kind = "completion",
        TransportMode = "herdr-only",
    };

    private NotifyConsumptionReceipt Receipt(string cursor, DateTimeOffset consumedAt) => new()
    {
        ReceiptId = NotifyCompletionChannelStore.BuildReceiptId("G811-demo", "g811-nonce", "https://example.test/pr/1762", LogicalRoleNormalizer.Steward, cursor),
        Domain = Domain,
        Team = Team,
        Role = LogicalRoleNormalizer.Steward,
        TaskId = "G811-demo",
        ResultNonce = "g811-nonce",
        Artifact = "https://example.test/pr/1762",
        Cursor = cursor,
        ReaderPath = Path.Combine(root, "reader.jsonl"),
        ConsumedAt = consumedAt,
    };
}
