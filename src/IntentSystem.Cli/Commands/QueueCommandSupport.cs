using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class QueueCommandSupport
{
    public static QueueState? LoadQueueState(CliContext context, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            writer.WriteLine($"No queue state found at {queueStatePath}");
            return null;
        }

        return QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
    }

    public static void WriteItemDetails(TextWriter writer, QueueItem item)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(item);

        writer.WriteLine($"Execution unit: {item.ExecutionUnit}");
        writer.WriteLine($"Title: {item.Title}");
        writer.WriteLine($"State: {item.State}");
        writer.WriteLine($"Priority: {item.Priority}");
        writer.WriteLine($"Dependencies: {FormatList(item.Dependencies)}");
        writer.WriteLine($"Blocked by: {FormatList(item.BlockedBy)}");
        writer.WriteLine($"Clarification return path: {item.ClarificationReturnPath}");
        writer.WriteLine($"Packet implementation: {item.PacketPaths.Implementation}");
        writer.WriteLine($"Packet review context: {item.PacketPaths.ReviewContext}");
        writer.WriteLine($"Packet yaml: {item.PacketPaths.Yaml}");

        if (item.LinkedIssue is null)
        {
            writer.WriteLine("Linked issue: -");
        }
        else
        {
            writer.WriteLine($"Linked issue repo: {item.LinkedIssue.Repo}");
            writer.WriteLine($"Linked issue number: {item.LinkedIssue.Number}");
            writer.WriteLine($"Linked issue url: {item.LinkedIssue.Url}");
        }

        writer.WriteLine($"Worker role: {item.WorkerRole}");
        writer.WriteLine($"Review role: {item.ReviewRole}");
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "-"
            : string.Join(", ", values);
    }
}
