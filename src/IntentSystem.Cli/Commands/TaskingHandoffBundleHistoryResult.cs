using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G200 history result record. Snake_case JSON shape that
/// <c>intent-cli tasking handoff-bundle-history</c> emits to STDOUT when
/// <c>--format json</c> is requested. The result is a deterministic, read-only
/// directory scan of zero or more local G194
/// <see cref="TaskingHandoffBundleArtifact"/> bundle JSON files: the history
/// command does NOT write any artifact file, never mutates inputs, and never
/// recurses into subdirectories.
///
/// <para>
/// <see cref="Entries"/> is sorted by <see cref="HistoryEntry.FileName"/>
/// (ordinal) so reviewers can rely on stable diff-friendly output. Each entry
/// has a <see cref="HistoryEntry.Classification"/> drawn from
/// <see cref="TaskingHandoffBundleHistoryConstants.Classifications"/>; for
/// non-<c>valid_bundle</c> entries the bundle-identity fields are <c>null</c>.
/// </para>
/// </summary>
internal sealed record TaskingHandoffBundleHistoryResult
{
    [JsonPropertyName("from_directory")]
    public required string FromDirectory { get; init; }

    [JsonPropertyName("entry_count")]
    public required int EntryCount { get; init; }

    [JsonPropertyName("valid_count")]
    public required int ValidCount { get; init; }

    [JsonPropertyName("invalid_count")]
    public required int InvalidCount { get; init; }

    [JsonPropertyName("malformed_count")]
    public required int MalformedCount { get; init; }

    [JsonPropertyName("unreadable_count")]
    public required int UnreadableCount { get; init; }

    [JsonPropertyName("entries")]
    public required IReadOnlyList<HistoryEntry> Entries { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// G200 single-entry record. One per top-level <c>*.json</c> file in the
/// scanned directory.
/// </summary>
internal sealed record HistoryEntry
{
    [JsonPropertyName("file_name")]
    public required string FileName { get; init; }

    [JsonPropertyName("absolute_path")]
    public required string AbsolutePath { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("domain")]
    public required string? Domain { get; init; }

    [JsonPropertyName("bundle_status")]
    public required string? BundleStatus { get; init; }

    [JsonPropertyName("is_published")]
    public required bool? IsPublished { get; init; }

    [JsonPropertyName("is_automation_visible")]
    public required bool? IsAutomationVisible { get; init; }

    [JsonPropertyName("source_task_packet_path")]
    public required string? SourceTaskPacketPath { get; init; }

    [JsonPropertyName("source_preview_path")]
    public required string? SourcePreviewPath { get; init; }

    [JsonPropertyName("source_checklist_path")]
    public required string? SourceChecklistPath { get; init; }

    [JsonPropertyName("source_handoff_path")]
    public required string? SourceHandoffPath { get; init; }

    [JsonPropertyName("checklist_ready_for_handoff")]
    public required bool? ChecklistReadyForHandoff { get; init; }

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }
}

/// <summary>
/// G200 stable classification constants. The history analyzer assigns exactly
/// one of these strings to every entry; tests assert against the literal
/// values so the contract cannot drift silently.
/// </summary>
internal static class TaskingHandoffBundleHistoryConstants
{
    public static class Classifications
    {
        public const string ValidBundle = "valid_bundle";
        public const string InvalidBundle = "invalid_bundle";
        public const string Malformed = "malformed";
        public const string Unreadable = "unreadable";
    }
}
