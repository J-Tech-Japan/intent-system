using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G454: classification of a host-loop wake that stopped after a single
/// label transition (review-start / approved / request-update). Encodes
/// whether the stop is terminal for the wake and what the next command is,
/// so an LLM agent following installed guidance does not mistake an
/// intermediate label state for a completed workflow.
/// </summary>
internal sealed record HostLoopStopClassification
{
    [JsonPropertyName("stop_state")]
    public required string StopState { get; init; }

    /// <summary>
    /// <c>true</c> when the wake legitimately ends at this state with no
    /// continuation owed by the host loop (e.g. <c>request-update</c>, which
    /// waits on the child worker). <c>false</c> when the agent MUST continue
    /// in the same wake (e.g. <c>approved</c>, which still owes merge +
    /// closeout).
    /// </summary>
    [JsonPropertyName("terminal")]
    public required bool Terminal { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }

    [JsonPropertyName("next_command")]
    public required string NextCommand { get; init; }
}

/// <summary>
/// G454: machine-usable contract for a host-loop blocker. Recoverable
/// blockers carry a concrete <see cref="RepairCommand"/>, the stage to
/// resume from, the exact retry command, and whether the repair is allowed
/// to mutate state. Unrecoverable blockers set <see cref="Terminal"/> and
/// name the operator action required instead of a repair command.
/// </summary>
internal sealed record HostLoopBlockerContract
{
    [JsonPropertyName("blocker_category")]
    public required string BlockerCategory { get; init; }

    [JsonPropertyName("recoverable")]
    public required bool Recoverable { get; init; }

    /// <summary>
    /// <c>true</c> when no safe in-loop repair exists and the wake must stop
    /// with an explicit operator hand-off.
    /// </summary>
    [JsonPropertyName("terminal")]
    public required bool Terminal { get; init; }

    [JsonPropertyName("mutation_allowed")]
    public required bool MutationAllowed { get; init; }

    [JsonPropertyName("repair_command")]
    public string? RepairCommand { get; init; }

    [JsonPropertyName("resume_stage")]
    public string? ResumeStage { get; init; }

    [JsonPropertyName("retry_command")]
    public string? RetryCommand { get; init; }

    [JsonPropertyName("operator_action_required")]
    public string? OperatorActionRequired { get; init; }
}

/// <summary>
/// G454: the canonical, machine-usable host-loop continuation contract.
/// Installed guidance surfaces (host-loop prompt, <c>task review-pr</c>)
/// advertise this same structured object so any LLM agent — Claude, Codex,
/// Copilot, OpenCode, Cursor — can follow the continuation rules without
/// learning stale conversation history:
/// <list type="bullet">
///   <item><c>intent-pr-approved</c> is intermediate, not terminal, unless
///   a concrete gate blocks merge.</item>
///   <item>Approval continues to merge, merged-state verification, and
///   <c>intent-cli closeout pr</c> when all gates pass.</item>
///   <item>Recoverable blockers return a repair command, resume stage, retry
///   command, and mutation-allowed flag — repair and retry ONCE before
///   declaring blocked.</item>
///   <item>A wake that stopped after a partial label transition is classified
///   terminal / non-terminal with the next command to run.</item>
/// </list>
/// </summary>
internal sealed record HostLoopContinuationContractModel
{
    [JsonPropertyName("contract_id")]
    public required string ContractId { get; init; }

    [JsonPropertyName("approval_is_terminal")]
    public required bool ApprovalIsTerminal { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("continuation_after_approval")]
    public required IReadOnlyList<string> ContinuationAfterApproval { get; init; }

    [JsonPropertyName("retry_once_policy")]
    public required string RetryOncePolicy { get; init; }

    [JsonPropertyName("stop_classifications")]
    public required IReadOnlyList<HostLoopStopClassification> StopClassifications { get; init; }

    [JsonPropertyName("blocker_contracts")]
    public required IReadOnlyList<HostLoopBlockerContract> BlockerContracts { get; init; }

    [JsonPropertyName("rail_recovery")]
    public required IReadOnlyList<string> RailRecovery { get; init; }
}

