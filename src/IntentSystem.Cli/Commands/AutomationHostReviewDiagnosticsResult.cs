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

    /// <summary>
    /// G286: terminal class returned when a host wake should publish exactly one
    /// next-slice issue without further operator acceptance: WIP empty, no review
    /// PR actionable, no Hard Clarification, candidate contract complete.
    /// Distinct from <see cref="CandidateReady"/> historically (preserved for
    /// backward compatibility on existing callers); the analyzer now returns
    /// <c>IssuePublishReady</c> together with the deterministic publish chain
    /// in <c>recommended_next_command</c>.
    /// </summary>
    public const string IssuePublishReady = "issue-publish-ready";

    /// <summary>
    /// G286: terminal class returned when reconcile reports unsafe stops
    /// (e.g. <c>ambiguous-queue-linkage</c>) — the host loop must surface
    /// structured clarification rather than guess past ambiguous metadata.
    /// </summary>
    public const string UnsafeMetadata = "unsafe-metadata";

    /// <summary>
    /// G286: terminal class returned when reconcile has unapplied
    /// high-confidence repairs available and no review/WIP/clarification
    /// blocker is present. The host loop should re-run reconcile with
    /// <c>--write</c> and retry the wake.
    /// </summary>
    public const string RepairedAndRetry = "repaired-and-retry";

    /// <summary>
    /// G313: terminal class returned when <c>publish-recovery</c> reports
    /// at least one unapplied high-confidence repair targeting the
    /// selected review PR (publish-artifact-backed evidence converged on
    /// a single execution unit / source issue). The host loop should run
    /// <c>automation publish-recovery --write</c> first — before generic
    /// reconcile — and retry the wake. Distinct from
    /// <see cref="RepairedAndRetry"/> so the host-loop guidance can route
    /// publish-artifact-backed missing-linked_pr blockers to the
    /// publish-recovery lane as the primary recovery surface.
    /// </summary>
    public const string PublishRecoveryReady = "publish-recovery-ready";

    /// <summary>
    /// G297: terminal class returned when a selected review PR is still
    /// draft. Host approval / closeout / next-slice publish must be
    /// blocked because GitHub will reject the merge with "Pull Request is
    /// still a draft". The host loop should skip approval, run
    /// <c>pr-transition --transition review-release</c> to drop the
    /// review lease cleanly, and surface the gap to the operator or
    /// implementer rather than mutate parent durable state.
    /// </summary>
    public const string DraftMergeBlocked = "draft-merge-blocked";
}
