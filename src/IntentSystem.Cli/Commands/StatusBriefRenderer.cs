using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Output renderer for the read-only <c>status brief</c> command (G179).
/// </summary>
internal static class StatusBriefRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static void WriteText(TextWriter writer, StatusBriefSummary summary)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(summary);

        writer.WriteLine($"Domain: {summary.Domain}");
        writer.WriteLine(
            $"Queue state: {(summary.QueueStatePresent ? (summary.QueueStateReadable ? "present" : "present-unreadable") : "missing")}"
            + $" ({summary.QueueStatePath})");
        writer.WriteLine($"In-flight units: {FormatList(summary.InFlightUnits)}");
        writer.WriteLine($"Review units: {FormatList(summary.ReviewUnits)}");
        writer.WriteLine($"WIP present: {(summary.WipPresent ? "yes" : "no")}");
        if (summary.CapabilityMatrix is { } capabilityMatrix)
        {
            writer.WriteLine($"Team mode: {capabilityMatrix.TeamMode} ({capabilityMatrix.ModeSource})");
            writer.WriteLine($"Not applicable: {string.Join(", ", capabilityMatrix.NotApplicableClasses)}");
        }
        writer.WriteLine($"Clarification open: {(summary.ClarificationOpen ? "yes" : "no")}"
            + (summary.ClarificationOpenPath is null ? string.Empty : $" ({summary.ClarificationOpenPath})"));
        writer.WriteLine($"Next candidate: {summary.NextCandidate ?? "-"}");

        writer.WriteLine("Recent events:");
        if (summary.RecentEvents.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var recentEvent in summary.RecentEvents)
            {
                writer.WriteLine(
                    $"- {recentEvent.Timestamp} {recentEvent.ExecutionUnit} {recentEvent.Event} by={recentEvent.By}");
            }
        }

        writer.WriteLine($"Recommended action: {summary.RecommendedAction}");

        if (summary.Notes.Count > 0)
        {
            writer.WriteLine("Notes:");
            foreach (var note in summary.Notes)
            {
                writer.WriteLine($"- {note}");
            }
        }
    }

    public static void WriteJson(TextWriter writer, StatusBriefSummary summary)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(summary);

        writer.WriteLine(JsonSerializer.Serialize(summary, JsonOptions));
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "-"
            : string.Join(", ", values);
    }
}
