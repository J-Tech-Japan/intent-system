using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G203: Read-only result emitted by <c>intent-cli worker pr-review-preflight</c>.
/// Field names are stable snake_case for AI-thread JSON ingestion. CamelCase
/// alias properties echo the issue contract verbatim alongside the snake_case
/// primaries. Round-trippable via <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
internal sealed record WorkerPrReviewPreflightResult
{
    [JsonPropertyName("actionable")]
    public required bool Actionable { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("pr_state")]
    public required string PrState { get; init; }

    [JsonPropertyName("is_draft")]
    public required bool IsDraft { get; init; }

    [JsonPropertyName("labels")]
    public required IReadOnlyList<string> Labels { get; init; }

    [JsonPropertyName("source_issue")]
    public required int? SourceIssue { get; init; }

    /// <summary>
    /// G203 issue contract alias. Issue #511's minimum-JSON example names this
    /// field <c>sourceIssue</c> (camelCase). The primary property keeps
    /// snake_case for local style consistency; this read-only alias property
    /// emits the camelCase key alongside it so the issue contract holds
    /// verbatim. On deserialize the alias is ignored — the snake_case
    /// property is the canonical source — keeping round-trip stable.
    /// </summary>
    [JsonPropertyName("sourceIssue")]
    public int? SourceIssueCamelCase => SourceIssue;

    [JsonPropertyName("source_issue_labels")]
    public required IReadOnlyList<string>? SourceIssueLabels { get; init; }

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<string> Reasons { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }

    /// <summary>
    /// G203 issue contract alias. Issue #511's minimum-JSON example names this
    /// field <c>recommendedAction</c> (camelCase). The primary property keeps
    /// snake_case; this read-only alias emits the camelCase key alongside.
    /// </summary>
    [JsonPropertyName("recommendedAction")]
    public string RecommendedActionCamelCase => RecommendedAction;

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// G203: Stable string constants for the ten classification states emitted by
/// <c>intent-cli worker pr-review-preflight</c>. Order matches the
/// first-match-wins precedence documented in the issue body.
/// </summary>
internal static class WorkerPrReviewPreflightConstants
{
    public static class Classifications
    {
        public const string ReadyToReview = "ready-to-review";
        public const string AlreadyReviewing = "already-reviewing";
        public const string RequestUpdatePending = "request-update-pending";
        public const string UpdateInProgress = "update-in-progress";
        public const string ApprovedOrMerged = "approved-or-merged";
        public const string MissingTargetLabel = "missing-target-label";
        public const string SourceIssueMissing = "source-issue-missing";
        public const string SourceIssueNotTarget = "source-issue-not-target";
        public const string TargetMismatch = "target-mismatch";
        public const string NonActionable = "non-actionable";
    }

    public static class RecommendedActions
    {
        public const string Review = "review";
        public const string NoAction = "no-action";
        public const string WaitForWorkerUpdate = "wait-for-worker-update";
        public const string DeclineWithSummary = "decline-with-summary";
        public const string LabelCleanupRequired = "label-cleanup-required";
        public const string SwitchRepo = "switch-repo";
    }

    public static class Labels
    {
        public const string IntentTarget = "intent-target";
        public const string IntentPrCreated = "intent-pr-created";
        public const string IntentPrReviewing = "intent-pr-reviewing";
        public const string IntentPrRequestUpdate = "intent-pr-request-update";
        public const string IntentPrUpdateInProgress = "intent-pr-update-in-progress";
        public const string IntentPrApproved = "intent-pr-approved";
    }
}
