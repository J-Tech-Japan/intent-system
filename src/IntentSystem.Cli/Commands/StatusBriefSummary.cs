using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Compact, AI-thread-friendly read-only snapshot produced by <c>intent-cli status brief</c>.
/// Field names are stable and snake_case-serialized for JSON ingestion (G179).
/// </summary>
internal sealed record StatusBriefSummary
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("capability_matrix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TeamModeCapabilityMatrix? CapabilityMatrix { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("queue_state_present")]
    public required bool QueueStatePresent { get; init; }

    [JsonPropertyName("queue_state_readable")]
    public required bool QueueStateReadable { get; init; }

    [JsonPropertyName("in_flight_units")]
    public required IReadOnlyList<string> InFlightUnits { get; init; }

    [JsonPropertyName("review_units")]
    public required IReadOnlyList<string> ReviewUnits { get; init; }

    [JsonPropertyName("wip_present")]
    public required bool WipPresent { get; init; }

    [JsonPropertyName("clarification_open_path")]
    public string? ClarificationOpenPath { get; init; }

    [JsonPropertyName("clarification_open")]
    public required bool ClarificationOpen { get; init; }

    [JsonPropertyName("next_candidate")]
    public string? NextCandidate { get; init; }

    [JsonPropertyName("recent_events")]
    public required IReadOnlyList<StatusBriefRecentEvent> RecentEvents { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed record StatusBriefRecentEvent
{
    [JsonPropertyName("ts")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("event")]
    public required string Event { get; init; }

    [JsonPropertyName("by")]
    public required string By { get; init; }
}
