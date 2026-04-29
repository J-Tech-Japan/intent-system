using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Output renderer for the read-only <c>context collect</c> command (G180).
/// Produces a Markdown packet (default) or stable snake_case JSON.
/// </summary>
internal static class ContextCollectRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static void WriteMarkdown(TextWriter writer, ContextCollectPacket packet)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(packet);

        writer.WriteLine($"# Context packet: {packet.Domain}");
        writer.WriteLine();

        writer.WriteLine("## Queue state");
        writer.WriteLine($"- Path: `{packet.QueueStatePath}`");
        writer.WriteLine(
            "- Status: "
            + (packet.QueueStatePresent
                ? (packet.QueueStateReadable ? "present" : "present-unreadable")
                : "missing"));
        writer.WriteLine($"- In-flight units: {FormatList(packet.InFlightUnits)}");
        writer.WriteLine($"- Review units: {FormatList(packet.ReviewUnits)}");
        writer.WriteLine($"- Next candidate: {packet.NextCandidate ?? "-"}");
        writer.WriteLine();

        writer.WriteLine("## Focus");
        writer.WriteLine($"- Unit: {packet.FocusUnit ?? "-"}");
        if (packet.FocusPacket is not null)
        {
            writer.WriteLine(
                $"- Implementation: `{packet.FocusPacket.ImplementationPath}` ({(packet.FocusPacket.ImplementationPresent ? "present" : "missing")})");
            writer.WriteLine(
                $"- Review context: `{packet.FocusPacket.ReviewContextPath}` ({(packet.FocusPacket.ReviewContextPresent ? "present" : "missing")})");
            writer.WriteLine(
                $"- Yaml: `{packet.FocusPacket.YamlPath}` ({(packet.FocusPacket.YamlPresent ? "present" : "missing")})");
        }
        writer.WriteLine();

        writer.WriteLine("## Clarification");
        writer.WriteLine($"- Path: {(packet.ClarificationOpenPath is null ? "-" : $"`{packet.ClarificationOpenPath}`")}");
        writer.WriteLine($"- Open blocker: {(packet.ClarificationOpen ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(packet.ClarificationExcerpt))
        {
            writer.WriteLine();
            writer.WriteLine("```markdown");
            writer.WriteLine(packet.ClarificationExcerpt.TrimEnd());
            writer.WriteLine("```");
        }
        writer.WriteLine();

        writer.WriteLine("## Automation bindings");
        writer.WriteLine($"- Path: {(packet.AutomationBindingsPath is null ? "-" : $"`{packet.AutomationBindingsPath}`")}");
        writer.WriteLine($"- Present: {(packet.AutomationBindingsPresent ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(packet.AutomationBindingsExcerpt))
        {
            writer.WriteLine();
            writer.WriteLine("```markdown");
            writer.WriteLine(packet.AutomationBindingsExcerpt.TrimEnd());
            writer.WriteLine("```");
        }
        writer.WriteLine();

        writer.WriteLine("## Recent events");
        if (packet.RecentEvents.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var recentEvent in packet.RecentEvents)
            {
                writer.WriteLine(
                    $"- `{recentEvent.Timestamp}` {recentEvent.ExecutionUnit} {recentEvent.Event} (by={recentEvent.By})");
            }
        }
        writer.WriteLine();

        if (packet.Notes.Count > 0)
        {
            writer.WriteLine("## Notes");
            foreach (var note in packet.Notes)
            {
                writer.WriteLine($"- {note}");
            }
        }
    }

    public static void WriteJson(TextWriter writer, ContextCollectPacket packet)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(packet);

        writer.WriteLine(JsonSerializer.Serialize(packet, JsonOptions));
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "-"
            : string.Join(", ", values);
    }
}
