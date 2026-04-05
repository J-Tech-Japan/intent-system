using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunRereviewCommand
{
    private const string TransitionActor = "intent-cli";

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run rereview command requires an execution unit.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var executionUnit = args[0];
        if (!queueState.Items.Any(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)))
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            writer.WriteLine($"Run log was not found at {runLogPath}");
            return 1;
        }

        try
        {
            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            var linkedPr = LatestLinkedPrResolver.Resolve(runEvents, executionUnit);
            var transition = QueueManager.ResubmitForReview(
                queueState,
                executionUnit,
                TransitionActor,
                TimestampFactory(),
                linkedPr);

            PersistRereview(context, transition);
            writer.WriteLine($"Run rereviewed for {executionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void PersistRereview(CliContext context, QueueTransitionResult result)
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
}
