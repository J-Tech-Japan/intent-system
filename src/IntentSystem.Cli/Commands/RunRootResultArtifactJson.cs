using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record RunRootResultArtifact
{
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("stop_reason")]
    public required string StopReason { get; init; }

    [JsonPropertyName("touched_execution_units")]
    public required IReadOnlyList<string> TouchedExecutionUnits { get; init; }

    [JsonPropertyName("reused_child_command_refs")]
    public required IReadOnlyList<string> ReusedChildCommandRefs { get; init; }

    [JsonPropertyName("execution_unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

internal static class RunRootResultArtifactJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static string Serialize(RunRootResultArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return JsonSerializer.Serialize(artifact, Options);
    }

    public static RunRootResultArtifact Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<RunRootResultArtifact>(json, Options)
            ?? throw new InvalidOperationException("Run root result artifact deserialized to null.");
    }
}
