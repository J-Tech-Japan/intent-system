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
        QueueReprioritizeCommand.OnLockAcquiredForTest = null;
    }

    public void Dispose()
    {
        QueueReprioritizeCommand.UtcNowFactory = null;
        QueueReprioritizeCommand.AppendPriorityChangedEventOverride = null;
        QueueReprioritizeCommand.WriteQueueStateOverride = null;
        QueueReprioritizeCommand.OnLockAcquiredForTest = null;
    }

    [Fact]
    public void Execute_DryRunDefault_ReportsWouldChangeWithoutMutating()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Active, "normal", LinkedIssue: null)));

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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "urgent", "--reason", "x", "--write"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --priority value", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Write_MigratesLegacyMediumPriorityToDocumentedHigh_G543()
    {
        // G543: the canonical migration recipe for a legacy/out-of-enum
        // priority value (e.g. the field-observed "medium", 59 items on
        // the host) is this exact, already-working command -- only the
        // REQUESTED value is validated against the documented enum; the
        // OLD value is read/reported/compared with no validation at all,
        // so an item currently at "medium" can move to any documented
        // value without hand-editing queue-state.json. No new command was
        // needed for this; this test locks the existing behavior in as
        // the documented recipe.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G543", QueueItemState.Queued, "medium", LinkedIssue: null)));
        var changedAt = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        QueueReprioritizeCommand.UtcNowFactory = () => changedAt;

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G543", "--priority", "high", "--reason", "migrate legacy medium value off the enum", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("write", root.GetProperty("mode").GetString());
        Assert.Equal("medium", root.GetProperty("old_priority").GetString());
        Assert.Equal("high", root.GetProperty("requested_priority").GetString());
        Assert.True(root.GetProperty("changed").GetBoolean());

        var updatedState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", updatedState.Items.Single().Priority);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var runEvent = Assert.Single(events);
        Assert.Equal("priority-changed", runEvent.Event);
        Assert.Contains("medium", runEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("high", runEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_SamePriorityRequested_IsIdempotentNoOp()
    {
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "high", LinkedIssue: null)));

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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
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
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
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

    // ─── G537 round-4 review repair: dedup bound to a durable injective
    // priority_revision counter, never content fingerprinting or wall-clock
    // ordering ───────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_RevisitedByteIdenticalPrestate_RepeatedTransitionStillGetsItsOwnEvent()
    {
        // The round-4 review's exact counter-example to round 3's content
        // fingerprint: seed queue-state.json with UpdatedAt EQUAL to the
        // fixed clock used for every operation below. After normal->high
        // (R) then high->normal (S) — both under that SAME fixed clock —
        // the file's priority/state/updated_at genuinely revisit the
        // ORIGINAL prestate bytes (asserted explicitly below, ignoring
        // only the priority_revision counter itself). A content
        // fingerprint of the whole file would be fooled into treating the
        // stale first event as pending for the third (repeated) request;
        // the durable `priority_revision` counter cannot be, because it
        // never repeats a value once consumed.
        using var workspace = new ReprioritizeWorkspace();
        var fixedNow = new DateTimeOffset(2026, 7, 19, 4, 0, 0, TimeSpan.Zero);
        var originalRaw = BuildQueueStateWithUpdatedAt(("G537", QueueItemState.Queued, "normal", LinkedIssue: null), fixedNow);
        workspace.WriteQueueState(originalRaw);
        QueueReprioritizeCommand.UtcNowFactory = () => fixedNow;

        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write"], new StringWriter()));
        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "normal", "--reason", "S", "--write"], new StringWriter()));

        // Prove the revisit is real: every field except priority_revision
        // is now byte-for-byte identical to the original prestate.
        var afterTwoOpsRaw = File.ReadAllText(workspace.QueueStatePath);
        Assert.Equal(StripPriorityRevision(originalRaw), StripPriorityRevision(afterTwoOpsRaw));
        Assert.NotEqual(originalRaw, afterTwoOpsRaw); // only priority_revision differs (0 -> 2)

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
        Assert.All(events, e => Assert.Equal(fixedNow, e.Ts)); // clock never discriminated
        Assert.Contains(events, e => e.Reason!.EndsWith("(revision 0->1)", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Reason!.EndsWith("(revision 1->2)", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Reason!.EndsWith("(revision 2->3)", StringComparison.Ordinal));

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", stateAfter.Items.Single().Priority);
        Assert.Equal(3, stateAfter.Items.Single().PriorityRevision);
    }

    [Fact]
    public void Execute_ClockRollback_StillGetsItsOwnEventAndNeverDedupesAgainstAnEarlierTimestampedEvent()
    {
        // A clock rollback between operations (op2's UtcNowFactory value
        // is EARLIER than op1's) must not break anything, since the
        // revision counter never consults time at all.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

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
        Assert.Equal(2, stateAfter.Items.Single().PriorityRevision);

        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Equal(2, events.Count);
    }

    // ─── G537 round-5 review repair: revision-pair recovery classification,
    // checked/validated PriorityRevision, and concurrent-writer protection ──

    [Fact]
    public void Execute_ConflictingClaimSameRevisionPairDifferentReason_FailsClosedQueueUntouched()
    {
        // Round-5 review: an existing event claiming the SAME revision
        // pair but a DIFFERENT reason is a genuine data contradiction —
        // the revision pair IS the operation identity, so this must fail
        // closed (never silently append a second, different claim on the
        // same pair).
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        SeedHistoricalEvent(workspace, "G537", "priority changed from 'normal' to 'high': OLD_REASON (revision 0->1)");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "NEW_REASON", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("conflicting", error, StringComparison.Ordinal);
        Assert.False(document.RootElement.GetProperty("changed").GetBoolean());

        // Queue untouched, no second event appended.
        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
        Assert.Equal(0, stateAfter.Items.Single().PriorityRevision);
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)));
    }

    [Fact]
    public void Execute_ConflictingClaimSameRevisionPairOppositeDirection_FailsClosedQueueUntouched()
    {
        // Round-5 review: same revision pair, but the existing event
        // records the OPPOSITE transition direction — also a conflict,
        // not a "wrong reason, append anyway" case.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        SeedHistoricalEvent(workspace, "G537", "priority changed from 'high' to 'normal': R (revision 0->1)");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("conflicting", error, StringComparison.Ordinal);

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)));
    }

    [Theory]
    [InlineData(0, 1)] // conflicting event listed FIRST, matching-shape event would be second (never appended)
    [InlineData(1, 0)] // reversed order: conflicting event listed SECOND
    public void Execute_ConflictingClaim_OrderIndependent_AlwaysFailsClosed(int conflictingIndex, int unusedIndex)
    {
        // "Reversed/order-independent": classification must not depend on
        // which position in runs.jsonl the conflicting event occupies.
        // This fixture seeds exactly one (conflicting) historical event —
        // the SAME conflict must be detected regardless of the requested
        // insertion order semantics exercised via conflictingIndex.
        _ = unusedIndex;
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        if (conflictingIndex == 0)
        {
            SeedHistoricalEvent(workspace, "G537", "priority changed from 'normal' to 'high': DIFFERENT (revision 0->1)");
        }
        else
        {
            // Seed an unrelated (non-matching-unit) event first, THEN the
            // conflicting one, to prove order of appearance in the file
            // never matters to the classification.
            SeedHistoricalEvent(workspace, "G000", "priority changed from 'normal' to 'high': unrelated (revision 0->1)");
            SeedHistoricalEvent(workspace, "G537", "priority changed from 'normal' to 'high': DIFFERENT (revision 0->1)");
        }

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("conflicting", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal("normal", QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath)).Items.Single().Priority);
    }

    [Fact]
    public void Execute_DuplicateIdenticalEventsForSameRevisionPair_FailsClosedQueueUntouched()
    {
        // Round-5 review: TWO existing events, both claiming the SAME
        // revision pair with the IDENTICAL reason, is itself an integrity
        // problem (how did that happen?) — must fail closed, not be
        // silently treated as "one pending retry."
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        SeedHistoricalEvent(workspace, "G537", "priority changed from 'normal' to 'high': R (revision 0->1)");
        SeedHistoricalEvent(workspace, "G537", "priority changed from 'normal' to 'high': R (revision 0->1)");

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("duplicate-identical", error, StringComparison.Ordinal);

        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfter.Items.Single().Priority);
        Assert.Equal(2, RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)).Count); // no third event appended
    }

    [Fact]
    public void Execute_NegativePriorityRevision_FailsClosedInDryRunAndWrite()
    {
        // Round-5 review: a negative priority_revision is corrupted
        // durable state — must refuse before any preview/mutation, in
        // BOTH dry-run and write mode.
        using var workspace = new ReprioritizeWorkspace();
        var baseState = QueueStateSerializer.Deserialize(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        workspace.WriteQueueState(QueueStateSerializer.Serialize(
            baseState with { Items = new[] { baseState.Items.Single() with { PriorityRevision = -1 } } }));

        using var dryRunWriter = new StringWriter();
        var dryRunExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--format", "json"], dryRunWriter);
        Assert.Equal(1, dryRunExitCode);
        using var dryRunDoc = JsonDocument.Parse(dryRunWriter.ToString());
        Assert.Contains("negative priority_revision", dryRunDoc.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);

        using var writeWriter = new StringWriter();
        var writeExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"], writeWriter);
        Assert.Equal(1, writeExitCode);
        using var writeDoc = JsonDocument.Parse(writeWriter.ToString());
        Assert.Contains("negative priority_revision", writeDoc.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);

        Assert.False(File.Exists(workspace.RunsLogPath));
        Assert.Equal(-1, QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath)).Items.Single().PriorityRevision);
    }

    [Fact]
    public void Execute_ExhaustedMaxRevision_FailsClosedInDryRunAndWrite_CheckedArithmeticNeverWraps()
    {
        // Round-5 review: `int.MaxValue + 1` unchecked wraps to
        // int.MinValue, directly violating the monotonic/injective
        // invariant. Must fail closed instead, in BOTH dry-run and write.
        using var workspace = new ReprioritizeWorkspace();
        var baseState = QueueStateSerializer.Deserialize(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        workspace.WriteQueueState(QueueStateSerializer.Serialize(
            baseState with { Items = new[] { baseState.Items.Single() with { PriorityRevision = int.MaxValue } } }));

        using var dryRunWriter = new StringWriter();
        var dryRunExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--format", "json"], dryRunWriter);
        Assert.Equal(1, dryRunExitCode);
        using var dryRunDoc = JsonDocument.Parse(dryRunWriter.ToString());
        Assert.Contains("exhausted", dryRunDoc.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);

        using var writeWriter = new StringWriter();
        var writeExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"], writeWriter);
        Assert.Equal(1, writeExitCode);
        using var writeDoc = JsonDocument.Parse(writeWriter.ToString());
        Assert.Contains("exhausted", writeDoc.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);

        Assert.False(File.Exists(workspace.RunsLogPath));
        Assert.Equal(int.MaxValue, QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath)).Items.Single().PriorityRevision);
    }

    [Fact]
    public void Execute_MalformedPriorityRevisionType_FailsClosedAtQueueStateParse()
    {
        // "Malformed": priority_revision as a JSON string (wrong type)
        // fails at the existing queue-state.json parse step — pinned
        // explicitly per review request.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-08T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G537",
                  "title": "malformed priority_revision type",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "Claude",
                  "review_role": "Codex",
                  "priority": "normal",
                  "priority_revision": "not-a-number"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("could not be parsed", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.RunsLogPath));
    }

    [Fact]
    public void Execute_ConcurrentQueueStateChangeBetweenEventAppendAndFinalWrite_FailsClosedWithoutOverwriting()
    {
        // Round-5 review: protect the read -> event -> queue-write
        // boundary. Simulate a concurrent writer mutating queue-state.json
        // (bumping priority_revision unrelated to this attempt) DURING
        // the runs-event append step, via the AppendPriorityChangedEventOverride
        // seam. The subsequent final write must detect the mismatch and
        // refuse, rather than blindly overwriting the concurrent change.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

        QueueReprioritizeCommand.AppendPriorityChangedEventOverride = (path, runEvent) =>
        {
            File.AppendAllText(path, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);

            // Simulate a concurrent, unrelated writer bumping this same
            // item's priority_revision after our read but before our
            // write.
            var concurrentState = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
            var concurrentItem = concurrentState.Items.Single();
            var mutated = concurrentState with
            {
                Items = new[] { concurrentItem with { Priority = "low", PriorityRevision = concurrentItem.PriorityRevision + 100 } },
            };
            File.WriteAllText(workspace.QueueStatePath, QueueStateSerializer.Serialize(mutated));
        };

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.Contains("changed concurrently", error, StringComparison.Ordinal);

        // The concurrent writer's own change survives untouched — our
        // stale mutation must never have overwritten it.
        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("low", stateAfter.Items.Single().Priority);
        Assert.Equal(100, stateAfter.Items.Single().PriorityRevision);

        // The audit event for OUR attempt was still durably recorded.
        Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath)));
    }

    // ─── G537 round-6 review repair: authoritative interprocess lock ───────

    [Fact]
    public void Execute_ConcurrentInvocationWhileLockHeld_LoserFailsClosed_WinnerAppliesExactlyOneMutation()
    {
        // Round-6 review: the round-5 "re-read + compare" was still a
        // TOCTOU check, not authoritative mutual exclusion — two
        // concurrent invocations could both pass the compare before
        // either commits. This deterministically proves the fix: while
        // the OUTER invocation holds the lock (via OnLockAcquiredForTest,
        // fired synchronously right after lock acquisition), a SECOND,
        // fully independent Execute call for the SAME repo/execution unit
        // is attempted. It must fail to acquire the same OS-level lock
        // and fail closed immediately — never racing, never silently
        // partially applying. After the outer call completes, exactly
        // ONE mutation and ONE event must exist.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

        int? innerExitCode = null;
        string? innerOutput = null;
        QueueReprioritizeCommand.OnLockAcquiredForTest = () =>
        {
            // Prevent recursion: the inner call must NOT itself try to
            // spawn a third concurrent attempt.
            QueueReprioritizeCommand.OnLockAcquiredForTest = null;

            using var innerWriter = new StringWriter();
            innerExitCode = QueueReprioritizeCommand.Execute(
                workspace.Context,
                ["G537", "--priority", "high", "--reason", "R (inner, concurrent)", "--write", "--format", "json"],
                innerWriter);
            innerOutput = innerWriter.ToString();
        };

        using var outerWriter = new StringWriter();
        var outerExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R (outer, winner)", "--write", "--format", "json"],
            outerWriter);

        // The outer (lock-holding) invocation wins and succeeds.
        Assert.Equal(0, outerExitCode);
        using var outerDoc = JsonDocument.Parse(outerWriter.ToString());
        Assert.True(outerDoc.RootElement.GetProperty("changed").GetBoolean());

        // The inner (concurrent) invocation could not acquire the same
        // lock and failed closed immediately — it never raced, never
        // partially wrote anything.
        Assert.NotNull(innerExitCode);
        Assert.Equal(1, innerExitCode);
        Assert.NotNull(innerOutput);
        using var innerDoc = JsonDocument.Parse(innerOutput!);
        var innerError = innerDoc.RootElement.GetProperty("error").GetString();
        Assert.Contains("holds the exclusive lock", innerError, StringComparison.Ordinal);
        Assert.False(innerDoc.RootElement.GetProperty("changed").GetBoolean());

        // Exactly ONE mutation, ONE event — the outer winner's, never a
        // duplicate or a conflicting write from the inner loser.
        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", stateAfter.Items.Single().Priority);
        Assert.Equal(1, stateAfter.Items.Single().PriorityRevision);
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var runEvent = Assert.Single(events);
        Assert.Contains("outer, winner", runEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DryRunNeverAcquiresLock_ConcurrentWriteStillProceedsUnaffected()
    {
        // Dry-run never mutates, so it must never contend for the lock —
        // a concurrent --write invocation must be able to proceed
        // normally even while a dry-run preview is (conceptually)
        // in-flight. Since dry-run doesn't hold the lock at all, this is
        // really just confirming dry-run doesn't regress: it must still
        // succeed even with the lock file already present/created by a
        // prior write.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

        // A prior --write created (and released) the lock file; it stays
        // on disk afterward (never deleted — only ever held/released).
        Assert.Equal(0, QueueReprioritizeCommand.Execute(
            workspace.Context, ["G537", "--priority", "high", "--reason", "R", "--write"], new StringWriter()));

        using var dryRunWriter = new StringWriter();
        var dryRunExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "normal", "--reason", "S", "--format", "json"],
            dryRunWriter);

        Assert.Equal(0, dryRunExitCode);
        using var document = JsonDocument.Parse(dryRunWriter.ToString());
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());
    }

    // ─── G537 round-7 review repair: exception-safe lock release ──────────

    [Fact]
    public void Execute_CallbackThrowsAfterLockAcquired_LockStillReleasedDeterministically()
    {
        // Round-7 review: OnLockAcquiredForTest previously fired BEFORE
        // entering the try/finally that disposes lockStream. A throwing
        // callback would therefore leak the acquired OS-level lock handle
        // undisposed, leaving a subsequent independent Execute locked out
        // until GC/finalization. This proves the fix: the callback now
        // runs inside the try/finally, so even when it throws, the lock
        // is released deterministically and a second call can proceed
        // immediately — with the queue/runs state left byte-unchanged by
        // the failed first call.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));

        QueueReprioritizeCommand.OnLockAcquiredForTest = () =>
        {
            QueueReprioritizeCommand.OnLockAcquiredForTest = null;
            throw new InvalidOperationException("simulated callback failure");
        };

        using var firstWriter = new StringWriter();
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            QueueReprioritizeCommand.Execute(
                workspace.Context,
                ["G537", "--priority", "high", "--reason", "R (first, throws)", "--write", "--format", "json"],
                firstWriter));
        Assert.Equal("simulated callback failure", thrown.Message);

        // The failed first call must never have reached the write path.
        var stateAfterFirst = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("normal", stateAfterFirst.Items.Single().Priority);
        Assert.Equal(0, stateAfterFirst.Items.Single().PriorityRevision);
        Assert.False(File.Exists(workspace.RunsLogPath));

        // A second, fully independent invocation must acquire the same
        // lock immediately (proving it was released, not leaked) and
        // succeed normally.
        using var secondWriter = new StringWriter();
        var secondExitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R (second, after throw)", "--write", "--format", "json"],
            secondWriter);

        Assert.Equal(0, secondExitCode);
        using var secondDoc = JsonDocument.Parse(secondWriter.ToString());
        Assert.True(secondDoc.RootElement.GetProperty("changed").GetBoolean());

        var stateAfterSecond = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal("high", stateAfterSecond.Items.Single().Priority);
        Assert.Equal(1, stateAfterSecond.Items.Single().PriorityRevision);
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        var runEvent = Assert.Single(events);
        Assert.Contains("second, after throw", runEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_StaleHistoricalEventForDifferentExecutionUnit_NeverSuppressesTheNewEvent()
    {
        // "Wrong unit": same matching revision pair AND reason text, but
        // for a DIFFERENT execution unit — must never dedupe this unit's
        // request.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        SeedHistoricalEvent(workspace, "G999", "priority changed from 'normal' to 'high': R (revision 0->1)");

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
    public void Execute_HistoricalEventMissingRevisionTagEntirely_NeverSuppressesTheNewEvent()
    {
        // "Malformed/missing generation": a historical event predating
        // this fix (or hand-edited) with no revision tag at all, but
        // otherwise-matching unit/reason — a tagged expected reason can
        // never exact-match an untagged one, so this must never dedupe.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
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
        Assert.Contains(events, e => e.Reason!.Contains("(revision ", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GenuinePendingEventWithMatchingRevisionPair_IsDedupedAsRetry()
    {
        // Direct fixture for the genuine-retry path (complementary to the
        // WriteQueueStateOverride fault-injection test above): an event
        // already recorded with the CORRECT from/to revision pair for the
        // CURRENT, still-unmutated queue-state.json, exactly matching
        // unit/event/reason, must be recognized as the pending audit for
        // an in-progress attempt and never duplicated.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(BuildQueueState(("G537", QueueItemState.Queued, "normal", LinkedIssue: null)));
        SeedHistoricalEvent(workspace, "G537", "priority changed from 'normal' to 'high': R (revision 0->1)");

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
        Assert.Equal(1, stateAfter.Items.Single().PriorityRevision);

        // Still exactly ONE event — recognized as the pending retry, not duplicated.
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Single(events);
    }

    [Fact]
    public void Execute_LegacyItemMissingPriorityRevisionField_MigratesAsZero()
    {
        // Legacy migration semantics: a hand-authored/pre-G537
        // queue-state.json with no `priority_revision` field at all
        // deserializes it as 0 — the first reprioritize on such an item
        // records revision 0->1, same as a brand-new item.
        using var workspace = new ReprioritizeWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-08T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G537",
                  "title": "legacy item, no priority_revision field",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "Claude",
                  "review_role": "Codex",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = QueueReprioritizeCommand.Execute(
            workspace.Context,
            ["G537", "--priority", "high", "--reason", "R", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var stateAfter = QueueStateSerializer.Deserialize(File.ReadAllText(workspace.QueueStatePath));
        Assert.Equal(1, stateAfter.Items.Single().PriorityRevision);
        var events = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunsLogPath));
        Assert.Contains(events, e => e.Reason!.EndsWith("(revision 0->1)", StringComparison.Ordinal));
    }

    private static string StripPriorityRevision(string queueStateJson) =>
        System.Text.RegularExpressions.Regex.Replace(queueStateJson, @",?\s*""priority_revision""\s*:\s*\d+", string.Empty);

    private static string BuildQueueStateWithUpdatedAt(
        (string ExecutionUnit, QueueItemState State, string Priority, LinkedIssue? LinkedIssue) item, DateTimeOffset updatedAt)
    {
        var raw = BuildQueueState(item);
        var state = QueueStateSerializer.Deserialize(raw) with { UpdatedAt = updatedAt };
        return QueueStateSerializer.Serialize(state);
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
