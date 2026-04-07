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

        try
        {
            var result = ExecuteCore(context, args[0]);
            writer.WriteLine($"Run rereviewed for {result.ExecutionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static RunRereviewResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueStatePath = context.GetQueueStatePath();
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null);
        if (queueState is null)
        {
            throw new InvalidOperationException($"No queue state found at {queueStatePath}");
        }

        if (!queueState.Items.Any(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Execution unit '{executionUnit}' was not found in queue state.");
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            throw new InvalidOperationException($"Run log was not found at {runLogPath}");
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        var linkedPr = LatestLinkedPrResolver.Resolve(runEvents, executionUnit);
        var transition = QueueManager.ResubmitForReview(
            queueState,
            executionUnit,
            TransitionActor,
            TimestampFactory(),
            linkedPr);

        PersistRereview(context, transition);
        return new RunRereviewResult
        {
            ExecutionUnit = executionUnit,
            LinkedPr = linkedPr
        };
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
