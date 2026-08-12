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

    /// <summary>
    /// G346: the effective base branch policy for this domain/host, sourced from
    /// <c>.intent-cli/config.toml</c> <c>base_branch_policy</c> (defaults to
    /// <c>direct-main</c> when the key is absent). AI threads should derive the
    /// expected PR target branch from this field rather than relying on prompt memory.
    /// </summary>
    [JsonPropertyName("effective_base_branch_policy")]
    public required string EffectiveBaseBranchPolicy { get; init; }

    /// <summary>
    /// G346: the expected GitHub PR base branch derived from the effective policy
    /// (<c>main</c> for <c>direct-main</c>; <c>main-ai</c> for <c>main-ai</c>).
    /// </summary>
    [JsonPropertyName("implementation_base_branch")]
    public required string ImplementationBaseBranch { get; init; }

    /// <summary>
    /// G362: same-repo topology metadata branch contract surfaced to
    /// the host loop so the prompt no longer has to infer branches
    /// from memory. <c>same_repo_topology</c> is true when the host
    /// is configured for same-repository topology (host metadata and
    /// implementation code share one repo). The branch fields name
    /// the metadata SOURCE (read), metadata WRITE (commit target),
    /// and implementation PR base branches — empty string means
    /// "not configured", in which case the loop keeps its pre-G362
    /// pull-first <c>main</c> behavior (G357).
    /// </summary>
    [JsonPropertyName("same_repo_topology")]
    public bool SameRepoTopology { get; init; }

    [JsonPropertyName("metadata_source_branch")]
    public string MetadataSourceBranch { get; init; } = string.Empty;

    [JsonPropertyName("metadata_write_branch")]
    public string MetadataWriteBranch { get; init; } = string.Empty;

    [JsonPropertyName("issue_workflow_labels")]
    public required IReadOnlyList<string> IssueWorkflowLabels { get; init; }

    [JsonPropertyName("pr_workflow_labels")]
    public required IReadOnlyList<string> PrWorkflowLabels { get; init; }

    [JsonPropertyName("host_loop_responsibilities")]
    public required IReadOnlyList<string> HostLoopResponsibilities { get; init; }

    [JsonPropertyName("host_pr_transition_commands")]
    public required IReadOnlyList<string> HostPrTransitionCommands { get; init; }

    [JsonPropertyName("automation_capability_schema_version")]
    public required string AutomationCapabilitySchemaVersion { get; init; }

    [JsonPropertyName("automationCapabilitySchemaVersion")]
    public string AutomationCapabilitySchemaVersionCamel => AutomationCapabilitySchemaVersion;

    [JsonPropertyName("automation_command_surface_version")]
    public required string AutomationCommandSurfaceVersion { get; init; }

    [JsonPropertyName("automationCommandSurfaceVersion")]
    public string AutomationCommandSurfaceVersionCamel => AutomationCommandSurfaceVersion;

    [JsonPropertyName("automation_command_capabilities")]
    public required IReadOnlyList<AutomationCommandCapability> AutomationCommandCapabilities { get; init; }

    [JsonPropertyName("automationCommandCapabilities")]
    public IReadOnlyList<AutomationCommandCapability> AutomationCommandCapabilitiesCamel => AutomationCommandCapabilities;

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
    public const string AutomationCapabilitySchemaVersion = "1";

    public const string AutomationCommandSurfaceVersion = "automation-command-surface/v1";

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
        "Select open intent-target PRs with no PR state label or with intent-pr-rereview-ready, then set intent-pr-reviewing while processing through closeout",
        "Request updates via intent-pr-request-update with concrete repair notes",
        "Approve via intent-pr-approved when all checks pass; direct lanes merge+closeout, while operator-merge lanes wait for a human merge and then resume closeout only",
        "Cut next-slice issues only when WIP cap allows",
        "Apply intent-target only after parent source-of-truth state is durable"
    ];

    public static readonly IReadOnlyList<string> HostPrTransitionCommands =
    [
        "intent-cli automation pr-transition --transition review-start --write adds intent-target and intent-pr-reviewing, and removes intent-pr-rereview-ready plus legacy rereview-ready",
        "intent-cli automation pr-transition --transition request-update --write removes intent-pr-reviewing and adds intent-pr-request-update",
        "intent-cli automation pr-transition --transition approved --write removes intent-pr-reviewing and adds intent-pr-approved"
    ];

    public static readonly IReadOnlyList<AutomationCommandCapability> AutomationCommandCapabilities =
    [
        new()
        {
            Capability = "issue-publish",
            Command = "intent-cli automation issue-publish",
            WorkflowKind = "issue",
            TargetKind = "issue",
            Transition = null,
            DryRunSupported = true,
            WriteSupported = true,
            AddLabels = ["intent-target"],
            RemoveLabels = []
        },
        new()
        {
            Capability = "pr-transition.review-start",
            Command = "intent-cli automation pr-transition",
            WorkflowKind = "pr",
            TargetKind = "pr",
            Transition = "review-start",
            DryRunSupported = true,
            WriteSupported = true,
            AddLabels = ["intent-target", "intent-pr-reviewing"],
            RemoveLabels = ["intent-pr-rereview-ready", "rereview-ready"]
        },
        new()
        {
            Capability = "pr-transition.request-update",
            Command = "intent-cli automation pr-transition",
            WorkflowKind = "pr",
            TargetKind = "pr",
            Transition = "request-update",
            DryRunSupported = true,
            WriteSupported = true,
            AddLabels = ["intent-pr-request-update"],
            RemoveLabels = ["intent-pr-reviewing"]
        },
        new()
        {
            Capability = "pr-transition.approved",
            Command = "intent-cli automation pr-transition",
            WorkflowKind = "pr",
            TargetKind = "pr",
            Transition = "approved",
            DryRunSupported = true,
            WriteSupported = true,
            AddLabels = ["intent-pr-approved"],
            RemoveLabels = ["intent-pr-reviewing"]
        }
    ];

    public static readonly IReadOnlyList<string> ChildLoopResponsibilities =
    [
        "Stage 1: repair PRs labeled intent-pr-request-update and swap to intent-pr-rereview-ready",
        "Stage 2: implement intent-target issues, open draft PRs, and mark the linked Issue with intent-pr-created (PR publication/review eligibility is handled by PR-side intent-target and PR state labels; never apply intent-pr-created to the PR itself)",
        "Honor single-branch cap and HARD CLARIFICATION over branch cap",
        "Never apply or remove intent-target from the child loop"
    ];

    public const string PublishBoundaryGuidance =
        "intent-target marks the parent-durable publish boundary; only the parent automation may apply or remove it";

    public const string WipCapGuidance =
        "Default child WIP cap is one in-flight branch per loop; when WIP is non-empty, defer new work until WIP drains";
}

internal sealed record AutomationCommandCapability
{
    [JsonPropertyName("capability")]
    public required string Capability { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("workflow_kind")]
    public required string WorkflowKind { get; init; }

    [JsonPropertyName("workflowKind")]
    public string WorkflowKindCamel => WorkflowKind;

    [JsonPropertyName("target_kind")]
    public required string TargetKind { get; init; }

    [JsonPropertyName("targetKind")]
    public string TargetKindCamel => TargetKind;

    [JsonPropertyName("transition")]
    public required string? Transition { get; init; }

    [JsonPropertyName("dry_run_supported")]
    public required bool DryRunSupported { get; init; }

    [JsonPropertyName("dryRunSupported")]
    public bool DryRunSupportedCamel => DryRunSupported;

    [JsonPropertyName("write_supported")]
    public required bool WriteSupported { get; init; }

    [JsonPropertyName("writeSupported")]
    public bool WriteSupportedCamel => WriteSupported;

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    [JsonPropertyName("addLabels")]
    public IReadOnlyList<string> AddLabelsCamel => AddLabels;

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    [JsonPropertyName("removeLabels")]
    public IReadOnlyList<string> RemoveLabelsCamel => RemoveLabels;
}
