using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G189 candidate issue spec planner artifact. Snake_case JSON shape that
/// <c>intent-cli issue plan-candidate</c> writes.
///
/// The planner is explicitly local-only and never publishes a GitHub issue,
/// applies labels, or mutates queue/runs state. <see cref="IsPublished"/>,
/// <see cref="IsAutomationVisible"/> are ALWAYS literal <c>false</c> and
/// <see cref="CandidateSpecStatus"/> is ALWAYS the literal value
/// <see cref="IssuePlanCandidateConstants.UnpublishedCandidateStatus"/>. These
/// are contract fields so future tooling can observe them and never confuse a
/// candidate spec with a real published issue.
/// </summary>
internal sealed record IssuePlanCandidateArtifact
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("is_published")]
    public required bool IsPublished { get; init; }

    [JsonPropertyName("is_automation_visible")]
    public required bool IsAutomationVisible { get; init; }

    [JsonPropertyName("candidate_spec_status")]
    public required string CandidateSpecStatus { get; init; }

    [JsonPropertyName("context_paths")]
    public required IReadOnlyList<CandidateSourceReference> ContextPaths { get; init; }

    [JsonPropertyName("packet_path")]
    public CandidateSourceReference? PacketPath { get; init; }

    [JsonPropertyName("review_context_path")]
    public CandidateSourceReference? ReviewContextPath { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// Single source-reference record reused for every <c>*_path</c> field on the
/// G189 candidate planner artifact. <see cref="Path"/> is captured as-passed
/// (relative-to-RepoRoot when the input is relative). <see cref="SizeBytes"/>
/// and <see cref="Sha256"/> are <c>null</c> when the file does not exist or is
/// not readable.
/// </summary>
internal sealed record CandidateSourceReference
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("exists")]
    public required bool Exists { get; init; }

    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }
}

internal static class IssuePlanCandidateConstants
{
    public const string UnpublishedCandidateStatus = "unpublished_candidate";
}