/// <summary>
/// G454: single source of truth for the host-loop continuation contract.
/// Centralizing the contract means the host-loop guidance prompt
/// (<see cref="GuidePromptMatrixCommand"/>) and the <c>task review-pr</c>
/// planner (<see cref="TaskCommand"/>) advertise the same structured object
/// with the same canonical ids, and tests can assert the continuation rules
/// without re-stating prose in two places.
/// </summary>
internal static class HostLoopContinuationContract
{
    public const string ContractId = "host-loop-continuation/v1";

    // Stop states (also the values the agent reads back from a host-loop
    // transition result `transition` / wake `wake_action`).
    public const string StopReviewStart = "review-start";
    public const string StopApproved = "approved";
    public const string StopAwaitingOperatorMerge = "awaiting-operator-merge";
    public const string StopRequestUpdate = "request-update";

    // Recoverable host-owned blocker categories (aligned with the host-side
    // safe-repair surface: workspace-safe-dirty / review-linkage-gap /
    // stale-review-lease / closeout-drift-repair).
    public const string BlockerWorkspaceSafeDirty = "workspace-safe-dirty";
    public const string BlockerReviewLinkageGap = "review-linkage-gap";
    public const string BlockerStaleReviewLease = "stale-review-lease";
    public const string BlockerCloseoutDriftRepair = "closeout-drift-repair";

    // Unrecoverable blockers — operator action required, never auto-repaired.
    public const string BlockerMergeConflict = "merge-conflict";
    public const string BlockerCiFailing = "ci-failing";

