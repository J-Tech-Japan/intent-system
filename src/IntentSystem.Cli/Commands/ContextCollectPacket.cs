using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only context packet emitted by <c>intent-cli context collect</c> (G180).
/// Aggregates queue-state, runs.jsonl, parent-host clarification and automation
/// binding files, and packet paths for the focus execution unit so a high-context
/// AI tasking thread can decide its next move without manually opening five files.
/// Field names are stable snake_case for JSON ingestion.
/// </summary>
internal sealed record ContextCollectPacket
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    /// <summary>
    /// G530: the four G529 semantic-facet groups (<c>vocabulary</c>,
    /// <c>invariant</c>, <c>decider</c>, <c>acceptance-property</c>, in that
    /// order), each carrying every domain node classified under it —
    /// narrowed by <c>--scope</c> when passed, restricted to the requested
    /// subset by <c>--facets</c> when passed. Rendered AHEAD of the
    /// unclassified queue/clarification/automation-bindings/events context
    /// below, since this is the semantic core a change must respect.
    /// </summary>
    [JsonPropertyName("facet_context")]
    public required IReadOnlyList<FacetContextGroup> FacetContext { get; init; }

    /// <summary>
    /// G530: set only when the DOMAIN has zero facet-annotated nodes at all
    /// (not merely when a <c>--scope</c>/<c>--facets</c> query matched
    /// nothing) — explicit graceful-degradation note, never an error.
    /// </summary>
    [JsonPropertyName("facet_context_note")]
    public string? FacetContextNote { get; init; }

    /// <summary>
    /// G530 review repair: every node excluded from <see cref="FacetContext"/>
    /// because its own <c>facets:</c> declaration was malformed, or that
    /// carried at least one unknown value — never silent, so an agent can
    /// tell "omitted with a reason" apart from "never had facets at all".
    /// </summary>
    [JsonPropertyName("facet_context_warnings")]
    public required IReadOnlyList<FacetContextWarning> FacetContextWarnings { get; init; }

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

    [JsonPropertyName("next_candidate")]
    public string? NextCandidate { get; init; }

    [JsonPropertyName("focus_unit")]
    public string? FocusUnit { get; init; }

    [JsonPropertyName("focus_packet")]
    public ContextCollectPacketReference? FocusPacket { get; init; }

    [JsonPropertyName("clarification_open_path")]
    public string? ClarificationOpenPath { get; init; }

    [JsonPropertyName("clarification_open")]
    public required bool ClarificationOpen { get; init; }

    [JsonPropertyName("clarification_excerpt")]
    public string? ClarificationExcerpt { get; init; }

    [JsonPropertyName("automation_bindings_path")]
    public string? AutomationBindingsPath { get; init; }

    [JsonPropertyName("automation_bindings_present")]
    public required bool AutomationBindingsPresent { get; init; }

    [JsonPropertyName("automation_bindings_excerpt")]
    public string? AutomationBindingsExcerpt { get; init; }

    [JsonPropertyName("recent_events")]
    public required IReadOnlyList<ContextCollectRecentEvent> RecentEvents { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed record ContextCollectPacketReference
{
    [JsonPropertyName("implementation_path")]
    public required string ImplementationPath { get; init; }

    [JsonPropertyName("implementation_present")]
    public required bool ImplementationPresent { get; init; }

    [JsonPropertyName("review_context_path")]
    public required string ReviewContextPath { get; init; }

    [JsonPropertyName("review_context_present")]
    public required bool ReviewContextPresent { get; init; }

    [JsonPropertyName("yaml_path")]
    public required string YamlPath { get; init; }

    [JsonPropertyName("yaml_present")]
    public required bool YamlPresent { get; init; }
}

internal sealed record ContextCollectRecentEvent
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
