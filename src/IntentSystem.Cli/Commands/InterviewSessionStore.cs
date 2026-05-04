using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G250: Durable JSON-backed interview session store. Questions are
/// addressed by stable id. Answers may be empty (pending) or recorded.
/// File path: <c>&lt;root&gt;/intents/&lt;domain&gt;/interviews/&lt;session&gt;.json</c>.
/// The store never launches an AI provider and only mutates the file
/// when explicitly asked to.
/// </summary>
internal static class InterviewSessionStore
{
    public static string ResolvePath(string repoRoot, string domain, string session)
    {
        return Path.Combine(repoRoot, "intents", domain, "interviews", $"{session}.json");
    }

    public static InterviewSession? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InterviewSession>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Write(string path, InterviewSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(session, JsonOptions));
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record InterviewSession
{
    [JsonPropertyName("session")]
    public required string Session { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("questions")]
    public required List<InterviewQuestion> Questions { get; init; }
}

internal sealed record InterviewQuestion
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("answer")]
    public string? Answer { get; set; }
}
