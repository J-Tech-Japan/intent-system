using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G190 tasking-thread handoff packet artifact. Snake_case JSON shape that
/// <c>intent-cli tasking handoff</c> writes. Combines the existing G179 status
/// brief, G180 context collect, and G185 next-slice classify packets into a
/// single deterministic local artifact for an outer tasking thread or human
/// inspector.
///
/// The handoff is explicitly local-only: it never publishes a GitHub issue,
/// applies labels, mutates queue/runs state, or launches provider processes.
/// <see cref="IsPublished"/> and <see cref="IsAutomationVisible"/> are ALWAYS
/// literal <c>false</c>; <see cref="TaskingHandoffStatus"/> is ALWAYS the
/// literal value <see cref="TaskingHandoffConstants.LocalOnlyStatus"/>. These
/// are contract fields so future tooling can observe them and never confuse a
/// handoff packet with a real published surface.
/// </summary>
internal sealed record TaskingHandoffPacket
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("is_published")]
    public required bool IsPublished { get; init; }

    [JsonPropertyName("is_automation_visible")]
    public required bool IsAutomationVisible { get; init; }

    [JsonPropertyName("tasking_handoff_status")]
    public required string TaskingHandoffStatus { get; init; }

    [JsonPropertyName("status_brief")]
    public StatusBriefSummary? StatusBrief { get; init; }

    [JsonPropertyName("status_brief_error")]
    public string? StatusBriefError { get; init; }

    [JsonPropertyName("context_collect")]
    public ContextCollectPacket? ContextCollect { get; init; }

    [JsonPropertyName("context_collect_error")]
    public string? ContextCollectError { get; init; }

    [JsonPropertyName("next_slice_classify")]
    public NextSliceClassifyResult? NextSliceClassify { get; init; }

    [JsonPropertyName("next_slice_classify_error")]
    public string? NextSliceClassifyError { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

internal static class TaskingHandoffConstants
{
    public const string LocalOnlyStatus = "local_only";
}