    public static HostLoopContinuationContractModel Default { get; } = new()
    {
        ContractId = ContractId,
        ApprovalIsTerminal = false,
        Summary =
            "Workflow labels are state markers, not completion boundaries. `intent-pr-approved` is an "
            + "INTERMEDIATE state: a direct lane continues through merge, merged-state verification, and "
            + "`intent-cli closeout pr`; an `operator-merge` lane stops at the visible patient "
            + "`awaiting-operator-merge` state and only resumes closeout after a human merge is detected. "
            + "Concrete gates include draft, failing CI, merge conflict, base-policy mismatch, and missing linkage. "
            + "A wake that stops at a label state "
            + "without either completing the continuation or naming a concrete blocking gate is incomplete.",
        ContinuationAfterApproval = new[]
        {
            "After `automation pr-transition --transition approved --write`, do NOT stop at the `intent-pr-approved` label.",
            "Read the immutable routing snapshot landing_mode before any landing action. On `operator-merge`, never invoke a merge path: enter `awaiting-operator-merge`, notify design once, and wait without urging or age escalation.",
            "For a direct lane only, merge via the host's existing merge step (the approval transition does not merge).",
            "For a direct lane, verify the merge landed: `IS_MERGED=$(gh pr view <n> --repo <r> --json merged --jq .merged)`.",
            "Only when `IS_MERGED == true`, run `intent-cli closeout pr --pr <n> --repo <r> --pr-merged $IS_MERGED --write --format json` (G297 — closeout refuses `--pr-merged false`, so a blocked merge can never record closeout).",
            "Stage 2 (next-slice publish) is gated on `closeout pr --write` succeeding for THIS wake; never publish a new child issue after a merge that did not actually land.",
            "If merge is blocked by a concrete gate, classify the blocker (see `blocker_contracts`) and stop with that gate named — do NOT silently leave the PR approved-but-unmerged."
        },
        RetryOncePolicy =
            "For a recoverable blocker, apply the `repair_command`, then re-run the `retry_command` (or resume from "
            + "`resume_stage`) EXACTLY ONCE. If the blocker persists after one repair+retry, stop and surface it as an "
            + "operator-action-required blocker; never loop the same repair, and never escalate to raw label mutation.",
        StopClassifications = new[]
        {
            new HostLoopStopClassification
            {
                StopState = StopReviewStart,
                Terminal = false,
                Meaning =
                    "`intent-pr-reviewing` means review is IN PROGRESS, not complete. Stopping here leaves the PR "
                    + "leased with no decision recorded.",
                NextCommand =
                    "Continue the SAME wake: gather evidence (`review closeout-plan`, `guide review`, "
                    + "`automation base-branch-check`, diff check), then take the approve or request-update decision."
            },
            new HostLoopStopClassification
            {
                StopState = StopApproved,
                Terminal = false,
                Meaning =
                    "`intent-pr-approved` is INTERMEDIATE. Inspect the immutable landing mode: direct still owes merge "
                    + "and closeout in this wake, while operator-merge enters the patient waiting state.",
                NextCommand =
                    "Direct: continue the SAME wake by merging, verifying `merged == true`, then running "
                    + "`intent-cli closeout pr --pr <n> --repo <r> --pr-merged true --write --format json`. "
                    + "Operator-merge: enter `awaiting-operator-merge`; do not merge or close out yet."
            },
            new HostLoopStopClassification
            {
                StopState = StopAwaitingOperatorMerge,
                Terminal = true,
                Meaning =
                    "Approved + green is waiting on the lane's human landing authority. This is visible patient state, "
                    + "not review debt or a stall; elapsed time never creates an automation action.",
                NextCommand =
                    "None until a human merge is detected. Then resume automatically at "
                    + "`intent-cli closeout pr --pr <n> --repo <r> --pr-merged true --write --format json`."
            },
            new HostLoopStopClassification
            {
                StopState = StopRequestUpdate,
                Terminal = true,
                Meaning =
                    "`intent-pr-request-update` legitimately ENDS the wake: the host has handed an actionable comment "
                    + "back to the child worker and owns no further step until the child pushes a fix.",
                NextCommand =
                    "None this wake. The PR re-enters host review via `intent-pr-rereview-ready` after the child "
                    + "worker repairs and pushes; the next host wake re-selects it."
            }
        },
        BlockerContracts = new[]
        {
            new HostLoopBlockerContract
            {
                BlockerCategory = BlockerWorkspaceSafeDirty,
                Recoverable = true,
                Terminal = false,
                MutationAllowed = true,
                RepairCommand = "intent-cli automation workspace-guard --mode begin --write --format json",
                ResumeStage = "review-start",
                RetryCommand = "intent-cli automation host-review-preflight --repo <r> --format json",
                OperatorActionRequired = null
            },
            new HostLoopBlockerContract
            {
                BlockerCategory = BlockerReviewLinkageGap,
                Recoverable = true,
                Terminal = false,
                MutationAllowed = true,
                RepairCommand = "intent-cli review closeout-plan --pr <n> --repo <r> --write-recovered-linkage --format json",
                ResumeStage = "closeout",
                RetryCommand = "intent-cli closeout pr --pr <n> --repo <r> --pr-merged true --write --format json",
                OperatorActionRequired = null
            },
            new HostLoopBlockerContract
            {
                BlockerCategory = BlockerStaleReviewLease,
                Recoverable = true,
                Terminal = false,
                MutationAllowed = true,
                RepairCommand = "intent-cli automation pr-transition --transition review-release --repo <r> --pr <n> --write --format json",
                ResumeStage = "review-start",
                RetryCommand = "intent-cli automation host-review-preflight --repo <r> --format json",
                OperatorActionRequired = null
            },
            new HostLoopBlockerContract
            {
                BlockerCategory = BlockerCloseoutDriftRepair,
                Recoverable = true,
                Terminal = false,
                MutationAllowed = true,
                RepairCommand = "intent-cli automation state-doctor --repo <r> --write --format json",
                ResumeStage = "closeout",
                RetryCommand = "intent-cli closeout pr --pr <n> --repo <r> --pr-merged true --write --format json",
                OperatorActionRequired = null
            },
            new HostLoopBlockerContract
            {
                BlockerCategory = BlockerMergeConflict,
                Recoverable = false,
                Terminal = true,
                MutationAllowed = false,
                RepairCommand = null,
                ResumeStage = null,
                RetryCommand = null,
                OperatorActionRequired =
                    "Merge is blocked by a conflict the host loop must not auto-resolve. Leave the PR approved, do NOT "
                    + "run closeout, and surface a structured operator stop: the child branch needs a rebase/merge from "
                    + "the implementation worker."
            },
            new HostLoopBlockerContract
            {
                BlockerCategory = BlockerCiFailing,
                Recoverable = false,
                Terminal = true,
                MutationAllowed = false,
                RepairCommand = null,
                ResumeStage = null,
                RetryCommand = null,
                OperatorActionRequired =
                    "Required checks are failing. Do NOT approve or merge. Route back to the child worker via "
                    + "`automation pr-transition --transition request-update --write` with the failing-check evidence, "
                    + "or surface an operator stop when the failure is infrastructural."
            }
        },
        RailRecovery = new[]
        {
            "If the previous wake stopped after a partial step (review-start, approved, or a blocker) without completing the continuation, treat this wake as a RAIL-RECOVERY wake — do not re-discover unrelated work first.",
            "Re-read the current contract from installed `intent-cli` guidance (do NOT rely on stale conversation memory): `intent-cli automation summary` and the host-loop guidance prompt.",
            "Re-derive the PR's true state from labels + GitHub (`gh pr view <n> --json merged,isDraft,mergeable,reviewDecision`), then match it to a `stop_classifications` entry and run that entry's `next_command`.",
            "For a PR already at `intent-pr-approved`, inspect landing_mode before acting: direct continues to merge + `closeout pr`; operator-merge waits patiently and resumes with closeout only after a human merge is detected. Neither path re-reviews solely because approval already exists.",
            "For a recoverable blocker, apply the `repair_command` and retry ONCE (see `retry_once_policy`) before declaring the PR blocked.",
            "Only after the in-flight PR reaches a terminal state (merged + closeout, or a named unrecoverable blocker) may the wake consider cutting the next slice."
        }
    };

