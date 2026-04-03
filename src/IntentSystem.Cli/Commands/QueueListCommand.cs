using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class QueueListCommand
{
    public static int Execute(CliContext context, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            writer.WriteLine($"No queue state found at {queueStatePath}");
            return 0;
        }

        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));

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
