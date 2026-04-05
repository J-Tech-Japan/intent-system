using IntentSystem.Clarify.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

internal static class ClarifyListRenderer
{
    public static void Write(
        TextWriter writer,
        IReadOnlyList<ClarificationItem> clarifications,
        IReadOnlyDictionary<string, QueueItem> queueItemsByExecutionUnit)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(clarifications);
        ArgumentNullException.ThrowIfNull(queueItemsByExecutionUnit);

        if (clarifications.Count == 0)
        {
            writer.WriteLine("No open clarifications found.");
            return;
        }

        writer.WriteLine("Open clarifications:");

        foreach (var clarification in clarifications)
        {
            writer.WriteLine($"Execution unit: {clarification.ExecutionUnit}");
            writer.WriteLine($"Status: {clarification.Status}");
            writer.WriteLine($"Question: {clarification.QuestionText}");
            writer.WriteLine($"Reason: {clarification.Reason}");
            writer.WriteLine($"Return path: {clarification.ClarificationReturnPath}");

            if (queueItemsByExecutionUnit.TryGetValue(clarification.ExecutionUnit, out var queueItem))
            {
                writer.WriteLine($"Queue title: {queueItem.Title}");
                writer.WriteLine($"Queue state: {queueItem.State}");
            }

            writer.WriteLine($"Artifact question id: {clarification.QuestionId}");
            writer.WriteLine(string.Empty);
        }
    }
}
