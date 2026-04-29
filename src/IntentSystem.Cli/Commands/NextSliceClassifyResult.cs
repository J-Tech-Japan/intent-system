using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only continuation classification produced by
/// <c>intent-cli next-slice classify</c> (G185). Field names are stable and
/// snake_case-serialized for AI-thread JSON ingestion. Round-trippable via
/// <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
internal sealed record NextSliceClassifyResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    [JsonPropertyName("wip_refs")]
    public required IReadOnlyList<string> WipRefs { get; init; }

    [JsonPropertyName("clarification_summary")]
    public string? ClarificationSummary { get; init; }

    [JsonPropertyName("candidate_execution_unit")]
    public string? CandidateExecutionUnit { get; init; }

    [JsonPropertyName("candidate_packet_path")]
    public string? CandidatePacketPath { get; init; }

    [JsonPropertyName("recommended_next_action")]
    public required string RecommendedNextAction { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// Allowed classification values emitted by next-slice classify (G185).
/// Two of these (<see cref="ExistingIssueNeedsRepair"/> and
/// <see cref="ParentOnlyUpdate"/>) are reserved output values for future
/// expansion: G185 does not yet ship a deterministic precedence rule that
/// triggers them, but they are part of the stable enum surface so downstream
/// AI-thread consumers can switch on them without breaking when later slices
/// add the rules.
/// </summary>
internal static class NextSliceClassification
{
    public const string IssueCutReady = "issue-cut-ready";
    public const string ClarificationRequired = "clarification-required";
    public const string SkipDueToWip = "skip-due-to-wip";
    public const string ExistingIssueNeedsRepair = "existing-issue-needs-repair";
    public const string ParentOnlyUpdate = "parent-only-update";
    public const string NoActionableItem = "no-actionable-item";
    public const string InspectManually = "inspect-manually";
}
