using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class QueueTransitionCommand
{
    private const string TransitionActor = "intent-cli";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length < 2
            || string.IsNullOrWhiteSpace(args[0])
            || string.IsNullOrWhiteSpace(args[1]))
        {
            writer.WriteLine("Queue transition command requires an execution unit and target state.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        if (!TryParseTargetState(args[1], out var targetState))
        {
            writer.WriteLine(
                "Unsupported queue transition target state. Supported states: queued, active, review, fixing, completed, retired, blocked, clarify-blocked.");
            return 1;
        }

        if (!TryParseReason(args[2..], out var reason, out var reasonError))
        {
            writer.WriteLine(reasonError);
            return 1;
        }

        if (IsBlockingTargetState(targetState) && string.IsNullOrWhiteSpace(reason))
        {
            writer.WriteLine("Blocking queue transitions require --reason <text>.");
            return 1;
        }

        if (!IsBlockingTargetState(targetState) && reason is not null)
        {
            writer.WriteLine("Reason is only supported for blocked and clarify-blocked transitions.");
            return 1;
        }

        try
        {
            // G534 review repair: `retired` is a guarded, idempotent,
            // terminal transition (refuses Completed, no-ops on an
            // already-Retired item) — routed through the dedicated
            // QueueManager.Retire rather than the generic non-blocking
            // path, which never asserted a source state at all.
            if (targetState == QueueItemState.Retired)
            {
                var retireResult = QueueManager.Retire(
                    queueState,
                    args[0],
                    TransitionActor,
                    DateTimeOffset.UtcNow);

                if (!retireResult.WasRetired)
                {
                    writer.WriteLine($"{args[0]} is already retired; no changes made (idempotent).");
                    return 0;
                }

                PersistTransition(context, new QueueTransitionResult
                {
                    UpdatedState = retireResult.UpdatedState,
                    Event = retireResult.Event!
                });

                writer.WriteLine($"Transitioned {args[0]} to retired.");
                return 0;
            }

            var result = IsBlockingTargetState(targetState)
                ? QueueManager.TransitionBlocking(
                    queueState,
                    args[0],
                    targetState,
                    reason!,
                    TransitionActor,
                    DateTimeOffset.UtcNow)
                : QueueManager.TransitionNonBlocking(
                    queueState,
                    args[0],
                    targetState,
                    TransitionActor,
                    DateTimeOffset.UtcNow);

            PersistTransition(context, result);

            writer.WriteLine($"Transitioned {args[0]} to {FormatState(targetState)}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool TryParseTargetState(string rawState, out QueueItemState state)
    {
        switch (rawState.Trim().ToLowerInvariant())
        {
            case "queued":
                state = QueueItemState.Queued;
                return true;
            case "active":
                state = QueueItemState.Active;
                return true;
            case "review":
                state = QueueItemState.Review;
                return true;
            case "fixing":
                state = QueueItemState.Fixing;
                return true;
            case "completed":
                state = QueueItemState.Completed;
                return true;
            case "retired":
                // G534: backfill target for a packet already retired
                // outside queue tracking (G525 lifecycle) — e.g. a packet
                // predating queue-state that must be recorded without a
                // hand-edit. QueueManager.MapNonBlockingEventName carries
                // the matching "retired" run-event case.
                state = QueueItemState.Retired;
                return true;
            case "blocked":
                state = QueueItemState.Blocked;
                return true;
            case "clarify-blocked":
                state = QueueItemState.ClarifyBlocked;
                return true;
            default:
                state = default;
                return false;
        }
    }

    private static bool TryParseReason(string[] args, out string? reason, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            reason = null;
            error = null;
            return true;
        }

        if (args.Length < 2 || !string.Equals(args[0], "--reason", StringComparison.Ordinal))
        {
            reason = null;
            error = "Queue transition command only supports optional '--reason <text>'.";
            return false;
        }

        reason = string.Join(' ', args[1..]).Trim();
        if (reason.Length == 0)
        {
            error = "Queue transition reason must not be empty.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsBlockingTargetState(QueueItemState state)
    {
        return state is QueueItemState.Blocked or QueueItemState.ClarifyBlocked;
    }

    private static void PersistTransition(CliContext context, QueueTransitionResult result)
    {
        var queueStatePath = context.GetQueueStatePath();
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(result.UpdatedState));

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(result.Event) + Environment.NewLine);
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }
}
