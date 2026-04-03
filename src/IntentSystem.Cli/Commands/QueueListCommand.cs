namespace IntentSystem.Cli.Commands;

internal static class QueueListCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 0;
        }

        writer.WriteLine("Execution Unit | State | Priority | Dependencies | Title");
        foreach (var item in queueState.Items)
        {
            var dependencies = item.Dependencies.Count == 0
                ? "-"
                : string.Join(", ", item.Dependencies);

            writer.WriteLine(
                $"{item.ExecutionUnit} | {item.State} | {item.Priority} | {dependencies} | {item.Title}");
        }

        return 0;
    }
}
