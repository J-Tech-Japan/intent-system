using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G204: Read-only result emitted by <c>intent-cli worker pr-comment-preflight</c>.
/// Field names are stable snake_case for AI-thread JSON ingestion. CamelCase
/// alias properties echo the issue contract verbatim alongside the snake_case
/// primaries. Round-trippable via <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
internal sealed record WorkerPrCommentPreflightResult
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
    /// G204 issue contract alias. Issue #513's minimum-JSON example names this
    /// field <c>sourceIssue</c> (camelCase). The primary property keeps
    /// snake_case for local style consistency; this read-only alias property
    /// emits the camelCase key alongside it so the issue contract holds
    /// verbatim.
    /// </summary>
    [JsonPropertyName("sourceIssue")]
    public int? SourceIssueCamelCase => SourceIssue;

    [JsonPropertyName("source_issue_labels")]
    public required IReadOnlyList<string>? SourceIssueLabels { get; init; }

    [JsonPropertyName("actionable_comments")]
    public required IReadOnlyList<WorkerPrCommentPreflightActionableComment> ActionableComments { get; init; }

    /// <summary>
    /// G204 issue contract alias. Issue #513 lists this field as
    /// <c>actionableComments</c> (camelCase).
    /// </summary>
    [JsonPropertyName("actionableComments")]
    public IReadOnlyList<WorkerPrCommentPreflightActionableComment> ActionableCommentsCamelCase => ActionableComments;

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<string> Reasons { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }

    /// <summary>
    /// G204 issue contract alias. Issue #513 lists this field as
    /// <c>recommendedAction</c> (camelCase).
    /// </summary>
    [JsonPropertyName("recommendedAction")]
    public string RecommendedActionCamelCase => RecommendedAction;

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// G204: One actionable comment entry emitted by the analyzer. <c>kind</c> is
/// one of <c>review_thread</c>, <c>review</c>, or <c>issue_comment</c> and is
/// locked in <see cref="WorkerPrCommentPreflightConstants.CommentKinds"/>.
/// <c>excerpt</c> is the first 120 characters of the comment body so AI
/// consumers can disambiguate threads without fetching the full body.
/// </summary>
internal sealed record WorkerPrCommentPreflightActionableComment
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("author")]
    public required string Author { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("excerpt")]
    public required string Excerpt { get; init; }
}

/// <summary>
/// G204: Stable string constants for the ten classification states emitted by
/// <c>intent-cli worker pr-comment-preflight</c>. Order matches the
/// first-match-wins precedence documented in the issue body.
/// </summary>
internal static class WorkerPrCommentPreflightConstants
{
    public static class Classifications
    {
        public const string RepairRequired = "repair-required";
        public const string NoActionableComments = "no-actionable-comments";
        public const string RequestUpdatePending = "request-update-pending";
        public const string UpdateInProgress = "update-in-progress";
        public const string MissingTargetLabel = "missing-target-label";
        public const string SourceIssueMissing = "source-issue-missing";
        public const string SourceIssueNotTarget = "source-issue-not-target";
        public const string TargetMismatch = "target-mismatch";
        public const string ApprovedOrMerged = "approved-or-merged";
        public const string NonActionable = "non-actionable";
    }

    public static class RecommendedActions
    {
        public const string RepairPr = "repair-pr";
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
        public const string IntentPrRequestUpdate = "intent-pr-request-update";
        public const string IntentPrUpdateInProgress = "intent-pr-update-in-progress";
        public const string IntentPrApproved = "intent-pr-approved";
    }

    public static class CommentKinds
    {
        public const string ReviewThread = "review_thread";
        public const string Review = "review";
        public const string IssueComment = "issue_comment";
    }

    /// <summary>
    /// G204: Case-insensitive author allowlist. Comments authored by these
    /// identities are treated as non-actionable bot/automation chatter even
    /// when the body is otherwise non-empty. Documented here so the policy is
    /// reviewable in one place.
    /// </summary>
    public static readonly IReadOnlySet<string> NonActionableAuthors =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "github-actions",
            "dependabot",
            "claude"
        };

    /// <summary>
    /// G204: Auto-generated marker substrings (case-insensitive). A comment
    /// whose body contains any of these is treated as non-actionable so the
    /// repair loop does not chase its own update markers, AI generation
    /// banners, PR-description summary echoes, or already-addressed host
    /// review automation comments.
    ///
    /// G204 follow-up: the original entries only matched the heading-style
    /// worker update note (<c>"## Update — fix applied"</c>) and missed the
    /// inline form actually emitted by this loop (<c>Update — fix applied
    /// (commit ...)</c>). They also did not exclude the host's deterministic
    /// review/rereview comments that triggered <c>intent-pr-request-update</c>
    /// in the first place, so once draft gating no longer masked the comment
    /// pass the loop could classify already-addressed feedback as
    /// <c>repair-required</c> and chase its own history. Both classes of
    /// comment are now matched by stable openers below.
    /// </summary>
    public static readonly IReadOnlyList<string> AutoGeneratedMarkers =
        new[]
        {
            // Worker update / status notes posted by this loop after a fix.
            // Matches both the heading-style and inline forms.
            "Update — fix applied",

            // Host deterministic review-automation repair-request comments.
            // The host emits two stable variants for first-pass and rereview.
            "Deterministic review found",
            "Deterministic rereview found",

            // AI-generation banners and PR-description summary echoes.
            "🤖 Generated",
            "## Summary"
        };
}
