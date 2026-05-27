using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G280: Read-only result emitted by
/// <c>intent-cli automation host-review-diagnostics</c>. Differentiates true
/// idle from stale-CLI, stuck review label, missing target, conflicting
/// review-side labels, WIP-cap blockage, and clarification-required so an
/// operator running the host loop can tell why no host action advanced.
/// Producing this record never mutates GitHub, never applies labels, never
/// touches durable parent state, and never launches an AI provider.
/// </summary>
internal sealed record AutomationHostReviewDiagnosticsResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("read_only")]
    public required bool ReadOnly { get; init; }

    [JsonPropertyName("readOnly")]
    public bool ReadOnlyCamel => ReadOnly;

    [JsonPropertyName("recommended_next_command")]
    public required string? RecommendedNextCommand { get; init; }

    [JsonPropertyName("recommendedNextCommand")]
    public string? RecommendedNextCommandCamel => RecommendedNextCommand;

    [JsonPropertyName("structured_clarification")]
    public required AutomationHostReviewDiagnosticsClarification? StructuredClarification { get; init; }

    [JsonPropertyName("structuredClarification")]
    public AutomationHostReviewDiagnosticsClarification? StructuredClarificationCamel => StructuredClarification;

    [JsonPropertyName("details")]
    public required IReadOnlyList<AutomationHostReviewDiagnosticsDetail> Details { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// G355: <c>true</c> when the diagnostic result indicates a high-confidence
    /// deterministic repair is available that the host loop can apply before
    /// reporting blocked or idle. The exact repair command is in
    /// <see cref="RecommendedNextCommand"/>; the category is in
    /// <see cref="SafeRepairCategory"/>. <c>false</c> for
    /// <c>unsafe-metadata</c>, <c>clarification-required</c>, and
    /// <c>true-idle</c> — those must NOT be auto-repaired.
    /// </summary>
    [JsonPropertyName("safe_repair_available")]
    public required bool SafeRepairAvailable { get; init; }

    [JsonPropertyName("safeRepairAvailable")]
    public bool SafeRepairAvailableCamel => SafeRepairAvailable;

    /// <summary>
    /// G355: repair category when <see cref="SafeRepairAvailable"/> is
    /// <c>true</c>; <c>null</c> otherwise. One of the constants in
    /// <see cref="SafeRepairCategories"/>.
    /// </summary>
    [JsonPropertyName("safe_repair_category")]
    public required string? SafeRepairCategory { get; init; }

    [JsonPropertyName("safeRepairCategory")]
    public string? SafeRepairCategoryCamel => SafeRepairCategory;

    /// <summary>
    /// G433: required Child Issue Contract sections that are absent from
    /// the candidate packet's <c>github-body.md</c>. Non-empty only when
    /// <see cref="Classification"/> is <c>clarification-required</c> due
    /// to a contract gap on an explicit candidate. Mirrors the field emitted
    /// by <c>intent next-slice --dry-run</c> so callers can use the same
    /// repair path.
    /// </summary>
    [JsonPropertyName("missing_contract_sections")]
    public IReadOnlyList<string> MissingContractSections { get; init; } = Array.Empty<string>();

    [JsonPropertyName("missingContractSections")]
    public IReadOnlyList<string> MissingContractSectionsCamel => MissingContractSections;
}

internal sealed record AutomationHostReviewDiagnosticsDetail
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

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

internal sealed record AutomationHostReviewDiagnosticsClarification
{
    [JsonPropertyName("background")]
    public required string Background { get; init; }

    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("options")]
    public required IReadOnlyList<string> Options { get; init; }
}

internal static class AutomationHostReviewDiagnosticsClassifications
{
    public const string TrueIdle = "true-idle";
    public const string StuckReviewing = "stuck-reviewing";
    public const string MissingTargetOnPr = "missing-target-on-pr";
    public const string RequestUpdateRereviewConflict = "request-update-rereview-conflict";
    public const string WipCapBlocked = "wip-cap-blocked";
    public const string ClarificationRequired = "clarification-required";
    public const string StaleHostCli = "stale-host-cli";
    public const string ReviewPrActionable = "review-pr-actionable";
    public const string CandidateReady = "candidate-ready";

    /// <summary>
    /// G286: terminal class returned when a host wake should publish exactly one
    /// next-slice issue without further operator acceptance: WIP empty, no review
    /// PR actionable, no Hard Clarification, candidate contract complete.
    /// Distinct from <see cref="CandidateReady"/> historically (preserved for
    /// backward compatibility on existing callers); the analyzer now returns
    /// <c>IssuePublishReady</c> together with the deterministic publish chain
    /// in <c>recommended_next_command</c>.
    /// </summary>
    public const string IssuePublishReady = "issue-publish-ready";

