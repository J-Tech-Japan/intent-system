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

    public const string PriorityHigh = "high";
    public const string PriorityNormal = "normal";
    public const string PriorityLow = "low";

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

        if (!write)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: true,
                error: null));
            return 0;
        }

        var changedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var updatedItem = item with { Priority = requestedPriority! };
        var newItems = queueState.Items.ToArray();
        newItems[index0] = updatedItem;
        var updatedState = queueState with { Items = newItems, UpdatedAt = changedAt };
        var runLogPath = context.GetRunLogPath();

        // G537 review repair: fail-closed, repairable write strategy. The
        // audit event is written FIRST, queue-state SECOND — the reverse
        // of the naive ordering — so a failure at either step never
        // produces a silent, unaudited priority mutation:
        //
        // - If the event append fails, queue-state is never touched: no
        //   durable change happened at all, and a plain retry starts
        //   fresh.
        // - If the event append succeeds but the queue-state write then
        //   fails, the audit trail already proves the attempted change
        //   and its reason even though the state file doesn't yet
        //   reflect it. Re-running this EXACT command detects the
        //   already-recorded event and skips re-appending — it only
        //   retries the queue-state write, so convergence never produces
        //   a duplicate event.
        //
        // Round-2 review repair: execution unit + event name + the
        // deterministic reason text ALONE is not enough to tell "the
        // pending event from my own immediately-preceding failed attempt"
        // apart from "a genuinely historical, fully-completed transition
        // that happens to share the same old/new priority and reason"
        // (e.g. normal->high reason R, then high->normal reason S, then
        // normal->high reason R again — a real, distinct third mutation
        // whose reason text collides byte-for-byte with the first). The
        // match is now ALSO bound to `queueState.UpdatedAt` — the CURRENT,
        // not-yet-mutated queue-state generation read at the top of this
        // invocation: only an event timestamped AT OR AFTER that
        // generation could possibly be the audit record from an attempt
        // that started against this SAME unmutated state and then failed
        // before the queue-state write landed. Any older event was
        // necessarily superseded by at least one successful queue-state
        // write since (this command always advances `UpdatedAt` on every
        // successful write) and can never be mistaken for a pending retry.
        var expectedReason = $"priority changed from '{item.Priority}' to '{requestedPriority}': {reason}";
        var alreadyAudited = RunsLogHasMatchingPriorityChangedEvent(runLogPath, executionUnit!, expectedReason, queueState.UpdatedAt);

        if (!alreadyAudited)
        {
            var runEvent = new RunEvent
            {
                Ts = changedAt,
                ExecutionUnit = executionUnit!,
                Event = PriorityChangedEventName,
                By = ReprioritizeActor,
                Reason = expectedReason,
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

        try
        {
            WriteQueueState(queueStatePath, updatedState);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: false,
                error: $"the priority-changed runs event was recorded, but queue-state.json could not be updated ({exception.Message}); "
                    + $"queue-state on disk still shows the OLD priority '{item.Priority}'. Re-run this exact command to retry — it will "
                    + "detect the already-recorded event and only retry the queue-state write, without appending a duplicate event."));
            return 1;
        }

        EmitResult(writer, format, NewResult(executionUnit!, write, oldPriority: item.Priority, requestedPriority!, reason!, changed: true, error: null));
        return 0;
    }

    private static bool RunsLogHasMatchingPriorityChangedEvent(
        string runLogPath, string executionUnit, string expectedReason, DateTimeOffset currentQueueStateGeneration)
    {
        if (!File.Exists(runLogPath))
        {
            return false;
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
            return false;
        }

        // Round-2 review repair: `Ts >= currentQueueStateGeneration` is the
        // binding that tells a genuine pending-retry event (recorded
        // against THIS still-unmutated queue-state generation) apart from
        // a stale, fully-historical event that happens to share the same
        // execution unit/event name/reason text. Every successful write
        // this command makes advances `queue-state.json`'s `UpdatedAt` to
        // the SAME timestamp used for its own event's `Ts` — so an event
        // from any PRIOR generation is necessarily older than the current
        // `UpdatedAt` and can never satisfy this bound.
        return events.Any(runEvent =>
            string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal)
            && string.Equals(runEvent.Event, PriorityChangedEventName, StringComparison.Ordinal)
            && string.Equals(runEvent.Reason, expectedReason, StringComparison.Ordinal)
            && runEvent.Ts >= currentQueueStateGeneration);
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

    private static void WriteQueueState(string queueStatePath, QueueState state)
    {
        if (WriteQueueStateOverride is not null)
        {
            WriteQueueStateOverride(queueStatePath, state);
            return;
        }

        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(state));
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
