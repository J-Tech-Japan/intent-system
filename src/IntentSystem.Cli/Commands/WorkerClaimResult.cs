using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211: Read-only result emitted by <c>intent-cli worker claim</c>.
/// Names the proposed (or applied) label transition for a single
/// issue/PR target, plus the dry-run / write mode flag and any policy
/// warnings or stale-selection errors.
///
/// Producing this record never launches an AI provider, never creates
/// branches, PRs, issues, or comments, and never touches the queue or
/// runs logs. In dry-run mode (the default), the record also represents
/// the no-mutation guarantee — the claim transition is described, not
/// applied.
/// </summary>
internal sealed record WorkerClaimResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("proceed")]
    public required bool Proceed { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("addLabels")]
    public IReadOnlyList<string> AddLabelsCamelCase => AddLabels;

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("removeLabels")]
    public IReadOnlyList<string> RemoveLabelsCamelCase => RemoveLabels;

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    /// <summary>G211 contract alias — camelCase for AI-thread JSON ingestion.</summary>
    [JsonPropertyName("currentLabels")]
    public IReadOnlyList<string> CurrentLabelsCamelCase => CurrentLabels;

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

/// <summary>
/// G211: Stable string constants for the worker claim/complete commands.
/// </summary>
internal static class WorkerClaimCompleteConstants
{
    /// <summary>
    /// Mode tokens. Default is <c>dry-run</c> — the command must NOT
    /// mutate GitHub unless <c>write</c> is explicitly requested.
    /// </summary>
    public static class Modes
    {
        public const string DryRun = "dry-run";
        public const string Write = "write";
    }

    /// <summary>
    /// Stable error codes emitted in <c>errors[]</c> when the analyzer
    /// refuses a transition. Tests assert against these codes; downstream
    /// callers can branch on them without parsing the human-readable
    /// summary.
    /// </summary>
    public static class ErrorCodes
    {
        public const string MissingTarget = "claim.missing.intent-target";
        public const string AlreadyInProgress = "claim.stale.already-in-progress";
        public const string AlreadyCompleted = "claim.stale.already-completed";
        public const string MissingRepairRequested = "claim.missing.intent-pr-request-update";
        public const string AlreadyRereviewReady = "claim.stale.already-rereview-ready";
        public const string CompleteNotClaimed = "complete.stale.not-claimed";
        public const string CompleteAlreadyCompleted = "complete.stale.already-completed";
        public const string CompleteIntentPrCreatedOnPr = "complete.policy.intent-pr-created-on-pr";
        public const string UnsupportedKindOutcome = "complete.unsupported.kind-outcome";
    }
}
