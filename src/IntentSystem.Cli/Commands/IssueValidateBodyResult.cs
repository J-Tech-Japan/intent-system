using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Result of validating a standalone Markdown child issue body against the
/// host-review-loop Child Issue Contract (G183). Field names are stable
/// snake_case for JSON ingestion.
/// </summary>
internal sealed record IssueValidateBodyResult
{
    [JsonPropertyName("source_path")]
    public required string SourcePath { get; init; }

    [JsonPropertyName("is_valid")]
    public required bool IsValid { get; init; }

    [JsonPropertyName("body_bytes")]
    public int BodyBytes { get; init; }

    [JsonPropertyName("body_too_large")]
    public bool BodyTooLarge { get; init; }

    [JsonPropertyName("body_size_warning")]
    public bool BodySizeWarning { get; init; }

    [JsonPropertyName("body_size_reason")]
    public string? BodySizeReason { get; init; }

    [JsonPropertyName("missing_headings")]
    public required IReadOnlyList<string> MissingHeadings { get; init; }

    [JsonPropertyName("related_links_invalid")]
    public required bool RelatedLinksInvalid { get; init; }

    [JsonPropertyName("related_links_reason")]
    public string? RelatedLinksReason { get; init; }

    [JsonPropertyName("target_paths_invalid")]
    public required bool TargetPathsInvalid { get; init; }

    [JsonPropertyName("target_paths_reason")]
    public string? TargetPathsReason { get; init; }
}
