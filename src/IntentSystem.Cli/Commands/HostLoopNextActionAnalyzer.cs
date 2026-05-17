using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G308: pure analyzer that aggregates the host loop's specialized
/// diagnostics into ONE primary recommended action so an operator (or
/// the host loop's own guidance) does not have to interpret several
/// commands. Inputs are structured snapshots of the existing
/// diagnostics (host-sync-preflight, host-review-preflight /
/// diagnostics, intent next-slice --dry-run, automation doctor); the
/// analyzer returns a single classification with a deterministic next
/// command and structured evidence.
///
/// Classifications, in priority order (highest first — the analyzer
/// short-circuits on the first match):
///
/// 1. <c>stale-cli</c> — installed CLI surface reports stale; refresh
///    before any further mutation.
/// 2. <c>dirty-host-state</c> — durable host-state is dirty (G304/G306
///    hard stop); reconcile before any further mutation.
/// 3. <c>safe-stash</c> — only unrelated dirty paths are present
///    (G306 lane); recommend the begin/end safe-stash flow.
/// 4. <c>review-pr</c> — there is an actionable review PR; recommend
///    closeout-plan and approval/merge.
/// 4.5 (G319) <c>approved-pr-merge-closeout</c> — an open PR already
///    carries <c>intent-pr-approved</c> and must be merged + closed out
///    before any wip-cap-blocked stop. Unsafe variants surface as
///    <c>approved-pr-draft-blocked</c> (G297), <c>approved-pr-merge-blocked</c>
///    (mergeStateStatus != CLEAN), or <c>approved-pr-metadata-blocked</c>
///    (queue/linked_pr/linked_issue inconsistent).
/// 5. <c>repair-host-metadata</c> — `intent-pr-created` exists on an
///    issue but the queue has no `linked_pr` (G303 publish-recovery
///    case), or the publish artifact lifecycle has a deterministic
///    upgrade pending (G307).
/// 6. <c>publish-next-issue</c> — `intent next-slice --dry-run`
///    reports `issue-cut-ready` and there is no review PR / WIP cap
///    blocker.
/// 7. <c>hard-clarification</c> — Hard Clarification is open; stop
///    with structured clarification.
/// 8. <c>wait-for-child</c> — there is at least one open
///    `intent-target` PR/issue currently held by a worker
///    (`intent-issue-in-progress` / `intent-pr-update-in-progress`);
///    no host action available until the child completes.
/// 9. <c>true-idle</c> — none of the above; the host loop is genuinely
///    idle and no mutation is required.
///
/// Pure data in / pure data out: no `gh` calls, no file I/O, no state
/// mutation. The command layer captures the snapshots and feeds them
/// in.
/// </summary>
internal static class HostLoopNextActionAnalyzer
{
    public const string ClassificationStaleCli = "stale-cli";
    public const string ClassificationDirtyHostState = "dirty-host-state";
    public const string ClassificationSafeStash = "safe-stash";
    public const string ClassificationReviewPr = "review-pr";
    // G319: approved-PR continuation lane — fires BEFORE WIP-cap so an
    // already-approved-but-unmerged PR is completed before the wake stops
    // for "an open intent-target exists".
    public const string ClassificationApprovedPrMergeCloseout = "approved-pr-merge-closeout";
    public const string ClassificationApprovedPrDraftBlocked = "approved-pr-draft-blocked";
    public const string ClassificationApprovedPrMergeBlocked = "approved-pr-merge-blocked";
    public const string ClassificationApprovedPrMetadataBlocked = "approved-pr-metadata-blocked";
    public const string ClassificationRepairHostMetadata = "repair-host-metadata";
    public const string ClassificationPublishNextIssue = "publish-next-issue";
    public const string ClassificationHardClarification = "hard-clarification";
    public const string ClassificationWipCapBlocked = "wip-cap-blocked";
    public const string ClassificationWaitForChild = "wait-for-child";
    /// <summary>
    /// G328: emitted when no actionable host signal exists AND the
    /// next-slice probe reports `design-needed` (no prepared packet
    /// and runtime creation is not permitted). Surfaces ABOVE
    /// <c>true-idle</c> so the host loop can call out the missing
    /// design work instead of reporting genuine idleness.
    /// </summary>
    public const string ClassificationDesignNeeded = "design-needed";