    /// <summary>
    /// G286: terminal class returned when reconcile reports unsafe stops
    /// (e.g. <c>ambiguous-queue-linkage</c>) — the host loop must surface
    /// structured clarification rather than guess past ambiguous metadata.
    /// </summary>
    public const string UnsafeMetadata = "unsafe-metadata";

    /// <summary>
    /// G286: terminal class returned when reconcile has unapplied
    /// high-confidence repairs available and no review/WIP/clarification
    /// blocker is present. The host loop should re-run reconcile with
    /// <c>--write</c> and retry the wake.
    /// </summary>
    public const string RepairedAndRetry = "repaired-and-retry";

    /// <summary>
    /// G313: terminal class returned when <c>publish-recovery</c> reports
    /// at least one unapplied high-confidence repair targeting the
    /// selected review PR (publish-artifact-backed evidence converged on
    /// a single execution unit / source issue). The host loop should run
    /// <c>automation publish-recovery --write</c> first — before generic
    /// reconcile — and retry the wake. Distinct from
    /// <see cref="RepairedAndRetry"/> so the host-loop guidance can route
    /// publish-artifact-backed missing-linked_pr blockers to the
    /// publish-recovery lane as the primary recovery surface.
    /// </summary>
    public const string PublishRecoveryReady = "publish-recovery-ready";

    /// <summary>
    /// G297: terminal class returned when a selected review PR is still
    /// draft. Host approval / closeout / next-slice publish must be
    /// blocked because GitHub will reject the merge with "Pull Request is
    /// still a draft". The host loop should skip approval, run
    /// <c>pr-transition --transition review-release</c> to drop the
    /// review lease cleanly, and surface the gap to the operator or
    /// implementer rather than mutate parent durable state.
    /// </summary>
    public const string DraftMergeBlocked = "draft-merge-blocked";

    /// <summary>
    /// G376: terminal class returned when a selected review PR is draft but
    /// the host positively verified it is otherwise review-ready (closeout
    /// ready, guide ready, base on policy, diff check passed, no findings)
    /// and the draft state is NOT operator-intended. Rather than releasing
    /// the review lease with no outcome (the pre-G376 behavior), the host
    /// loop should mark the PR ready for review (`gh pr ready`) and continue
    /// approval/merge/closeout. This keeps a review-ready draft from looping
    /// in repeated lease-release.
    /// </summary>
    public const string DraftReadyToPromote = "draft-ready-to-promote";

    /// <summary>
    /// G376: terminal class returned when a draft review PR has
    /// implementation findings — it should be marked ready only after the
    /// worker addresses them. The host loop requests a worker update with a
    /// clear, implementation-actionable reason instead of leaving the PR in
    /// repeated lease-release.
    /// </summary>
    public const string DraftRequestUpdate = "draft-request-update";

    /// <summary>
    /// G383: terminal class returned when a PR is blocked only by a
    /// visible/manual/runtime-gated verification AC whose missing piece is
    /// a host-owned policy/design decision (not a PR implementation
    /// finding). The host loop records it ONCE as a durable clarification/
    /// signal and reports this stable classification so later wakes do not
    /// re-ask the operator the same standing A/B/C policy question.
    /// </summary>
    public const string ReviewPolicyGap = "review-policy-gap";

    /// <summary>
    /// G383: terminal class returned when a visible-verification AC gap is
    /// implementation-actionable — the implementer can add the missing
    /// evidence on the PR branch. Route it as an intent-cli-managed PR
    /// feedback comment + <c>request-update</c>, never a chat question.
    /// </summary>
    public const string ImplementationFinding = "implementation-finding";

    /// <summary>
    /// G384: terminal class returned when an internal submodule working-tree
    /// edit is provably redundant with the selected PR head (matching diff
    /// fingerprints, no unique local content). A bounded safe repair clears
    /// the stale edit so the wake proceeds instead of repeating the
    /// dirty-unrelated-submodule operator stop every wake.
    /// </summary>
    public const string RedundantInSubmoduleEdit = "redundant-in-submodule-edit";

    /// <summary>
    /// G384: terminal class returned when an internal submodule edit has
    /// unique or unproven local content. intent-cli refuses auto-repair and
    /// reports a protected-operator-work blocker (never silently discards
    /// operator work).
    /// </summary>
    public const string ProtectedOperatorWork = "protected-operator-work";

    /// <summary>
    /// G384: terminal class returned when the selected PR's required CI is
    /// failing — an implementation-actionable blocker that must stay visible
    /// even when host-sync also sees a redundant-safe local submodule edit.
    /// </summary>
    public const string RequiredCiFailing = "required-ci-failing";

