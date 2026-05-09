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
    public const string ClassificationRepairHostMetadata = "repair-host-metadata";
    public const string ClassificationPublishNextIssue = "publish-next-issue";
    public const string ClassificationHardClarification = "hard-clarification";
    public const string ClassificationWaitForChild = "wait-for-child";
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

        // 2. dirty host state (G304/G306 hard stop)
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

        // 9. true idle
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

    /// <summary>G303: count of high-confidence linked-ref repairs available.</summary>
    public int PublishRecoveryRepairsAvailable { get; init; }

    /// <summary>G307: count of stale lifecycle drift entries available for safe upgrade.</summary>
    public int PublishLifecycleDriftCount { get; init; }

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
}

internal sealed record ActionableReviewPr
{
    public required int Number { get; init; }
    public required string Url { get; init; }
}

internal sealed record HostLoopNextActionResult
{
    public required string Classification { get; init; }
    public required bool MutationAllowed { get; init; }
    public required string? RecommendedCommand { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
    public required string Summary { get; init; }
}
