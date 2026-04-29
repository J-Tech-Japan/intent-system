using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Output renderer for the read-only <c>clarify draft</c> command (G181).
/// Produces a Markdown packet (default) or stable snake_case JSON.
/// </summary>
internal static class ClarifyDraftRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static void WriteMarkdown(TextWriter writer, ClarifyDraftPacket packet)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(packet);

        writer.WriteLine($"# Clarification draft: {packet.Domain}");
        writer.WriteLine();

        writer.WriteLine("## Question");
        writer.WriteLine(packet.Question);
        writer.WriteLine();

        writer.WriteLine("## Background");
        if (packet.Background.Count == 0)
        {
            writer.WriteLine("- (none captured)");
        }
        else
        {
            foreach (var entry in packet.Background)
            {
                writer.WriteLine(entry.StartsWith("- ", StringComparison.Ordinal) ? entry : $"- {entry}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Options");
        if (packet.Options.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var option in packet.Options)
            {
                writer.WriteLine($"### {option.Label}. {option.Description}");
                writer.WriteLine();
                writer.WriteLine("Pros:");
                if (option.Pros.Count == 0)
                {
                    writer.WriteLine("- (operator to fill)");
                }
                else
                {
                    foreach (var pro in option.Pros)
                    {
                        writer.WriteLine($"- {pro}");
                    }
                }
                writer.WriteLine();
                writer.WriteLine("Cons:");
                if (option.Cons.Count == 0)
                {
                    writer.WriteLine("- (operator to fill)");
                }
                else
                {
                    foreach (var con in option.Cons)
                    {
                        writer.WriteLine($"- {con}");
                    }
                }
                writer.WriteLine();
            }
        }

        writer.WriteLine("## Recommendation");
        writer.WriteLine(string.IsNullOrWhiteSpace(packet.Recommendation)
            ? "(operator to fill)"
            : packet.Recommendation);
        writer.WriteLine();

        writer.WriteLine("## Return path");
        writer.WriteLine(packet.ReturnPath ?? "-");
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

    public static void WriteJson(TextWriter writer, ClarifyDraftPacket packet)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(packet);

        writer.WriteLine(JsonSerializer.Serialize(packet, JsonOptions));
    }
}
