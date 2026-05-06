using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G277: Result of <c>intent-cli automation reconcile</c>. Lists the safe
/// label-drift repairs detected for a repo, plus any unsafe stops that
/// require operator clarification. Host-only by policy — never produced
/// from a child implementation loop. Dry-run by default; <c>--write</c>
/// applies high-confidence repairs through the host-owned reconcile mutator.
/// </summary>
internal sealed record AutomationReconcileResult
{
    [JsonPropertyName("lane")]
    public required string Lane { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("host_only")]
    public required bool HostOnly { get; init; }

    [JsonPropertyName("hostOnly")]
    public bool HostOnlyCamel => HostOnly;

    [JsonPropertyName("safe_repairs")]
    public required IReadOnlyList<AutomationReconcileRepair> SafeRepairs { get; init; }

    [JsonPropertyName("safeRepairs")]
    public IReadOnlyList<AutomationReconcileRepair> SafeRepairsCamel => SafeRepairs;

    [JsonPropertyName("unsafe_stops")]
    public required IReadOnlyList<AutomationReconcileUnsafeStop> UnsafeStops { get; init; }

    [JsonPropertyName("unsafeStops")]
    public IReadOnlyList<AutomationReconcileUnsafeStop> UnsafeStopsCamel => UnsafeStops;

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

/// <summary>
/// G277: One mechanically provable repair entry. Confidence categorizes how
/// strong the evidence is; only repairs with confidence
/// <see cref="AutomationReconcileConfidence.High"/> are applied in
/// <c>--write</c> mode. Lower-confidence entries are advisory and may carry
/// a follow-up command for the operator to invoke through an existing
/// intent-cli surface.
/// </summary>
internal sealed record AutomationReconcileRepair
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("target_kind")]
    public required string TargetKind { get; init; }

    [JsonPropertyName("targetKind")]
    public string TargetKindCamel => TargetKind;

    [JsonPropertyName("target_number")]
    public required int? TargetNumber { get; init; }

    [JsonPropertyName("targetNumber")]
    public int? TargetNumberCamel => TargetNumber;

    [JsonPropertyName("target_url")]
    public required string? TargetUrl { get; init; }

    [JsonPropertyName("targetUrl")]
    public string? TargetUrlCamel => TargetUrl;

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    [JsonPropertyName("addLabels")]
    public IReadOnlyList<string> AddLabelsCamel => AddLabels;

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    [JsonPropertyName("removeLabels")]
    public IReadOnlyList<string> RemoveLabelsCamel => RemoveLabels;

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<string> Evidence { get; init; }

    [JsonPropertyName("confidence")]
    public required string Confidence { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("requires_followup_command")]
    public required string? RequiresFollowupCommand { get; init; }

    [JsonPropertyName("requiresFollowupCommand")]
    public string? RequiresFollowupCommandCamel => RequiresFollowupCommand;
}

internal sealed record AutomationReconcileUnsafeStop
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("target_kind")]
    public required string? TargetKind { get; init; }

    [JsonPropertyName("targetKind")]
    public string? TargetKindCamel => TargetKind;

    [JsonPropertyName("target_number")]
    public required int? TargetNumber { get; init; }

    [JsonPropertyName("targetNumber")]
    public int? TargetNumberCamel => TargetNumber;

    [JsonPropertyName("target_url")]
    public required string? TargetUrl { get; init; }

    [JsonPropertyName("targetUrl")]
    public string? TargetUrlCamel => TargetUrl;

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("missing_evidence")]
    public required IReadOnlyList<string> MissingEvidence { get; init; }

    [JsonPropertyName("missingEvidence")]
    public IReadOnlyList<string> MissingEvidenceCamel => MissingEvidence;
}

internal static class AutomationReconcileLanes
{
    public const string HostReview = "host-review";
    public const string NextSlice = "next-slice";
    public const string All = "all";
}

internal static class AutomationReconcileModes
{
    public const string DryRun = "dry-run";
    public const string Write = "write";
}

internal static class AutomationReconcileConfidence
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Advisory = "advisory";
}

internal static class AutomationReconcileRepairTypes
{
    public const string MissingPrIntentTarget = "missing-pr-intent-target";
    public const string MisplacedPrIntentPrCreated = "misplaced-pr-intent-pr-created";
    public const string MissingIssueIntentPrCreated = "missing-issue-intent-pr-created";
    public const string MissingPrClosesKeyword = "missing-pr-closes-keyword";
    public const string MissingLinkedPrMetadata = "missing-linked-pr-metadata";
    public const string StaleNextSliceCandidateCache = "stale-next-slice-candidate-cache";
}

internal static class AutomationReconcileUnsafeStopKinds
{
    public const string AmbiguousIssueLink = "ambiguous-issue-link";
    public const string MissingPublishedIssueEvidence = "missing-published-issue-evidence";
    public const string ChildLoopProhibited = "child-loop-prohibited";
}
