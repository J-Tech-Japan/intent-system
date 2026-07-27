using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G537: <c>intent-cli queue reprioritize &lt;execution-unit&gt; --priority
/// &lt;high|normal|low&gt; --reason &lt;text&gt; [--write] [--format
/// markdown|json]</c> — the bounded canonical transition that lets an
/// operator/orchestrator express a publish-order ruling (e.g. "publish
/// G532 ahead of G530") without hand-editing <c>queue-state.json</c>.
///
/// Only ever mutates a QUEUED, NOT-YET-PUBLISHED item's <c>priority</c>
/// field. Refuses (no mutation) on any other state or once a GitHub issue
/// is already linked — a priority change past that point cannot influence
/// candidate selection and would be misleading to allow. Dry-run by
/// default; <c>--write</c> is required to actually mutate
/// <c>queue-state.json</c> and append the <c>priority-changed</c> runs
/// event that records the old/new priority and the operator's reason.
///
/// This command has no opinion on ordering — <see cref="IntentNextSliceCommand"/>
/// is the sole consumer of <see cref="QueueItem.Priority"/> for candidate
/// ordering (priority-class-first, authoring-order tiebreak, gates keep
/// absolute precedence).
/// </summary>
internal static class QueueReprioritizeCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string ModeDryRun = "dry-run";
    private const string ModeWrite = "write";

    public const string PriorityHigh = QueuePriorityClassification.High;
    public const string PriorityNormal = QueuePriorityClassification.Normal;
    public const string PriorityLow = QueuePriorityClassification.Low;

    public const string PriorityChangedEventName = "priority-changed";
    private const string ReprioritizeActor = "intent-cli";

    /// <summary>G537 test seam: overrides the recorded event timestamp.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    /// <summary>
    /// G537 review repair test seam: replaces the real
    /// <c>runs.jsonl</c> append. Throwing simulates an append failure so
    /// tests can prove the fail-closed/repairable write strategy without
    /// depending on real filesystem-permission tricks.
    /// </summary>
    internal static Action<string, RunEvent>? AppendPriorityChangedEventOverride { get; set; }

    /// <summary>
    /// G537 review repair test seam: replaces the real
    /// <c>queue-state.json</c> write. Throwing simulates a write failure
    /// AFTER the runs event has already been durably recorded.
    /// </summary>
    internal static Action<string, QueueState>? WriteQueueStateOverride { get; set; }

    /// <summary>
    /// G537 round-6 review repair test seam: invoked synchronously,
    /// exactly once, immediately after this invocation successfully
    /// acquires the interprocess lock — while it is still held. Tests use
    /// this to deterministically run a SECOND concurrent
    /// <c>Execute</c> call (which must fail to acquire the same lock and
    /// fail closed) without depending on genuine OS-thread-scheduling
    /// nondeterminism.
    /// </summary>
    internal static Action? OnLockAcquiredForTest { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var executionUnit, out var requestedPriority, out var reason, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var queueStatePath = context.GetQueueStatePath();

        // G537 round-6 review repair: a fresh re-read + compare immediately
        // before the final write (round 5) is still a TOCTOU check, not
        // authoritative mutual exclusion — two concurrent invocations can
        // both pass that compare before either commits. Write mode now
        // acquires an OS-level interprocess exclusive lock (an
        // OpenOrCreate + FileShare.None handle on a stable sibling path)
        // BEFORE the authoritative queue-state/runs.jsonl read, and holds
        // it across revision validation, event-claim classification and
        // append, the fresh re-read, and the final commit — releasing it
        // only once this invocation is completely done, success or
        // failure. A concurrent second invocation that cannot acquire the
        // lock fails closed immediately (no retry/wait) rather than
        // racing; dry-run never mutates and never takes the lock.
        FileStream? lockStream = null;
        if (write)
        {
            var lockPath = GetLockPath(queueStatePath);
            if (!TryAcquireLock(lockPath, out lockStream))
            {
                EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: null, requestedPriority!, reason!, changed: false,
                    error: $"another 'queue reprioritize' invocation currently holds the exclusive lock at {lockPath}; refusing to proceed concurrently (avoids a lost-update race). Retry once it completes."));
                return 1;
            }
        }

        // G537 round-7 review repair: every post-acquisition operation —
        // including the test-only callback — must run inside the
        // try/finally that disposes lockStream. Invoking the callback
        // before entering try/finally left a window where a throwing
        // callback would leak the OS-level lock handle undisposed.
        try
        {
            if (write)
            {
                OnLockAcquiredForTest?.Invoke();
            }

            return ExecuteUnderLock(context, queueStatePath, executionUnit!, requestedPriority!, reason!, write, format, writer);
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    private static int ExecuteUnderLock(
        CliContext context,
        string queueStatePath,
        string executionUnit,
        string requestedPriority,
        string reason,
        bool write,
        string format,
        TextWriter writer)
    {
        if (!File.Exists(queueStatePath))
        {
            writer.WriteLine($"No queue state found at {queueStatePath}");
            return 1;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            writer.WriteLine($"queue-state.json could not be parsed: {exception.Message}");
            return 1;
        }

        var matchingIndices = new List<int>();
        for (var index = 0; index < queueState.Items.Count; index++)
        {
            if (string.Equals(queueState.Items[index].ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                matchingIndices.Add(index);
            }
        }

        if (matchingIndices.Count == 0)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: null, requestedPriority!, reason!, changed: false,
                error: $"queue-state.json has no item with execution_unit '{executionUnit}'."));
            return 1;
        }

        if (matchingIndices.Count > 1)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: null, requestedPriority!, reason!, changed: false,
                error: $"queue-state.json has {matchingIndices.Count} items for execution_unit '{executionUnit}'; refusing to reprioritize an ambiguous entry."));
            return 1;
        }

        var index0 = matchingIndices[0];
        var item = queueState.Items[index0];

        if (item.State != QueueItemState.Queued)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"'{executionUnit}' is in state '{FormatState(item.State)}', not queued; refusing to reprioritize a published/in-flight/completed/retired unit."));
            return 1;
        }

        if (item.LinkedIssue is not null)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"'{executionUnit}' already has a linked GitHub issue (#{item.LinkedIssue.Number}); refusing to reprioritize a published unit."));
            return 1;
        }

        if (string.Equals(item.Priority, requestedPriority, StringComparison.Ordinal))
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: null));
            return 0;
        }

        // Round-5 review repair: `PriorityRevision` is the durable,
        // injective operation identity that every downstream step
        // depends on — validate it BEFORE previewing or mutating
        // anything, in both dry-run and write mode. A negative value, or
        // one that would overflow on increment, is corrupted/exhausted
        // durable state, not a value this command can safely reason
        // about.
        if (item.PriorityRevision < 0)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"'{executionUnit}' has a negative priority_revision ({item.PriorityRevision}) in queue-state.json; refusing to reprioritize corrupted durable state. Repair queue-state.json manually before retrying."));
            return 1;
        }

        int toRevision;
        try
        {
            toRevision = checked(item.PriorityRevision + 1);
        }
        catch (OverflowException)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"'{executionUnit}' has priority_revision at int.MaxValue ({item.PriorityRevision}); the revision counter is exhausted and refuses to wrap. Manual intervention (e.g. a durable migration to a wider counter) is required before this unit can be reprioritized again."));
            return 1;
        }
        var fromRevision = item.PriorityRevision;

        if (!write)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: true,
                error: null));
            return 0;
        }

        var changedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var runLogPath = context.GetRunLogPath();

        // G537 review repair: fail-closed, repairable write strategy. The
        // audit event is written FIRST, queue-state SECOND — the reverse
        // of the naive ordering — so a failure at either step never
        // produces a silent, unaudited priority mutation. Round-4:
        // `fromRevision`/`toRevision` is the durable, injective operation
        // identity embedded in the recorded reason — it can never be
        // produced twice by two distinct successful mutations of this
        // item, even if every other field of `queue-state.json` later
        // cycles back to byte-identical content, since the counter is
        // itself part of that durable content and only ever moves
        // forward.
        var expectedReason = $"priority changed from '{item.Priority}' to '{requestedPriority}': {reason}";
        var taggedReason = $"{expectedReason} (revision {fromRevision}->{toRevision})";

        // Round-5 review repair: the revision pair (fromRevision,
        // toRevision) IS the operation identity, so recovery must
        // classify its cardinality/ownership explicitly rather than using
        // `.Any(...)` — a bare existence check silently accepts
        // duplicate-identical events as "one pending attempt" and
        // silently ignores a genuinely conflicting claim on the SAME
        // pair (appending yet another event instead of stopping).
        var classification = ClassifyPriorityChangedRecovery(
            runLogPath, executionUnit!, fromRevision, toRevision, taggedReason, out var recoveryDetail);

        if (classification is PriorityChangedRecoveryClassification.Conflicting or PriorityChangedRecoveryClassification.DuplicateIdentical)
        {
            var kind = classification == PriorityChangedRecoveryClassification.Conflicting ? "conflicting" : "duplicate-identical";
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"runs.jsonl has a {kind} claim on revision {fromRevision}->{toRevision} for '{executionUnit}': {recoveryDetail} "
                    + "queue-state.json was NOT touched. Reconcile runs.jsonl manually before retrying."));
            return 1;
        }

        if (classification == PriorityChangedRecoveryClassification.None)
        {
            var runEvent = new RunEvent
            {
                Ts = changedAt,
                ExecutionUnit = executionUnit!,
                Event = PriorityChangedEventName,
                By = ReprioritizeActor,
                Reason = taggedReason,
            };

            try
            {
                AppendPriorityChangedEvent(runLogPath, runEvent);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                    error: $"failed to append the required priority-changed runs event ({exception.Message}); queue-state.json was NOT touched — no durable change was made. Retry once the failure is resolved."));
                return 1;
            }
        }

        // Round-5 review repair: protect the read -> event -> queue-write
        // boundary from a stale concurrent queue writer. The event above
        // is already durably recorded proving this attempt happened, but
        // the actual mutation must be applied against a FRESH read of
        // queue-state.json — never the stale copy read at the top of this
        // invocation — and must refuse (not silently overwrite) if the
        // target item's `priority_revision` no longer matches
        // `fromRevision`, since that proves something else changed this
        // item since this attempt started. Rebuilding the final state
        // from the fresh read (rather than the stale one) also means an
        // unrelated concurrent change to any OTHER item is preserved
        // rather than clobbered.
        QueueState currentOnDisk;
        try
        {
            currentOnDisk = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"the priority-changed runs event was recorded (revision {fromRevision}->{toRevision}), but queue-state.json could not be re-read to verify no concurrent change occurred before the final write ({exception.Message}); refusing to write blind. Re-run this exact command to retry."));
            return 1;
        }

        var currentItem = currentOnDisk.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (currentItem is null || currentItem.PriorityRevision != fromRevision)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"queue-state.json changed concurrently since this attempt started (expected priority_revision {fromRevision} for '{executionUnit}', "
                    + $"found {(currentItem is null ? "the item missing entirely" : currentItem.PriorityRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))}); "
                    + $"refusing to overwrite a concurrent change. The priority-changed runs event was already recorded (revision {fromRevision}->{toRevision}); "
                    + "re-run this command to retry against the current state."));
            return 1;
        }

        var freshItems = currentOnDisk.Items.ToArray();
        var freshIndex = Array.FindIndex(freshItems, candidate =>
            string.Equals(candidate.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        freshItems[freshIndex] = currentItem with { Priority = requestedPriority!, PriorityRevision = toRevision };
        var finalState = currentOnDisk with { Items = freshItems, UpdatedAt = changedAt };

        try
        {
            WriteQueueState(queueStatePath, currentOnDisk, finalState);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"the priority-changed runs event was recorded (revision {fromRevision}->{toRevision}), but queue-state.json could not be updated ({exception.Message}); "
                    + $"queue-state on disk still shows the OLD priority '{item.Priority}'. Re-run this exact command to retry — it will "
                    + "detect the already-recorded event and only retry the queue-state write, without appending a duplicate event."));
            return 1;
        }

        EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: true, error: null));
        return 0;
    }

    private enum PriorityChangedRecoveryClassification
    {
        /// <summary>No existing event claims this exact revision pair — safe to append a new one.</summary>
        None,

        /// <summary>Exactly one existing event claims this exact revision pair, with the EXACT same reason — the pending audit for an in-progress retry.</summary>
        PendingRetry,

        /// <summary>Two or more IDENTICAL events already claim this exact revision pair — an integrity problem, never silently treated as "fine."</summary>
        DuplicateIdentical,

        /// <summary>An event (or events) claim this exact revision pair with a DIFFERENT reason — a genuine data contradiction.</summary>
        Conflicting,
    }

    private static readonly System.Text.RegularExpressions.Regex RevisionPairPattern = new(
        @"\(revision (?<from>-?\d+)->(?<to>-?\d+)\)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// G537 round-5 review repair: the revision pair (<paramref name="fromRevision"/>,
    /// <paramref name="toRevision"/>) IS the operation identity, so recovery
    /// classifies cardinality/ownership explicitly instead of a bare
    /// existence check: zero matches means safe to append; exactly one
    /// EXACT match (same reason too) means a pending retry; two or more
    /// IDENTICAL matches, or any mismatched-reason match, means the durable
    /// log has an integrity problem that must fail closed rather than be
    /// silently resolved either direction.
    /// </summary>
    private static PriorityChangedRecoveryClassification ClassifyPriorityChangedRecovery(
        string runLogPath, string executionUnit, int fromRevision, int toRevision, string expectedReason, out string? detail)
    {
        detail = null;
        if (!File.Exists(runLogPath))
        {
            return PriorityChangedRecoveryClassification.None;
        }

        IReadOnlyList<RunEvent> events;
        try
        {
            events = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            // Fail OPEN here: an unreadable/malformed runs.jsonl must never
            // suppress recording a genuinely-needed audit event. Worst
            // case on this path is a redundant append once the file is
            // repaired, which is far preferable to a silently missing one.
            return PriorityChangedRecoveryClassification.None;
        }

        var matching = events
            .Where(runEvent =>
                string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                && string.Equals(runEvent.Event, PriorityChangedEventName, StringComparison.Ordinal)
                && TryParseRevisionPair(runEvent.Reason, out var parsedFrom, out var parsedTo)
                && parsedFrom == fromRevision
                && parsedTo == toRevision)
            .ToArray();

        if (matching.Length == 0)
        {
            return PriorityChangedRecoveryClassification.None;
        }

        var distinctReasons = matching.Select(runEvent => runEvent.Reason).Distinct(StringComparer.Ordinal).ToArray();
        if (distinctReasons.Length > 1)
        {
            detail = $"{matching.Length} event(s) disagree on the claim for this revision pair: {string.Join(" | ", distinctReasons)}.";
            return PriorityChangedRecoveryClassification.Conflicting;
        }

        if (matching.Length > 1)
        {
            detail = $"{matching.Length} identical events already claim this revision pair ('{distinctReasons[0]}'); this duplication must be reconciled manually.";
            return PriorityChangedRecoveryClassification.DuplicateIdentical;
        }

        if (!string.Equals(matching[0].Reason, expectedReason, StringComparison.Ordinal))
        {
            detail = $"an existing event claims this revision pair with reason '{matching[0].Reason}', which does not match the expected reason for this request ('{expectedReason}').";
            return PriorityChangedRecoveryClassification.Conflicting;
        }

        return PriorityChangedRecoveryClassification.PendingRetry;
    }

    private static bool TryParseRevisionPair(string? reasonText, out int fromRevision, out int toRevision)
    {
        fromRevision = 0;
        toRevision = 0;
        if (string.IsNullOrEmpty(reasonText))
        {
            return false;
        }

        var match = RevisionPairPattern.Match(reasonText);
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["from"].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out fromRevision)
            && int.TryParse(match.Groups["to"].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out toRevision);
    }

    private static void AppendPriorityChangedEvent(string runLogPath, RunEvent runEvent)
    {
        if (AppendPriorityChangedEventOverride is not null)
        {
            AppendPriorityChangedEventOverride(runLogPath, runEvent);
            return;
        }

        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(runLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    private static void WriteQueueState(string queueStatePath, QueueState baseState, QueueState state)
    {
        if (WriteQueueStateOverride is not null)
        {
            WriteQueueStateOverride(queueStatePath, state);
            return;
        }

        // G548: guarded write. This command already re-reads the file before
        // writing (G537), so the base handed to the guard is that fresh read
        // — the guard then still catches a write racing in between.
        QueueStatePersistence.Persist(queueStatePath, baseState, state);
    }

    /// <summary>
    /// G537 round-6 review repair: a stable sibling path to
    /// <c>queue-state.json</c> — one lock file per repo/host, so
    /// concurrent <c>queue reprioritize</c> invocations against the SAME
    /// queue-state.json (and only that one) mutually exclude, regardless
    /// of which execution unit each targets (a global-per-repo lock is
    /// the simplest correct scope; this command's critical section is
    /// already brief).
    /// </summary>
    private static string GetLockPath(string queueStatePath) =>
        Path.Combine(Path.GetDirectoryName(queueStatePath) ?? ".", "queue-state.reprioritize.lock");

    /// <summary>
    /// G537 round-6 review repair: a non-blocking, OS-enforced exclusive
    /// lock — <see cref="FileShare.None"/> means any other process (or,
    /// for tests, any other open handle to the same path) attempting the
    /// same open fails immediately with <see cref="IOException"/> rather
    /// than blocking. This command never waits/retries for the lock: the
    /// loser of a contention race fails closed immediately, and the
    /// operator/orchestrator decides whether and when to retry.
    /// </summary>
    private static bool TryAcquireLock(string lockPath, out FileStream? lockStream)
    {
        try
        {
            var lockDirectory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(lockDirectory))
            {
                Directory.CreateDirectory(lockDirectory);
            }

            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            lockStream = null;
            return false;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? priority,
        out string? reason,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = null;
        priority = null;
        reason = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            error = "Queue reprioritize command requires an execution unit as the first argument.";
            return false;
        }

        executionUnit = args[0];

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--priority":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--priority requires a value (high, normal, or low).";
                        return false;
                    }
                    if (!TryNormalizePriority(args[++index], out priority))
                    {
                        error = $"Unsupported --priority value '{args[index]}'. Supported values: high, normal, low.";
                        return false;
                    }
                    break;
                case "--reason":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--reason requires a value.";
                        return false;
                    }
                    reason = args[++index].Trim();
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requestedFormat = args[++index].Trim();
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (priority is null)
        {
            error = "Queue reprioritize command requires '--priority <high|normal|low>'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            error = "Queue reprioritize command requires '--reason <text>' — a priority change without a recorded reason is not permitted.";
            return false;
        }

        return true;
    }

    private static bool TryNormalizePriority(string raw, out string? normalized)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case PriorityHigh:
                normalized = PriorityHigh;
                return true;
            case PriorityNormal:
                normalized = PriorityNormal;
                return true;
            case PriorityLow:
                normalized = PriorityLow;
                return true;
            default:
                normalized = null;
                return false;
        }
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant(),
        };
    }

    private static QueueReprioritizeResult NewResult(
        string executionUnit,
        bool write,
        string? oldPriority,
        string requestedPriority,
        string reason,
        bool changed,
        string? error) => new()
    {
        ExecutionUnit = executionUnit,
        Mode = write ? ModeWrite : ModeDryRun,
        OldPriority = oldPriority,
        RequestedPriority = requestedPriority,
        Reason = reason,
        Changed = changed,
        Error = error,
    };

    private static void EmitResult(TextWriter writer, string format, QueueReprioritizeResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine($"# Queue reprioritize — {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- old priority: {result.OldPriority ?? "-"}");
        writer.WriteLine($"- requested priority: {result.RequestedPriority}");
        writer.WriteLine($"- reason: {result.Reason}");
        writer.WriteLine($"- changed: {(result.Changed ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine($"- error: {result.Error}");
        }
        else if (result.Changed && string.Equals(result.Mode, ModeDryRun, StringComparison.Ordinal))
        {
            writer.WriteLine($"- would set priority to '{result.RequestedPriority}' and append a `{PriorityChangedEventName}` runs event; re-run with --write to apply.");
        }
    }
}

internal sealed record QueueReprioritizeResult
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("old_priority")]
    public string? OldPriority { get; init; }

    [JsonPropertyName("requested_priority")]
    public required string RequestedPriority { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("changed")]
    public required bool Changed { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
