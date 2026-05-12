using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G206: Read-only result emitted by <c>intent-cli worker next-action</c>.
/// Names a single coding-automation target (or <c>none</c>) for a coding
/// loop to act on next, plus a recommended workflow hint and any policy
/// warnings.
///
/// Field names mirror the issue contract exactly (camelCase for
/// <c>recommendedWorkflow</c> / <c>sourceClassification</c>) and add
/// snake_case aliases for local style consistency, mirroring the
/// G202/G203/G204/G205 alias pattern. The snake_case form is the canonical
/// source; the camelCase form is the contract surface.
///
/// Producing this record never mutates GitHub, never applies labels, never
/// creates branches/PRs/comments, never touches the queue/runs logs, and
/// never launches any AI provider.
/// </summary>
internal sealed record WorkerNextActionResult
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("number")]
    public int? Number { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("recommended_workflow")]
    public string? RecommendedWorkflow { get; init; }

    /// <summary>G206 issue contract alias — camelCase per #517.</summary>
    [JsonPropertyName("recommendedWorkflow")]
    public string? RecommendedWorkflowCamelCase => RecommendedWorkflow;

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("source_classification")]
    public string? SourceClassification { get; init; }

    /// <summary>G206 issue contract alias — camelCase per #517.</summary>
    [JsonPropertyName("sourceClassification")]
    public string? SourceClassificationCamelCase => SourceClassification;

    /// <summary>
    /// G333: true when the caller passed <c>--github-only</c>. The
    /// underlying selection is already GitHub-label-based (no
    /// queue-state read), so the flag is an explicit binding the
    /// host loop can audit. Null on host/review-runtime invocations
    /// that do not assert the strict child-loop contract.
    /// </summary>
    [JsonPropertyName("github_only")]
    public bool? GithubOnly { get; init; }

    [JsonPropertyName("githubOnly")]
    public bool? GithubOnlyCamel => GithubOnly;
}

/// <summary>
/// G206: Stable string constants for the actions, workflows, and source
/// classifications emitted by <c>intent-cli worker next-action</c>.
/// </summary>
internal static class WorkerNextActionConstants
{
    public static class Actions
    {
        public const string PrCommentFix = "pr-comment-fix";
        public const string IssueToPr = "issue-to-pr";

        /// <summary>
        /// G325: emitted when an existing PR update lease is still active
        /// (<c>intent-pr-update-in-progress</c> present). The selector
        /// MUST NOT return <c>none</c> in this case because the system
        /// is not idle — another worker is in flight. The
        /// <see cref="WorkerNextActionResult.Number"/> /
        /// <see cref="WorkerNextActionResult.Url"/> fields point at the
        /// PR holding the lease so an operator or controller knows
        /// which lease to monitor or — with explicit authorization —
        /// release.
        /// </summary>
        public const string Wait = "wait";

        public const string None = "none";
    }

    public static class RecommendedWorkflows
    {
        public const string PrCommentFix = "pr-comment-fix";
        public const string GhIssueToPr = "gh-issue-to-pr";
    }

    public static class SourceClassifications
    {
        public const string RepairRequired = "repair-required";
        public const string ReadyToImplement = "ready-to-implement";

        /// <summary>
        /// G325: emitted alongside <see cref="Actions.Wait"/> when an
        /// active PR update lease (<c>intent-pr-update-in-progress</c>)
        /// is currently held. Surfaced explicitly so operators can
        /// distinguish "system is idle" from "another worker is
        /// repairing this PR" — and so stale-lease recovery is an
        /// explicit operator/intent-cli action rather than a silent
        /// fallback.
        /// </summary>
        public const string PrUpdateInProgress = "pr-update-in-progress";
    }

    public static class Labels
    {
        public const string IntentTarget = "intent-target";
        public const string IntentPrRequestUpdate = "intent-pr-request-update";
        public const string IntentPrUpdateInProgress = "intent-pr-update-in-progress";
        public const string IntentPrRereviewReady = "intent-pr-rereview-ready";
        public const string IntentPrApproved = "intent-pr-approved";
        public const string IntentIssueInProgress = "intent-issue-in-progress";
        public const string IntentPrCreated = "intent-pr-created";
        public const string IntentPrReviewing = "intent-pr-reviewing";
    }
}