    /// <summary>
    /// G361: emitted when host-sync-preflight reports
    /// <c>dirty-host-durable-state</c> but the dirty surface is
    /// actually a complete prepared packet directory under
    /// <c>.intent-cli/issues/&lt;execution-unit&gt;/</c> that
    /// durable-state-preflight already classified
    /// <c>prepared-packet-commit-ready</c>. The host loop must commit
    /// (and push) the verified packet directory BEFORE downstream
    /// next-slice publish, so this lane overrides the generic
    /// <see cref="ClassificationDirtyHostState"/> lane when the
    /// repair-before-publish path is safe.
    /// </summary>
    public const string ClassificationPreparedPacketCommitReady = "prepared-packet-commit-ready";

    public const string ClassificationTrueIdle = "true-idle";

    public static HostLoopNextActionResult Analyze(HostLoopNextActionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. stale-cli (highest priority — block everything)
        if (input.StaleCli)
        {
            return Result(ClassificationStaleCli, mutationAllowed: false,
                "intent-cli automation doctor --format json",
                new[] { "automation doctor reported stale-host-cli or a missing required surface; refresh the installed CLI before any mutation." },
                "Stale installed CLI — refresh before any further host loop wake.");
        }

        // 2. dirty host state (G304/G306 hard stop) — but first check
        // whether the dirty surface is actually a complete prepared
        // packet directory that durable-state-preflight has already
        // classified `prepared-packet-commit-ready` (G361). When so, the
        // safe path is to commit + push that directory BEFORE the next
        // publish wake, not to surface a generic dirty-host-state stop.
        //
        // PR #824 review repair: this lane is restricted to pure
        // `dirty-host-durable-state`. `dirty-mixed` ALSO contains
        // unrelated dirty paths the operator must handle explicitly
        // (the G304/G306 contract requires this — see the host-sync
        // guidance) so taking the commit-before-publish lane on
        // `dirty-mixed` would hide non-durable-state changes and
        // recommend a too-narrow commit. On `dirty-mixed`, fall through
        // to the dirty-host-state stop so the operator separates the
        // packet directory from the unrelated portion before any
        // auto-commit.
        if (input.SyncClassification == "dirty-host-durable-state"
            && input.PreparedPacketCommitReadyAvailable
            && !string.IsNullOrWhiteSpace(input.PreparedPacketExecutionUnit))
        {
            return Result(ClassificationPreparedPacketCommitReady, mutationAllowed: true,
                $"intent-cli automation durable-state-preflight --domain {input.Domain ?? "<DOMAIN>"} --target-repo {input.Repo} --format json",
                new[]
                {
                    $"host-sync-preflight classification: {input.SyncClassification}.",
                    $"durable-state-preflight reports prepared packet directory `.intent-cli/issues/{input.PreparedPacketExecutionUnit}/` is commit-ready (G361): four canonical files present, packet.yaml parses, domain/target-repo bindings match.",
                    "Commit + push the verified packet directory before the next publish wake; the operator must NOT bypass durable-state-preflight or commit additional untracked content."
                },
                $"Prepared packet `{input.PreparedPacketExecutionUnit}` commit-ready — commit before next-slice publish (G361).");
        }
        if (input.SyncClassification is "dirty-host-durable-state" or "dirty-mixed")
        {
            return Result(ClassificationDirtyHostState, mutationAllowed: false,
                "intent-cli automation host-sync-preflight --format json",
                new[]
                {
                    $"host-sync-preflight classification: {input.SyncClassification}.",
                    "Reconcile dirty durable host-state through the G304 fail-closed path before re-running the wake."
                },
                "Dirty durable host-state present — refusing to mutate (G304/G306).");
        }

        // 3. safe-stash (G306 lane: only unrelated dirty paths)
        if (input.SyncClassification == "dirty-unrelated-submodule" || input.SafeStashRequired)
        {
            return Result(ClassificationSafeStash, mutationAllowed: true,
                "intent-cli automation workspace-guard --mode begin --write --format json",
                new[]
                {
                    "Unrelated dirty paths present; durable host-state is clean.",
                    "Run `automation workspace-guard --mode begin --write` to stash, then run the wake, then `--mode end --write` to restore. On stash-pop conflict surface the structured recovery instruction."
                },
                "Unrelated dirty worktree paths — use the G306 safe-stash lane.");
        }

        // 4. review PR actionable
        if (input.ActionableReviewPr is { } reviewPr)
        {
            return Result(ClassificationReviewPr, mutationAllowed: true,
                $"intent-cli review closeout-plan --pr {reviewPr.Number} --repo {input.Repo} --format json",
                new[]
                {
                    $"PR #{reviewPr.Number} ({reviewPr.Url}) carries `intent-target` and is ready for host review.",
                    "Run `review closeout-plan` to confirm host metadata, then `pr-transition --transition approved --write`, merge, then `closeout pr --pr-merged true --write`."
                },
                $"Review PR actionable: #{reviewPr.Number}.");
        }

        // 4.5 (G319) approved-PR continuation — fires BEFORE WIP-cap so an
        // approved-but-unmerged PR is completed first. Distinguish the
        // happy path (merge + closeout ready) from specific unsafe
        // blockers so the operator gets a precise stop instead of generic
        // wip-cap-blocked.
        if (input.ApprovedPrPendingMergeCloseout is { } approvedPr)
        {
            if (approvedPr.IsDraft)
            {
                return Result(ClassificationApprovedPrDraftBlocked, mutationAllowed: false,
                    $"intent-cli automation pr-transition --transition review-release --repo {input.Repo} --pr {approvedPr.Number} --write --format json",
                    new[]
                    {
                        $"PR #{approvedPr.Number} ({approvedPr.Url}) carries `intent-pr-approved` but is currently a draft (G297 — draft PRs cannot be merged).",
                        "Drop the lease via `pr-transition --transition review-release --write` and surface the gap; the PR must be readied for review by the implementer or operator before approval/merge can complete."
                    },
                    $"Approved PR #{approvedPr.Number} is draft — refusing to merge (G297).");
            }
            if (!string.IsNullOrEmpty(approvedPr.MergeStateStatus)
                && !string.Equals(approvedPr.MergeStateStatus, "CLEAN", StringComparison.OrdinalIgnoreCase))
            {
                return Result(ClassificationApprovedPrMergeBlocked, mutationAllowed: false,
                    $"gh pr view {approvedPr.Number} --repo {input.Repo} --json mergeStateStatus,mergeable,headRefName,baseRefName",
                    new[]
                    {
                        $"PR #{approvedPr.Number} ({approvedPr.Url}) carries `intent-pr-approved` but mergeStateStatus is `{approvedPr.MergeStateStatus}` (expected CLEAN).",
                        "Surface the gap to the operator: rebase / resolve conflicts / re-run CI before re-attempting merge. Do NOT silently retry."
                    },
                    $"Approved PR #{approvedPr.Number} merge blocked — mergeStateStatus={approvedPr.MergeStateStatus}.");
            }
            if (approvedPr.HostMetadataBlocked)
            {
                return Result(ClassificationApprovedPrMetadataBlocked, mutationAllowed: false,
                    $"intent-cli review closeout-plan --pr {approvedPr.Number} --repo {input.Repo} --format json",
                    new[]
                    {
                        $"PR #{approvedPr.Number} ({approvedPr.Url}) carries `intent-pr-approved` but host metadata is inconsistent (e.g. queue-state linked_pr missing, linked_issue mismatch, packet directory missing).",
                        "Run `review closeout-plan` to surface the structured `host-metadata-blocked` reason and the `recommended_recovery_command` (typically `automation publish-recovery --write` for G313/G315 cases). Host metadata blockers MUST NOT become PR repair comments."
                    },
                    $"Approved PR #{approvedPr.Number} host-metadata-blocked — run closeout-plan + publish-recovery before merge.");
            }

            // Happy path: clean approved PR pending merge + closeout.
            var mergeCommand = $"intent-cli review closeout-plan --pr {approvedPr.Number} --repo {input.Repo} --format json";
            var evidence = new List<string>
            {
                $"PR #{approvedPr.Number} ({approvedPr.Url}) carries `intent-target` + `intent-pr-approved` and is open, non-draft, and not blocked.",
                "Approved continuation outranks wip-cap-blocked: the open approved PR IS the next host action — merge and closeout before any new next-slice publish.",
                $"Sequence: `review closeout-plan --pr {approvedPr.Number} --write` → merge via host's existing merge step → capture `IS_MERGED=$(gh pr view {approvedPr.Number} --json merged --jq .merged)` → `closeout pr --pr {approvedPr.Number} --pr-merged $IS_MERGED --write` (G297 gate). Commit + push parent durable state before stopping the wake."
            };
            if (approvedPr.LinkedIssueNumber is int linkedIssue)
            {
                evidence.Add($"linked source issue #{linkedIssue} — confirm `Closes #{linkedIssue}` is present in the PR body before merge (G311); closeout pr --write validates and refuses on missing/wrong/multiple.");
            }

            return Result(ClassificationApprovedPrMergeCloseout, mutationAllowed: true,
                mergeCommand,
                evidence,
                $"Approved PR #{approvedPr.Number} pending merge + closeout — continue before WIP-cap.");
        }

        // 5. host metadata repair (G303 publish-recovery + G307 lifecycle)
        if (input.PublishRecoveryRepairsAvailable > 0)
        {
            return Result(ClassificationRepairHostMetadata, mutationAllowed: true,
                $"intent-cli automation publish-recovery --repo {input.Repo} --format json",
                new[]
                {
                    $"automation publish-recovery has {input.PublishRecoveryRepairsAvailable} high-confidence repair(s) for missing linked refs (G303).",
                    "Host metadata MUST NOT become PR repair comments; run publish-recovery, then re-run the wake."
                },
                "Host metadata drift detected — run G303 publish-recovery, not PR comments.");
        }
        if (input.PublishLifecycleDriftCount > 0)
        {
            return Result(ClassificationRepairHostMetadata, mutationAllowed: true,
                $"intent-cli automation publish-lifecycle-repair --repo {input.Repo} --format json",
                new[]
                {
                    $"publish-lifecycle-repair has {input.PublishLifecycleDriftCount} drift entrie(s) ready to upgrade (G307).",
                    "Run publish-lifecycle-repair to resync publish.yaml lifecycle with queue + GitHub state."
                },
                "Publish artifact lifecycle drift — run G307 publish-lifecycle-repair.");
        }
        if (input.CloseoutDriftRepairsAvailable > 0)
        {
            return Result(ClassificationRepairHostMetadata, mutationAllowed: true,
                $"intent-cli automation closeout-drift-check --repo {input.Repo} --write --format json",
                new[]
                {
                    $"closeout-drift-check detected {input.CloseoutDriftRepairsAvailable} queue item(s) with linked_issue but no linked_pr where GitHub confirms exactly one merged closing PR (G358).",
                    "Run closeout-drift-check --write to infer and persist the linked_pr, mark the item Completed, and append pr-merged / closeout-recorded events.",
                    "Re-run the host loop after the repair to confirm the item is resolved and no further drift remains."
                },
                "Closeout drift (G358): linked_issue-only items have an inferred closing PR — run closeout-drift-check --write.");
        }

        // 6. publish next issue
        if (input.NextSliceIssueCutReady && input.PublishNextSliceExecutionUnit is { } unit && !input.OpenIntentTargetPrOrIssueExists)
        {
            return Result(ClassificationPublishNextIssue, mutationAllowed: true,
                $"intent-cli packet draft --execution-unit {unit} --target-repo {input.Repo} --format json",
                new[]
                {
                    $"intent next-slice --dry-run reports `issue-cut-ready` for execution unit `{unit}`.",
                    "WIP cap is empty (no open intent-target issue/PR). Proceed with the deterministic publish chain: packet draft → issue publish-flow --write → automation issue-publish --write."
                },
                $"Next slice ready to publish: `{unit}`.");
        }

        // 7. hard clarification
        if (input.HardClarificationOpen)
        {
            return Result(ClassificationHardClarification, mutationAllowed: false,
                $"intent-cli clarification next --domain {input.Domain ?? "<DOMAIN>"} --format json",
                new[]
                {
                    "Hard clarification is open; the host loop must stop and surface the question to the operator before mutating.",
                    "Use `clarification next` to retrieve the structured question and `clarification answer --write` after the operator answers."
                },
                "Hard clarification open — surface to operator before any mutation.");
        }

        if (input.OpenIntentTargetPrOrIssueExists && !input.AnyChildWorkerLeaseHeld)
        {
            return Result(ClassificationWipCapBlocked, mutationAllowed: false,
                $"intent-cli automation host-review-diagnostics --repo {input.Repo} --format json",
                new[]
                {
                    "An open `intent-target` PR/issue exists, so the WIP cap blocks new next-slice publication.",
                    "No child worker lease is currently visible; re-run host-review-diagnostics to distinguish waiting from a stale label/metadata condition."
                },
                "WIP cap blocked by open intent-target work — no new issue publication.");
        }

        // 8. wait-for-child (open intent-target work currently held by a worker)
        if (input.OpenIntentTargetPrOrIssueExists && input.AnyChildWorkerLeaseHeld)
        {
            return Result(ClassificationWaitForChild, mutationAllowed: false,
                $"intent-cli automation host-review-diagnostics --repo {input.Repo} --format json",
                new[]
                {
                    "An `intent-target` PR/issue is currently in-progress under a child worker lease (`intent-issue-in-progress` / `intent-pr-update-in-progress`).",
                    "Wait for the child loop to complete and re-run the host wake; re-running diagnostics is read-only and safe."
                },
                "Child worker holds the lease — wait, no host mutation needed.");
        }

        // 9. G328 design-needed: next-slice probe says no prepared
        //    packet exists and runtime creation is not permitted —
        //    the next move is a design-side packet draft, NOT
        //    waiting on a child or sitting idle. Surfaces ABOVE
        //    true-idle so the host loop reports the gap explicitly.
        if (input.DesignNeeded)
        {
            return Result(ClassificationDesignNeeded, mutationAllowed: false,
                $"intent-cli intent next-slice --dry-run --domain {input.Domain ?? "<DOMAIN>"} --runtime-creation-allowed --format json",
                new[]
                {
                    "intent next-slice --dry-run reports `design-needed`: no prepared packet exists and runtime creation is not permitted on this host.",
                    "The next move is a design-side packet draft on the MyIntentHost workspace, not a host-loop wait. Re-run next-slice with `--runtime-creation-allowed` only when this workspace is a review-runtime authorized to author packets."
                },
                "Design-needed — no prepared packet; draft one on the design workspace.");
        }

        // 10. true idle
        return Result(ClassificationTrueIdle, mutationAllowed: false,
            recommendedCommand: null,
            new[] { "No actionable host signal across review/publish/repair/clarification lanes." },
            "True idle — no mutation required.");
    }

