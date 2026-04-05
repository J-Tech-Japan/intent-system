using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

internal static class RunLogRenderer
{
    public static void Write(TextWriter writer, QueueItem queueItem, IReadOnlyList<RunEvent> runEvents)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentNullException.ThrowIfNull(runEvents);

        writer.WriteLine($"Execution unit: {queueItem.ExecutionUnit}");
        writer.WriteLine($"Current state: {FormatState(queueItem.State)}");
        writer.WriteLine($"Linked issue: {FormatValue(queueItem.LinkedIssue?.Url)}");
        writer.WriteLine("Run history:");

        if (runEvents.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (var runEvent in runEvents)
        {
            var parts = new List<string>
            {
                runEvent.Ts.ToString("O"),
                $"event={runEvent.Event}",
                $"by={runEvent.By}"
            };

            AppendOptionalPart(parts, "linked_issue", runEvent.LinkedIssue);
            AppendOptionalPart(parts, "linked_pr", runEvent.LinkedPr);
            AppendOptionalPart(parts, "comment_ref", runEvent.CommentRef);
            AppendOptionalPart(parts, "reason", runEvent.Reason);

            writer.WriteLine($"- {string.Join(" | ", parts)}");
        }
    }

    private static void AppendOptionalPart(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}={value}");
        }
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }
}
