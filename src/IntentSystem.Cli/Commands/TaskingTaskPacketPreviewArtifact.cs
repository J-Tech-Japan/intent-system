using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G192 worker-task preview artifact. Snake_case JSON shape that
/// <c>intent-cli tasking task-packet-preview</c> writes by consuming a G191
/// <see cref="TaskingTaskPacketArtifact"/>. The preview is explicitly
/// local-only: it never publishes a GitHub issue, applies labels, mutates
/// queue/runs state, or launches provider processes.
///
/// <see cref="IsPublished"/> and <see cref="IsAutomationVisible"/> are ALWAYS
/// literal <c>false</c>; <see cref="PreviewStatus"/> is ALWAYS the literal
/// value <see cref="TaskingTaskPacketPreviewConstants.LocalOnlyStatus"/>.
/// </summary>
internal sealed record TaskingTaskPacketPreviewArtifact
{
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

    [JsonPropertyName("preview_status")]
    public required string PreviewStatus { get; init; }

    [JsonPropertyName("embedded_status_brief")]
    public StatusBriefSummary? EmbeddedStatusBrief { get; init; }

    [JsonPropertyName("embedded_context_collect")]
    public ContextCollectPacket? EmbeddedContextCollect { get; init; }

    [JsonPropertyName("embedded_next_slice_classify")]
    public NextSliceClassifyResult? EmbeddedNextSliceClassify { get; init; }

    [JsonPropertyName("recommended_worker_action")]
    public required string RecommendedWorkerAction { get; init; }

    [JsonPropertyName("rendered_preview_markdown")]
    public required string RenderedPreviewMarkdown { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

internal static class TaskingTaskPacketPreviewConstants
{
    public const string LocalOnlyStatus = "local_only";
}