    private static HostLoopNextActionResult Result(
        string classification,
        bool mutationAllowed,
        string? recommendedCommand,
        IReadOnlyList<string> evidence,
        string summary) =>
        new()
        {
            Classification = classification,
            MutationAllowed = mutationAllowed,
            RecommendedCommand = recommendedCommand,
            Evidence = evidence,
            Summary = summary
        };
}

internal sealed record HostLoopNextActionInput
{
    public required string Repo { get; init; }
    public string? Domain { get; init; }

    /// <summary>G304 / automation doctor stale-host-cli signal.</summary>
    public bool StaleCli { get; init; }

    /// <summary>
    /// G304/G306: <see cref="HostSyncPreflightAnalyzer"/> classification
    /// (<c>clean</c>, <c>behind-origin</c>, <c>dirty-host-durable-state</c>,
    /// <c>dirty-unrelated-submodule</c>, <c>dirty-mixed</c>) or null when
    /// not captured.
    /// </summary>
    public string? SyncClassification { get; init; }

    /// <summary>G306: shortcut for safe-stash signal independent of classification.</summary>
    public bool SafeStashRequired { get; init; }

    /// <summary>An actionable review PR captured by host-review-preflight, or null.</summary>
    public ActionableReviewPr? ActionableReviewPr { get; init; }

