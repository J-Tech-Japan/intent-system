using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G692: one mode judgment shared by stalled-work, state-doctor, and status
/// surfaces. Team mode changes which observations are meaningful; it never
/// removes a publish or ownership gate.
/// </summary>
internal static class TeamModeCapabilityClasses
{
    public const string Authoring = "authoring";
    public const string Worker = "worker";
    public const string Review = "review";
    public const string Ci = "ci";
    public const string Delegation = "delegation";
    public const string Supervisor = "supervisor";
    public const string ClaimStale = "claim-stale";
    public const string ContractReadiness = "contract/readiness";
    public const string BranchLane = "branch-lane-decision-pending";
    public const string BranchRouting = "branch-routing-conflict";
    public const string PublishDurableStateDrift = "publish-durable-state-drift";
    public const string KnowledgeGuideWriteback = "knowledge/guide-writeback";

    public static readonly IReadOnlyList<string> All =
    [
        Authoring,
        Worker,
        Review,
        Ci,
        Delegation,
        Supervisor,
        ClaimStale,
        ContractReadiness,
        BranchLane,
        BranchRouting,
        PublishDurableStateDrift,
        KnowledgeGuideWriteback,
    ];
}

internal sealed record TeamModeCapabilityMatrix
{
    [JsonPropertyName("team_mode")]
    public required string TeamMode { get; init; }

    [JsonPropertyName("mode_source")]
    public required string ModeSource { get; init; }

    [JsonPropertyName("active_classes")]
    public required IReadOnlyList<string> ActiveClasses { get; init; }

    [JsonPropertyName("not_applicable_classes")]
    public required IReadOnlyList<string> NotApplicableClasses { get; init; }

    [JsonPropertyName("preview_status")]
    public string PreviewStatus { get; init; } = "preview-through-1.x";

    [JsonIgnore]
    public bool IsAuthoringOnly => global::IntentSystem.Cli.Commands.TeamMode.IsAuthoringOnly(TeamMode);

    public bool IsApplicable(string capabilityClass) =>
        !NotApplicableClasses.Contains(capabilityClass, StringComparer.Ordinal);

    /// <summary>
    /// Published-not-delegated is intentionally handled by the handoff
    /// collector rather than by the broad delegation class. A missing record
    /// must remain visible; a recorded external handoff is the only honest
    /// suppression.
    /// </summary>
    public bool IsStalledKindApplicable(string kind)
    {
        if (string.Equals(kind, AutomationStalledWorkCommand.KindPublishedNotDelegated, StringComparison.Ordinal))
        {
            return true;
        }

        return IsApplicable(ClassForStalledKind(kind));
    }

    public string ClassForStalledKind(string kind) => kind switch
    {
        AutomationStalledWorkCommand.KindClaimStale => TeamModeCapabilityClasses.ClaimStale,
        AutomationStalledWorkCommand.KindBranchLaneDecisionPending => TeamModeCapabilityClasses.BranchLane,
        AutomationStalledWorkCommand.KindBranchRoutingConflict => TeamModeCapabilityClasses.BranchRouting,
        AutomationStalledWorkCommand.KindKnowledgeWritebackPending
            or AutomationStalledWorkCommand.KindKnowledgeWritebackRecordedUncommitted
            or AutomationStalledWorkCommand.KindGuideReachabilityPending
            => TeamModeCapabilityClasses.KnowledgeGuideWriteback,
        AutomationStalledWorkCommand.KindBacklogReadyIdle
            or AutomationStalledWorkCommand.KindBlockedParked
            or AutomationStalledWorkCommand.KindStateDrift
            or AutomationStalledWorkCommand.KindDesignDecisionPending
            or AutomationStalledWorkCommand.KindVersionRollRequired
            => TeamModeCapabilityClasses.ContractReadiness,
        AutomationStalledWorkCommand.KindCiPending
            or AutomationStalledWorkCommand.KindCiAllGreenNotTransitioned
            or AutomationStalledWorkCommand.KindCiFailedNotTransitioned
            or AutomationStalledWorkCommand.KindCiHeadMoved
            => TeamModeCapabilityClasses.Ci,
        AutomationStalledWorkCommand.KindPendingDelegationOpen
            => TeamModeCapabilityClasses.Delegation,
        AutomationStalledWorkCommand.KindOperatorAttentionPending
            or AutomationStalledWorkCommand.KindOperatorAttentionCannotDetermine
            => TeamModeCapabilityClasses.Supervisor,
        AutomationStalledWorkCommand.KindClaimedButSilent
            or AutomationStalledWorkCommand.KindBlockedLabelDrift
            => TeamModeCapabilityClasses.Worker,
        AutomationStalledWorkCommand.KindPrCreatedNotReviewing
            or AutomationStalledWorkCommand.KindMergedNotClosedOut
            or AutomationStalledWorkCommand.KindAwaitingOperatorMerge
            or AutomationStalledWorkCommand.KindOperatorMergeDetected
            or AutomationStalledWorkCommand.KindApprovedNotMerged
            or AutomationStalledWorkCommand.KindRepairPending
            or AutomationStalledWorkCommand.KindRereviewPending
            or AutomationStalledWorkCommand.KindRepairStalled
            => TeamModeCapabilityClasses.Review,
        AutomationStalledWorkCommand.KindPublishedNotDelegated
            => TeamModeCapabilityClasses.Delegation,
        _ => TeamModeCapabilityClasses.ContractReadiness,
    };

    public string ClassForDoctorCategory(string category) =>
        TeamModeCapabilityClasses.PublishDurableStateDrift;

    public string ClassForQueueState(QueueItemState state) => state switch
    {
        QueueItemState.Active or QueueItemState.Fixing => TeamModeCapabilityClasses.Worker,
        QueueItemState.Review => TeamModeCapabilityClasses.Review,
        _ => TeamModeCapabilityClasses.ContractReadiness,
    };

    public static TeamModeCapabilityMatrix Resolve(
        string repoRoot,
        string domain,
        string? team)
    {
        return FromResolution(TeamModeStore.Resolve(repoRoot, domain, team));
    }

    public static TeamModeCapabilityMatrix FromResolution(TeamModeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var source = resolution.Source == TeamModeSource.Recorded ? "recorded" : "default";
        if (!resolution.IsAuthoringOnly)
        {
            return new TeamModeCapabilityMatrix
            {
                TeamMode = global::IntentSystem.Cli.Commands.TeamMode.Delivery,
                ModeSource = source,
                ActiveClasses = TeamModeCapabilityClasses.All,
                NotApplicableClasses = [],
            };
        }

        var notApplicable = new[]
        {
            TeamModeCapabilityClasses.Worker,
            TeamModeCapabilityClasses.Review,
            TeamModeCapabilityClasses.Ci,
            TeamModeCapabilityClasses.Delegation,
            TeamModeCapabilityClasses.Supervisor,
        };
        var active = TeamModeCapabilityClasses.All
            .Except(notApplicable, StringComparer.Ordinal)
            .ToArray();

        return new TeamModeCapabilityMatrix
        {
            TeamMode = global::IntentSystem.Cli.Commands.TeamMode.AuthoringOnly,
            ModeSource = source,
            ActiveClasses = active,
            NotApplicableClasses = notApplicable,
        };
    }

    public static bool IsWorkerRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = role.Trim();
        return normalized.Equals("implementation", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("review", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("worker", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("coder", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("developer", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("worker-", StringComparison.OrdinalIgnoreCase);
    }
}
