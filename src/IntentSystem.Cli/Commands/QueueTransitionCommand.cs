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

        if (args.Length != 2
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
                "Unsupported queue transition target state. Supported states: queued, active, review, fixing, completed.");
            return 1;
        }

        try
        {
            var result = QueueManager.TransitionNonBlocking(
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
            default:
                state = default;
                return false;
        }
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
        return state.ToString().ToLowerInvariant();
    }
}
