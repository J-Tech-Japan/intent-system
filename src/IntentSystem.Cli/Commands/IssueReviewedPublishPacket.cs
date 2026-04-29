using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G184 reviewed publish packet. Snake_case JSON shape that
/// <c>intent-cli issue prepare</c> writes and
/// <c>intent-cli issue publish-reviewed</c> consumes.
/// </summary>
internal sealed record IssueReviewedPublishPacket
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("source_path")]
    public required string SourcePath { get; init; }

    [JsonPropertyName("source_body_sha256")]
    public required string SourceBodySha256 { get; init; }

    [JsonPropertyName("validation_status")]
    public required string ValidationStatus { get; init; }

    [JsonPropertyName("reviewed")]
    public required bool Reviewed { get; init; }

    [JsonPropertyName("published")]
    public required bool Published { get; init; }

    [JsonPropertyName("issue_number")]
    public int? IssueNumber { get; init; }

    [JsonPropertyName("issue_url")]
    public string? IssueUrl { get; init; }

    [JsonPropertyName("prepared_at_utc")]
    public required string PreparedAtUtc { get; init; }

    [JsonPropertyName("published_at_utc")]
    public string? PublishedAtUtc { get; init; }
}
