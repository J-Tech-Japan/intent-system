using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G202: Read-only result emitted by <c>intent-cli worker issue-preflight</c>.
/// Field names are stable snake_case for AI-thread JSON ingestion and align
/// with the issue's Acceptance Criteria. Round-trippable via
/// <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
internal sealed record WorkerIssuePreflightResult
{
    [JsonPropertyName("actionable")]
    public required bool Actionable { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("issue_state")]
    public required string IssueState { get; init; }

    [JsonPropertyName("labels")]
    public required IReadOnlyList<string> Labels { get; init; }

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<string> Reasons { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }

    [JsonPropertyName("summary_line")]
    public required string SummaryLine { get; init; }
}

/// <summary>
/// G202: Stable string constants for the seven classification states emitted
/// by <c>intent-cli worker issue-preflight</c>. Order matches the
/// first-match-wins precedence documented in the issue body.
/// </summary>
internal static class WorkerIssuePreflightConstants
{
    public static class Classifications
    {
        public const string ReadyToImplement = "ready-to-implement";
        public const string AlreadyInProgress = "already-in-progress";
        public const string AlreadyPrCreated = "already-pr-created";
        public const string MissingTargetLabel = "missing-target-label";
        public const string ContractIncomplete = "contract-incomplete";
        public const string TargetMismatch = "target-mismatch";
        public const string NonActionable = "non-actionable";
    }

    public static class RecommendedActions
    {
        public const string Implement = "implement";
        public const string DeclineWithSummary = "decline-with-summary";
        public const string WaitForClarification = "wait-for-clarification";
        public const string SwitchRepo = "switch-repo";
        public const string NoAction = "no-action";
    }

    public static class Labels
    {
        public const string IntentTarget = "intent-target";
        public const string IntentIssueInProgress = "intent-issue-in-progress";
        public const string IntentPrCreated = "intent-pr-created";
    }
}