    /// <summary>
    /// G319: an open PR that already carries `intent-pr-approved` and
    /// is pending the merge + closeout continuation. When set, the
    /// analyzer surfaces this BEFORE wip-cap-blocked so the host loop
    /// completes the approved PR before stopping for "an open
    /// intent-target exists".
    /// </summary>
    public ApprovedPrContinuation? ApprovedPrPendingMergeCloseout { get; init; }

    /// <summary>G303: count of high-confidence linked-ref repairs available.</summary>
    public int PublishRecoveryRepairsAvailable { get; init; }

    /// <summary>G307: count of stale lifecycle drift entries available for safe upgrade.</summary>
    public int PublishLifecycleDriftCount { get; init; }

    /// <summary>
    /// G358: count of queue items where <c>linked_issue</c> is present but
    /// <c>linked_pr</c> is absent, and GitHub confirms exactly one merged
    /// closing PR in the target repo — deterministic closeout-drift repairs.
    /// When &gt; 0 the analyzer surfaces <c>repair-host-metadata</c> (priority 5)
    /// recommending <c>closeout-drift-check --write</c>.
    /// </summary>
    public int CloseoutDriftRepairsAvailable { get; init; }

    /// <summary>True when intent next-slice --dry-run reports `issue-cut-ready`.</summary>
    public bool NextSliceIssueCutReady { get; init; }

