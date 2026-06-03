using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G450: result of <c>intent-cli automation host-loop-wake</c> — a single
/// safe-wake orchestration surface that collapses the host loop's ordered
/// preflight / sync / review / closeout / publish / diagnostics choreography
/// into one structured verdict. It reuses the existing
/// <see cref="HostLoopNextActionAnalyzer"/> decision (and all its read-only
/// probes) plus the installed-CLI surface probe, and maps the result into one
/// of four agent-facing wake actions: <c>true-idle</c>, <c>review</c>,
/// <c>publish</c>, or <c>blocker</c>.
///
/// Read-only by default. <c>--write</c> is fail-closed: this surface never
/// performs destructive git operations, never auto-approves review, and never
/// bypasses the existing label-transition / closeout / publish commands. The
/// single safe next command is surfaced in <see cref="RecommendedCommand"/>
/// / <see cref="PendingCommand"/> for the host to run through the existing
/// detailed surfaces, which remain available and compatible.
/// </summary>
internal sealed record AutomationHostLoopWakeResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("host_only")]
    public required bool HostOnly { get; init; }

    [JsonPropertyName("hostOnly")]
    public bool HostOnlyCamel => HostOnly;

    /// <summary>One of <see cref="AutomationHostLoopWakeActions"/>.</summary>
    [JsonPropertyName("wake_action")]
    public required string WakeAction { get; init; }

    [JsonPropertyName("wakeAction")]
    public string WakeActionCamel => WakeAction;

    /// <summary>The underlying host-loop-next-action classification (preserved
    /// for parity with the detailed command).</summary>
    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("mutation_allowed")]
    public required bool MutationAllowed { get; init; }

    [JsonPropertyName("mutationAllowed")]
    public bool MutationAllowedCamel => MutationAllowed;

    [JsonPropertyName("recommended_command")]
    public string? RecommendedCommand { get; init; }

    [JsonPropertyName("recommendedCommand")]
    public string? RecommendedCommandCamel => RecommendedCommand;

    [JsonPropertyName("candidate_execution_unit")]
    public string? CandidateExecutionUnit { get; init; }

    [JsonPropertyName("candidateExecutionUnit")]
    public string? CandidateExecutionUnitCamel => CandidateExecutionUnit;

    /// <summary>Invariant: at most ONE PR review/closeout per wake.</summary>
    [JsonPropertyName("processed_pr_count")]
    public required int ProcessedPrCount { get; init; }

    [JsonPropertyName("processedPrCount")]
    public int ProcessedPrCountCamel => ProcessedPrCount;

    /// <summary>Invariant: at most ONE issue publish per wake.</summary>
    [JsonPropertyName("processed_issue_publish_count")]
    public required int ProcessedIssuePublishCount { get; init; }

    [JsonPropertyName("processedIssuePublishCount")]
    public int ProcessedIssuePublishCountCamel => ProcessedIssuePublishCount;

    /// <summary>Mutations actually applied by this command (empty unless a
    /// future slice wires per-lane auto-execution; always empty in read-only
    /// mode).</summary>
    [JsonPropertyName("mutations")]
    public required IReadOnlyList<string> Mutations { get; init; }

    /// <summary>Read-only validations performed (e.g. installed-CLI surface).</summary>
    [JsonPropertyName("validations")]
    public required IReadOnlyList<string> Validations { get; init; }

    /// <summary>True only when this command itself applied a mutation. Fail-closed:
    /// false in this slice — mutation lanes are delegated to the existing
    /// command surfaced in <see cref="PendingCommand"/>.</summary>
    [JsonPropertyName("write_executed")]
    public required bool WriteExecuted { get; init; }

    [JsonPropertyName("writeExecuted")]
    public bool WriteExecutedCamel => WriteExecuted;

    /// <summary>When a mutation lane is active under <c>--write</c>, the single
    /// safe command the host must run through the existing surface.</summary>
    [JsonPropertyName("pending_command")]
    public string? PendingCommand { get; init; }

    [JsonPropertyName("pendingCommand")]
    public string? PendingCommandCamel => PendingCommand;

    [JsonPropertyName("stop_reason")]
    public required string StopReason { get; init; }

    [JsonPropertyName("stopReason")]
    public string StopReasonCamel => StopReason;

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<string> Evidence { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal static class AutomationHostLoopWakeModes
{
    public const string ReadOnly = "read-only";
    public const string Write = "write";
}

internal static class AutomationHostLoopWakeActions
{
    public const string TrueIdle = "true-idle";
    public const string Review = "review";
    public const string Publish = "publish";
    public const string Blocker = "blocker";
}