    /// <summary>
    /// G454: render the continuation contract as a markdown section for the
    /// host-loop guidance prompt, so the prose operators read and the
    /// structured object controllers parse stay in lock-step.
    /// </summary>
    public static string RenderMarkdown(string targetRepoPlaceholder)
    {
        ArgumentNullException.ThrowIfNull(targetRepoPlaceholder);
        var model = Default;
        var lines = new List<string>
        {
            $"### Host-loop continuation contract ({model.ContractId}, G454)",
            string.Empty,
            model.Summary,
            string.Empty,
            "**Approval is not terminal.** After approval, continue the SAME wake:"
        };
        foreach (var entry in model.ContinuationAfterApproval)
        {
            lines.Add($"- {entry.Replace("<r>", targetRepoPlaceholder, StringComparison.Ordinal)}");
        }

        lines.Add(string.Empty);
        lines.Add("**Partial-stop classification.** A wake that stopped after one label transition is classified:");
        foreach (var stop in model.StopClassifications)
        {
            var terminalText = stop.Terminal ? "terminal-for-wake" : "NOT terminal — continue this wake";
            lines.Add($"- `{stop.StopState}` → {terminalText}. {stop.Meaning} Next: {stop.NextCommand.Replace("<r>", targetRepoPlaceholder, StringComparison.Ordinal)}");
        }

        lines.Add(string.Empty);
        lines.Add($"**Repair-and-retry-once.** {model.RetryOncePolicy}");
        foreach (var blocker in model.BlockerContracts)
        {
            if (blocker.Recoverable)
            {
                lines.Add(
                    $"- `{blocker.BlockerCategory}` (recoverable, mutation_allowed={blocker.MutationAllowed.ToString().ToLowerInvariant()}): "
                    + $"repair `{blocker.RepairCommand?.Replace("<r>", targetRepoPlaceholder, StringComparison.Ordinal)}`, "
                    + $"resume at `{blocker.ResumeStage}`, retry `{blocker.RetryCommand?.Replace("<r>", targetRepoPlaceholder, StringComparison.Ordinal)}` once.");
            }
            else
            {
                lines.Add(
                    $"- `{blocker.BlockerCategory}` (UNRECOVERABLE, terminal): {blocker.OperatorActionRequired}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("**Rail recovery** (previous wake stopped mid-workflow):");
        foreach (var rail in model.RailRecovery)
        {
            lines.Add($"- {rail}");
        }

        return string.Join('\n', lines);
    }
}
