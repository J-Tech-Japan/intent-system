using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class QueueReprioritizeCommandTests : IDisposable
{
    public QueueReprioritizeCommandTests()
    {
        QueueReprioritizeCommand.UtcNowFactory = null;
        QueueReprioritizeCommand.AppendPriorityChangedEventOverride = null;
        QueueReprioritizeCommand.WriteQueueStateOverride = null;
    }

    public void Dispose()
    {
        QueueReprioritizeCommand.UtcNowFactory = null;
        QueueReprioritizeCommand.AppendPriorityChangedEventOverride = null;
        QueueReprioritizeCommand.WriteQueueStateOverride = null;
    }

    [Fact]
    public void Execute_DryRunDefault_ReportsWouldChangeWithoutMutating()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var queueStateBefore = File.ReadAllText(workspace.QueueStatePath);

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "publish ahead of G530", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("normal", root.GetProperty("old_priority").GetString());
        Assert.Equal("high", root.GetProperty("requested_priority").GetString());
        Assert.True(root.GetProperty("changed").GetBoolean());

        // No mutation on dry-run.
        Assert.Equal(queueStateBefore, File.ReadAllText(workspace.QueueStatePath));
        Assert.False(File.Exists(workspace.RunsLogPath));
    }

    [Fact]
    public void Execute_Write_MutatesPriorityAndAppendsReasonedRunEvent()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var changedAt = new DateTimeOffset(2026, 7, 19, 3, 0, 0, TimeSpan.Zero);
        QueueReprioritizeCommand.UtcNowFactory = () => changedAt;

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "publish ahead of G530", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("changed").GetBoolean());

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", updatedState.Items.Single().Priority);
        Assert.Equal(changedAt, updatedState.UpdatedAt);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var runEvent = Assert.Single(events);
        Assert.Equal("priority-changed", runEvent.Event);
        Assert.Equal("G537", runEvent.ExecutionUnit);
        Assert.Equal("intent-cli", runEvent.By);
        Assert.Contains("normal", runEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("high", runEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("publish ahead of G530", runEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesOnNonQueuedState()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Active, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "x", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("not queued", error, StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.RunsLogPath));

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
    }

    [Fact]
    public void Execute_RefusesOnAlreadyPublishedUnit()
    {
        using var workspace = new ReprioritizeWorkspace();
        var linkedIssue = new LinkedIssue { Repo = "J-Tech-Japan/intent-system", Number = 1176, Url = "https://github.com/J-Tech-Japan/intent-system/issues/1176" };
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "x", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("already has a linked GitHub issue", error, StringComparison.Ordinal);

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
    }

    [Fact]
    public void Execute_RefusesOnUnknownExecutionUnit()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G999", "--priority", "high", "--reason", "x", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("no item with execution_unit", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesWithoutReason()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--reason", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RefusesUnsupportedPriorityValue()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "urgent", "--reason", "x", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --priority value", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_SamePriorityRequested_IsIdempotentNoOp()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "high", linkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "no-op", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("changed").GetBoolean());
        Assert.False(File.Exists(workspace.RunsLogPath));
    }

    // ─── G537 review repair: fail-closed, repairable write strategy ────────

    [Fact]
    public void Execute_RunsEventAppendFails_QueueStateNeverTouched_FailsLoud()
    {
        // The audit event is written FIRST; if that fails, queue-state.json
        // must never be mutated at all — no silent, unaudited change.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var queueStateBefore = File.ReadAllText(workspace.QueueStatePath);
        QueueReprioritizeCommand.AppendPriorityChangedEventOverride = (_, _) =>
            throw new IOException("simulated disk failure appending runs.jsonl");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "publish ahead", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("queue-state.json was NOT touched", error, StringComparison.Ordinal);
        Assert.Contains("no durable change was made", error, StringComparison.Ordinal);

        // Byte-for-byte: nothing was mutated.
        Assert.Equal(queueStateBefore, File.ReadAllText(workspace.QueueStatePath));
        Assert.False(File.Exists(workspace.RunsLogPath));
    }

    [Fact]
    public void Execute_QueueStateWriteFailsAfterEventAppended_FailsLoudNamingTheHalfTransition()
    {
        // The event append succeeds (durable audit trail exists — this is
        // NOT a silent unaudited mutation), but the queue-state write then
        // fails. Must fail loud and explain exactly what state the
        // operator is in.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        QueueReprioritizeCommand.WriteQueueStateOverride = (_, _) =>
            throw new IOException("simulated disk failure writing queue-state.json");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "publish ahead", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("priority-changed runs event was recorded", error, StringComparison.Ordinal);
        Assert.Contains("still shows the OLD priority", error, StringComparison.Ordinal);

        // The audit event WAS durably recorded, proving this is not silent.
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var runEvent = Assert.Single(events);
        Assert.Equal("priority-changed", runEvent.Event);

        // queue-state genuinely was not updated (the write failed).
        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
    }

    [Fact]
    public void Execute_RetryAfterQueueStateWriteFailure_ConvergesWithoutDuplicateEvent()
    {
        // Idempotent retry: the SAME command, re-run after the queue-state
        // write failure above, must detect the already-recorded event
        // (skip re-appending — no duplicate), retry ONLY the queue-state
        // write, and converge to a fully consistent, singly-audited state.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        QueueReprioritizeCommand.WriteQueueStateOverride = (_, _) =>
            throw new IOException("simulated disk failure writing queue-state.json");

        using (var firstAttempt = new StringWriter())
        {
            var firstExitCode = QueueReprioritizeCommand.Execute(
                workspace.Context,
                ["G537", "--priority", "high", "--reason", "publish ahead", "--write", "--format", "json"],
                firstAttempt);
            Assert.Equal(1, firstExitCode);
        }

        // Fault resolved — retry with the real write path restored.
        QueueReprioritizeCommand.WriteQueueStateOverride = null;

        using var retryWriter = new StringWriter();
        var retryExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "publish ahead", "--write", "--format", "json"],
            retryWriter);

        Assert.Equal(0, retryExitCode);
        using var document = JsonDocument.Parse(retryWriter.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", stateAfter.Items.Single().Priority);

        // Exactly ONE event — the retry did not append a duplicate.
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Single(events);
    }

    // ─── G537 round-3 review repair: dedup bound to a content fingerprint,
    // never wall-clock ordering ─────────────────────────────────────────────

    [Fact]
    public void Execute_RoundTripStaleCollision_ThirdMutationGetsItsOwnEvent()
    {
        // The reproduction from round 2's review: normal->high reason R
        // completes; high->normal reason S completes; normal->high reason
        // R is requested AGAIN. The third request's UNTAGGED reason text
        // is byte-identical to the first event's — but the first event's
        // content fingerprint is now stale (the second transition's write
        // changed queue-state.json), so the third mutation must get its
        // OWN new event.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        QueueReprioritizeCommand.UtcNowFactory = () => new DateTimeOffset(2026, 7, 19, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write"], new StringWriter()));

        QueueReprioritizeCommand.UtcNowFactory = () => new DateTimeOffset(2026, 7, 19, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "normal", "--reason", "S", "--write"], new StringWriter()));

        QueueReprioritizeCommand.UtcNowFactory = () => new DateTimeOffset(2026, 7, 19, 3, 0, 0, TimeSpan.Zero);
        using var thirdWriter = new StringWriter();
        var thirdExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            thirdWriter);

        Assert.Equal(0, thirdExitCode);
        using var document = JsonDocument.Parse(thirdWriter.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", stateAfter.Items.Single().Priority);

        // THREE distinct mutations -> THREE events, one per mutation.
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("priority-changed", e.Event));
        // Every event's own fingerprint tag is distinct (each was computed
        // against a genuinely different pre-mutation queue-state.json).
        Assert.Equal(3, events.Select(e => e.Reason).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Execute_RoundTripStaleCollision_SameFixedClockAcrossAllThreeOperations_StillGetsThreeEvents()
    {
        // The EXACT round-3 review reproduction: UtcNowFactory returns ONE
        // identical timestamp for all three successful operations, so
        // `Ts` can never discriminate between them at all. The content
        // fingerprint must still correctly produce three distinct events,
        // since it never looks at time.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var fixedNow = new DateTimeOffset(2026, 7, 19, 4, 0, 0, TimeSpan.Zero);
        QueueReprioritizeCommand.UtcNowFactory = () => fixedNow;

        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write"], new StringWriter()));
        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "normal", "--reason", "S", "--write"], new StringWriter()));

        using var thirdWriter = new StringWriter();
        var thirdExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            thirdWriter);

        Assert.Equal(0, thirdExitCode);
        using var document = JsonDocument.Parse(thirdWriter.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(fixedNow, e.Ts)); // confirms Ts truly never discriminated here
        Assert.Equal(3, events.Select(e => e.Reason).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Execute_ClockRollback_StillGetsItsOwnEventAndNeverDedupesAgainstAnEarlierTimestampedEvent()
    {
        // Round-3 review: a clock rollback between operations (op2's
        // UtcNowFactory value is EARLIER than op1's) must not break
        // anything, since the fingerprint never consults time at all.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));

        QueueReprioritizeCommand.UtcNowFactory = () => new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write"], new StringWriter()));

        // Rolled BACK relative to the first operation.
        QueueReprioritizeCommand.UtcNowFactory = () => new DateTimeOffset(2026, 7, 19, 9, 0, 0, TimeSpan.Zero);
        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "normal", "--reason", "S", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Execute_StaleHistoricalEventWithDifferentReasonText_NeverSuppressesTheNewEvent()
    {
        // "Wrong reason": a stale event sharing the SAME (correct, matching)
        // generation fingerprint but a DIFFERENT reason string must still
        // never dedupe — isolates that the reason text is independently
        // required, not merely a side effect of a mismatched fingerprint.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var matchingGenerationId = QueueReprioritizeCommand.ComputeGenerationId(File.ReadAllText(workspace.QueueStatePath));
        SeedHistoricalEvent(workspace, "G537", $"priority changed from 'normal' to 'high': OLD_REASON (generation {matchingGenerationId})");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "NEW_REASON", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Reason!.Contains("NEW_REASON", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_StaleHistoricalEventWithOppositeDirection_NeverSuppressesTheNewEvent()
    {
        // "Wrong direction": same matching fingerprint, but the event
        // records the OPPOSITE transition — must never dedupe.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var matchingGenerationId = QueueReprioritizeCommand.ComputeGenerationId(File.ReadAllText(workspace.QueueStatePath));
        SeedHistoricalEvent(workspace, "G537", $"priority changed from 'high' to 'normal': R (generation {matchingGenerationId})");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Reason!.StartsWith("priority changed from 'normal' to 'high': R", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_StaleHistoricalEventForDifferentExecutionUnit_NeverSuppressesTheNewEvent()
    {
        // "Wrong unit": same matching fingerprint AND reason text, but for
        // a DIFFERENT execution unit — must never dedupe this unit's request.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var matchingGenerationId = QueueReprioritizeCommand.ComputeGenerationId(File.ReadAllText(workspace.QueueStatePath));
        SeedHistoricalEvent(workspace, "G999", $"priority changed from 'normal' to 'high': R (generation {matchingGenerationId})");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.ExecutionUnit == "G537");
    }

    [Fact]
    public void Execute_HistoricalEventMissingGenerationTagEntirely_NeverSuppressesTheNewEvent()
    {
        // "Malformed/missing generation": a historical event predating
        // this fix (or hand-edited) with no fingerprint tag at all, but
        // otherwise-matching unit/reason — a tagged expected reason can
        // never exact-match an untagged one, so this must never dedupe.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        SeedHistoricalEvent(workspace, "G537", "priority changed from 'normal' to 'high': R");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Reason!.Contains("(generation ", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GenuinePendingEventWithMatchingFingerprint_IsDedupedAsRetry()
    {
        // Direct fixture for the genuine-retry path (complementary to the
        // WriteQueueStateOverride fault-injection test above): an event
        // already recorded with the CORRECT fingerprint for the CURRENT,
        // still-unmutated queue-state.json, exactly matching unit/event/
        // reason, must be recognized as the pending audit for an
        // in-progress attempt and never duplicated.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", linkedIssue: null)));
        var matchingGenerationId = QueueReprioritizeCommand.ComputeGenerationId(File.ReadAllText(workspace.QueueStatePath));
        SeedHistoricalEvent(workspace, "G537", $"priority changed from 'normal' to 'high': R (generation {matchingGenerationId})");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", stateAfter.Items.Single().Priority);

        // Still exactly ONE event — recognized as the pending retry, not duplicated.
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Single(events);
    }

    private static void SeedHistoricalEvent(ReprioritizeWorkspace workspace, string executionUnit, string reason) =>
        SeedHistoricalEvent(workspace, executionUnit, reason, new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero));

    private static void SeedHistoricalEvent(ReprioritizeWorkspace workspace, string executionUnit, string reason, DateTimeOffset ts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunsLogPath)!);
        var historicalEvent = new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = "priority-changed",
            By = "intent-cli",
            Reason = reason,
        };
        File.AppendAllText(workspace.RunsLogPath, RunLogSerializer.SerializeLine(historicalEvent) + Environment.NewLine);
    }

    private static string BuildQueueState((string ExecutionUnit, QueueItemState State, string Priority, LinkedIssue? LinkedIssue) item)
    {
        var state = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
            Items = new[]
            {
                new QueueItem
                {
                    ExecutionUnit = item.ExecutionUnit,
                    Title = $"{item.ExecutionUnit} title",
                    State = item.State,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{item.ExecutionUnit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{item.ExecutionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{item.ExecutionUnit}/review-context.md"
                    },
                    LinkedIssue = item.LinkedIssue,
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = item.Priority
                }
            }
        };
        return QueueStateSerializer.Serialize(state);
    }

    private sealed class ReprioritizeWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("queue-reprioritize-tests-")
            .FullName;

        public ReprioritizeWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public string QueueStatePath => Context.GetQueueStatePath();

        public string RunsLogPath => Context.GetRunLogPath();

        public void WriteQueueState(string json) => File.WriteAllText(QueueStatePath, json);

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