    /// <summary>
    /// G390: a host metadata blocker (e.g. a missing same-repo <c>linked_pr</c>)
    /// stopped a review wake before a verdict, after review-start consumed
    /// <c>intent-pr-rereview-ready</c>. The label must be restored (a
    /// <c>stale-review-lease</c> safe repair) so the PR stays review-actionable;
    /// the blocker must NOT become an implementation request-update comment.
    /// </summary>
    public const string MetadataBlockedReviewPreserved = "metadata-blocked-review-preserved";

    /// <summary>
    /// G356: terminal class returned when one or more queue items are not
    /// marked Completed even though their linked GitHub PR is already
    /// merged. The host loop should run
    /// <c>automation closeout-drift-check --write</c> to record closeout,
    /// commit/push durable state, and retry the wake rather than
    /// reporting a misleading <c>true-idle</c>.
    /// </summary>
    public const string CloseoutDriftRepair = "closeout-drift-repair";

    /// <summary>
    /// G365: terminal class returned when the operator did not supply
    /// <c>--domain</c> and the host-binding lookup returns
    /// <see cref="HostBindingDomainResolutionKind.Mismatch"/> — the
    /// host's <c>.intent-cli/host-binding.toml</c> records a different
    /// <c>target_repo</c> than the <c>--repo</c> argument. The host
    /// loop must NOT silently fall back to the configured domain in
    /// this case because the next-slice probe would run against the
    /// wrong domain and report a misleading <c>true-idle</c> /
    /// <c>design-needed</c> outcome. The recommended fix is to either
    /// pass <c>--domain</c> explicitly or update the binding.
    /// </summary>
    public const string MissingDomainBinding = "missing-domain-binding";
}

/// <summary>
/// G355: Repair category constants for <see cref="AutomationHostReviewDiagnosticsResult.SafeRepairCategory"/>.
/// Each value names the class of deterministic host-side repair that a wake
/// can apply before reporting blocked or idle. Categories are declared by
/// intent-cli diagnostics — never guessed by the AI agent.
/// </summary>
internal static class SafeRepairCategories
{
    /// <summary>
    /// The next publish is blocked because a drafted packet is missing only
    /// mechanical sections (<c>Verification</c>, <c>Related Links</c>). These
    /// can be appended from a standard template without operator input (G354).
    /// </summary>
    public const string DraftedPacketMechanicalGap = "drafted-packet-mechanical-gap";

    /// <summary>
    /// A PR's <c>linked_pr</c> or queue-item linkage is missing but can be
    /// deterministically recovered from publish-artifact evidence (G313 / G351).
    /// </summary>
    public const string ReviewLinkageGap = "review-linkage-gap";

    /// <summary>
    /// A generic host-side label-drift or queue-state drift that the reconcile
    /// command classifies as high-confidence and safe to apply (G342 / G344).
    /// </summary>
    public const string HostArtifactRepair = "host-artifact-repair";

    /// <summary>
    /// An issue-publish gap: the issue is not yet published but the host has
    /// everything needed to publish deterministically (G343 / G286
    /// <c>issue-publish-ready</c>).
    /// </summary>
    public const string IssuePublishGap = "issue-publish-gap";

    /// <summary>
    /// G355: A review lease is stale — the PR carries <c>intent-pr-reviewing</c>
    /// with no active review in progress. The deterministic repair is
    /// <c>pr-transition --transition review-release</c> (G292). After releasing
    /// the lease the host loop retries review selection once.
    /// </summary>
    public const string StaleReviewLease = "stale-review-lease";

    /// <summary>
    /// G355: The host working tree has an unrelated dirty submodule or safe dirty
    /// path that the workspace-guard stash lane can handle deterministically
    /// (G352). The repair is <c>automation workspace-guard --mode begin --write</c>
    /// before the wake body and <c>--mode end --write</c> after the push lands.
    /// </summary>
    public const string WorkspaceSafeDirty = "workspace-safe-dirty";

    /// <summary>
    /// G355 (child-loop only): A GitHub label state gap on the selected issue or
    /// PR can be closed deterministically by re-applying the correct label via
    /// <c>intent-cli worker</c> commands. This category is surfaced by child-loop
    /// preflight (<c>worker issue-preflight</c> / <c>worker pr-comment-preflight</c>)
    /// and MUST NOT be repaired by the child loop if the gap targets host metadata
    /// paths (<c>.intent-cli/**</c>, <c>intents/**</c>).
    /// </summary>
    public const string ChildSelectorLabelGap = "child-selector-label-gap";

    /// <summary>
    /// G356: A queue item is not marked Completed even though its linked GitHub
    /// PR is already merged. The deterministic repair is
    /// <c>automation closeout-drift-check --write</c> (host-only), which marks
    /// the item Completed and appends <c>pr-merged</c> / <c>closeout-recorded</c>
    /// run events. After applying the repair, commit/push durable state and
    /// retry the wake exactly once.
    /// </summary>
    public const string CloseoutDriftRepair = "closeout-drift-repair";
}
