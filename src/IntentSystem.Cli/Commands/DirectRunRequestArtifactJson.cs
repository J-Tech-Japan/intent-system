using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record DirectRunRequestArtifact
{
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("entry_kind")]
    public required string EntryKind { get; init; }

    [JsonPropertyName("upstream_request_ref")]
    public required string UpstreamRequestRef { get; init; }

    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("transport")]
    public required string Transport { get; init; }

    [JsonPropertyName("launched_at")]
    public required string LaunchedAt { get; init; }

    [JsonPropertyName("provider_session_id")]
    public required string ProviderSessionId { get; init; }

    [JsonPropertyName("transport_summary")]
    public required string TransportSummary { get; init; }
}

internal static class DirectRunRequestArtifactJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static string Serialize(DirectRunRequestArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return JsonSerializer.Serialize(artifact, Options);
    }

    public static DirectRunRequestArtifact Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<DirectRunRequestArtifact>(json, Options)
            ?? throw new InvalidOperationException("Direct run request artifact deserialized to null.");
    }
}
