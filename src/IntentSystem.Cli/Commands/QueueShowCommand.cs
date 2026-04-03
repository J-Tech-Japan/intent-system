namespace IntentSystem.Cli.Commands;

internal static class QueueShowCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Queue show command requires an execution unit.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 0;
        }

        var executionUnit = args[0];
        var item = queueState.Items.FirstOrDefault(queueItem =>
            string.Equals(queueItem.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (item is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        QueueCommandSupport.WriteItemDetails(writer, item);
        return 0;
    }
}
