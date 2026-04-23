using System.Text.Json;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor.Serialization;

public static class RunLogSerializer
{
    public static string SerializeLine(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        return JsonSerializer.Serialize(runEvent, SupervisorJsonOptions.Compact);
    }

    public static RunEvent DeserializeLine(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        return JsonSerializer.Deserialize<RunEvent>(line, SupervisorJsonOptions.Compact)
            ?? throw new InvalidOperationException("Run event payload deserialized to null.");
    }

    public static IReadOnlyList<RunEvent> DeserializeAll(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalizedContent = NormalizeLineEndings(content);
        if (normalizedContent.Length == 0)
        {
            return [];
        }

        var lines = normalizedContent.Split('\n');
        var runEvents = new List<RunEvent>(lines.Length);

        for (var index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            runEvents.Add(DeserializeLine(lines[index]));
        }

        return runEvents;
    }

    private static string NormalizeLineEndings(string content)
    {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        if (!normalized.EndsWith('\n'))
        {
            return normalized;
        }

        var endIndex = normalized.Length;
        while (endIndex > 0 && normalized[endIndex - 1] == '\n')
        {
            endIndex--;
        }

        return normalized[..endIndex];
    }
}
