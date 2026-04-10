using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record DirectRunResultArtifact
{
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("entry_kind")]
    public required string EntryKind { get; init; }

    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("run_status")]
    public required string RunStatus { get; init; }

    [JsonPropertyName("raw_log_ref")]
    public required string RawLogRef { get; init; }

    [JsonPropertyName("packet_ref")]
    public required string PacketRef { get; init; }

    [JsonPropertyName("review_context_ref")]
    public required string ReviewContextRef { get; init; }

    [JsonPropertyName("linked_issue_url")]
    public string? LinkedIssueUrl { get; init; }

    [JsonPropertyName("linked_pr_url")]
    public string? LinkedPrUrl { get; init; }

    [JsonPropertyName("worktree_path")]
    public required string WorktreePath { get; init; }
}

internal static class DirectRunResultArtifactJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static string Serialize(DirectRunResultArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return JsonSerializer.Serialize(artifact, Options);
    }

    public static DirectRunResultArtifact Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<DirectRunResultArtifact>(json, Options)
            ?? throw new InvalidOperationException("Direct run result artifact deserialized to null.");
    }
}
