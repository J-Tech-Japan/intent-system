using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G195 inspect summary record. Snake_case JSON shape that
/// <c>intent-cli tasking handoff-bundle-inspect</c> emits to STDOUT when
/// <c>--format json</c> is requested. The summary is a read-only projection of a
/// G194 <see cref="TaskingHandoffBundleArtifact"/> — the inspect command does NOT
/// write any artifact file. <see cref="IsPublished"/> and
/// <see cref="IsAutomationVisible"/> propagate verbatim from the source bundle
/// (which by G194 contract are always literal <c>false</c>);
/// <see cref="BundleStatus"/> propagates verbatim and is always
/// <c>"local_only"</c> for any bundle that passes the G195 status validation.
/// </summary>
internal sealed record TaskingHandoffBundleInspectSummary
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("bundle_status")]
    public required string BundleStatus { get; init; }

    [JsonPropertyName("is_published")]
    public required bool IsPublished { get; init; }

    [JsonPropertyName("is_automation_visible")]
    public required bool IsAutomationVisible { get; init; }

    [JsonPropertyName("checklist_ready_for_handoff")]
    public required bool ChecklistReadyForHandoff { get; init; }

    [JsonPropertyName("passed_check_count")]
    public required int PassedCheckCount { get; init; }

    [JsonPropertyName("failed_check_count")]
    public required int FailedCheckCount { get; init; }

    [JsonPropertyName("passed_check_ids")]
    public required IReadOnlyList<string> PassedCheckIds { get; init; }

    [JsonPropertyName("failed_check_ids")]
    public required IReadOnlyList<string> FailedCheckIds { get; init; }

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

    [JsonPropertyName("recommended_worker_action")]
    public required string RecommendedWorkerAction { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}
