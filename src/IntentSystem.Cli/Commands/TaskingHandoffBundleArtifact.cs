using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G194 worker handoff bundle artifact. Snake_case JSON shape that
/// <c>intent-cli tasking handoff-bundle</c> writes by consuming a G192
/// <see cref="TaskingTaskPacketPreviewArtifact"/> preview and a G193
/// <see cref="TaskingTaskPacketChecklistArtifact"/> checklist together. The
/// bundle is explicitly local-only: it never publishes a GitHub issue, applies
/// labels, mutates queue/runs state, or launches provider processes.
///
/// <see cref="IsPublished"/> and <see cref="IsAutomationVisible"/> are ALWAYS
/// literal <c>false</c>; <see cref="BundleStatus"/> is ALWAYS the literal value
/// <see cref="TaskingHandoffBundleConstants.LocalOnlyStatus"/>.
/// </summary>
internal sealed record TaskingHandoffBundleArtifact
{
    [JsonPropertyName("source_preview_path")]
    public required string SourcePreviewPath { get; init; }

    [JsonPropertyName("source_preview_sha256")]
    public required string SourcePreviewSha256 { get; init; }

    [JsonPropertyName("source_checklist_path")]
    public required string SourceChecklistPath { get; init; }

    [JsonPropertyName("source_checklist_sha256")]
    public required string SourceChecklistSha256 { get; init; }

    [JsonPropertyName("source_task_packet_path")]
    public required string SourceTaskPacketPath { get; init; }

    [JsonPropertyName("source_task_packet_sha256")]
    public required string SourceTaskPacketSha256 { get; init; }

    [JsonPropertyName("source_handoff_path")]
    public required string SourceHandoffPath { get; init; }

    [JsonPropertyName("source_handoff_sha256")]
    public required string SourceHandoffSha256 { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("is_published")]
    public required bool IsPublished { get; init; }

    [JsonPropertyName("is_automation_visible")]
    public required bool IsAutomationVisible { get; init; }

    [JsonPropertyName("bundle_status")]
    public required string BundleStatus { get; init; }

    [JsonPropertyName("checklist_ready_for_handoff")]
    public required bool ChecklistReadyForHandoff { get; init; }

    [JsonPropertyName("checklist_passed_check_ids")]
    public required IReadOnlyList<string> ChecklistPassedCheckIds { get; init; }

    [JsonPropertyName("checklist_failed_check_ids")]
    public required IReadOnlyList<string> ChecklistFailedCheckIds { get; init; }

    [JsonPropertyName("rendered_preview_excerpt")]
    public string? RenderedPreviewExcerpt { get; init; }

    [JsonPropertyName("recommended_worker_action")]
    public required string RecommendedWorkerAction { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

internal static class TaskingHandoffBundleConstants
{
    public const string LocalOnlyStatus = "local_only";
}
