using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G280: Read-only result emitted by
/// <c>intent-cli automation host-review-diagnostics</c>. Differentiates true
/// idle from stale-CLI, stuck review label, missing target, conflicting
/// review-side labels, WIP-cap blockage, and clarification-required so an
/// operator running the host loop can tell why no host action advanced.
/// Producing this record never mutates GitHub, never applies labels, never
/// touches durable parent state, and never launches an AI provider.
/// </summary>
internal sealed record AutomationHostReviewDiagnosticsResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("read_only")]
    public required bool ReadOnly { get; init; }

    [JsonPropertyName("readOnly")]
    public bool ReadOnlyCamel => ReadOnly;

    [JsonPropertyName("recommended_next_command")]
    public required string? RecommendedNextCommand { get; init; }

    [JsonPropertyName("recommendedNextCommand")]
    public string? RecommendedNextCommandCamel => RecommendedNextCommand;

    [JsonPropertyName("structured_clarification")]
    public required AutomationHostReviewDiagnosticsClarification? StructuredClarification { get; init; }

    [JsonPropertyName("structuredClarification")]
    public AutomationHostReviewDiagnosticsClarification? StructuredClarificationCamel => StructuredClarification;

    [JsonPropertyName("details")]
    public required IReadOnlyList<AutomationHostReviewDiagnosticsDetail> Details { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }
}

internal sealed record AutomationHostReviewDiagnosticsDetail
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("target_kind")]
    public required string? TargetKind { get; init; }

    [JsonPropertyName("targetKind")]
    public string? TargetKindCamel => TargetKind;

    [JsonPropertyName("target_number")]
    public required int? TargetNumber { get; init; }

    [JsonPropertyName("targetNumber")]
    public int? TargetNumberCamel => TargetNumber;

    [JsonPropertyName("target_url")]
    public required string? TargetUrl { get; init; }

    [JsonPropertyName("targetUrl")]
    public string? TargetUrlCamel => TargetUrl;

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

internal sealed record AutomationHostReviewDiagnosticsClarification
{
    [JsonPropertyName("background")]
    public required string Background { get; init; }

    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("options")]
    public required IReadOnlyList<string> Options { get; init; }
}

internal static class AutomationHostReviewDiagnosticsClassifications
{
    public const string TrueIdle = "true-idle";
    public const string StuckReviewing = "stuck-reviewing";
    public const string MissingTargetOnPr = "missing-target-on-pr";
    public const string RequestUpdateRereviewConflict = "request-update-rereview-conflict";
    public const string WipCapBlocked = "wip-cap-blocked";
    public const string ClarificationRequired = "clarification-required";
    public const string StaleHostCli = "stale-host-cli";
    public const string ReviewPrActionable = "review-pr-actionable";
    public const string CandidateReady = "candidate-ready";
}
