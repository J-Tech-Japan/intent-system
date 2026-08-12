using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyPendingDelegationG629Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly Workspace workspace = new();

    public NotifyPendingDelegationG629Tests()
    {
        NotifyCommand.UtcNowFactory = () => FixedNow;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        NotifyPendingDelegationStore.WriteOverride = null;
        NotifyReportOutboxStore.WriteOverride = null;
        workspace.Dispose();
    }

    [Fact]
    public void DelegateWritesDurableRecordAndStatusReportsLiveFromRunningFlag_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (delegateExit, delegated) = workspace.Run(DelegateArgs());
        Assert.Equal(0, delegateExit);
        var pendingPath = delegated.GetProperty("pending_record_path").GetString();
        Assert.NotNull(pendingPath);
        Assert.True(File.Exists(pendingPath));
        Assert.Contains("G629-demo", File.ReadAllText(pendingPath!), StringComparison.Ordinal);

        var (statusExit, status) = workspace.Run(StatusArgs());
        Assert.Equal(0, statusExit);
        Assert.Equal("live", status.GetProperty("verdict").GetString());
        Assert.True(status.GetProperty("recipient_running").GetBoolean());
        Assert.Equal("herdr.agent_running", status.GetProperty("liveness_source").GetString());
        Assert.Equal(FixedNow, status.GetProperty("dispatched_at").GetDateTimeOffset());
    }

    [Fact]
    public void StatusNamesHerdrActivityEvidenceAndDistinguishesWorkingFromLiveIdle_G652()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(
            implementationRunning: true,
            implementationStatus: "working",
            stateChangeSequence: 7,
            lastStateChangeAt: FixedNow.AddMinutes(1)));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);

        var supervisionRoot = workspace.Context.ResolveSupervisionArtifactRootPath();
        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(supervisionRoot, Workspace.Domain, Workspace.Team),
            new NotifySupervisionCycle
            {
                CycleId = "G652-baseline",
                StartedAt = FixedNow,
                CompletedAt = FixedNow,
                IntervalSeconds = 300,
                LastObservedStateChangeSequences = new Dictionary<string, long>
                {
                    ["activity:wH:wH:p2"] = 6,
                },
                LastObservedStateChangeTimes = new Dictionary<string, DateTimeOffset>
                {
                    ["activity:wH:wH:p2"] = FixedNow,
                },
            },
            write: true).Applied);

        var (_, working) = workspace.Run(StatusArgs());
        Assert.Equal("working", working.GetProperty("activity_verdict").GetString());
        Assert.Equal("working", working.GetProperty("agent_status").GetString());
        Assert.Equal(7, working.GetProperty("state_change_seq").GetInt64());

        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(supervisionRoot, Workspace.Domain, Workspace.Team),
            new NotifySupervisionCycle
            {
                CycleId = "G652-current-observation",
                StartedAt = FixedNow.AddMinutes(1),
                CompletedAt = FixedNow.AddMinutes(1),
                IntervalSeconds = 300,
                LastObservedStateChangeSequences = new Dictionary<string, long>
                {
                    ["activity:wH:wH:p2"] = 7,
                },
            },
            write: true).Applied);
        runner.AgentResponse = workspace.HerdrAgents(implementationRunning: true, implementationStatus: "working", stateChangeSequence: 7);
        var (_, idle) = workspace.Run(StatusArgs());
        Assert.Equal("live-idle", idle.GetProperty("activity_verdict").GetString());
        Assert.Contains("advancing_since_last_observation=false", idle.GetProperty("activity_inputs").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StatusTreatsColdStartStateChangesAsWorkingAndKeepsUnknownDistinctFromLiveIdle_G652()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(
            implementationRunning: true,
            implementationStatus: "working",
            stateChangeSequence: 7,
            lastStateChangeAt: FixedNow.AddMinutes(1)));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);

        var (_, first) = workspace.Run(StatusArgs());
        Assert.Equal("working", first.GetProperty("activity_verdict").GetString());
        Assert.Contains("activity_evidence=state-change-after-dispatch", first.GetProperty("activity_inputs").GetString(), StringComparison.Ordinal);

        runner.AgentResponse = workspace.HerdrAgents(
            implementationRunning: true,
            implementationStatus: "working",
            stateChangeSequence: 8,
            lastStateChangeAt: FixedNow.AddMinutes(2));
        var (_, second) = workspace.Run(StatusArgs());
        Assert.Equal("working", second.GetProperty("activity_verdict").GetString());

        runner.AgentResponse = workspace.HerdrAgents(
            implementationRunning: true,
            implementationStatus: "working",
            stateChangeSequence: 9,
            lastStateChangeAt: FixedNow.AddMinutes(3));
        var (_, third) = workspace.Run(StatusArgs());
        Assert.Equal("working", third.GetProperty("activity_verdict").GetString());

        runner.AgentResponse = workspace.HerdrAgents(
            implementationRunning: true,
            implementationStatus: "working",
            stateChangeSequence: 9);
        var (_, unknown) = workspace.Run(StatusArgs());
        Assert.Equal("activity-unknown", unknown.GetProperty("activity_verdict").GetString());
    }

    [Fact]
    public void StatusUsesRunningFlagNotIdleStatusStringAndReportsLost_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);

        runner.AgentResponse = workspace.HerdrAgents(
            implementationRunning: false,
            implementationStatus: "idle",
            includeImplementationSession: false);
        var (statusExit, status) = workspace.Run(StatusArgs());

        Assert.Equal(0, statusExit);
        Assert.Equal("lost", status.GetProperty("verdict").GetString());
        Assert.False(status.GetProperty("recipient_running").GetBoolean());
        Assert.Contains("status strings are ignored", status.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingReportResolvesRecordAndStatusStaysSettledAfterRecipientStops_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);

        runner.AgentResponse = workspace.HerdrAgents(
            implementationRunning: false,
            implementationStatus: "idle",
            includeImplementationSession: false);
        var (reportExit, report) = workspace.Run(ReportArgs());
        Assert.Equal(0, reportExit);
        Assert.True(report.GetProperty("delivered").GetBoolean());

        var (statusExit, status) = workspace.Run(StatusArgs());
        Assert.Equal(0, statusExit);
        Assert.Equal("settled", status.GetProperty("verdict").GetString());
        Assert.True(status.GetProperty("report_arrived").GetBoolean());
    }

    [Fact]
    public void DisposeOpenDelegationSettlesItAndKeepsDispositionVisible_G671()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);
        runner.Calls.Clear();

        var (disposeExit, disposed) = workspace.Run(DisposeArgs(
            "superseded",
            "orchestration",
            "replaced by a later review round",
            supersedingTaskId: "G671-replacement"));

        Assert.Equal(0, disposeExit);
        Assert.True(disposed.GetProperty("written").GetBoolean());
        Assert.Equal("disposition", disposed.GetProperty("settlement_basis").GetString());
        var disposition = disposed.GetProperty("disposition");
        Assert.Equal("superseded", disposition.GetProperty("kind").GetString());
        Assert.Equal("orchestration", disposition.GetProperty("actor").GetString());
        Assert.Equal(FixedNow, disposition.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal("G671-replacement", disposition.GetProperty("superseding_task_id").GetString());
        Assert.Contains("disposition", File.ReadAllText(disposed.GetProperty("pending_record_path").GetString()!), StringComparison.Ordinal);
        Assert.Empty(NotifyPendingDelegationStore.ReadOpen(workspace.RootPath, Workspace.Domain, Workspace.Team, out var readError));
        Assert.Null(readError);

        var (statusExit, status) = workspace.Run(StatusArgs());
        Assert.Equal(0, statusExit);
        Assert.Equal("settled", status.GetProperty("verdict").GetString());
        Assert.Equal("disposition", status.GetProperty("settlement_basis").GetString());
        Assert.False(status.GetProperty("report_arrived").GetBoolean());
        Assert.Equal("superseded", status.GetProperty("disposition").GetProperty("kind").GetString());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void AppliedElsewhereDispositionRequiresEvidenceAndSettlesWithoutTransport_G671()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);
        runner.Calls.Clear();

        var (disposeExit, disposed) = workspace.Run(DisposeArgs(
            "applied-elsewhere",
            "orchestration",
            "outcome was applied by the host review lane",
            appliedOutcomeEvidence: "PR #1451 merged and label transitioned"));

        Assert.Equal(0, disposeExit);
        Assert.Equal("applied-elsewhere", disposed.GetProperty("disposition").GetProperty("kind").GetString());
        Assert.Equal("PR #1451 merged and label transitioned", disposed.GetProperty("disposition").GetProperty("applied_outcome_evidence").GetString());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void DisposeRefusesSettledAndUnknownTaskIdsNamingBothStates_G671()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);
        Assert.Equal(0, workspace.Run(ReportArgs()).ExitCode);

        var (settledExit, settled) = workspace.Run(DisposeArgs(
            "superseded",
            "orchestration",
            "too late",
            supersedingTaskId: "G671-replacement"));
        Assert.Equal(1, settledExit);
        Assert.Equal("already-settled", settled.GetProperty("cause").GetString());
        Assert.Contains("G629-demo", settled.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("report-settled", settled.GetProperty("summary").GetString(), StringComparison.Ordinal);

        var (unknownExit, unknown) = workspace.Run(DisposeArgs(
            "applied-elsewhere",
            "orchestration",
            "no such task",
            appliedOutcomeEvidence: "none",
            taskId: "G671-unknown"));
        Assert.Equal(1, unknownExit);
        Assert.Equal("unknown-task-id", unknown.GetProperty("cause").GetString());
        Assert.Contains("G671-unknown", unknown.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("unknown", unknown.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LateReportForDisposedTaskIsDeliveredWithNamedDisagreementAndDoesNotReopenIt_G671()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);
        Assert.Equal(0, workspace.Run(DisposeArgs(
            "superseded",
            "orchestration",
            "replaced before the worker could report",
            supersedingTaskId: "G671-replacement")).ExitCode);
        runner.Calls.Clear();

        var (reportExit, report) = workspace.Run(ReportArgs());
        Assert.Equal(0, reportExit);
        Assert.True(report.GetProperty("delivered").GetBoolean());
        Assert.Contains("disagreement", report.GetProperty("advisory").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("late report", report.GetProperty("advisory").GetString(), StringComparison.OrdinalIgnoreCase);

        var (statusExit, status) = workspace.Run(StatusArgs());
        Assert.Equal(0, statusExit);
        Assert.Equal("settled", status.GetProperty("verdict").GetString());
        Assert.Equal("disposition", status.GetProperty("settlement_basis").GetString());
        Assert.True(status.GetProperty("report_arrived").GetBoolean());
        Assert.Contains("late report", status.GetProperty("late_report_disagreement").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(NotifyPendingDelegationStore.ReadOpen(workspace.RootPath, Workspace.Domain, Workspace.Team, out var readError));
        Assert.Null(readError);
    }

    [Fact]
    public void UnmatchedReportDeliversWithAdvisoryAndLeavesPendingRecordUnchanged_G640()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var (_, delegated) = workspace.Run(DelegateArgs());
        var pendingPath = delegated.GetProperty("pending_record_path").GetString()!;
        var pendingBefore = File.ReadAllText(pendingPath);
        runner.Calls.Clear();

        var (exitCode, result) = workspace.Run(ReportArgs(taskId: "G629-unknown"));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        var advisory = result.GetProperty("advisory").GetString()!;
        Assert.Contains("G629-unknown", advisory, StringComparison.Ordinal);
        Assert.Contains("No open pending delegation matched", advisory, StringComparison.Ordinal);
        Assert.Contains("G629-demo", advisory, StringComparison.Ordinal);
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("G629-unknown", StringComparison.Ordinal));
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains("Delivered notification", summary, StringComparison.Ordinal);
        Assert.Contains(runner.Calls, call => call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wH:p1"]));
        Assert.Equal(pendingBefore, File.ReadAllText(pendingPath));

        var (humanExit, humanOutput) = workspace.RunText(ReportArgs(taskId: "G629-human", format: "markdown"));
        Assert.Equal(0, humanExit);
        Assert.Contains("- advisory:", humanOutput, StringComparison.Ordinal);
        Assert.Contains("G629-human", humanOutput, StringComparison.Ordinal);
        Assert.Contains("No open pending delegation matched", humanOutput, StringComparison.Ordinal);
        Assert.Equal(pendingBefore, File.ReadAllText(pendingPath));
    }

    [Fact]
    public void CorruptPendingStoreRefusesReportWithoutDelivery_G640()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var pendingPath = NotifyPendingDelegationStore.ResolvePath(
            workspace.RootPath,
            Workspace.Domain,
            Workspace.Team);
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        File.WriteAllText(pendingPath, "{ not valid pending json");

        var (exitCode, result) = workspace.Run(ReportArgs(taskId: "G640-corrupt"));

        Assert.Equal(1, exitCode);
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("unknown-task-id", result.GetProperty("cause").GetString());
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains("G640-corrupt", summary, StringComparison.Ordinal);
        Assert.Contains("could not be read", summary, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void PendingRecordWriteFailureHappensBeforeAnyPaneDelivery_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyPendingDelegationStore.WriteOverride = (path, _) =>
            new NotifyPendingStoreWriteResult(false, path, "fixture denies pending write");

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(1, exitCode);
        Assert.Equal("pending-record-write-failed", result.GetProperty("cause").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ReportOutboxFailsClosedBeforeTransportAndSurvivesFailedDelivery_G653()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);
        runner.Calls.Clear();
        NotifyReportOutboxStore.WriteOverride = (path, _) =>
            new NotifyReportOutboxWriteResult(false, path, "fixture denies outbox write");

        var (unwritableExit, unwritable) = workspace.Run(ReportArgs());

        Assert.Equal(1, unwritableExit);
        Assert.Equal("report-outbox-write-failed", unwritable.GetProperty("cause").GetString());
        Assert.Empty(runner.Calls);

        NotifyReportOutboxStore.WriteOverride = null;
        runner.PromptExitCode = 1;
        var (failedExit, failed) = workspace.Run(ReportArgs());

        Assert.Equal(1, failedExit);
        Assert.False(failed.GetProperty("delivered").GetBoolean());
        var outboxPath = failed.GetProperty("outbox_entry_path").GetString()!;
        Assert.True(File.Exists(outboxPath));
        Assert.Contains(outboxPath, failed.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        var outbox = NotifyReportOutboxStore.Find(workspace.RootPath, Workspace.Domain, Workspace.Team, "G629-demo");
        Assert.True(outbox.Resolved);
        Assert.Equal("undelivered", outbox.Entry!.DeliveryState);
    }

    [Fact]
    public void CollectResendsOnlyPersistedReportAndRefusesSecondCollection_G653()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true)) { PromptExitCode = 1 };
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);
        Assert.Equal(1, workspace.Run(ReportArgs()).ExitCode);
        runner.PromptExitCode = 0;
        runner.Calls.Clear();

        var (repeatedExit, repeated) = workspace.Run(ReportArgs());
        Assert.Equal(1, repeatedExit);
        Assert.Equal("report-outbox-write-failed", repeated.GetProperty("cause").GetString());
        Assert.Contains(
            "intent-cli notify collect --domain intent-cli --team intent-cli-dev --task-id G629-demo --write",
            repeated.GetProperty("summary").GetString(),
            StringComparison.Ordinal);
        Assert.Empty(runner.Calls);

        var (collectExit, collected) = workspace.Run(CollectArgs());

        Assert.Equal(0, collectExit);
        Assert.True(collected.GetProperty("delivered").GetBoolean());
        var (_, status) = workspace.Run(StatusArgs());
        Assert.True(status.GetProperty("report_arrived").GetBoolean());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("send-text"));

        runner.Calls.Clear();
        var (secondExit, second) = workspace.Run(CollectArgs());
        Assert.Equal(1, secondExit);
        Assert.Equal("already-collected", second.GetProperty("cause").GetString());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ReusedTaskIdCreatesNewDispatchGenerationAndUnmatchedReportsRemainMessages_G653()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        Assert.Equal(0, workspace.Run(DelegateArgs("G653-reused", "g653-generation-1")).ExitCode);
        Assert.Equal(0, workspace.Run(ReportArgs("G653-reused")).ExitCode);
        Assert.Equal(0, workspace.Run(DelegateArgs("G653-reused", "g653-generation-2")).ExitCode);
        Assert.Equal(0, workspace.Run(ReportArgs("G653-reused")).ExitCode);

        var firstGeneration = NotifyReportOutboxStore.Find(
            workspace.RootPath, Workspace.Domain, Workspace.Team, "G653-reused", "g653-generation-1");
        var secondGeneration = NotifyReportOutboxStore.Find(
            workspace.RootPath, Workspace.Domain, Workspace.Team, "G653-reused", "g653-generation-2");
        Assert.Equal("delivered", firstGeneration.Entry!.DeliveryState);
        Assert.Equal("delivered", secondGeneration.Entry!.DeliveryState);

        var (_, unmatchedFirst) = workspace.Run(ReportArgs("G653-unmatched"));
        var (_, unmatchedSecond) = workspace.Run(ReportArgs("G653-unmatched"));
        Assert.True(unmatchedFirst.GetProperty("delivered").GetBoolean());
        Assert.True(unmatchedSecond.GetProperty("delivered").GetBoolean());
        Assert.Contains("without creating or resolving a pending record", unmatchedFirst.GetProperty("advisory").GetString(), StringComparison.Ordinal);
        Assert.Contains("without creating or resolving a pending record", unmatchedSecond.GetProperty("advisory").GetString(), StringComparison.Ordinal);

        Assert.Equal(0, workspace.Run(DelegateArgs("G653-question", "g653-question-generation")).ExitCode);
        var (_, question) = workspace.Run(ReportArgs("G653-question", status: "question"));
        var (_, completed) = workspace.Run(ReportArgs("G653-question", status: "completed"));
        Assert.True(question.GetProperty("delivered").GetBoolean());
        Assert.True(completed.GetProperty("delivered").GetBoolean());
        Assert.Contains("without creating or resolving a pending record", completed.GetProperty("advisory").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateRefusesStrandedGenerationAndUsedNonceBeforeWork_G653()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        Assert.Equal(0, workspace.Run(DelegateArgs("G653-stranded", "g653-generation-1")).ExitCode);
        runner.PromptExitCode = 1;
        Assert.Equal(1, workspace.Run(ReportArgs("G653-stranded")).ExitCode);
        runner.PromptExitCode = 0;
        runner.Calls.Clear();

        var (strandedExit, stranded) = workspace.Run(DelegateArgs("G653-stranded", "g653-generation-2"));
        Assert.Equal(1, strandedExit);
        Assert.Equal("undelivered-report-outbox", stranded.GetProperty("cause").GetString());
        Assert.Contains(
            $"intent-cli notify collect --domain intent-cli --team intent-cli-dev --task-id G653-stranded --write --routing-root {workspace.RootPath}",
            stranded.GetProperty("summary").GetString(),
            StringComparison.Ordinal);
        Assert.Empty(runner.Calls);

        Assert.Equal(0, workspace.Run(CollectArgs("G653-stranded")).ExitCode);

        Assert.Equal(0, workspace.Run(DelegateArgs("G653-nonce", "g653-nonce-1")).ExitCode);
        Assert.Equal(0, workspace.Run(ReportArgs("G653-nonce")).ExitCode);
        runner.Calls.Clear();

        var (reusedNonceExit, reusedNonce) = workspace.Run(DelegateArgs("G653-nonce", "g653-nonce-1"));
        Assert.Equal(1, reusedNonceExit);
        Assert.Equal("result-nonce-already-used", reusedNonce.GetProperty("cause").GetString());
        Assert.Contains("fresh --result-nonce or a new --task-id", reusedNonce.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Empty(runner.Calls);

        Assert.Equal(0, workspace.Run(DelegateArgs("G653-nonce", "g653-nonce-2")).ExitCode);
        Assert.Equal(0, workspace.Run(ReportArgs("G653-nonce")).ExitCode);
    }

    [Fact]
    public void DelegateAllowsOpenGenerationResendButRefusesSettledNonceReuse_G653()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        Assert.Equal(0, workspace.Run(DelegateArgs("G653-open-resend", "g653-open-nonce")).ExitCode);
        runner.Calls.Clear();

        var (resendExit, resend) = workspace.Run(DelegateArgs("G653-open-resend", "g653-open-nonce"));
        Assert.Equal(0, resendExit);
        Assert.True(resend.GetProperty("delivered").GetBoolean());
        Assert.Contains(runner.Calls, call => call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wH:p2"]));

        Assert.Equal(0, workspace.Run(ReportArgs("G653-open-resend")).ExitCode);
        runner.Calls.Clear();

        var (settledExit, settled) = workspace.Run(DelegateArgs("G653-open-resend", "g653-open-nonce"));
        Assert.Equal(1, settledExit);
        Assert.Equal("result-nonce-already-used", settled.GetProperty("cause").GetString());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void CollectCarriesAnUnmatchedUndeliveredReportWithoutRedispatch_G653()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true)) { PromptExitCode = 1 };
        NotifyCommand.ProcessRunnerFactory = () => runner;

        Assert.Equal(1, workspace.Run(ReportArgs("G653-unmatched-collect")).ExitCode);
        runner.PromptExitCode = 0;
        runner.Calls.Clear();

        var (collectExit, collected) = workspace.Run(CollectArgs("G653-unmatched-collect"));

        Assert.Equal(0, collectExit);
        Assert.True(collected.GetProperty("delivered").GetBoolean());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("send-text"));
    }

    private static string[] DelegateArgs(string taskId = "G629-demo", string resultNonce = "g629-nonce") =>
    [
        "notify", "delegate", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
        "--task-id", taskId, "--objective", "Inspect pending delegation state",
        "--input", "issue #1373", "--expected-artifact", "draft PR URL", "--result-nonce", resultNonce,
        "--write", "--format", "json",
    ];

    private static string[] ReportArgs(string taskId = "G629-demo", string format = "json", string status = "completed") =>
    [
        "notify", "report", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--from", "implementation", "--to", "orchestration", "--task-id", taskId,
        "--status", status, "--artifact", "https://example.test/pr/1373",
        "--summary", "pending state implemented", "--write", "--format", format,
    ];

    private static string[] DisposeArgs(
        string kind,
        string actor,
        string reason,
        string? supersedingTaskId = null,
        string? appliedOutcomeEvidence = null,
        string taskId = "G629-demo") =>
    BuildDisposeArgs(kind, actor, reason, supersedingTaskId, appliedOutcomeEvidence, taskId);

    private static string[] BuildDisposeArgs(
        string kind,
        string actor,
        string reason,
        string? supersedingTaskId,
        string? appliedOutcomeEvidence,
        string taskId)
    {
        var args = new List<string>
        {
            "notify", "dispose", "--domain", Workspace.Domain, "--team", Workspace.Team,
            "--task-id", taskId, "--kind", kind, "--actor", actor, "--reason", reason,
        };
        if (supersedingTaskId is not null)
        {
            args.AddRange(["--superseding-task-id", supersedingTaskId]);
        }
        if (appliedOutcomeEvidence is not null)
        {
            args.AddRange(["--applied-outcome-evidence", appliedOutcomeEvidence]);
        }
        args.AddRange(["--write", "--format", "json"]);
        return [.. args];
    }

    private static string[] StatusArgs() =>
    [
        "notify", "status", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--task-id", "G629-demo", "--format", "json",
    ];

    private static string[] CollectArgs(string taskId = "G629-demo") =>
    [
        "notify", "collect", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--task-id", taskId, "--write", "--format", "json",
    ];

    private sealed class FakeRunner(Func<string> agentResponse) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public string AgentResponse { get; set; } = agentResponse();
        public int PromptExitCode { get; set; }

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentResponse, string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wH:p2"]))
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                    string.Empty);
            }

            if (arguments.Take(3).SequenceEqual(["agent", "prompt", "wH:p1"]))
            {
                return new NotifyProcessResult(PromptExitCode, string.Empty, PromptExitCode == 0 ? string.Empty : "fixture prompt failure");
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";

        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("notify-g629-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
            WriteTopology();
            using var writer = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public (int ExitCode, string Output) RunText(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public string HerdrAgents(
            bool implementationRunning,
            string implementationStatus = "idle",
            bool includeImplementationSession = true,
            long? stateChangeSequence = null,
            DateTimeOffset? lastStateChangeAt = null)
        {
            object HerdrAgent(string name, string paneId, bool running, string status) => new
            {
                name,
                workspace_id = "wH",
                pane_id = paneId,
                agent = running || includeImplementationSession && name == "implementation" ? "codex" : null,
                agent_session = running || includeImplementationSession && name == "implementation"
                    ? new { id = name }
                    : null,
                agent_status = status,
                interactive_ready = running,
                state_change_seq = name == "implementation" ? stateChangeSequence : null,
                last_state_change_at = name == "implementation" ? lastStateChangeAt?.ToString("O") : null,
            };

            return JsonSerializer.Serialize(new
            {
                result = new
                {
                    agents = new[]
                    {
                        HerdrAgent("orchestration", "wH:p1", true, "idle"),
                        HerdrAgent("implementation", "wH:p2", implementationRunning, implementationStatus),
                    },
                },
            });
        }

        private void WriteTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wH",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new { resident = "herdr", workspace_id = "wH", pane_id = "wH:p1" },
                    ["implementation"] = new { resident = "herdr", workspace_id = "wH", pane_id = "wH:p2" },
                },
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
