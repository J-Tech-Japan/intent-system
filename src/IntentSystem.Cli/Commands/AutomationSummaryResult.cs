using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only automation interop summary produced by
/// <c>intent-cli automation summary</c> (G186). Field names are stable and
/// snake_case-serialized for AI-thread JSON ingestion. Round-trippable via
/// <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
internal sealed record AutomationSummaryResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("submodule_path")]
    public string? SubmodulePath { get; init; }

    [JsonPropertyName("queue_state_path")]
    public string? QueueStatePath { get; init; }

    [JsonPropertyName("runs_log_path")]
    public string? RunsLogPath { get; init; }

    [JsonPropertyName("packet_root")]
    public string? PacketRoot { get; init; }

    [JsonPropertyName("execution_unit_regex")]
    public string? ExecutionUnitRegex { get; init; }

    [JsonPropertyName("issue_workflow_labels")]
    public required IReadOnlyList<string> IssueWorkflowLabels { get; init; }

    [JsonPropertyName("pr_workflow_labels")]
    public required IReadOnlyList<string> PrWorkflowLabels { get; init; }

    [JsonPropertyName("host_loop_responsibilities")]
    public required IReadOnlyList<string> HostLoopResponsibilities { get; init; }

    [JsonPropertyName("child_loop_responsibilities")]
    public required IReadOnlyList<string> ChildLoopResponsibilities { get; init; }

    [JsonPropertyName("publish_boundary_guidance")]
    public required string PublishBoundaryGuidance { get; init; }

    [JsonPropertyName("wip_cap_guidance")]
    public required string WipCapGuidance { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Canonical hardcoded automation contract constants emitted by
/// <c>intent-cli automation summary</c> (G186). These derive from the issue's
/// In Scope section in <c>intents/rules/label-driven-automation.md</c> and do
/// not depend on any parent file. Order is stable.
/// </summary>
internal static class AutomationSummaryConstants
{
    public static readonly IReadOnlyList<string> IssueWorkflowLabels =
    [
        "intent-target",
        "intent-issue-in-progress",
        "intent-pr-created"
    ];

    public static readonly IReadOnlyList<string> PrWorkflowLabels =
    [
        "intent-pr-reviewing",
        "intent-pr-request-update",
        "intent-pr-update-in-progress",
        "intent-pr-rereview-ready",
        "intent-pr-approved"
    ];

    public static readonly IReadOnlyList<string> HostLoopResponsibilities =
    [
        "Process open intent-target PRs in intent-pr-reviewing through closeout",
        "Request updates via intent-pr-request-update with concrete repair notes",
        "Approve and merge via intent-pr-approved when all checks pass",
        "Cut next-slice issues only when WIP cap allows",
        "Apply intent-target only after parent source-of-truth state is durable"
    ];

    public static readonly IReadOnlyList<string> ChildLoopResponsibilities =
    [
        "Stage 1: repair PRs labeled intent-pr-request-update and swap to intent-pr-rereview-ready",
        "Stage 2: implement intent-target issues and open draft PRs labeled intent-pr-created",
        "Honor single-branch cap and HARD CLARIFICATION over branch cap",
        "Never apply or remove intent-target from the child loop"
    ];

    public const string PublishBoundaryGuidance =
        "intent-target marks the parent-durable publish boundary; only the parent automation may apply or remove it";

    public const string WipCapGuidance =
        "Default child WIP cap is one in-flight branch per loop; when WIP is non-empty, defer new work until WIP drains";
}
