using IntentSystem.Supervisor;

namespace IntentSystem.Cli.Commands;

internal static class QueueNextCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 0)
        {
            writer.WriteLine("Queue next command does not accept arguments.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 0;
        }

        var nextItem = QueueSelection.SelectNext(queueState);
        if (nextItem is null)
        {
            writer.WriteLine("No eligible queued item found.");
            return 0;
        }

        writer.WriteLine("Next candidate");
        QueueCommandSupport.WriteItemDetails(writer, nextItem);
        return 0;
    }
}
