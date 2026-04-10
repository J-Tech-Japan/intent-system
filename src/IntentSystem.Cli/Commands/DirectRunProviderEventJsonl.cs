using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record DirectRunProviderEvent
{
    [JsonPropertyName("ts")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("entry_kind")]
    public required string EntryKind { get; init; }

    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("provider_session_id")]
    public required string ProviderSessionId { get; init; }

    [JsonPropertyName("event_kind")]
    public required string EventKind { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("transport")]
    public string? Transport { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("raw")]
    public string? Raw { get; init; }
}

internal static class DirectRunProviderEventJsonl
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null
    };

    public static string SerializeLine(DirectRunProviderEvent providerEvent)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);

        return JsonSerializer.Serialize(providerEvent, Options);
    }

    public static DirectRunProviderEvent DeserializeLine(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        return JsonSerializer.Deserialize<DirectRunProviderEvent>(line, Options)
            ?? throw new InvalidOperationException("Direct run provider event payload deserialized to null.");
    }

    public static IReadOnlyList<DirectRunProviderEvent> DeserializeAll(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalizedContent = NormalizeLineEndings(content);
        if (normalizedContent.Length == 0)
        {
            return [];
        }

        var lines = normalizedContent.Split('\n');
        var providerEvents = new DirectRunProviderEvent[lines.Length];

        for (var index = 0; index < lines.Length; index++)
        {
            providerEvents[index] = DeserializeLine(lines[index]);
        }

        return providerEvents;
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

internal sealed class DirectRunProviderEventWriter
{
    private readonly string artifactPath;
    private readonly object gate = new();

    public DirectRunProviderEventWriter(string artifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        this.artifactPath = artifactPath;
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Direct run provider event artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
    }

    public void Append(DirectRunProviderEvent providerEvent)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);

        lock (gate)
        {
            File.AppendAllText(
                artifactPath,
                DirectRunProviderEventJsonl.SerializeLine(providerEvent) + Environment.NewLine);
        }
    }
}
