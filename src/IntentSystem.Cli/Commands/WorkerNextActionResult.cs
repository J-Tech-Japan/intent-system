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

    /// <summary>
    /// G389: true for <c>issue-to-pr</c> — the terminal output must be a GitHub
    /// PR, not local commits. Null for actions where it does not apply.
    /// </summary>
    [JsonPropertyName("must_create_pr")]
    public bool? MustCreatePr { get; init; }

    /// <summary>G389: the outcome values a wake on this action may legitimately end in.</summary>
    [JsonPropertyName("allowed_terminal_outcomes")]
    public IReadOnlyList<string>? AllowedTerminalOutcomes { get; init; }

    /// <summary>G389: outcome states that are never a valid terminal result (e.g. local commits with no PR).</summary>
    [JsonPropertyName("forbidden_terminal_outcomes")]
    public IReadOnlyList<string>? ForbiddenTerminalOutcomes { get; init; }

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

    /// <summary>G673: upstream GitHub availability state.</summary>
    [JsonPropertyName("github_api_status")]
    public string GithubApiStatus { get; init; } = GitHubApiQuotaConstants.Healthy;

    [JsonPropertyName("degraded")]
    public bool Degraded { get; init; }

    [JsonPropertyName("cause")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }

    [JsonPropertyName("degraded_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GitHubApiDegradedState? DegradedState { get; init; }

    [JsonPropertyName("resource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Resource => DegradedState?.Resource;

    [JsonPropertyName("remaining")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Remaining => DegradedState?.Remaining;

    [JsonPropertyName("reset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Reset => DegradedState?.Reset;

    [JsonPropertyName("reset_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResetAt => DegradedState?.ResetAt;
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
        /// G673: GitHub consultation was unavailable. This is distinct from
        /// <c>none</c>, which means a successful empty/negative selection.
        /// </summary>
        public const string Unavailable = "unavailable";

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

    /// <summary>
    /// G389: explicit allowed / forbidden terminal outcomes per action, so an
    /// AI agent cannot finish a wake in an invalid state (e.g. local commits
    /// with no PR). Surfaced on <see cref="WorkerNextActionResult"/> alongside
    /// <see cref="WorkerNextActionResult.MustCreatePr"/>.
    /// </summary>
    public static class TerminalOutcomes
    {
        public static readonly IReadOnlyList<string> IssueToPrAllowed = new[]
        {
            "pr-created",
            "declined-contract-incomplete",
            "clarification-required",
            "already-resolved",
            "ambiguous-contract",
            "failed",
            "label-cleanup-required",
        };

        public static readonly IReadOnlyList<string> IssueToPrForbidden = new[]
        {
            "local-commits-only",
            "partial-work-no-pr",
            "lease-released-without-pr",
        };

        public static readonly IReadOnlyList<string> PrCommentFixAllowed = new[]
        {
            "repair-pushed",
            "no-actionable-comments",
            "already-resolved",
            "clarification-required",
            "host-artifact-repair-required",
            "failed",
            "label-cleanup-required",
        };

        public static readonly IReadOnlyList<string> PrCommentFixForbidden = new[]
        {
            "local-commits-only",
            "lease-released-without-push",
        };
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

        /// <summary>
        /// G392: emitted with <see cref="Actions.Wait"/> when a PR carries
        /// <c>intent-pr-request-update</c> but has no resolvable source issue
        /// (no closing reference / no <c>Closes/Fixes/Resolves #N</c>). The
        /// requested update is host-owned (missing source issue / broader
        /// dependency or contract gap), not a narrow child PR-branch repair, so
        /// the selector must not return it as claimable <c>pr-comment-fix</c> —
        /// matching what <c>worker pr-comment-preflight</c> would reject. This
        /// stops the child loop re-selecting the same non-actionable PR every
        /// wake (the AIC #3648 contradiction).
        /// </summary>
        public const string RequestUpdateNotChildActionable = "request-update-pending-not-child-actionable";

        /// <summary>
        /// G392: emitted with <see cref="Actions.Wait"/> when the label/closing-ref
        /// selector picked a request-update PR as a <c>pr-comment-fix</c>
        /// candidate but the shared <c>worker pr-comment-preflight</c> classifier
        /// — consulted on that one selected PR (it fetches the PR comments + the
        /// source issue the label selector cannot see) — reports
        /// <c>actionable:false</c> (e.g. no actionable review comments yet, or
        /// every actionable comment targets host metadata
        /// <c>.intent-cli/**</c>/<c>intents/**</c>). The two surfaces MUST agree
        /// on child-actionability, so the selector downgrades its own choice to a
        /// stable wait instead of handing the child loop a PR that preflight
        /// would refuse to claim. The downgrade reason names the underlying
        /// preflight classification so an operator can route it.
        /// </summary>
        public const string PrCommentPreflightNotActionable = "pr-comment-preflight-not-actionable";
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

        /// <summary>
        /// G545: issue-level canonical representation of the queue's
        /// <c>state=blocked</c> — applied/cleared only via <c>automation
        /// issue-block</c>, never a raw <c>gh</c> label edit. Coexists with
        /// <see cref="IntentIssueInProgress"/> (the worker still owns the
        /// issue; it just cannot currently proceed) rather than replacing it.
        /// </summary>
        public const string IntentIssueBlocked = "intent-issue-blocked";
    }
}
