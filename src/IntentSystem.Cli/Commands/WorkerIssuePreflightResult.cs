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

    /// <summary>
    /// G717: when the claims store is configured, expose the ownership fact
    /// that was consulted so a label/claim disagreement is inspectable in the
    /// same preflight result. Legacy hosts omit these fields.
    /// </summary>
    [JsonPropertyName("claim_scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClaimScope { get; init; }

    [JsonPropertyName("claim_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClaimStatus { get; init; }

    [JsonPropertyName("claim_holder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClaimHolder { get; init; }

    [JsonPropertyName("claim_holder_team")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClaimHolderTeam { get; init; }

    [JsonPropertyName("claim_detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClaimDetail { get; init; }

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<string> Reasons { get; init; }

    [JsonPropertyName("advisories")]
    public required IReadOnlyList<string> Advisories { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }

    /// <summary>
    /// G202 issue contract alias. Issue #509's minimum-JSON example and
    /// Acceptance Criteria name this field <c>recommendedAction</c>
    /// (camelCase). The primary property keeps snake_case for local style
    /// consistency with every other tasking/worker artifact in this
    /// codebase; this read-only alias property emits the camelCase key
    /// alongside it so the issue contract holds verbatim.
    /// On deserialize the alias is ignored (read-only) — the snake_case
    /// property is the canonical source — keeping round-trip stable.
    /// </summary>
    [JsonPropertyName("recommendedAction")]
    public string RecommendedActionCamelCase => RecommendedAction;

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
        public const string MissingTargetDeclaration = "missing-target-declaration";
        public const string NonActionable = "non-actionable";

        /// <summary>
        /// G462: the issue carries <c>intent-target</c> but its declared target
        /// paths are exclusively host/design-owned (<c>intents/**</c>,
        /// <c>.intent-cli/**</c>). A GitHub-contract-only child loop cannot edit
        /// host metadata, so the issue is NOT actionable as a child
        /// implementation issue; it should be released from the child target
        /// (or retargeted to child-owned paths) on the host/design side. This is
        /// the G458 / issue #1018 regression class.
        /// </summary>
        public const string HostOnlyPacket = "host-only-packet";

        /// <summary>
        /// G717: the canonical claim could not be read, so preflight cannot
        /// safely decide whether a lifecycle label is a valid work marker.
        /// </summary>
        public const string ClaimUnavailable = "claim-unavailable";
    }

    public static class RecommendedActions
    {
        public const string Implement = "implement";
        public const string DeclineWithSummary = "decline-with-summary";
        public const string WaitForClarification = "wait-for-clarification";
        public const string SwitchRepo = "switch-repo";
        public const string NoAction = "no-action";

        /// <summary>
        /// G462: release the mistakenly-targeted host-only issue from the child
        /// target via <c>intent-cli automation issue-release --write</c> (never
        /// raw <c>gh</c> label mutation), or retarget it to child-owned paths on
        /// the host/design side.
        /// </summary>
        public const string ReleaseFromTarget = "release-from-target";
    }

    public static class Labels
    {
        public const string IntentTarget = "intent-target";
        public const string IntentIssueInProgress = "intent-issue-in-progress";
        public const string IntentPrCreated = "intent-pr-created";
    }
}
