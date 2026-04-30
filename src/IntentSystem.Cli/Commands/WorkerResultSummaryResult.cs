using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G205: Read-only result emitted by <c>intent-cli worker result-summary</c>.
/// Normalizes a worker run's outcome (issue-to-PR, PR comment fix) into a
/// machine-readable summary that automation loops can log and act on.
///
/// Field names are stable snake_case for AI-thread JSON ingestion. The
/// camelCase aliases (<c>recommendedLabelActions</c>) hold the issue
/// contract's exact key alongside the snake_case primary, mirroring the
/// G202 / G203 / G204 alias pattern.
///
/// This record is pure data: producing it never mutates GitHub, never
/// applies labels, never creates branches/PRs/comments, never touches the
/// queue/runs logs, and never launches any AI provider.
/// </summary>
internal sealed record WorkerResultSummaryResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("issue")]
    public int? Issue { get; init; }

    [JsonPropertyName("pr")]
    public int? Pr { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("recommended_label_actions")]
    public required IReadOnlyList<WorkerResultSummaryLabelAction> RecommendedLabelActions { get; init; }

    /// <summary>
    /// G205 issue contract alias. Issue #515's JSON shape names this field
    /// <c>recommendedLabelActions</c> (camelCase). The snake_case primary
    /// stays the canonical source; this read-only alias emits the camelCase
    /// key alongside it so the contract holds verbatim and round-trip stays
    /// stable (alias is ignored on deserialize).
    /// </summary>
    [JsonPropertyName("recommendedLabelActions")]
    public IReadOnlyList<WorkerResultSummaryLabelAction> RecommendedLabelActionsCamelCase
        => RecommendedLabelActions;

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

/// <summary>
/// G205: Single advisory label action emitted in
/// <see cref="WorkerResultSummaryResult.RecommendedLabelActions"/>. Advisory
/// only — this slice does NOT mutate any GitHub label. The action shape
/// stays narrow on purpose: a verb (<c>add</c> / <c>remove</c>), a target
/// (<c>issue</c> / <c>pr</c>), and the label name. Automation that wants to
/// apply the action does so via its own gh CLI / label policy layer.
/// </summary>
internal sealed record WorkerResultSummaryLabelAction
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }
}

/// <summary>
/// G205: Stable string constants for the eight outcomes, two kinds, and
/// derived statuses emitted by <c>intent-cli worker result-summary</c>.
/// </summary>
internal static class WorkerResultSummaryConstants
{
    public static class Kinds
    {
        public const string IssueToPr = "issue-to-pr";
        public const string PrCommentFix = "pr-comment-fix";
    }

    public static class Outcomes
    {
        public const string PrCreated = "pr-created";
        public const string RepairPushed = "repair-pushed";
        public const string DeclinedContractIncomplete = "declined-contract-incomplete";
        public const string ClarificationRequired = "clarification-required";
        public const string AlreadyResolved = "already-resolved";
        public const string NoActionableComments = "no-actionable-comments";
        public const string Failed = "failed";
        public const string LabelCleanupRequired = "label-cleanup-required";
    }

    public static class Statuses
    {
        public const string Completed = "completed";
        public const string Declined = "declined";
        public const string ClarificationRequired = "clarification-required";
        public const string Failed = "failed";
        public const string LabelCleanupRequired = "label-cleanup-required";
    }

    public static class LabelActionVerbs
    {
        public const string Add = "add";
        public const string Remove = "remove";
        public const string Swap = "swap";
    }

    public static class LabelActionTargets
    {
        public const string Issue = "issue";
        public const string Pr = "pr";
    }

    public static class Labels
    {
        public const string IntentTarget = "intent-target";
        public const string IntentIssueInProgress = "intent-issue-in-progress";
        public const string IntentPrCreated = "intent-pr-created";
        public const string IntentPrUpdateInProgress = "intent-pr-update-in-progress";
        public const string IntentPrRereviewReady = "intent-pr-rereview-ready";
        public const string IntentPrRequestUpdate = "intent-pr-request-update";
    }

    public static IReadOnlyList<string> AllKinds { get; } = new[]
    {
        Kinds.IssueToPr,
        Kinds.PrCommentFix,
    };

    public static IReadOnlyList<string> AllOutcomes { get; } = new[]
    {
        Outcomes.PrCreated,
        Outcomes.RepairPushed,
        Outcomes.DeclinedContractIncomplete,
        Outcomes.ClarificationRequired,
        Outcomes.AlreadyResolved,
        Outcomes.NoActionableComments,
        Outcomes.Failed,
        Outcomes.LabelCleanupRequired,
    };
}
