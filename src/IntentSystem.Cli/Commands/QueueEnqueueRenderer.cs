namespace IntentSystem.Cli.Commands;

internal static class QueueEnqueueRenderer
{
    public static void WriteSummary(TextWriter writer, QueueEnqueueCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Queue enqueue processed for execution unit '{result.ExecutionUnit}'.");
        writer.WriteLine("Enqueued execution units:");
        WriteList(writer, result.EnqueuedExecutionUnits);
        writer.WriteLine("Packet paths:");
        WriteList(writer, result.PacketPaths);
        writer.WriteLine("Skipped units:");
        WriteList(writer, result.SkippedUnits);
    }

    private static void WriteList(TextWriter writer, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (var value in values)
        {
            writer.WriteLine($"- {value}");
        }
    }
}
