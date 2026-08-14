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

    /// <summary>Non-fatal warnings — e.g. a safe-repair lane that failed to
    /// execute and therefore claimed no mutation (fail-closed).</summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>G695: durable completion-signal evidence observed by this wake.</summary>
    [JsonPropertyName("continuation_chain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContinuationChainRecord? ContinuationChain { get; init; }

    /// <summary>True when this command executed a safe mutation lane through an
    /// existing surface (the deterministic host-metadata repair lane under
    /// <c>--write</c>). Judgement-gated (review approval) and multi-step (publish
    /// chain) lanes stay fail-closed: <c>false</c>, with the single safe command
    /// surfaced in <see cref="PendingCommand"/>.</summary>
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

    /// <summary>G673: preserve the named GitHub availability state from the
    /// delegated host-loop-next-action surface.</summary>
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
