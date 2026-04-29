using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G201 AI-thread session summary attachment artifact. Snake_case JSON shape
/// that <c>intent-cli tasking ai-thread-summary-attach</c> writes when an
/// operator binds a manually-authored AI-thread session summary file to one of
/// the existing local tasking chain artifacts (G190 handoff packet, G191 task
/// packet, G192 preview, G193 checklist, or G194 handoff bundle).
///
/// The attachment is explicitly local-only and provenance-only: it never
/// publishes a GitHub issue, applies labels, mutates queue/runs state, launches
/// provider processes, or copies the actual summary text into the artifact.
///
/// <see cref="IsPublished"/> and <see cref="IsAutomationVisible"/> are ALWAYS
/// literal <c>false</c>; <see cref="AttachmentStatus"/> is ALWAYS the literal
/// value <see cref="TaskingAiThreadSummaryAttachConstants.LocalOnlyStatus"/>.
/// </summary>
internal sealed record TaskingAiThreadSummaryAttachArtifact
{
    [JsonPropertyName("source_artifact_path")]
    public required string SourceArtifactPath { get; init; }

    [JsonPropertyName("source_artifact_sha256")]
    public required string SourceArtifactSha256 { get; init; }

    [JsonPropertyName("source_artifact_kind")]
    public required string SourceArtifactKind { get; init; }

    [JsonPropertyName("source_summary_path")]
    public required string SourceSummaryPath { get; init; }

    [JsonPropertyName("source_summary_sha256")]
    public required string SourceSummarySha256 { get; init; }

    [JsonPropertyName("source_summary_byte_count")]
    public required int SourceSummaryByteCount { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("is_published")]
    public required bool IsPublished { get; init; }

    [JsonPropertyName("is_automation_visible")]
    public required bool IsAutomationVisible { get; init; }

    [JsonPropertyName("attachment_status")]
    public required string AttachmentStatus { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

internal static class TaskingAiThreadSummaryAttachConstants
{
    public const string LocalOnlyStatus = "local_only";

    /// <summary>
    /// Stable kind tags that record which chain artifact deserialized
    /// successfully when the source artifact was inspected. Locked here so
    /// downstream consumers and tests can match exact strings.
    /// </summary>
    public static class SourceArtifactKinds
    {
        public const string HandoffBundle = "handoff_bundle";
        public const string HandoffPacket = "handoff_packet";
        public const string TaskPacket = "task_packet";
        public const string TaskPacketPreview = "task_packet_preview";
        public const string TaskPacketChecklist = "task_packet_checklist";
    }
}
