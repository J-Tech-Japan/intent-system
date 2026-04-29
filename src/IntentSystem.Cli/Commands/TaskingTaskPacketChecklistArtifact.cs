using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G193 worker-task acceptance checklist artifact. Snake_case JSON shape that
/// <c>intent-cli tasking task-packet-checklist</c> writes by consuming a G192
/// <see cref="TaskingTaskPacketPreviewArtifact"/>. The checklist is explicitly
/// local-only: it never publishes a GitHub issue, applies labels, mutates
/// queue/runs state, or launches provider processes.
///
/// <see cref="IsPublished"/> and <see cref="IsAutomationVisible"/> are ALWAYS
/// literal <c>false</c>; <see cref="ChecklistStatus"/> is ALWAYS the literal
/// value <see cref="TaskingTaskPacketChecklistConstants.LocalOnlyStatus"/>.
/// </summary>
internal sealed record TaskingTaskPacketChecklistArtifact
{
    [JsonPropertyName("source_preview_path")]
    public required string SourcePreviewPath { get; init; }

    [JsonPropertyName("source_preview_sha256")]
    public required string SourcePreviewSha256 { get; init; }

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

    [JsonPropertyName("checklist_status")]
    public required string ChecklistStatus { get; init; }

    [JsonPropertyName("ready_for_handoff")]
    public required bool ReadyForHandoff { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<ChecklistCheck> Checks { get; init; }

    [JsonPropertyName("recommended_worker_action")]
    public required string RecommendedWorkerAction { get; init; }

    [JsonPropertyName("rendered_preview_excerpt")]
    public string? RenderedPreviewExcerpt { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// Single check entry in the G193 checklist. <see cref="Id"/> is a stable
/// constant string from <see cref="TaskingTaskPacketChecklistConstants.CheckIds"/>
/// — tests grep on these without relying on ordering.
/// </summary>
internal sealed record ChecklistCheck
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}

internal static class TaskingTaskPacketChecklistConstants
{
    public const string LocalOnlyStatus = "local_only";

    /// <summary>
    /// Stable check ids. Tests assert each constant appears exactly once in
    /// the produced checklist so the check set cannot drift silently.
    /// </summary>
    public static class CheckIds
    {
        public const string PreviewStatusLocalOnly = "preview_status_local_only";
        public const string PreviewIsPublishedFalse = "preview_is_published_false";
        public const string PreviewIsAutomationVisibleFalse = "preview_is_automation_visible_false";
        public const string SourceTaskPacketPathPresent = "source_task_packet_path_present";
        public const string SourceTaskPacketSha256Present = "source_task_packet_sha256_present";
        public const string SourceHandoffPathPresent = "source_handoff_path_present";
        public const string SourceHandoffSha256Present = "source_handoff_sha256_present";
        public const string RenderedPreviewMarkdownNonEmpty = "rendered_preview_markdown_non_empty";
        public const string RenderedPreviewMarksUnpublished = "rendered_preview_marks_unpublished";
        public const string RecommendedWorkerActionNonEmpty = "recommended_worker_action_non_empty";

        public static IReadOnlyList<string> All { get; } =
        [
            PreviewStatusLocalOnly,
            PreviewIsPublishedFalse,
            PreviewIsAutomationVisibleFalse,
            SourceTaskPacketPathPresent,
            SourceTaskPacketSha256Present,
            SourceHandoffPathPresent,
            SourceHandoffSha256Present,
            RenderedPreviewMarkdownNonEmpty,
            RenderedPreviewMarksUnpublished,
            RecommendedWorkerActionNonEmpty
        ];
    }
}
