using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G545 (round-2 review repair): tests for the ONE canonical bounded
/// transition that converges BOTH authoritative representations of "blocked"
/// — queue-state (<c>state=blocked</c> + <c>blocked_by</c> reason) and the
/// GitHub <c>intent-issue-blocked</c> label — for a single execution unit.
///
/// Every write-path test asserts against the ACTUAL on-disk
/// <c>queue-state.json</c> / <c>runs.jsonl</c> and the ACTUAL label set the
/// mutator now holds, never merely against the command's own result record,
/// so "both sides agree" is proven rather than reported. Failure fixtures
/// inject a failure on each side independently and then re-run the exact same
/// command to prove the retry converges only what has not converged yet,
/// without duplicating the audit event.
/// </summary>
// G553 repair: shares the same static command seams the host-review-preflight
// suite drives when it executes the canonical clear end to end, so both run in
// the serialized collection rather than racing each other's factories.
[Collection("WorkerNextActionSharedState")]
public sealed class AutomationIssueBlockCommandTests : IDisposable
{
    private const string Repo = "sekiban-as-a-service/sekiban";
    private const string BlockedLabel = "intent-issue-blocked";
    private const string Unit = "SKS-G818";
    private const string Reason = "SKS-G837";
    private const int Issue = 818;

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    public AutomationIssueBlockCommandTests()
    {
        ResetSeams();
        AutomationIssueBlockCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose() => ResetSeams();

    private static void ResetSeams()
    {
        AutomationIssueBlockCommand.MutatorFactory = null;
        AutomationIssueBlockCommand.UtcNowFactory = null;
        AutomationIssueBlockCommand.AppendRunEventOverride = null;
        AutomationIssueBlockCommand.WriteQueueStateOverride = null;
    }

    // ---------------------------------------------------------------
    // Happy path: one transition converges both sides.
    // ---------------------------------------------------------------

    [Fact]
    public void Execute_Write_ConvergesQueueStateAndLabelInOneTransition()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-target", "intent-issue-in-progress");

        var result = workspace.Run(BlockArgs());

        Assert.True(result.Converged);
        Assert.False(result.Queue.AlreadyConverged);
        Assert.True(result.Queue.Applied);
        Assert.Equal(Reason, result.Reason);

        // Queue side, read back from disk.
        var item = workspace.ReadQueueItem();
        Assert.Equal(QueueItemState.Blocked, item.State);
        Assert.Equal([Reason], item.BlockedBy);

        // Durable audit, exactly one event, carrying the reason and actor.
        var events = workspace.ReadRunEvents();
        var appended = Assert.Single(events);
        Assert.Equal("blocked", appended.Event);
        Assert.Equal(Unit, appended.ExecutionUnit);
        Assert.Equal(Reason, appended.Reason);
        Assert.Equal("intent-cli automation issue-block", appended.By);

        // GitHub side, read back from the mutator's actual label set.
        Assert.Contains(BlockedLabel, mutator.Labels);
        var transition = Assert.Single(mutator.Transitions);
        Assert.Equal("issue", transition.Kind);
        Assert.Equal(Issue, transition.Number);
        Assert.Equal([BlockedLabel], transition.AddLabels);
        Assert.Empty(transition.RemoveLabels);

        // The claim the review demands: after apply, both sides agree.
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    [Fact]
    public void Execute_Write_Clear_ConvergesBothSidesBackToUnblocked_AndEmptiesBlockedBy()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: [Reason]);
        var mutator = workspace.UseMutator("intent-target", "intent-issue-in-progress", BlockedLabel);

        var result = workspace.Run(ClearArgs());

        Assert.True(result.Converged);
        Assert.True(result.Queue.Applied);

        // blocked_by must be EMPTIED, not merely left behind with the state
        // flipped: IntentNextSliceCommand's gate rejects any item with a
        // non-empty blocked_by, so a stale reason would keep the "cleared"
        // unit unselectable — the same drift, relocated into queue-state.
        var item = workspace.ReadQueueItem();
        Assert.Equal(QueueItemState.Queued, item.State);
        Assert.Empty(item.BlockedBy);
        Assert.Empty(result.Queue.AfterBlockedBy);

        var appended = Assert.Single(workspace.ReadRunEvents());
        Assert.Equal("queued", appended.Event);
        Assert.Equal("intent-cli automation issue-block", appended.By);

        Assert.DoesNotContain(BlockedLabel, mutator.Labels);
        var transition = Assert.Single(mutator.Transitions);
        Assert.Equal([BlockedLabel], transition.RemoveLabels);
        Assert.Empty(transition.AddLabels);