    /// <summary>Execution unit ready to publish (set when NextSliceIssueCutReady is true).</summary>
    public string? PublishNextSliceExecutionUnit { get; init; }

    /// <summary>True when any open intent-target PR or issue exists in the target repo.</summary>
    public bool OpenIntentTargetPrOrIssueExists { get; init; }

    /// <summary>True when any open intent-target PR/issue carries an in-progress lease label.</summary>
    public bool AnyChildWorkerLeaseHeld { get; init; }

    /// <summary>True when a Hard Clarification (open structured clarification or markdown blocker) is recorded.</summary>
    public bool HardClarificationOpen { get; init; }

    /// <summary>
    /// G328: true when the `intent next-slice --dry-run` probe
    /// reports `design-needed` (no prepared packet AND runtime
    /// creation not permitted). Routes the analyzer to the
    /// `design-needed` classification before falling through to
    /// `true-idle`.
    /// </summary>
    public bool DesignNeeded { get; init; }

    /// <summary>
    /// G361: true when <c>automation durable-state-preflight</c>
    /// reports that the dirty host-durable-state is actually a
    /// complete prepared packet directory under
    /// <c>.intent-cli/issues/&lt;unit&gt;/</c> classified
    /// <c>prepared-packet-commit-ready</c>. Combined with a
    /// <see cref="SyncClassification"/> of
    /// <c>dirty-host-durable-state</c> / <c>dirty-mixed</c>, routes
    /// the analyzer to the <c>prepared-packet-commit-ready</c> lane
    /// instead of the generic dirty-host-state stop.
    /// </summary>
    public bool PreparedPacketCommitReadyAvailable { get; init; }

