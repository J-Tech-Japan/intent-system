using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G191 worker-task packet artifact. Snake_case JSON shape that
/// <c>intent-cli tasking task-packet</c> writes by consuming a G190
/// <see cref="TaskingHandoffPacket"/> handoff artifact. The packet is
/// explicitly local-only: it never publishes a GitHub issue, applies labels,
/// mutates queue/runs state, or launches provider processes.
///
/// <see cref="IsPublished"/> and <see cref="IsAutomationVisible"/> are ALWAYS
/// literal <c>false</c>; <see cref="TaskPacketStatus"/> is ALWAYS the literal
/// value <see cref="TaskingTaskPacketConstants.LocalOnlyStatus"/>. These are
/// contract fields so future tooling can observe them and never confuse a task
/// packet with a real published surface.
///
/// The embedded sub-packets are deep-copied from the source handoff so the
/// task packet stands alone — outer consumers do not need to re-read the
/// handoff to act on the packet.
/// </summary>
internal sealed record TaskingTaskPacketArtifact
{
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

    [JsonPropertyName("task_packet_status")]
    public required string TaskPacketStatus { get; init; }

    [JsonPropertyName("embedded_status_brief")]
    public StatusBriefSummary? EmbeddedStatusBrief { get; init; }

    [JsonPropertyName("embedded_context_collect")]
    public ContextCollectPacket? EmbeddedContextCollect { get; init; }

    [JsonPropertyName("embedded_next_slice_classify")]
    public NextSliceClassifyResult? EmbeddedNextSliceClassify { get; init; }

    [JsonPropertyName("recommended_worker_action")]
    public required string RecommendedWorkerAction { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

internal static class TaskingTaskPacketConstants
{
    public const string LocalOnlyStatus = "local_only";

    /// <summary>
    /// Stable fallback string when the source handoff has no
    /// <c>next_slice_classify</c> sub-packet (or it has no recommended action).
    /// Kept deterministic so JSON round-trip tests can assert against it.
    /// </summary>
    public const string FallbackRecommendedWorkerAction =
        "No deterministic next action available from handoff; revisit parent intents before tasking a worker.";
}