        AssertSidesAgree(workspace, mutator, expectedBlocked: false);
    }

    // ---------------------------------------------------------------
    // Idempotency / one-sided convergence.
    // ---------------------------------------------------------------

    [Fact]
    public void Execute_Write_AlreadyConvergedBothSides_MutatesNeitherSide()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: [Reason]);
        var mutator = workspace.UseMutator("intent-issue-in-progress", BlockedLabel);
        var queueBefore = workspace.ReadQueueStateBytes();

        var result = workspace.Run(BlockArgs());

        Assert.True(result.Converged);
        Assert.True(result.Queue.AlreadyConverged);
        Assert.False(result.Queue.Applied);
        Assert.True(result.Label.HadBlockedLabel);
        Assert.False(result.Label.Applied);

        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_Write_QueueAlreadyBlockedButLabelMissing_ConvergesLabelSideOnly()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: [Reason]);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        var queueBefore = workspace.ReadQueueStateBytes();

        var result = workspace.Run(BlockArgs());

        Assert.True(result.Converged);
        Assert.True(result.Queue.AlreadyConverged);
        Assert.False(result.Queue.Applied);
        Assert.True(result.Label.Applied);

        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Contains(BlockedLabel, mutator.Labels);
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    [Fact]
    public void Execute_Write_LabelAlreadyPresentButQueueNotBlocked_ConvergesQueueSideOnly()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress", BlockedLabel);

        var result = workspace.Run(BlockArgs());

        Assert.True(result.Converged);
        Assert.True(result.Queue.Applied);
        Assert.True(result.Label.HadBlockedLabel);
        Assert.False(result.Label.Applied);

        var item = workspace.ReadQueueItem();
        Assert.Equal(QueueItemState.Blocked, item.State);
        Assert.Equal([Reason], item.BlockedBy);
        Assert.Single(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    [Fact]
    public void Execute_Write_ClearWhenAlreadyUnblockedOnBothSides_MutatesNeitherSide()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        var queueBefore = workspace.ReadQueueStateBytes();

        var result = workspace.Run(ClearArgs());

        Assert.True(result.Converged);
        Assert.True(result.Queue.AlreadyConverged);
        Assert.False(result.Label.Applied);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_Write_ClearWhenStateUnblockedButBlockedByStale_StillConvergesQueueSide()
    {
        // The exact residue QueueManager.TransitionNonBlocking leaves behind
        // when something else moved the unit off blocked: state is no longer
        // blocked, but blocked_by still names the blocker, so the selector
        // still refuses the unit. --clear must finish the job.
        using var workspace = new Workspace(QueueItemState.Queued, blockedBy: [Reason]);
        var mutator = workspace.UseMutator("intent-issue-in-progress", BlockedLabel);

        var result = workspace.Run(ClearArgs());

        Assert.True(result.Converged);
        Assert.False(result.Queue.AlreadyConverged);
        Assert.True(result.Queue.Applied);
        Assert.Empty(workspace.ReadQueueItem().BlockedBy);
        Assert.DoesNotContain(BlockedLabel, mutator.Labels);
        AssertSidesAgree(workspace, mutator, expectedBlocked: false);
    }

    [Fact]
    public void Execute_Write_ReblockAfterFullCycle_AppendsFreshAuditEvent_NotAPendingRetry()
    {
        // block -> clear -> block with the SAME reason text. The final block
        // must append a THIRD event: the matching earlier "blocked" event is
        // historical, not a pending partial-failure retry.
        using var workspace = new Workspace(QueueItemState.Queued);
        workspace.UseMutator("intent-issue-in-progress");

        Assert.True(workspace.Run(BlockArgs()).Converged);
        Assert.True(workspace.Run(ClearArgs()).Converged);
        Assert.True(workspace.Run(BlockArgs()).Converged);

        var events = workspace.ReadRunEvents();
        Assert.Equal(3, events.Count);
        Assert.Equal(["blocked", "queued", "blocked"], events.Select(e => e.Event).ToArray());
        Assert.Equal(QueueItemState.Blocked, workspace.ReadQueueItem().State);
    }

    // ---------------------------------------------------------------
    // Fail-closed refusals — all BEFORE any mutation.
    // ---------------------------------------------------------------

    [Fact]
    public void Execute_AlreadyBlockedWithDifferentReason_RefusesWithoutMutatingEitherSide()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["some other blocker"]);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        var queueBefore = workspace.ReadQueueStateBytes();

        var (exitCode, output) = workspace.RunRaw(BlockArgs());

        Assert.Equal(1, exitCode);
        Assert.Contains("already blocked with reason 'some other blocker'", output, StringComparison.Ordinal);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_LinkedIssueNumberMismatch_RefusesBeforeAnyInteraction()
    {
        using var workspace = new Workspace(
            QueueItemState.Queued,
            linkedIssue: new LinkedIssue { Repo = Repo, Number = 999 });

        AssertRefusedWithoutTouchingAnything(workspace, BlockArgs(), "linked_issue is #999");
    }

    [Fact]
    public void Execute_LinkedIssueRepoMismatch_SameNumber_RefusesBeforeAnyInteraction()
    {
        // The dangerous case: issue #818 exists in almost every repository,
        // so number-only agreement is not evidence of the same issue.
        using var workspace = new Workspace(
            QueueItemState.Queued,
            linkedIssue: new LinkedIssue { Repo = "some-other-org/some-other-repo", Number = Issue });

        AssertRefusedWithoutTouchingAnything(
            workspace, BlockArgs(), "refusing to label an issue in a different repository");
    }

    [Fact]
    public void Execute_LinkedIssueMissingEntirely_RefusesBeforeAnyInteraction()
    {
        // Absent linkage is missing evidence, not consent.
        using var workspace = new Workspace(QueueItemState.Queued, omitLinkedIssue: true);

        AssertRefusedWithoutTouchingAnything(
            workspace, BlockArgs(), "has no complete queue-state linked_issue");
    }

    [Fact]
    public void Execute_LinkedIssueNumberMissing_RefusesBeforeAnyInteraction()
    {
        using var workspace = new Workspace(
            QueueItemState.Queued,
            linkedIssue: new LinkedIssue { Repo = Repo, Number = null });

        AssertRefusedWithoutTouchingAnything(
            workspace, BlockArgs(), "has no complete queue-state linked_issue");
    }

    [Fact]
    public void Execute_LinkedIssueRepoDiffersOnlyInCaseOrUrlShape_IsCanonicallyEqual_AndProceeds()
    {
        // GitHub owner/repo names are case-insensitive, and queue-state has
        // been observed carrying URL/.git shapes. Canonical equality must not
        // be mistaken for a mismatch and block a legitimate transition.
        using var workspace = new Workspace(
            QueueItemState.Queued,
            linkedIssue: new LinkedIssue { Repo = "https://github.com/Sekiban-As-A-Service/Sekiban.git", Number = Issue });
        var mutator = workspace.UseMutator("intent-issue-in-progress");

        var result = workspace.Run(BlockArgs());

        Assert.True(result.Converged);
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    [Fact]
    public void Execute_UnknownExecutionUnit_RefusesBeforeAnyMutation()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");

        var (exitCode, output) = workspace.RunRaw(
            ["SKS-G999", "--repo", Repo, "--issue", "818", "--reason", Reason, "--write", "--format", "json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("has no item with execution_unit 'SKS-G999'", output, StringComparison.Ordinal);
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_MissingExecutionUnitArgument_Refuses()
    {
        using var workspace = new Workspace(QueueItemState.Queued);

        var (exitCode, output) = workspace.RunRaw(
            ["--repo", Repo, "--issue", "818", "--reason", Reason, "--write"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit as the first argument", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesReasonWithClear()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: [Reason]);

        var (exitCode, output) = workspace.RunRaw(
            [Unit, "--repo", Repo, "--issue", "818", "--clear", "--reason", "x", "--write"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason is only supported when applying", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesMissingReasonWithoutClear()
    {
        using var workspace = new Workspace(QueueItemState.Queued);

        var (exitCode, output) = workspace.RunRaw([Unit, "--repo", Repo, "--issue", "818", "--write"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason is required unless --clear", output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    // Dry-run: reports, never touches anything.
    // ---------------------------------------------------------------

    [Fact]
    public void Execute_DryRun_LeavesQueueRunsAndLabelsByteIdentical()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        var queueBefore = workspace.ReadQueueStateBytes();

        var result = workspace.Run([Unit, "--repo", Repo, "--issue", "818", "--reason", Reason, "--format", "json"]);

        Assert.False(result.Converged);
        Assert.True(result.Queue.Applied);          // "would apply"
        Assert.Equal("blocked", result.Queue.AfterState);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
        Assert.DoesNotContain(BlockedLabel, mutator.Labels);
    }

    [Fact]
    public void Execute_DryRun_Clear_LeavesQueueRunsAndLabelsByteIdentical()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: [Reason]);
        var mutator = workspace.UseMutator("intent-issue-in-progress", BlockedLabel);
        var queueBefore = workspace.ReadQueueStateBytes();

        var result = workspace.Run([Unit, "--repo", Repo, "--issue", "818", "--clear", "--format", "json"]);

        Assert.False(result.Converged);
        Assert.Equal("queued", result.Queue.AfterState);
        Assert.Empty(result.Queue.AfterBlockedBy);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
        Assert.Contains(BlockedLabel, mutator.Labels);
    }

    // ---------------------------------------------------------------
    // Failure fixtures + retry convergence, one per side.
    // ---------------------------------------------------------------

    [Fact]
    public void Execute_AuditAppendFails_FailsLoud_AndLeavesQueueStateAndLabelsUntouched()
    {
        // Audit is appended BEFORE queue-state is written, so an append
        // failure must leave a completely unmutated system: no state write,
        // no label call, non-zero exit with a message naming the unit.
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        var queueBefore = workspace.ReadQueueStateBytes();
        AutomationIssueBlockCommand.AppendRunEventOverride = (_, _) => throw new IOException("runs.jsonl is read-only");

        var (exitCode, output) = workspace.RunRaw(BlockArgs());

        Assert.Equal(1, exitCode);
        Assert.Contains("failed to converge queue-state for 'SKS-G818'", output, StringComparison.Ordinal);
        Assert.Contains("runs.jsonl is read-only", output, StringComparison.Ordinal);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Empty(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_QueueStateWriteFailsAfterAudit_RetryReusesPendingEventAndConverges()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        var queueBefore = workspace.ReadQueueStateBytes();
        AutomationIssueBlockCommand.WriteQueueStateOverride = (_, _) => throw new IOException("queue-state.json is read-only");

        var (exitCode, output) = workspace.RunRaw(BlockArgs());

        // Fail loud: the audit event is durable, queue-state is not yet.
        Assert.Equal(1, exitCode);
        Assert.Contains("failed to converge queue-state for 'SKS-G818'", output, StringComparison.Ordinal);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Single(workspace.ReadRunEvents());
        Assert.Empty(mutator.Transitions);

        // Retry the EXACT same command once the write path recovers.
        AutomationIssueBlockCommand.WriteQueueStateOverride = null;
        var retry = workspace.Run(BlockArgs());

        Assert.True(retry.Converged);
        var item = workspace.ReadQueueItem();
        Assert.Equal(QueueItemState.Blocked, item.State);
        Assert.Equal([Reason], item.BlockedBy);

        // No duplicate audit: the pending event is reused, not re-appended.
        var appended = Assert.Single(workspace.ReadRunEvents());
        Assert.Equal("blocked", appended.Event);
        Assert.Equal(Reason, appended.Reason);

        Assert.Contains(BlockedLabel, mutator.Labels);
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    [Fact]
    public void Execute_LabelApplyFails_QueueSideStaysConverged_RetryConvergesLabelOnly()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        mutator.ThrowOnApply = new InvalidOperationException("gh issue edit failed with exit code 1");

        var (exitCode, output) = workspace.RunRaw(BlockArgs());

        // Fail loud, and say exactly what to do about it.
        Assert.Equal(1, exitCode);
        Assert.Contains("failed to apply the intent-issue-blocked label", output, StringComparison.Ordinal);
        Assert.Contains("Re-run this exact command to retry the label step only", output, StringComparison.Ordinal);

        // Queue side already converged and audited exactly once.
        Assert.Equal(QueueItemState.Blocked, workspace.ReadQueueItem().State);
        Assert.Single(workspace.ReadRunEvents());
        Assert.DoesNotContain(BlockedLabel, mutator.Labels);

        // Retry: queue side is skipped, label side is applied.
        mutator.ThrowOnApply = null;
        var retry = workspace.Run(BlockArgs());

        Assert.True(retry.Converged);
        Assert.True(retry.Queue.AlreadyConverged);
        Assert.False(retry.Queue.Applied);
        Assert.True(retry.Label.Applied);
        Assert.Single(workspace.ReadRunEvents());
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    [Fact]
    public void Execute_Clear_LabelRemoveFails_RetryConvergesLabelOnly()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: [Reason]);
        var mutator = workspace.UseMutator("intent-issue-in-progress", BlockedLabel);
        mutator.ThrowOnApply = new InvalidOperationException("gh issue edit failed with exit code 1");

        var (exitCode, output) = workspace.RunRaw(ClearArgs());

        Assert.Equal(1, exitCode);
        Assert.Contains("failed to clear the intent-issue-blocked label", output, StringComparison.Ordinal);

        // Queue side already cleared (state AND blocked_by), audited once.
        var afterFailure = workspace.ReadQueueItem();
        Assert.Equal(QueueItemState.Queued, afterFailure.State);
        Assert.Empty(afterFailure.BlockedBy);
        Assert.Single(workspace.ReadRunEvents());
        Assert.Contains(BlockedLabel, mutator.Labels);

        mutator.ThrowOnApply = null;
        var retry = workspace.Run(ClearArgs());

        Assert.True(retry.Converged);
        Assert.True(retry.Queue.AlreadyConverged);
        Assert.True(retry.Label.Applied);
        Assert.Single(workspace.ReadRunEvents());
        AssertSidesAgree(workspace, mutator, expectedBlocked: false);
    }

    [Fact]
    public void Execute_LabelReadFails_QueueSideStaysConverged_AndRetryIsSafe()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        mutator.ThrowOnRead = new InvalidOperationException("gh issue view failed with exit code 1");

        var (exitCode, output) = workspace.RunRaw(BlockArgs());

        Assert.Equal(1, exitCode);
        Assert.Contains("failed to read current labels", output, StringComparison.Ordinal);
        Assert.Equal(QueueItemState.Blocked, workspace.ReadQueueItem().State);
        Assert.Single(workspace.ReadRunEvents());

        mutator.ThrowOnRead = null;
        var retry = workspace.Run(BlockArgs());

        Assert.True(retry.Converged);
        Assert.Single(workspace.ReadRunEvents());
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    [Fact]
    public void Execute_ExistingRunLogUnparseable_FailsLoudBeforeAnyInteraction()
    {
        // A present-but-unreadable audit trail must stop everything: no
        // append, no queue write, no label read or mutation. Appending into a
        // file whose existing content cannot be parsed would both corrupt the
        // trail further and decide retry-vs-fresh-append from no evidence.
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        workspace.WriteRawRunLog("{\"ts\":\"2026-07-21T10:00:00Z\",\"execution_unit\":\"SKS-G818\"" + Environment.NewLine + "not json at all" + Environment.NewLine);
        var queueBefore = workspace.ReadQueueStateBytes();
        var runLogBefore = workspace.ReadRunLogBytes();

        var (exitCode, output) = workspace.RunRaw(BlockArgs());

        Assert.Equal(1, exitCode);
        Assert.Contains("could not be read", output, StringComparison.Ordinal);
        Assert.Contains("Refusing to transition against an unreadable audit trail", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation runs-audit", output, StringComparison.Ordinal);

        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Equal(runLogBefore, workspace.ReadRunLogBytes());
        Assert.Equal(0, mutator.ReadCount);
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_ExistingRunLogUnparseable_AlsoFailsLoudOnClearAndDryRun()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: [Reason]);
        var mutator = workspace.UseMutator("intent-issue-in-progress", BlockedLabel);
        workspace.WriteRawRunLog("}{ this is not a run event" + Environment.NewLine);
        var runLogBefore = workspace.ReadRunLogBytes();

        var (clearExit, clearOutput) = workspace.RunRaw(ClearArgs());
        var (dryRunExit, dryRunOutput) = workspace.RunRaw(
            [Unit, "--repo", Repo, "--issue", "818", "--reason", Reason, "--format", "json"]);

        Assert.Equal(1, clearExit);
        Assert.Equal(1, dryRunExit);
        Assert.Contains("could not be read", clearOutput, StringComparison.Ordinal);
        Assert.Contains("could not be read", dryRunOutput, StringComparison.Ordinal);
        Assert.Equal(runLogBefore, workspace.ReadRunLogBytes());
        Assert.Equal(0, mutator.ReadCount);
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void Execute_MissingRunLog_RemainsTheValidFirstEventCase()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        Assert.False(File.Exists(workspace.RunLogPath));

        var result = workspace.Run(BlockArgs());

        Assert.True(result.Converged);
        Assert.Single(workspace.ReadRunEvents());
        AssertSidesAgree(workspace, mutator, expectedBlocked: true);
    }

    // ---------------------------------------------------------------
    // Routing / help.
    // ---------------------------------------------------------------

    [Fact]
    public void CommandRouter_RegistersAutomationIssueBlock()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        workspace.UseMutator("intent-issue-in-progress");

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "issue-block", Unit, "--repo", Repo, "--issue", "818", "--reason", Reason, "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockResult>(writer.ToString())!;
        Assert.Equal(Issue, result.Issue);
        Assert.Equal(Unit, result.ExecutionUnit);
    }

    [Fact]
    public void Execute_Help_DocumentsThePositionalExecutionUnitAndBothSides()
    {
        using var workspace = new Workspace(QueueItemState.Queued);
        using var writer = new StringWriter();

        var exitCode = AutomationIssueBlockCommand.Execute(workspace.Context, ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("issue-block <execution-unit> --repo", output, StringComparison.Ordinal);
        Assert.Contains("queue-state", output, StringComparison.Ordinal);
        Assert.Contains("intent-issue-blocked", output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------

    // ---------------------------------------------------------------
    // G561: the PRE-PUBLISH exit — a unit blocked before publish to encode
    // publish priority has no linked issue, so the two-sided path above
    // cannot clear it. Before this flag there was no canonical exit at all.
    // ---------------------------------------------------------------

    [Fact]
    public void PrePublishClear_OnALinkedIssueLessBlockedUnit_QueuesItAndEmptiesBlockedBy_G561()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["G560"], omitLinkedIssue: true);

        var (exitCode, output) = workspace.RunRaw(PrePublishClearArgs());
        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockPrePublishResult>(output)!;

        Assert.True(result.PrePublish);
        Assert.True(result.Converged);
        Assert.True(result.Queue.Applied);
        Assert.False(result.Queue.AlreadyConverged);
        Assert.Equal(["G560"], result.ClearedBlockedBy);

        // Read back from disk: both fields the selector reads must move
        // together. A cleared state with a stale blocked_by is a unit that is
        // still, in effect, blocked.
        var item = workspace.ReadQueueItem();
        Assert.Equal(QueueItemState.Queued, item.State);
        Assert.Empty(item.BlockedBy);

        // The durable event names WHAT was cleared — a run log recording only
        // "queued" would leave the publish-priority decision unauditable.
        var appended = Assert.Single(workspace.ReadRunEvents());
        Assert.Equal("queued", appended.Event);
        Assert.Equal(Unit, appended.ExecutionUnit);
        Assert.Equal("intent-cli automation issue-block", appended.By);
        Assert.Contains("G560", appended.Reason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void PrePublishClear_NeverTouchesGitHub_BecauseThereIsNoIssue_G561()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["G560"], omitLinkedIssue: true);
        var mutator = workspace.UseMutator("whatever");

        Assert.Equal(0, workspace.RunRaw(PrePublishClearArgs()).ExitCode);

        // Not merely "did not mutate" — did not even READ. An unpublished unit
        // has no issue to read, so reaching for one could only fail on absent
        // evidence rather than on a real problem.
        Assert.Equal(0, mutator.ReadCount);
        Assert.Empty(mutator.Transitions);
    }

    [Fact]
    public void PrePublishClear_DryRun_ReportsTheTransitionWithoutWritingAnything_G561()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["G560"], omitLinkedIssue: true);
        var queueBefore = workspace.ReadQueueStateBytes();
        var runLogBefore = workspace.ReadRunLogBytes();

        var (exitCode, output) = workspace.RunRaw([Unit, "--clear", "--pre-publish", "--dry-run", "--format", "json"]);
        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockPrePublishResult>(output)!;

        Assert.Equal("queued", result.Queue.AfterState);
        Assert.Empty(result.Queue.AfterBlockedBy);
        // Dry-run never claims convergence — nothing was written.
        Assert.False(result.Converged);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Equal(runLogBefore, workspace.ReadRunLogBytes());
    }

    [Fact]
    public void PrePublishClear_IsIdempotent_OnAnAlreadyQueuedUnit_G561()
    {
        using var workspace = new Workspace(QueueItemState.Queued, blockedBy: [], omitLinkedIssue: true);

        var (exitCode, output) = workspace.RunRaw(PrePublishClearArgs());
        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationIssueBlockPrePublishResult>(output)!;

        Assert.True(result.Queue.AlreadyConverged);
        Assert.False(result.Queue.Applied);
        Assert.Empty(workspace.ReadRunEvents());
    }

    [Fact]
    public void PrePublishClear_OnAUnitThatHasALinkedIssue_FailsClosed_G561()
    {
        // Fail-closed case 1. The two-sided path also converges the GitHub
        // label; taking the queue-only shortcut for a published unit would
        // leave the label behind — the exact drift G545 exists to prevent.
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["G560"]);

        AssertRefusedWithoutTouchingAnything(
            workspace,
            PrePublishClearArgs(),
            "has a queue-state linked_issue");
    }

    [Fact]
    public void PrePublishClear_OnAUnitWithPartialLinkage_FailsClosed_G561()
    {
        // A half-recorded linkage is evidence of a publish attempt, not
        // evidence of its absence.
        using var workspace = new Workspace(
            QueueItemState.Blocked,
            blockedBy: ["G560"],
            linkedIssue: new LinkedIssue { Repo = Repo, Number = null, Url = null });

        AssertRefusedWithoutTouchingAnything(
            workspace,
            PrePublishClearArgs(),
            "has a queue-state linked_issue");
    }

    [Fact]
    public void PrePublishClear_WithRepoAndIssue_FailsClosed_G561()
    {
        // Fail-closed case 2: identifiers this path can neither verify nor act
        // on. Ignoring them would let a caller believe a GitHub side was
        // converged when none was touched.
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["G560"], omitLinkedIssue: true);

        AssertRefusedWithoutTouchingAnything(
            workspace,
            [Unit, "--repo", Repo, "--issue", "818", "--clear", "--pre-publish", "--write", "--format", "json"],
            "--pre-publish takes no --repo/--issue");
    }

    [Fact]
    public void PrePublish_WithoutClear_IsRejected_G561()
    {
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["G560"], omitLinkedIssue: true);

        AssertRefusedWithoutTouchingAnything(
            workspace,
            [Unit, "--pre-publish", "--write", "--format", "json"],
            "--pre-publish is only supported together with --clear");
    }

    [Fact]
    public void PublishPriorityLifecycle_BlockPrePublish_ThenClear_MakesTheUnitSelectorEligible_G561()
    {
        // The demo sequence the slice exists for, end to end on real state:
        // a unit blocked before publish is invisible to the selector, and the
        // canonical pre-publish clear is what makes it selectable again.
        using var workspace = new Workspace(QueueItemState.Blocked, blockedBy: ["G560"], omitLinkedIssue: true);

        var blocked = workspace.ReadQueueItem();
        Assert.False(IsSelectorEligible(blocked));

        Assert.Equal(0, workspace.RunRaw(PrePublishClearArgs()).ExitCode);

        var cleared = workspace.ReadQueueItem();
        Assert.True(
            IsSelectorEligible(cleared),
            $"expected a selectable unit, got state={cleared.State} blocked_by=[{string.Join(", ", cleared.BlockedBy)}]");
    }

    /// <summary>
    /// The two conditions <c>intent next-slice</c> applies before a queued item
    /// is a candidate: it must not be in a blocked state, and its
    /// <c>blocked_by</c> must be empty — the second is the one a bare queue
    /// transition leaves behind, producing a unit that reports itself unblocked
    /// while remaining unselectable.
    /// </summary>
    private static bool IsSelectorEligible(QueueItem item) =>
        item.State == QueueItemState.Queued && item.BlockedBy.Count == 0;

    private static string[] PrePublishClearArgs() =>
        [Unit, "--clear", "--pre-publish", "--write", "--format", "json"];

    private static string[] BlockArgs() =>
        [Unit, "--repo", Repo, "--issue", "818", "--reason", Reason, "--write", "--format", "json"];

    private static string[] ClearArgs() =>
        [Unit, "--repo", Repo, "--issue", "818", "--clear", "--write", "--format", "json"];

    /// <summary>
    /// A fail-closed refusal must leave ALL THREE sides untouched: the
    /// queue-state file byte-identical, the run log byte-identical, and
    /// GitHub not even read (let alone mutated).
    /// </summary>
    private static void AssertRefusedWithoutTouchingAnything(Workspace workspace, string[] args, string expectedMessage)
    {
        var mutator = workspace.UseMutator("intent-issue-in-progress");
        var queueBefore = workspace.ReadQueueStateBytes();
        var runLogBefore = workspace.ReadRunLogBytes();

        var (exitCode, output) = workspace.RunRaw(args);

        Assert.Equal(1, exitCode);
        Assert.Contains(expectedMessage, output, StringComparison.Ordinal);
        Assert.Equal(queueBefore, workspace.ReadQueueStateBytes());
        Assert.Equal(runLogBefore, workspace.ReadRunLogBytes());
        Assert.Equal(0, mutator.ReadCount);
        Assert.Empty(mutator.Transitions);
    }

    /// <summary>
    /// The acceptance the round-2 review demands: the ACTUAL persisted queue
    /// state and the ACTUAL label set say the same thing about "blocked".
    /// </summary>
    private static void AssertSidesAgree(Workspace workspace, FakeMutator mutator, bool expectedBlocked)
    {
        var item = workspace.ReadQueueItem();
        var queueSaysBlocked = item.State == QueueItemState.Blocked;
        var labelSaysBlocked = mutator.Labels.Contains(BlockedLabel);

        Assert.Equal(expectedBlocked, queueSaysBlocked);
        Assert.Equal(expectedBlocked, labelSaysBlocked);
        Assert.Equal(expectedBlocked, item.BlockedBy.Count > 0);
    }

    private sealed class FakeMutator : IGitHubLabelMutator
    {
        public List<string> Labels { get; }
        public List<Transition> Transitions { get; } = new();
        public Exception? ThrowOnRead { get; set; }
        public Exception? ThrowOnApply { get; set; }

        /// <summary>Any GitHub interaction at all — a fail-closed refusal must leave this at 0.</summary>
        public int ReadCount { get; private set; }

        public FakeMutator(IEnumerable<string> labels) => Labels = labels.ToList();

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
        {
            ReadCount++;
            if (ThrowOnRead is not null)
            {
                throw ThrowOnRead;
            }

            return Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();
        }

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            if (ThrowOnApply is not null)
            {
                throw ThrowOnApply;
            }

            Transitions.Add(new Transition(kind, number, addLabels.ToArray(), removeLabels.ToArray()));
            foreach (var remove in removeLabels)
            {
                Labels.Remove(remove);
            }

            foreach (var add in addLabels.Where(add => !Labels.Contains(add)))
            {
                Labels.Add(add);
            }
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    private sealed record Transition(string Kind, int Number, IReadOnlyList<string> AddLabels, IReadOnlyList<string> RemoveLabels);

    private sealed class Workspace : IDisposable
    {
        /// <param name="linkedIssue">
        /// Overrides the default COMPLETE, canonical linkage. Safe/happy
        /// fixtures deliberately leave this at the default so they exercise
        /// the same complete-linkage requirement production callers face.
        /// </param>
        /// <param name="omitLinkedIssue">Drops <c>linked_issue</c> entirely.</param>
        public Workspace(
            QueueItemState state,
            IReadOnlyList<string>? blockedBy = null,
            LinkedIssue? linkedIssue = null,
            bool omitLinkedIssue = false)
        {
            RootPath = Directory.CreateTempSubdirectory("automation-issue-block-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" } },
            };

            File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = FixedNow,
                Items =
                [
                    CreateItem(
                        Unit,
                        state,
                        blockedBy ?? [],
                        omitLinkedIssue
                            ? null
                            : linkedIssue ?? new LinkedIssue { Repo = Repo, Number = Issue, Url = $"https://github.com/{Repo}/issues/{Issue}" }),
                    CreateItem("SKS-G901", QueueItemState.Queued, [], null),
                ],
            }));
        }

        public string RootPath { get; }
        public CliContext Context { get; }
        public string QueueStatePath => Path.Combine(RootPath, ".intent-cli", "queue-state.json");
        public string RunLogPath => Path.Combine(RootPath, ".intent-cli", "runs.jsonl");

        public FakeMutator UseMutator(params string[] labels)
        {
            var mutator = new FakeMutator(labels);
            AutomationIssueBlockCommand.MutatorFactory = () => mutator;
            return mutator;
        }

        public (int ExitCode, string Output) RunRaw(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = AutomationIssueBlockCommand.Execute(Context, args, writer);
            return (exitCode, writer.ToString());
        }

        public AutomationIssueBlockResult Run(string[] args)
        {
            var (exitCode, output) = RunRaw(args);
            Assert.Equal(0, exitCode);
            return JsonSerializer.Deserialize<AutomationIssueBlockResult>(output)!;
        }

        public QueueItem ReadQueueItem() =>
            QueueStateSerializer.Deserialize(File.ReadAllText(QueueStatePath))
                .Items.Single(item => item.ExecutionUnit == Unit);

        public string ReadQueueStateBytes() => File.ReadAllText(QueueStatePath);

        public string ReadRunLogBytes() => File.Exists(RunLogPath) ? File.ReadAllText(RunLogPath) : "(absent)";

        public void WriteRawRunLog(string content) => File.WriteAllText(RunLogPath, content);

        public IReadOnlyList<RunEvent> ReadRunEvents() =>
            File.Exists(RunLogPath)
                ? RunLogSerializer.DeserializeAll(File.ReadAllText(RunLogPath))
                : Array.Empty<RunEvent>();

        private static QueueItem CreateItem(string executionUnit, QueueItemState state, IReadOnlyList<string> blockedBy, LinkedIssue? linkedIssue) =>
            new()
            {
                ExecutionUnit = executionUnit,
                Title = $"[{executionUnit}] Queue Item",
                State = state,
                Dependencies = [],
                BlockedBy = blockedBy,
                ClarificationReturnPath = "intents/sekiban-as-a-service/clarifications/open.md",
                PacketPaths = new PacketPaths
                {
                    Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                    ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                    Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                },
                LinkedIssue = linkedIssue,
                WorkerRole = "coder",
                ReviewRole = "reviewer",
                Priority = "normal",
            };

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