    /// <summary>
    /// G361: execution unit of the commit-ready packet directory
    /// (e.g. <c>Z4R-G3</c>). Used in the evidence + summary lines
    /// emitted on the <c>prepared-packet-commit-ready</c> lane.
    /// Null when no such directory is present.
    /// </summary>
    public string? PreparedPacketExecutionUnit { get; init; }
}

internal sealed record ActionableReviewPr
{
    public required int Number { get; init; }
    public required string Url { get; init; }
}

/// <summary>
/// G319: open PR carrying `intent-pr-approved` that the host loop must
/// continue through merge + closeout before any new next-slice publish.
/// Caller may pre-fetch additional state (draft, merge state, host
/// metadata readiness) and feed those into the analyzer; when all
/// blockers are absent, the analyzer selects the happy-path
/// <c>approved-pr-merge-closeout</c> classification.
/// </summary>
internal sealed record ApprovedPrContinuation
{
    public required int Number { get; init; }
    public required string Url { get; init; }

    /// <summary>
    /// True when the PR is currently a draft. G297 forbids merging a
    /// draft PR even if approval transitioned earlier; analyzer maps to
    /// <c>approved-pr-draft-blocked</c>.
    /// </summary>
    public bool IsDraft { get; init; }

    /// <summary>
    /// Optional `gh pr view --json mergeStateStatus` value, e.g.
    /// `CLEAN` / `BLOCKED` / `CONFLICTING` / `BEHIND` / `DIRTY` /
    /// `UNKNOWN`. When null, the analyzer treats it as unverified and
    /// proceeds on the happy path (the merge step itself will refuse if
    /// the live state is unsafe). When non-null and not `CLEAN`, the
    /// analyzer maps to <c>approved-pr-merge-blocked</c>.
    /// </summary>
    public string? MergeStateStatus { get; init; }

    /// <summary>
    /// True when the operator/preflight has determined the parent host
    /// metadata is not ready for closeout (e.g. queue-state linked_pr
    /// missing / linked_issue mismatch / packet directory missing).
    /// Maps to <c>approved-pr-metadata-blocked</c>.
    /// </summary>
    public bool HostMetadataBlocked { get; init; }

    /// <summary>
    /// Optional GitHub closing-reference issue number captured from
    /// `gh pr view --json closingIssuesReferences`. Surfaced in the
    /// happy-path evidence to remind the operator that
    /// <c>closeout pr --write</c> validates the G311 closing reference.
    /// </summary>
    public int? LinkedIssueNumber { get; init; }
}

internal sealed record HostLoopNextActionResult
{
    public required string Classification { get; init; }
    public required bool MutationAllowed { get; init; }
    public required string? RecommendedCommand { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
    public required string Summary { get; init; }
}
