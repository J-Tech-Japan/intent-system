using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G271: Read-only <c>intent-cli guide prompt-matrix</c>. Returns a canonical
/// matrix of the four operational modes: recurring child implement/update loop,
/// recurring host review/next-slice loop, one-shot child implement/update, and
/// one-shot host review/next-slice. Each entry includes paste-ready prompt text
/// and subordinate <c>intent-cli guide</c> commands. Never mutates state.
/// Never launches an AI provider.
/// </summary>
internal static class GuidePromptMatrixCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeChildLoop = "child-loop";
    private const string ModeHostLoop = "host-loop";
    private const string ModeChildOneshot = "child-oneshot";
    private const string ModeHostOneshot = "host-oneshot";

    private const string KindLoop = "loop";
    private const string KindOneshot = "oneshot";

    private const string TargetChild = "child";
    private const string TargetHost = "host";

    private const string FrequencyGuidanceRecurring =
        "5 minutes for high-frequency local loops; ~20 minutes for low-frequency local loops; ask the operator for frequency before scheduling";

    private const string FrequencyGuidanceOneshot =
        "N/A — one-shot execution; frequency is forbidden";

    private const string AgentClaude = "claude";
    private const string AgentCodex = "codex";
    private const string AgentGeneric = "generic";

    /// <summary>
    /// G345: GitHub Copilot cloud/assignment agent. Assignment-oriented:
    /// triggered by GitHub issue assignment or PR mention. Does NOT
    /// participate in the local 5-minute heartbeat loop, does NOT exec
    /// host <c>intent-cli</c>, and does NOT touch host <c>.intent-cli/</c>
    /// metadata. <c>"copilot"</c> is kept as the canonical spelling;
    /// <c>"copilot-cloud"</c> is accepted as an alias (G349).
    /// </summary>
    private const string AgentCopilot = "copilot";

    /// <summary>
    /// G349: alias for the cloud/assignment Copilot path. Accepted on input;
    /// normalised to <c>"copilot"</c> internally so existing downstream logic
    /// is unchanged.
    /// </summary>
    private const string AgentCopilotCloud = "copilot-cloud";

    /// <summary>
    /// G349: local Copilot coding-agent running in a host cwd. Unlike the
    /// cloud/assignment path, a local Copilot agent CAN exec <c>intent-cli</c>
    /// in the host repo and perform host-owned intent/clarification/packet/
    /// publish workflows. In a child implementation cwd it remains
    /// host-state-free (same restrictions as cloud Copilot child work).
    /// </summary>
    private const string AgentCopilotLocal = "copilot-local";

    /// <summary>
    /// G279 follow-up (PR #662): the rendered prompts only contain meaningful
    /// scheduler instructions when <c>--frequency</c> matches the documented
    /// shape. Accept positive integers followed by the unit token: <c>m</c>
    /// (minutes) or <c>h</c> (hours). Anything else is rejected with a usage
    /// error so unparseable values like <c>--frequency bananas</c> never reach
    /// the rendered guidance.
    /// </summary>
    private static readonly Regex FrequencyPattern = new(
        @"^[1-9][0-9]*[mh]$",
        RegexOptions.Compiled);

    private const string TopologySameRepo = "same-repo";

    private const string UsageLine =
        "Usage: intent-cli guide prompt-matrix [--mode child-loop|host-loop|child-oneshot|host-oneshot] [--topology same-repo] [--domain <name>] [--target-repo <owner/repo>] [--agent claude|codex|generic|copilot|copilot-cloud|copilot-local] [--frequency <NNm|NNh>] [--base-branch-policy direct-main|main-ai] [--format markdown|json]";

    private static readonly string[] ForbiddenSources =
    [
        "intents/rules/**",
        "local skill files (gh-issue-to-pr, gh-fix-pr-comment, etc.)",
        "copied prompt files"
    ];

    /// <summary>
    /// G348: additional forbidden sources added to child prompts when
    /// <c>--topology same-repo</c> is set. In same-repo topology the
    /// host metadata paths are visible in the child checkout but MUST
    /// NOT be read or mutated by implementation agents.
    /// </summary>
    private static readonly string[] SameRepoAdditionalForbiddenSources =
    [
        ".intent-cli/** (visible in same-repo checkout but FORBIDDEN for implementation agents — do not read, write, or commit host queue-state, runs.jsonl, packet artifacts, or config from an implementation PR branch)",
        "intents/** (visible in same-repo checkout but FORBIDDEN for implementation work — intent authoring, clarification recording, and spec updates are host-design tasks, not implementation tasks)"
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var mode, out var format, out var domain, out var targetRepo, out var agent, out var frequency, out var baseBranchPolicy, out var topology, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var entries = BuildEntries(context, mode, domain, targetRepo, agent, frequency, baseBranchPolicy, topology);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            if (mode is not null)
            {
                // Single entry
                writer.Write(JsonSerializer.Serialize(entries[0], JsonOptions));
            }
            else
            {
                writer.Write(JsonSerializer.Serialize(entries, JsonOptions));
            }
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, entries);
        }

        return 0;
    }

    private static IReadOnlyList<GuidePromptMatrixEntry> BuildEntries(
        CliContext context,
        string? mode,
        string? domain,
        string? targetRepo,
        string? agent,
        string? frequency,
        string? baseBranchPolicy,
        string? topology)
    {
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;
        var targetRepoPlaceholder = string.IsNullOrWhiteSpace(targetRepo) ? "<TARGET-REPO>" : targetRepo;
        // G346: when --base-branch-policy is omitted, fall back to the persisted
        // host/domain config value (default direct-main when not configured).
        var resolvedPolicy = string.IsNullOrWhiteSpace(baseBranchPolicy)
            ? context.Config.Project.BaseBranchPolicy
            : baseBranchPolicy;

        var isSameRepo = string.Equals(topology, TopologySameRepo, StringComparison.Ordinal);

        // G349: normalise once for dispatch. copilot-cloud → copilot.
        // copilot-local keeps its own string so the per-mode dispatch below
        // can route to the new local-host builders.
        var resolvedAgentForDispatch = NormalizeAgent(agent);
        var isCopilotLocal = string.Equals(resolvedAgentForDispatch, AgentCopilotLocal, StringComparison.Ordinal);
        var isCopilotCloud = string.Equals(resolvedAgentForDispatch, AgentCopilot, StringComparison.Ordinal);

        var all = new[]
        {
            // child-loop: copilot-local child cwd stays host-state-free.
            isCopilotCloud
                ? BuildCopilotChildLoop(resolvedPolicy)
                : isCopilotLocal
                    ? BuildCopilotLocalChildLoop(resolvedPolicy, isSameRepo)
                    : BuildChildLoop(domainPlaceholder, agent, frequency, resolvedPolicy, isSameRepo),

            // host-loop: copilot-local CAN run intent-cli in host cwd.
            isCopilotCloud
                ? BuildCopilotUnsupportedHostMode(ModeHostLoop, KindLoop, TargetHost, resolvedPolicy)
                : isCopilotLocal
                    ? BuildCopilotLocalHostLoop(domainPlaceholder, targetRepoPlaceholder, resolvedPolicy)
                    : BuildHostLoop(domainPlaceholder, targetRepoPlaceholder, agent, frequency, resolvedPolicy),

            // child-oneshot: copilot-local child cwd stays host-state-free.
            isCopilotCloud
                ? BuildCopilotChildOneshot(resolvedPolicy)
                : isCopilotLocal
                    ? BuildCopilotLocalChildOneshot(resolvedPolicy, isSameRepo)
                    : BuildChildOneshot(domainPlaceholder, resolvedPolicy, isSameRepo),

            // host-oneshot: copilot-local CAN run intent-cli host steps.
            isCopilotCloud
                ? BuildCopilotHostOneshot(targetRepoPlaceholder, resolvedPolicy)
                : isCopilotLocal
                    ? BuildCopilotLocalHostOneshot(domainPlaceholder, targetRepoPlaceholder, resolvedPolicy)
                    : BuildHostOneshot(domainPlaceholder, targetRepoPlaceholder, resolvedPolicy)
        };

        if (mode is null)
        {
            return all;
        }

        return mode switch
        {
            ModeChildLoop => [all[0]],
            ModeHostLoop => [all[1]],
            ModeChildOneshot => [all[2]],
            ModeHostOneshot => [all[3]],
            _ => all
        };
    }

    /// <summary>
    /// G279: Frequency block for the recurring loop modes. When the operator
    /// supplies <c>--frequency</c> the rendered prompt names the resolved
    /// interval and the agent-specific scheduling primitive; when omitted the
    /// rendered prompt explicitly tells the agent to ask the operator before
    /// creating any cron / monitor / wakeup so a default interval is never
    /// silently chosen.
    /// </summary>
    private static string RenderFrequencyBlock(string? agent, string? frequency)
    {
        var resolvedAgent = NormalizeAgent(agent);
        if (string.IsNullOrWhiteSpace(frequency))
        {
            // G314 review feedback (PR #732): the local scheduling contract
            // must surface BEFORE the operator resolves `--frequency`. The
            // typical operator path is to ask the agent to set up a loop
            // without naming an interval; the agent calls
            // `prompt-matrix` to learn the contract, then asks the operator
            // for the interval. If this branch only emits the short
            // paragraph, the agent never sees the agent-specific
            // current-thread / `/loop` requirement and may pick a
            // separate-thread or remote schedule before the interval is
            // even resolved. So we emit the same per-agent contract here
            // with `<frequency>` as a placeholder, plus the standard
            // forbidden list.
            var unresolvedSchedulingContract = RenderLocalSchedulingContract(resolvedAgent, "<frequency>");
            return
$@"IMPORTANT — frequency is unresolved; ask the operator for the desired frequency before creating any cron, monitor, or recurring wakeup. Never guess or use a tool-default interval.
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): ~20 minutes.
- Local same-thread loops are the baseline for workflows that depend on local paths or local `.intent-cli` packages. Cloud or new-thread schedulers cannot access local paths.

{unresolvedSchedulingContract}";
        }

        var schedulingHint = resolvedAgent switch
        {
            AgentClaude => $"Schedule via Claude Code same-thread `/loop {frequency} <prompt>` so each wake reuses this thread's local paths and `.intent-cli` packages.",
            AgentCodex => $"Schedule via Codex current-thread local automation / heartbeat at {frequency}; do not spawn a new thread or remote scheduler that cannot reach local paths.",
            _ => $"Schedule via the agent's local same-thread/current-thread automation at {frequency}; cloud or new-thread schedulers cannot access local paths."
        };

        var schedulingContract = RenderLocalSchedulingContract(resolvedAgent, frequency);

        return
$@"Frequency: {frequency} (operator-resolved). {schedulingHint}
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): ~20 minutes.

{schedulingContract}";
    }

    /// <summary>
    /// G314: agent-specific local scheduling contract surfaced as a
    /// stand-alone hard rule paragraph in the rendered prompt. The
    /// substance was previously buried as a single-sentence frequency
    /// hint, which let Codex spawn a new thread or remote scheduler
    /// that could not reach the operator's local <c>.intent-cli</c>
    /// host paths. This block names the wake primitive each agent
    /// must use, the local-path access requirement, and the explicit
    /// forbidden list (new thread / remote scheduler / cloud schedule
    /// / disconnected monitor) so a fresh agent has no path to a
    /// non-local schedule.
    /// </summary>
    private static string RenderLocalSchedulingContract(string resolvedAgent, string frequency) =>
        resolvedAgent switch
        {
            AgentClaude =>
$@"**Local scheduling contract (G314)**: this loop runs **same-thread** in the active Claude Code session via `/loop {frequency} <prompt>`. Each wake reuses this thread's local filesystem paths (host repo cwd, child worktree, `.intent-cli/` packages); a different thread / new chat / remote process cannot reach those paths. Do NOT create a new Claude Code thread, a separate chat, a remote/cloud scheduler, an external cron, an out-of-process monitor, or any automation that cannot exec the host's local `intent-cli` against the host repo. If `/loop` is unavailable in the current session, surface the gap to the operator instead of substituting a non-local schedule.",
            AgentCodex =>
$@"**Local scheduling contract (G314)**: this loop runs **current-thread** in the active Codex session via local-automation / heartbeat at {frequency}. Each wake reuses this thread's local filesystem paths (host repo cwd, child worktree, `.intent-cli/` packages); a different thread / new chat / remote process cannot reach those paths. Do NOT spawn a new Codex thread, a separate chat, a remote/cloud scheduler, an external cron, an out-of-process monitor, or any automation that cannot exec the host's local `intent-cli` against the host repo. If current-thread heartbeat is unavailable in this session, surface the gap to the operator instead of substituting a non-local schedule.",
            _ =>
$@"**Local scheduling contract (G314)**: this loop runs in the **agent's current-thread / same-session** automation surface at {frequency}. Each wake reuses this thread's local filesystem paths (host repo cwd, child worktree, `.intent-cli/` packages); a different thread / new chat / remote process cannot reach those paths. Do NOT create a new thread / chat, a remote/cloud scheduler, an external cron, an out-of-process monitor, or any automation that cannot exec the host's local `intent-cli` against the host repo. If the agent has no same-thread loop primitive, surface the gap to the operator instead of substituting a non-local schedule."
        };

    private static string NormalizeAgent(string? agent)
    {
        if (string.IsNullOrWhiteSpace(agent))
        {
            return AgentGeneric;
        }
        var normalized = agent.Trim().ToLowerInvariant();
        // G349: copilot-cloud is an alias for copilot (cloud/assignment path).
        // copilot-local retains its own identity for dispatch.
        if (string.Equals(normalized, AgentCopilotCloud, StringComparison.Ordinal))
        {
            return AgentCopilot;
        }
        return normalized;
    }

    private static string ResolvedFrequencyGuidance(string? frequency) =>
        string.IsNullOrWhiteSpace(frequency)
            ? FrequencyGuidanceRecurring
            : $"{frequency} (operator-resolved)";

    private static string RenderBaseBranchPolicyBlock(string baseBranchPolicy, string targetRoleVerb)
    {
        var expected = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy);
        var description = BaseBranchPolicyContract.DescribePolicy(baseBranchPolicy);
        return
$@"Base branch policy: `{baseBranchPolicy}` (expected base branch: `{expected}`).
- {description}
- {targetRoleVerb} this policy mechanically: derive the PR base branch from `intent-cli` config (`base_branch_policy`), never from prompt memory. Use `intent-cli automation base-branch-check --repo <r> --pr <n> --policy {baseBranchPolicy} --actual-base $(gh pr view <n> --repo <r> --json baseRefName --jq .baseRefName) --format json` to flag mismatches.";
    }

    /// <summary>
    /// G348: same-repo topology block inserted into child prompts when
    /// <c>--topology same-repo</c> is set. The block names the two
    /// forbidden host-metadata path groups that are visible in a same-repo
    /// checkout but must NOT be read or mutated by implementation agents.
    /// </summary>
    private static string RenderSameRepoTopologyBlock() =>
$@"Same-repo topology (G348): the implementation repo and host intent metadata (`.intent-cli/`, `intents/`) are in the SAME repository. Both paths are visible in this checkout, but they are FORBIDDEN for implementation agents:
- `.intent-cli/**` — FORBIDDEN: do not read, write, or commit host queue-state, runs.jsonl, packet artifacts, or config from an implementation PR branch. Host metadata mutations go through `intent-cli automation` / `intent-cli worker` command surfaces only.
- `intents/**` — FORBIDDEN: intent authoring, clarification recording, and spec updates are host-design tasks. Do not edit intent tree files from an implementation PR branch.
- Implementation PRs MUST target the integration branch (e.g. `main-ai`), NOT `main`. Verify with: `gh pr view <n> --repo <r> --json baseRefName --jq .baseRefName` must equal the integration branch configured in `base_branch_policy`.";

    // G345: Copilot-specific base-branch block. Copilot MUST NOT invoke
    // `intent-cli` from the implementation side, so the policy is expressed
    // as a GitHub-facts-only check (`gh pr view ... --json baseRefName`) and
    // the canonical `intent-cli automation base-branch-check` mismatch flag
    // is explicitly marked host/operator-owned.
    private static string RenderCopilotBaseBranchPolicyBlock(string baseBranchPolicy)
    {
        var expected = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy);
        var description = BaseBranchPolicyContract.DescribePolicy(baseBranchPolicy);
        return
$@"Base branch policy: `{baseBranchPolicy}` (expected base branch: `{expected}`).
- {description}
- Verify the PR base branch via GitHub facts only: `gh pr view <n> --repo <owner>/<repo> --json baseRefName --jq .baseRefName` must equal `{expected}`. If it does not, do NOT mutate the PR or labels from the implementation side — surface the mismatch in a PR reply so the host operator can reconcile.
- Base-branch policy enforcement is HOST / operator-owned. Copilot does NOT invoke any `intent-cli` command from the implementation side; the host's intent review loop runs the mismatch check on its own cadence.";
    }

    private static GuidePromptMatrixEntry BuildChildLoop(string domainPlaceholder, string? agent, string? frequency, string baseBranchPolicy, bool isSameRepo = false)
    {
        var resolvedAgent = NormalizeAgent(agent);
        var frequencyBlock = RenderFrequencyBlock(agent, frequency);
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Honor");
        var sameRepoBlock = isSameRepo ? $"\n{RenderSameRepoTopologyBlock()}" : string.Empty;
        var prompt =
$@"Set up the child implementation loop for the repo in the current worktree. Run the loop body exactly once per wake; the operator or scheduler drives subsequent wakes.

{frequencyBlock}

{basePolicyBlock}{sameRepoBlock}

If the installed CLI surface is stale or any required automation command is missing, abort the wake before any mutation: `intent-cli automation doctor --format json` (or `automation host-review-preflight` reporting `stale-host-cli`) is the canonical signal — refresh the installed CLI; never fall back to raw `gh` label mutation. The installed CLI may come from a global dotnet tool install on `PATH` (e.g. `$HOME/.dotnet/tools/intent-cli`); that is the default local-testing route and the doctor reports `binary_source: path-global-tool` in that case. A cwd-local `.intent-cli/bin/intent-cli` shim still wins when present (`binary_source: cwd-local-shim`) and `INTENT_CLI_INSTALLED_PATH` pins a specific binary for version-specific tests (`binary_source: explicit-override`).

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — `primary` vs `support` vs `advanced` (`run`) vs `experimental` classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON for the parent intent domain.

Loop body (single wake; the operator drives subsequent wakes if any):
1. Save the child worktree path: `CHILD_WORKTREE=""$PWD""`. Confirm it is a git worktree root. Stop with `wrong-worktree` if not.
2. Resolve `<OWNER>/<REPO>` from the child cwd: `gh repo view --json nameWithOwner --jq .nameWithOwner` (fall back to `git remote get-url origin`).
3. `git fetch --all --prune` and `git status --short`. If dirty in a dedicated automation worktree, clean local residue (`git reset --hard`, `git clean -fd`, submodule reset). Never `git clean -fdx`. Never clean a personal/shared checkout.
4. From the child cwd (the child implementation loop does NOT require parent host root access — G333 child-cwd / GitHub-contract-only mode), run `intent-cli worker next-action --repo <OWNER>/<REPO> --github-only --format json`. Pass `--github-only` so the selector is pinned to the label-only data path and the result records the binding (`github_only: true`). Dispatch on `action`:
   - `none` → stop with `idle`.
   - `issue-to-pr` → claim with `intent-cli worker claim --kind issue --number <n> --repo <OWNER>/<REPO> --github-only --write --format json`, run the issue-to-PR workflow on the returned URL only, classify outcome, then `worker result-summary --kind issue-to-pr --repo <OWNER>/<REPO> ...` and `worker complete --kind issue --number <n> --repo <OWNER>/<REPO> --github-only --outcome <outcome> --write --format json`.
   - `pr-comment-fix` → claim with `intent-cli worker claim --kind pr --number <n> --repo <OWNER>/<REPO> --github-only --write --format json`, repair only the narrow requested change on the PR branch, classify outcome, then `worker result-summary --kind pr-comment-fix --repo <OWNER>/<REPO> ...` and `worker complete --kind pr --number <n> --repo <OWNER>/<REPO> --github-only --outcome <outcome> --write --format json`.

Host metadata gaps surfaced by `worker complete` (e.g. `linked_pr_synced: false` from child-cwd mode, G330) are HOST-owned blockers, not child instructions to enter the host repo. The child loop records the gap and moves on; parent host metadata reconciliation runs on the host/review-runtime loop via `intent-cli review closeout-plan --pr <n> --repo <OWNER>/<REPO> --write-recovered-linkage` (G329) — never from the child cwd.

Hard rules:
- Do not read `intents/rules/**`, local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, etc.), or copied prompt files for routine collaboration. Use `intent-cli guide ...` instead.
- Do not call `intent-cli run` from this loop. `run` is advanced runtime (integration smoke / replay / dogfooding), not the chat-first path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` / `intent-cli worker` commands. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Never apply `intent-target` from the child loop; it is host-owned.
- Never apply `intent-pr-created` to a PR; it is an issue-side completion marker.
- **PR draft state (G296)**: create child implementation/update PRs as **ready-for-review** (non-draft) by default. Do NOT pass `--draft` to `gh pr create` unless the operator explicitly asks for a draft. After opening the PR, pass the actual draft state into `worker result-summary --pr-draft true|false` so the host review loop can detect a draft PR before approval. If you must keep the PR draft (incomplete work, operator hold), do not transition the issue to ready-for-review states; mark the outcome accordingly and document the reason.
- **PR closing reference is mandatory (G311)**: when opening a child implementation PR for an issue selected by `worker next-action`, the PR body MUST include a deterministic GitHub closing reference to the source issue — `Closes #<issue>`, `Fixes #<issue>`, or `Resolves #<issue>` (case-insensitive; `#N` form, not bare links such as `see #N`). `worker complete --kind issue --outcome pr-created --pr <n> --write` validates this and refuses to mark complete when the reference is missing, points at a different issue, or names multiple distinct issues. If the gate refuses, repair the PR body via `gh pr edit <pr> --body-file <path-to-new-body>` so the body ends with `Closes #<issue>`, then re-run `worker complete`. Do NOT use raw label mutation or `gh issue close` to bypass the gate. For `pr-comment-fix` repairs the PR body must continue to carry the original closing reference; do not strip it during a repair commit.
- **Child cwd is GitHub-contract-only (G300 / G330 / G333)**: the implementation repo must NOT contain its own `.intent-cli/` and the child loop MUST NOT read parent host queue-state, runs.jsonl, packets, or intent metadata. `worker next-action` / `claim` / `complete` / `result-summary` are runnable from a child cwd because they take `--repo <owner/repo>` and use `intent-cli` only for GitHub label transitions. Pass `--github-only` to **assert** the strict child-loop contract (G333): the underlying selection/claim/complete data flow is already label-only, and `--github-only` records the binding on the result (`github_only: true`) and — for `worker complete` — refuses to touch parent durable state regardless of disk presence. From a child cwd (no parent root configured, no local `.intent-cli/queue-state.json`), `worker complete` auto-enters **child-cwd mode** (G330): the result records `child_cwd: true`, `linked_pr_synced: false`, and surfaces a pointer to the host-side recovery path `intent-cli review closeout-plan --pr <n> --repo <owner/repo> --write-recovered-linkage` (G329). Parent host metadata reconciliation is owned by the host/review-runtime loop, never by the child loop. Absence of `.intent-cli/` in the implementation repo is the expected steady state and MUST NOT by itself abort the child workflow (G305).
- **Worker selector is the source of truth (G305)**: issue and PR numbers come from `intent-cli worker next-action`, NEVER from operator-supplied prompt text. If the operator names a specific issue/PR, treat that as a hint only and confirm via `worker next-action` before mutating; if `worker next-action` returns `none` or a different target, stop with `idle` or use the `worker next-action` target rather than the operator hint. Do not invent issue/PR numbers from prompt memory.
- **Abort conditions (G305)**: stop immediately and surface the gap to the operator (do NOT silently fall back to ordinary GitHub review or raw label mutation) when ANY of the following hold:
  - the global `intent-cli` is missing from `PATH` or `intent-cli automation doctor --format json` reports `stale-host-cli` / a missing required surface;
  - `gh auth status` fails or GitHub network is unreachable for the target repo;
  - `worker next-action` cannot resolve a deterministic single target (e.g. ambiguous repo / multiple matches reported as warnings);
  - the resolved target's labels indicate another worker already holds the lease (`intent-issue-in-progress` / `intent-pr-update-in-progress`) and the operator has not authorized re-claiming;
  - `worker complete --write` returns errors that are not the expected `linked_pr_synced: false` queue-state warning.
- **Self-healing wake policy (G355)**: before reporting `idle` or `no-actionable`, ask intent-cli whether a deterministic repair is available: run `intent-cli worker issue-preflight --number <n> --repo <OWNER>/<REPO> --format json` (for issue targets) or `intent-cli worker pr-comment-preflight --pr <n> --repo <OWNER>/<REPO> --format json` (for PR targets) to get the canonical `safe_repair_available` signal. When the preflight returns `safe_repair_available: true`, follow the `safe_repair_category` and `recommended_repair_command` to apply the repair. Child-loop categories: `child-selector-label-gap` (re-apply the correct label via `intent-cli worker` commands — never edit raw GitHub labels). Host-only categories that MUST NOT be repaired by the child loop: `issue-publish-gap`, `review-linkage-gap`, `host-artifact-repair`, `drafted-packet-mechanical-gap`, `stale-review-lease`, `workspace-safe-dirty`, `closeout-drift-repair` (G356 — host-owned queue closeout; never repair parent durable state from the child loop) — if preflight returns any of these, surface it as `host-artifact-repair-required` and return to the host loop for handling. Do NOT repair host metadata paths (`.intent-cli/**`, `intents/**`) from the child loop. After applying the repair, retry the same selected action EXACTLY ONCE. When `safe_repair_available: false` or the preflight is not available, proceed with the normal dispatch/idle path.
- Process at most one action per wake.";

        var forbiddenSources = isSameRepo
            ? [.. ForbiddenSources, .. SameRepoAdditionalForbiddenSources]
            : (IReadOnlyList<string>)ForbiddenSources;

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildLoop,
            Kind = KindLoop,
            Target = TargetChild,
            FrequencyGuidance = ResolvedFrequencyGuidance(frequency),
            ForbiddenSources = forbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json"
            ],
            Prompt = prompt,
            Agent = resolvedAgent,
            Frequency = string.IsNullOrWhiteSpace(frequency) ? null : frequency,
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
            Topology = isSameRepo ? TopologySameRepo : null,
        };
    }

    private static GuidePromptMatrixEntry BuildHostLoop(string domainPlaceholder, string targetRepoPlaceholder, string? agent, string? frequency, string baseBranchPolicy)
    {
        var resolvedAgent = NormalizeAgent(agent);
        var frequencyBlock = RenderFrequencyBlock(agent, frequency);
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Closeout / merge expectation honors");
        var prompt =
$@"Set up the host review and next-slice loop for domain `{domainPlaceholder}` against `{targetRepoPlaceholder}`. Run the loop body exactly once per wake; the operator or scheduler drives subsequent wakes.

{frequencyBlock}

{basePolicyBlock}

If the installed CLI surface is stale or any required automation command is missing, abort the wake before any mutation: `intent-cli automation doctor --format json` (or `automation host-review-preflight` reporting `stale-host-cli`) is the canonical signal — refresh the installed CLI; never fall back to raw `gh` label mutation. The installed CLI may come from a global dotnet tool install on `PATH` (e.g. `$HOME/.dotnet/tools/intent-cli`); that is the default local-testing route and the doctor reports `binary_source: path-global-tool` in that case. A cwd-local `.intent-cli/bin/intent-cli` shim still wins when present (`binary_source: cwd-local-shim`) and `INTENT_CLI_INSTALLED_PATH` pins a specific binary for version-specific tests (`binary_source: explicit-override`).

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domainPlaceholder} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — verify WIP cap and clarification gates.

Loop body (single wake):
1. Confirm cwd is the parent host repo root. **Initial pull (G357)**: run `git pull --ff-only origin main` as the VERY FIRST action of every wake — before any intent-cli call, any read-only preflight, and before host-sync-preflight. This guarantees all queue-state, runs.jsonl, packet, and submodule gitlink reads see fresh parent durable state. If the pull fails (non-fast-forward, dirty worktree conflict, or no upstream), stop and surface the gap to the operator — do NOT proceed past this gate. **Pre-wake host sync (G304)**: AFTER the initial pull, run `intent-cli automation host-sync-preflight --format json`. Exit code 0 (`classification: clean`) is the only state that allows the wake to proceed past this gate. On `classification: behind-origin`, run `git pull --ff-only` and re-run the preflight. On `classification: dirty-host-durable-state`, do NOT immediately abort — run `intent-cli automation durable-state-preflight --format json` (G312). When it returns `classification: verified-commit-ready`, the dirty changes are deterministic forward-only metadata updates (e.g. `linked_pr` added to `.intent-cli/queue-state.json`, append-only `.intent-cli/runs.jsonl` events): `git pull --ff-only`, stage ONLY the verified paths returned by the preflight (`git add <path1> <path2>`), commit using the preflight's `recommended_commit_message`, push, then re-run `host-sync-preflight` to confirm `clean` before continuing the wake. On `classification: needs-operator-review` or `unsafe-durable-state`, refuse to continue: surface the dirty paths and the per-path reason to the operator (commit/push if intentional, revert if stale local edits) — do NOT silently stash, overwrite, or auto-commit. On `classification: dirty-mixed`, the durable-state portion still goes through `durable-state-preflight`; the unrelated portion still requires explicit operator handling — refuse to silently mix the two. On `classification: dirty-unrelated-submodule` (G306 safe-stash lane), do NOT abort: run `intent-cli automation workspace-guard --mode begin --write` to stash the unrelated paths into a recoverable safe-stash ref before the wake, then run `--mode end --write` after Stage 1's commit/push lands. If `--mode end` reports a stash-pop conflict, do NOT claim the wake completed; surface the structured recovery instruction. On `classification: submodule-checkout-mismatch` (G357 submodule update lane), do NOT use the safe-stash lane: instead run `git submodule update --init <path>` for each path listed in `submodule_checkout_mismatch_paths`, then re-run `host-sync-preflight` to confirm `classification: clean`. This is safe because the preflight verified the submodule has no local uncommitted changes. After ANY closeout/reconcile/publish-recovery `--write` mutates parent durable state, re-run host-sync-preflight before Stage 2 to confirm the push landed and the working tree is clean again.
2. Stage 1 — review/closeout:
   - `intent-cli automation host-review-preflight --repo {targetRepoPlaceholder} --format json` to find an eligible PR.
   - **Draft-aware approval (G297)**: BEFORE applying any approval transition, capture the PR's draft state: `IS_DRAFT=$(gh pr view <n> --repo {targetRepoPlaceholder} --json isDraft --jq .isDraft)`. If `IS_DRAFT == true`, classify as `draft-merge-blocked` and stop this stage: do NOT call `pr-transition --transition approved`, run `intent-cli automation pr-transition --transition review-release --repo {targetRepoPlaceholder} --pr <n> --write --format json` to drop `intent-pr-reviewing` cleanly, then surface the gap (the PR must be readied for review by the implementer or operator before host review continues). Never apply final approval to a draft PR.
   - For the selected (non-draft) PR: `intent-cli review closeout-plan --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json` and `intent-cli guide review --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json`.
   - **Intent-and-packet-aware review (G316)**: `guide review` now returns structured `packet_paths` (packet.yaml, implementation.md, review-context.md, github-body.md) and `intent_reference_paths` (`intents/{domainPlaceholder}/{{specs,intent-tree,rules}}`) plus `approval_summary_requirements` and `request_update_requirements` lists. Treat the JSON `tests_pass_is_necessary_not_sufficient: true` field as a hard signal — a green test run alone is NEVER sufficient for approval. Before approving, the reviewer MUST cite (a) packet.yaml execution-unit contract, (b) implementation.md requirements mapped to diff, (c) review-context.md focus areas inspected, and (d) issue Acceptance Criteria + Out-of-Scope boundaries honored. Related parent intent / spec / rule references from `intent_reference_paths` are consulted only when the packet/review-context points at them — do not read the full intent tree per PR.
   - **Selected-PR linkage recovery (G284, G313)**: when `closeout-plan` or `guide review` returns `ready: false` because the parent queue has no item with `linked_pr` matching the selected PR, do NOT abort the review yet. **Run `intent-cli automation publish-recovery --repo {targetRepoPlaceholder} --format json` FIRST** (G313): when `.intent-cli/issues/<unit>/publish.yaml` exists, publish-recovery is the first-class repair surface — it converges GitHub `closingIssuesReferences`, the publish artifact, and the queue item on a single execution unit. Read the `recommended_recovery_command` field on `closeout-plan` output: when it names `automation publish-recovery`, run that exact command; if its dry-run reports a single high-confidence repair, re-run with `--write` and then **retry the same selected PR exactly once** (re-run `closeout-plan` and `guide review`). Only if publish-recovery reports `no high-confidence repair` or `no publish artifact present` should you fall back to `intent-cli automation reconcile --lane host-review --repo {targetRepoPlaceholder} --format json`; the same retry-once rule applies. If either lane reports `unsafe stops` (multiple closing PRs, repo mismatch, `ambiguous-queue-linkage`), stop with structured clarification — never write parent state without deterministic evidence. If the post-recovery retry still returns `ready: false`, surface the gap as a structured operator stop instead of looping further.
   - If review passes (and PR is non-draft): the approval summary MUST satisfy `guide review` `approval_summary_requirements` (G316) — name the execution unit + packet contract checked, name the issue Acceptance Criteria verified, state Out-of-Scope was honored, cite at least one related intent/spec/rule reference (or explicitly state none applied), pair the test-pass result with packet/intent evidence, and confirm the PR `Closes #<source-issue>` reference (G311). Do NOT approve when the only evidence in your summary is ""tests passed"". Once the summary is complete, run `intent-cli automation pr-transition --transition approved --repo {targetRepoPlaceholder} --pr <n> --write --format json`, merge via the host's existing merge step, then capture `IS_MERGED=$(gh pr view <n> --repo {targetRepoPlaceholder} --json merged --jq .merged)` and only call `intent-cli closeout pr --pr <n> --repo {targetRepoPlaceholder} --pr-merged $IS_MERGED --write --format json` (G297 — closeout refuses when `--pr-merged false`, so a failed/blocked merge can never record closeout). Stage 2 (next-slice publish) is gated on `closeout pr --write` succeeding for THIS wake; never publish a new child issue after a merge that did not actually land.
   - **Host-metadata blockers do NOT become PR repair comments (G287)**: when `review closeout-plan` returns `ready: false` with `blocker_classification: host-metadata-blocked` (e.g. `no queue item found with linked_pr matching #<n>`, missing `linked_issue`, missing/invalid queue-state, missing packet directory), do NOT post a PR comment and do NOT call `pr-transition --transition request-update`. The implementer cannot repair parent host metadata from the PR branch. Instead run the `recommended_recovery_command` (typically `intent-cli automation reconcile --lane host-review --repo <r> --format json` followed by `--write` if a high-confidence repair exists) and retry the wake. If reconcile reports unsafe stops or no high-confidence repair, surface a structured operator stop.
   - **Release the review lease on host-metadata blockers (G292)**: if `review-start` was already applied to the PR (so `intent-pr-reviewing` is on it) and host metadata then blocks the wake, run `intent-cli automation pr-transition --transition review-release --repo {targetRepoPlaceholder} --pr <n> --write --format json` to drop `intent-pr-reviewing` cleanly without adding `intent-pr-request-update`. The next wake reselects the PR after reconcile completes. Never leave a PR stuck with `intent-pr-reviewing` while no review is in progress.
   - If review needs repair AND `blocker_classification: implementation-review-finding` (real code/contract gap the implementer can fix on the PR branch): leave an actionable PR comment, then `intent-cli automation pr-transition --transition request-update --repo {targetRepoPlaceholder} --pr <n> --write --format json`. **Request-update content (G316)**: each finding in the comment MUST satisfy `guide review` `request_update_requirements` — classify it as one of `implementation-finding` / `host-metadata-blocked` / `intent-ambiguity`, tie implementation findings to a specific packet.yaml clause, implementation.md requirement, acceptance criterion, or intent reference (vague review notes are not actionable), and never post host-metadata-blocked or intent-ambiguity findings as PR comments (those become structured operator stops). When the diff makes tests pass but the PR provides no packet/intent conformance evidence, that itself is an `implementation-finding` — request the implementer add the missing evidence (test names mapped to AC, comments tying changes to packet clauses, etc.) rather than approving.
3. Stage 2 — next-slice (only if WIP cap and clarification gates allow). **Post-closeout fresh-state reload (G289, G357)**: when Stage 1 just merged a PR and pushed parent durable state, run `git pull --ff-only origin main` on the host repo BEFORE re-running `intent next-slice --dry-run` — this refreshes `queue-state.json`, `runs.jsonl`, and the submodule gitlink pointer. After the pull, check if any submodule path appears in `host-sync-preflight`'s `submodule_checkout_mismatch_paths`; if so run `git submodule update --init <path>` for each before re-running the preflight. The diagnostic's WIP filter already excludes closed issues / merged PRs (G289 defensive filter), but `intent next-slice --dry-run` reads the local queue-state file and is the authoritative `wip` / `recommended_outcome` source for Stage 2; treat its `wip: []` + `issue-cut-ready` as the green light to proceed even if a stale read of `automation host-review-diagnostics` would have said otherwise.
   - `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — confirm `recommended_outcome` is `issue-cut-ready`.
   - **Stale clarification metadata (G285)**: when the result includes `warnings: [""stale-clarification-metadata""]` (front-matter still says `intent_state: open` but the body explicitly records no current blockers / open questions), do NOT treat it as Hard Clarification. The `recommended_outcome` is the source of truth — if it is `issue-cut-ready`, proceed with publish; the warning is a host-side repair hint (re-stamp the clarification file's front-matter to `clarified` after the slice publishes), not a stop signal. A real Hard Clarification surfaces as `recommended_outcome: clarification-required` with substantive blocker / question text.
   - **Structured clarification workflow (G310)**: when `recommended_outcome: clarification-required`, do NOT ask the operator a free-form question. Run `intent-cli clarification next --domain {domainPlaceholder} --format markdown` to fetch the structured product-owner question (background, question, options with pros/cons, recommendation, blocks). Present the markdown verbatim to the operator; after they answer, record durably with `intent-cli clarification answer --domain {domainPlaceholder} --id <id> --choice <option-id> [--note ""<text>""] --write` (G302). Only ad-hoc questions are allowed when `clarification next` returns `has_open: false` AND the next-slice clarification block is genuinely free-form. After `clarification answer --write`, re-run `intent next-slice --dry-run` — `clarification_open` flips to false and `recommended_outcome` should advance.
   - `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --dry-run --format markdown` — preview the packet.
   - The operator's request to set up this host loop is pre-approval to publish exactly one next-slice issue per wake when ALL of the following hold: `intent next-slice --dry-run` returned `issue-cut-ready`, no open `intent-target` issue/PR is in flight (WIP empty), no Hard Clarification is open for the candidate, and the candidate's standalone Child Issue Contract is complete. In that case proceed without an additional operator acceptance prompt: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --format json` then `intent-cli issue publish-flow <id> --repo {targetRepoPlaceholder} --write --format json`.
   - If any of those preconditions fails, stop and surface the gap (clarification or contract repair); do not silently publish.
   - After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <n> --write --format json`.
4. Stage 3 — safe reconcile (host-only; only if Stage 1 and Stage 2 would otherwise stop with idle / no-actionable-item / clarification-required):
   - `intent-cli automation reconcile --lane host-review --repo {targetRepoPlaceholder} --format json` — dry-run plan with evidence and confidence per drift entry.
   - High-confidence repairs are mechanically provable label drift only; advisory entries point at the existing closeout/packet/next-slice surface and must not be applied through this lane.
   - With operator acceptance: re-run with `--write` to apply only high-confidence repairs through the host-owned reconcile mutator.
   - If `unsafe_stops` is non-empty, stop with structured clarification rather than guessing.
5. Stage 4 — convergence diagnostics before idle (read-only, G286):
   - Before reporting a no-actionable / idle wake, run `intent-cli automation host-review-diagnostics --repo {targetRepoPlaceholder} --candidate <id?> --format json` and pass the next-slice flags so the diagnostic can converge on a single terminal class:
     - `--clarification-required` only when `intent next-slice --dry-run` returned `recommended_outcome: clarification-required` with substantive blocker / question text.
     - `--stale-clarification-metadata` when the next-slice result carried `warnings: [""stale-clarification-metadata""]`. This surfaces in `warnings` without flipping the terminal class (G285 + G286).
     - `--reconcile-unsafe-stop <kind>` (repeatable) when reconcile reported `unsafe_stops` (e.g. `ambiguous-queue-linkage`).
     - `--reconcile-repairs-available <N>` when reconcile dry-run produced N high-confidence repairs not yet applied.
     - `--closeout-drift-repairs-available <N>` (G356): run `intent-cli automation closeout-drift-check --repo {targetRepoPlaceholder} --format json` first; pass `safe_repair_count` as N when > 0. This guard detects queue items whose linked PR is already merged but whose state is not Completed — GitHub facts are deterministic evidence and the repair must run before declaring idle.
   - Terminal classifications (G286, G356): `true-idle`, `stuck-reviewing`, `missing-target-on-pr`, `request-update-rereview-conflict`, `wip-cap-blocked`, `clarification-required`, `stale-host-cli`, `review-pr-actionable`, `issue-publish-ready`, `unsafe-metadata`, `repaired-and-retry`, `candidate-ready` (legacy alias), `closeout-drift-repair` (G356 — queue item not Completed despite merged PR). Anything other than `true-idle` is a signal that the loop did not stop because the system is genuinely idle.
   - When `classification: issue-publish-ready`, the `recommended_next_command` is the deterministic publish chain (`packet draft` → `issue publish-flow --write` → `automation issue-publish --write`); proceed without a fresh acceptance prompt.
   - When `classification: repaired-and-retry`, run `automation reconcile --lane host-review --write` and re-enter the wake. When `classification: closeout-drift-repair` (G356), run `automation closeout-drift-check --write`, commit and push durable state, then re-enter the wake exactly once. When `classification: unsafe-metadata`, stop with structured clarification — never guess past ambiguous parent state.
   - When the diagnostic surfaces a `recommended_next_command`, surface it to the operator; when it surfaces `structured_clarification`, present background/question/options instead of guessing.
   - **Self-healing wake policy (G355 + G356)**: check `safe_repair_available` on the diagnostics result. When `true`, the `safe_repair_category` + `recommended_next_command` name a deterministic host-owned repair to apply before reporting blocked or idle. Defined categories: `issue-publish-gap` (publish the next-slice issue without further operator prompt), `review-linkage-gap` (run `automation publish-recovery --write` to recover missing `linked_pr`), `host-artifact-repair` (run `automation reconcile --lane host-review --write`), `drafted-packet-mechanical-gap` (append mechanical section templates — Verification, Related Links — to `github-body.md` using repair guidance from packet-gap-analysis then retry), `stale-review-lease` (run `automation pr-transition --transition review-release` to release the stale review lease then retry review selection once), `workspace-safe-dirty` (run `automation workspace-guard --mode begin --write` before the wake body and `--mode end --write` after the push lands to stash and restore safe dirty paths), `closeout-drift-repair` (G356 — run `automation closeout-drift-check --write` to mark queue items Completed and append pr-merged/closeout-recorded run events, then commit/push durable state and retry). After applying any repair, commit and push all host durable state, then re-enter the wake body EXACTLY ONCE. If the retried wake still cannot advance, stop with the terminal classification — do NOT repair again (no infinite repair loops). When `safe_repair_available: false` (e.g. `unsafe-metadata`, `clarification-required`, `true-idle`), do NOT repair — stop with structured clarification.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files for routine review/closeout. Use `intent-cli guide ...` and `intent-cli automation ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the host review/closeout path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Every label transition goes through installed `intent-cli automation pr-transition` / `automation issue-publish` / `automation reconcile` / `worker claim` / `worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback.
- Never apply `intent-pr-created` to a PR.
- Honor the WIP cap: do not cut a new child issue while any open `intent-target` issue/PR remains. **Operator-approved queue warming (G288)**: only when the operator explicitly asks to keep the child queue warm beyond the cap, pass `--allow-wip-cap-override` to `automation host-review-diagnostics`. With that flag and a complete candidate, the diagnostic returns `issue-publish-ready` with `wip-cap-overridden` in `warnings`. The override publishes at most one prepared next-slice issue per wake; clarification gates and contract completeness are still hard blockers, and the override never lands without an operator ask.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.
- `automation reconcile --write` must come from this host loop only; child implementation loops never invoke it.
- **Publish-artifact-backed metadata recovery (G303)**: when `closeout-plan` returns `host-metadata-blocked` because the queue item has BOTH `linked_issue` and `linked_pr` null but `.intent-cli/issues/<execution-unit>/publish.yaml` recorded a created GitHub issue, run `intent-cli automation publish-recovery --repo {targetRepoPlaceholder} --format json` (read-only) first. If the dry-run reports a single high-confidence repair, re-run with `--write` and retry the wake; if it reports unsafe stops (multiple closing PRs, repo mismatch, missing publish artifact), surface a structured operator stop. Host metadata blockers like this MUST NOT become PR repair comments.
- **Queue-state-backed linked_pr recovery (G315)**: when `closeout-plan` returns `host-metadata-blocked` because the queue item already has `linked_issue` populated (durable host source-of-truth) but `linked_pr` is null, the same `intent-cli automation publish-recovery --repo {targetRepoPlaceholder} --format json` lane now also covers this case — publish artifact evidence is NOT required. The analyzer pairs the existing `linked_issue` with the unique open PR whose GitHub `closingIssuesReferences` (or PR body `Closes #<n>` text) closes that issue. If the dry-run reports a single high-confidence repair of type `queue-linked-issue-closing-pr-recovery`, re-run with `--write` and retry the wake. Unsafe stops in this lane (`no-closing-pr-for-linked-issue` → PR is missing a closing reference; G311 owns body repair / `multiple-closing-prs-for-linked-issue` / `multiple-queue-items-for-linked-issue` / `linked-issue-repo-mismatch`) MUST surface as a structured operator stop — never guess. Run G315 lane BEFORE falling back to `automation reconcile --lane host-review`; do not stop the wake just because the older publish-artifact lane (G303) reported `no publish artifact present`.
- **Tests-pass is necessary, not sufficient (G316)**: passing tests is necessary but NEVER sufficient for approval. `guide review` returns `tests_pass_is_necessary_not_sufficient: true` plus structured `approval_summary_requirements` and `request_update_requirements` lists; treat them as a hard contract. An approval summary that only says ""tests passed"" is itself an implementation-finding that must produce a request-update, not an approval. Approval requires explicit packet/intent conformance evidence: packet.yaml execution-unit contract checked, implementation.md requirements mapped to diff, review-context.md focus areas inspected, issue Acceptance Criteria + Out-of-Scope boundaries honored, and at least one related intent/spec/rule reference cited (or an explicit statement that none applied). Related intent paths come from `intent_reference_paths` for the resolved domain — do NOT read the full intent tree per PR; consult them only when the packet/review-context points at them.
- `automation host-review-diagnostics` is read-only and must not be used to mutate labels or parent files.
- Process at most one PR review and one new child issue per wake.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeHostLoop,
            Kind = KindLoop,
            Target = TargetHost,
            FrequencyGuidance = ResolvedFrequencyGuidance(frequency),
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json",
                $"intent-cli intent status --domain {domainPlaceholder} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json"
            ],
            Prompt = prompt,
            Agent = resolvedAgent,
            Frequency = string.IsNullOrWhiteSpace(frequency) ? null : frequency,
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    private static GuidePromptMatrixEntry BuildChildOneshot(string domainPlaceholder, string baseBranchPolicy, bool isSameRepo = false)
    {
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Honor");
        var sameRepoBlock = isSameRepo ? $"\n{RenderSameRepoTopologyBlock()}" : string.Empty;
        var prompt =
$@"Run one child implementation/update wake exactly once.

Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup. This is a one-shot execution. Frequency is forbidden.

{basePolicyBlock}{sameRepoBlock}

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — `primary` vs `support` vs `advanced` (`run`) vs `experimental` classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON for the parent intent domain.

Loop body (single wake only — do not repeat):
1. Save the child worktree path: `CHILD_WORKTREE=""$PWD""`. Confirm it is a git worktree root. Stop with `wrong-worktree` if not.
2. Resolve `<OWNER>/<REPO>` from the child cwd: `gh repo view --json nameWithOwner --jq .nameWithOwner` (fall back to `git remote get-url origin`).
3. `git fetch --all --prune` and `git status --short`. If dirty in a dedicated automation worktree, clean local residue (`git reset --hard`, `git clean -fd`, submodule reset). Never `git clean -fdx`. Never clean a personal/shared checkout.
4. From the child cwd (the child one-shot does NOT require parent host root access — G333 child-cwd / GitHub-contract-only mode), run `intent-cli worker next-action --repo <OWNER>/<REPO> --github-only --format json`. Pass `--github-only` so the selector is pinned to the label-only data path. Dispatch on `action`:
   - `none` → stop with `idle`.
   - `issue-to-pr` → claim with `intent-cli worker claim --kind issue --number <n> --repo <OWNER>/<REPO> --github-only --write --format json`, run the issue-to-PR workflow on the returned URL only, classify outcome, then `worker result-summary --kind issue-to-pr --repo <OWNER>/<REPO> ...` and `worker complete --kind issue --number <n> --repo <OWNER>/<REPO> --github-only --outcome <outcome> --write --format json`.
   - `pr-comment-fix` → claim with `intent-cli worker claim --kind pr --number <n> --repo <OWNER>/<REPO> --github-only --write --format json`, repair only the narrow requested change on the PR branch, classify outcome, then `worker result-summary --kind pr-comment-fix --repo <OWNER>/<REPO> ...` and `worker complete --kind pr --number <n> --repo <OWNER>/<REPO> --github-only --outcome <outcome> --write --format json`.

Host metadata gaps surfaced by `worker complete` (e.g. `linked_pr_synced: false` from child-cwd mode, G330) are HOST-owned blockers, not child instructions to enter the host repo.

Hard rules:
- Do not read `intents/rules/**`, local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, etc.), or copied prompt files for routine collaboration. Use `intent-cli guide ...` instead.
- Do not call `intent-cli run` from this loop. `run` is advanced runtime (integration smoke / replay / dogfooding), not the chat-first path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` / `intent-cli worker` commands. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Never apply `intent-target` from the child loop; it is host-owned.
- Never apply `intent-pr-created` to a PR; it is an issue-side completion marker.
- **PR draft state (G296)**: create child implementation/update PRs as **ready-for-review** (non-draft) by default. Do NOT pass `--draft` to `gh pr create` unless the operator explicitly asks for a draft. After opening the PR, pass the actual draft state into `worker result-summary --pr-draft true|false` so the host review loop can detect a draft PR before approval. If you must keep the PR draft (incomplete work, operator hold), do not transition the issue to ready-for-review states; mark the outcome accordingly and document the reason.
- **PR closing reference is mandatory (G311)**: when opening a child implementation PR for an issue selected by `worker next-action`, the PR body MUST include a deterministic GitHub closing reference to the source issue — `Closes #<issue>`, `Fixes #<issue>`, or `Resolves #<issue>` (case-insensitive; `#N` form, not bare links such as `see #N`). `worker complete --kind issue --outcome pr-created --pr <n> --write` validates this and refuses to mark complete when the reference is missing, points at a different issue, or names multiple distinct issues. If the gate refuses, repair the PR body via `gh pr edit <pr> --body-file <path-to-new-body>` so the body ends with `Closes #<issue>`, then re-run `worker complete`. Do NOT use raw label mutation or `gh issue close` to bypass the gate. For `pr-comment-fix` repairs the PR body must continue to carry the original closing reference; do not strip it during a repair commit.
- **Child cwd is GitHub-contract-only (G300 / G330 / G333)**: the implementation repo must NOT contain its own `.intent-cli/` and the child loop MUST NOT read parent host queue-state, runs.jsonl, packets, or intent metadata. `worker next-action` / `claim` / `complete` / `result-summary` are runnable from a child cwd because they take `--repo <owner/repo>` and use `intent-cli` only for GitHub label transitions. Pass `--github-only` to **assert** the strict child-loop contract (G333): the underlying selection/claim/complete data flow is already label-only, and `--github-only` records the binding on the result (`github_only: true`) and — for `worker complete` — refuses to touch parent durable state regardless of disk presence. From a child cwd (no parent root configured, no local `.intent-cli/queue-state.json`), `worker complete` auto-enters **child-cwd mode** (G330): the result records `child_cwd: true`, `linked_pr_synced: false`, and surfaces a pointer to the host-side recovery path `intent-cli review closeout-plan --pr <n> --repo <owner/repo> --write-recovered-linkage` (G329). Parent host metadata reconciliation is owned by the host/review-runtime loop, never by the child loop. Absence of `.intent-cli/` in the implementation repo is the expected steady state and MUST NOT by itself abort the child workflow (G305).
- **Worker selector is the source of truth (G305)**: issue and PR numbers come from `intent-cli worker next-action`, NEVER from operator-supplied prompt text. If the operator names a specific issue/PR, treat that as a hint only and confirm via `worker next-action` before mutating; if `worker next-action` returns `none` or a different target, stop with `idle` or use the `worker next-action` target rather than the operator hint. Do not invent issue/PR numbers from prompt memory.
- **Abort conditions (G305)**: stop immediately and surface the gap to the operator (do NOT silently fall back to ordinary GitHub review or raw label mutation) when ANY of the following hold:
  - the global `intent-cli` is missing from `PATH` or `intent-cli automation doctor --format json` reports `stale-host-cli` / a missing required surface;
  - `gh auth status` fails or GitHub network is unreachable for the target repo;
  - `worker next-action` cannot resolve a deterministic single target (e.g. ambiguous repo / multiple matches reported as warnings);
  - the resolved target's labels indicate another worker already holds the lease (`intent-issue-in-progress` / `intent-pr-update-in-progress`) and the operator has not authorized re-claiming;
  - `worker complete --write` returns errors that are not the expected `linked_pr_synced: false` queue-state warning.
- Process at most one action per wake.
- Do not create a cron, monitor, scheduler, reminder, or new thread after completing this wake.";

        var forbiddenSourcesOneshot = isSameRepo
            ? [.. ForbiddenSources, .. SameRepoAdditionalForbiddenSources]
            : (IReadOnlyList<string>)ForbiddenSources;

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildOneshot,
            Kind = KindOneshot,
            Target = TargetChild,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = forbiddenSourcesOneshot,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json"
            ],
            Prompt = prompt,
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
            Topology = isSameRepo ? TopologySameRepo : null,
        };
    }

    private static GuidePromptMatrixEntry BuildHostOneshot(string domainPlaceholder, string targetRepoPlaceholder, string baseBranchPolicy)
    {
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Closeout / merge expectation honors");
        var prompt =
$@"Run the host review and next-slice for domain `{domainPlaceholder}` against `{targetRepoPlaceholder}` exactly once.

Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup. This is a one-shot execution. Frequency is forbidden.

{basePolicyBlock}

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domainPlaceholder} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — verify WIP cap and clarification gates.

Loop body (single wake only — do not repeat):
1. Confirm cwd is the parent host repo root. **Initial pull (G357)**: run `git pull --ff-only origin main` as the VERY FIRST action — before any intent-cli call, any read-only preflight, and before host-sync-preflight. This guarantees all queue-state, runs.jsonl, packet, and submodule gitlink reads see fresh parent durable state. If the pull fails (non-fast-forward, dirty worktree conflict, or no upstream), stop and surface the gap to the operator — do NOT proceed past this gate. **Pre-wake host sync (G304)**: AFTER the initial pull, run `intent-cli automation host-sync-preflight --format json`. Exit code 0 (`classification: clean`) is the only state that allows the wake to proceed past this gate. On `classification: behind-origin`, run `git pull --ff-only` and re-run the preflight. On `classification: dirty-host-durable-state`, do NOT immediately abort — run `intent-cli automation durable-state-preflight --format json` (G312). When it returns `classification: verified-commit-ready`, the dirty changes are deterministic forward-only metadata updates (e.g. `linked_pr` added to `.intent-cli/queue-state.json`, append-only `.intent-cli/runs.jsonl` events): `git pull --ff-only`, stage ONLY the verified paths returned by the preflight (`git add <path1> <path2>`), commit using the preflight's `recommended_commit_message`, push, then re-run `host-sync-preflight` to confirm `clean` before continuing the wake. On `classification: needs-operator-review` or `unsafe-durable-state`, refuse to continue: surface the dirty paths and the per-path reason to the operator (commit/push if intentional, revert if stale local edits) — do NOT silently stash, overwrite, or auto-commit. On `classification: dirty-mixed`, the durable-state portion still goes through `durable-state-preflight`; the unrelated portion still requires explicit operator handling — refuse to silently mix the two. On `classification: dirty-unrelated-submodule` (G306 safe-stash lane), do NOT abort: run `intent-cli automation workspace-guard --mode begin --write` to stash the unrelated paths into a recoverable safe-stash ref before the wake, then run `--mode end --write` after Stage 1's commit/push lands. If `--mode end` reports a stash-pop conflict, do NOT claim the wake completed; surface the structured recovery instruction. On `classification: submodule-checkout-mismatch` (G357 submodule update lane), do NOT use the safe-stash lane: instead run `git submodule update --init <path>` for each path listed in `submodule_checkout_mismatch_paths`, then re-run `host-sync-preflight` to confirm `classification: clean`. After ANY closeout/reconcile/publish-recovery `--write` mutates parent durable state, re-run host-sync-preflight before Stage 2 to confirm the push landed and the working tree is clean again.
2. Stage 1 — review/closeout:
   - `intent-cli automation host-review-preflight --repo {targetRepoPlaceholder} --format json` to find an eligible PR.
   - **Draft-aware approval (G297)**: BEFORE applying any approval transition, capture the PR's draft state: `IS_DRAFT=$(gh pr view <n> --repo {targetRepoPlaceholder} --json isDraft --jq .isDraft)`. If `IS_DRAFT == true`, classify as `draft-merge-blocked` and stop this stage: do NOT call `pr-transition --transition approved`, run `intent-cli automation pr-transition --transition review-release --repo {targetRepoPlaceholder} --pr <n> --write --format json` to drop `intent-pr-reviewing` cleanly, then surface the gap (the PR must be readied for review by the implementer or operator before host review continues). Never apply final approval to a draft PR.
   - For the selected (non-draft) PR: `intent-cli review closeout-plan --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json` and `intent-cli guide review --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json`.
   - **Intent-and-packet-aware review (G316)**: `guide review` now returns structured `packet_paths` (packet.yaml, implementation.md, review-context.md, github-body.md) and `intent_reference_paths` (`intents/{domainPlaceholder}/{{specs,intent-tree,rules}}`) plus `approval_summary_requirements` and `request_update_requirements` lists. Treat the JSON `tests_pass_is_necessary_not_sufficient: true` field as a hard signal — a green test run alone is NEVER sufficient for approval. Before approving, the reviewer MUST cite (a) packet.yaml execution-unit contract, (b) implementation.md requirements mapped to diff, (c) review-context.md focus areas inspected, and (d) issue Acceptance Criteria + Out-of-Scope boundaries honored. Related parent intent / spec / rule references from `intent_reference_paths` are consulted only when the packet/review-context points at them — do not read the full intent tree per PR.
   - **Selected-PR linkage recovery (G284, G313)**: when `closeout-plan` or `guide review` returns `ready: false` because the parent queue has no item with `linked_pr` matching the selected PR, do NOT abort the review yet. **Run `intent-cli automation publish-recovery --repo {targetRepoPlaceholder} --format json` FIRST** (G313): when `.intent-cli/issues/<unit>/publish.yaml` exists, publish-recovery is the first-class repair surface — it converges GitHub `closingIssuesReferences`, the publish artifact, and the queue item on a single execution unit. Read the `recommended_recovery_command` field on `closeout-plan` output: when it names `automation publish-recovery`, run that exact command; if its dry-run reports a single high-confidence repair, re-run with `--write` and then **retry the same selected PR exactly once** (re-run `closeout-plan` and `guide review`). Only if publish-recovery reports `no high-confidence repair` or `no publish artifact present` should you fall back to `intent-cli automation reconcile --lane host-review --repo {targetRepoPlaceholder} --format json`; the same retry-once rule applies. If either lane reports `unsafe stops` (multiple closing PRs, repo mismatch, `ambiguous-queue-linkage`), stop with structured clarification — never write parent state without deterministic evidence. If the post-recovery retry still returns `ready: false`, surface the gap as a structured operator stop instead of looping further.
   - If review passes (and PR is non-draft): the approval summary MUST satisfy `guide review` `approval_summary_requirements` (G316) — name the execution unit + packet contract checked, name the issue Acceptance Criteria verified, state Out-of-Scope was honored, cite at least one related intent/spec/rule reference (or explicitly state none applied), pair the test-pass result with packet/intent evidence, and confirm the PR `Closes #<source-issue>` reference (G311). Do NOT approve when the only evidence in your summary is ""tests passed"". Once the summary is complete, run `intent-cli automation pr-transition --transition approved --repo {targetRepoPlaceholder} --pr <n> --write --format json`, merge via the host's existing merge step, then capture `IS_MERGED=$(gh pr view <n> --repo {targetRepoPlaceholder} --json merged --jq .merged)` and only call `intent-cli closeout pr --pr <n> --repo {targetRepoPlaceholder} --pr-merged $IS_MERGED --write --format json` (G297 — closeout refuses when `--pr-merged false`, so a failed/blocked merge can never record closeout). Stage 2 (next-slice publish) is gated on `closeout pr --write` succeeding for THIS wake; never publish a new child issue after a merge that did not actually land.
   - **Host-metadata blockers do NOT become PR repair comments (G287)**: when `review closeout-plan` returns `ready: false` with `blocker_classification: host-metadata-blocked` (e.g. `no queue item found with linked_pr matching #<n>`, missing `linked_issue`, missing/invalid queue-state, missing packet directory), do NOT post a PR comment and do NOT call `pr-transition --transition request-update`. The implementer cannot repair parent host metadata from the PR branch. Instead run the `recommended_recovery_command` (typically `intent-cli automation reconcile --lane host-review --repo <r> --format json` followed by `--write` if a high-confidence repair exists) and retry the wake. If reconcile reports unsafe stops or no high-confidence repair, surface a structured operator stop.
   - **Release the review lease on host-metadata blockers (G292)**: if `review-start` was already applied to the PR (so `intent-pr-reviewing` is on it) and host metadata then blocks the wake, run `intent-cli automation pr-transition --transition review-release --repo {targetRepoPlaceholder} --pr <n> --write --format json` to drop `intent-pr-reviewing` cleanly without adding `intent-pr-request-update`. The next wake reselects the PR after reconcile completes. Never leave a PR stuck with `intent-pr-reviewing` while no review is in progress.
   - If review needs repair AND `blocker_classification: implementation-review-finding` (real code/contract gap the implementer can fix on the PR branch): leave an actionable PR comment, then `intent-cli automation pr-transition --transition request-update --repo {targetRepoPlaceholder} --pr <n> --write --format json`. **Request-update content (G316)**: each finding in the comment MUST satisfy `guide review` `request_update_requirements` — classify it as one of `implementation-finding` / `host-metadata-blocked` / `intent-ambiguity`, tie implementation findings to a specific packet.yaml clause, implementation.md requirement, acceptance criterion, or intent reference (vague review notes are not actionable), and never post host-metadata-blocked or intent-ambiguity findings as PR comments (those become structured operator stops). When the diff makes tests pass but the PR provides no packet/intent conformance evidence, that itself is an `implementation-finding` — request the implementer add the missing evidence (test names mapped to AC, comments tying changes to packet clauses, etc.) rather than approving.
3. Stage 2 — next-slice (only if WIP cap and clarification gates allow). **Post-closeout fresh-state reload (G289, G357)**: when Stage 1 just merged a PR and pushed parent durable state, run `git pull --ff-only origin main` on the host repo BEFORE re-running `intent next-slice --dry-run` — this refreshes `queue-state.json`, `runs.jsonl`, and the submodule gitlink pointer. After the pull, check if any submodule path appears in `host-sync-preflight`'s `submodule_checkout_mismatch_paths`; if so run `git submodule update --init <path>` for each before re-running the preflight. The diagnostic's WIP filter already excludes closed issues / merged PRs (G289 defensive filter), but `intent next-slice --dry-run` reads the local queue-state file and is the authoritative `wip` / `recommended_outcome` source for Stage 2; treat its `wip: []` + `issue-cut-ready` as the green light to proceed even if a stale read of `automation host-review-diagnostics` would have said otherwise.
   - `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — confirm `recommended_outcome` is `issue-cut-ready`.
   - **Stale clarification metadata (G285)**: when the result includes `warnings: [""stale-clarification-metadata""]` (front-matter still says `intent_state: open` but the body explicitly records no current blockers / open questions), do NOT treat it as Hard Clarification. The `recommended_outcome` is the source of truth — if it is `issue-cut-ready`, proceed with publish; the warning is a host-side repair hint (re-stamp the clarification file's front-matter to `clarified` after the slice publishes), not a stop signal. A real Hard Clarification surfaces as `recommended_outcome: clarification-required` with substantive blocker / question text.
   - **Structured clarification workflow (G310)**: when `recommended_outcome: clarification-required`, do NOT ask the operator a free-form question. Run `intent-cli clarification next --domain {domainPlaceholder} --format markdown` to fetch the structured product-owner question (background, question, options with pros/cons, recommendation, blocks). Present the markdown verbatim to the operator; after they answer, record durably with `intent-cli clarification answer --domain {domainPlaceholder} --id <id> --choice <option-id> [--note ""<text>""] --write` (G302). Only ad-hoc questions are allowed when `clarification next` returns `has_open: false` AND the next-slice clarification block is genuinely free-form. After `clarification answer --write`, re-run `intent next-slice --dry-run` — `clarification_open` flips to false and `recommended_outcome` should advance.
   - `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --dry-run --format markdown` — preview the packet.
   - With operator acceptance: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --format json` then `intent-cli issue publish-flow <id> --repo {targetRepoPlaceholder} --write --format json`.
   - After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <n> --write --format json`.
4. Stage 3 — safe reconcile (host-only; only if Stage 1 and Stage 2 would otherwise stop with idle / no-actionable-item / clarification-required):
   - `intent-cli automation reconcile --lane host-review --repo {targetRepoPlaceholder} --format json` — dry-run plan with evidence and confidence per drift entry.
   - High-confidence repairs are mechanically provable label drift only; advisory entries point at the existing closeout/packet/next-slice surface and must not be applied through this lane.
   - With operator acceptance: re-run with `--write` to apply only high-confidence repairs through the host-owned reconcile mutator.
   - If `unsafe_stops` is non-empty, stop with structured clarification rather than guessing.
5. Stage 4 — convergence diagnostics before idle (read-only, G286):
   - Before reporting a no-actionable / idle wake, run `intent-cli automation closeout-drift-check --repo {targetRepoPlaceholder} --format json` first (G356); if `safe_repair_count > 0`, apply `--write` before running diagnostics. Then run `intent-cli automation host-review-diagnostics --repo {targetRepoPlaceholder} --candidate <id?> --format json` and pass `--clarification-required`, `--stale-clarification-metadata`, `--reconcile-unsafe-stop <kind>` (repeatable), `--reconcile-repairs-available <N>`, and `--closeout-drift-repairs-available <N>` flags so the diagnostic converges on a single terminal class.
   - Terminal classifications (G286, G356): `true-idle`, `stuck-reviewing`, `missing-target-on-pr`, `request-update-rereview-conflict`, `wip-cap-blocked`, `clarification-required`, `stale-host-cli`, `review-pr-actionable`, `issue-publish-ready`, `unsafe-metadata`, `repaired-and-retry`, `candidate-ready` (legacy alias), `closeout-drift-repair` (G356). Anything other than `true-idle` is a signal the system is not genuinely idle; surface the `recommended_next_command` or `structured_clarification` to the operator.
   - When `classification: unsafe-metadata`, stop with structured clarification. When `classification: repaired-and-retry`, apply the safe reconcile and retry the wake. When `classification: closeout-drift-repair` (G356), run `automation closeout-drift-check --write`, commit/push durable state, then retry the wake exactly once.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files for routine review/closeout. Use `intent-cli guide ...` and `intent-cli automation ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the host review/closeout path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Every label transition goes through installed `intent-cli automation pr-transition` / `automation issue-publish` / `automation reconcile` / `worker claim` / `worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback.
- Never apply `intent-pr-created` to a PR.
- Honor the WIP cap: do not cut a new child issue while any open `intent-target` issue/PR remains. **Operator-approved queue warming (G288)**: only when the operator explicitly asks to keep the child queue warm beyond the cap, pass `--allow-wip-cap-override` to `automation host-review-diagnostics`. With that flag and a complete candidate, the diagnostic returns `issue-publish-ready` with `wip-cap-overridden` in `warnings`. The override publishes at most one prepared next-slice issue per wake; clarification gates and contract completeness are still hard blockers, and the override never lands without an operator ask.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.
- `automation reconcile --write` must come from this host one-shot only; child implementation loops never invoke it.
- **Linked_pr recovery from existing linked_issue (G315)**: when `closeout-plan` reports `host-metadata-blocked` because queue-state has `linked_issue` populated but `linked_pr` null, run `intent-cli automation publish-recovery --repo {targetRepoPlaceholder} --format json` first — the same lane now covers both the publish-artifact-backed (G303) and the queue-linked-issue-backed (G315) cases. A high-confidence repair of type `queue-linked-issue-closing-pr-recovery` indicates the unique open PR whose GitHub `closingIssuesReferences` close the linked issue; re-run with `--write` and retry the wake exactly once. Unsafe stops (`no-closing-pr-for-linked-issue` / `multiple-closing-prs-for-linked-issue` / `multiple-queue-items-for-linked-issue` / `linked-issue-repo-mismatch`) surface as a structured operator stop — never guess past ambiguous parent state.
- **Tests-pass is necessary, not sufficient (G316)**: `guide review` returns `tests_pass_is_necessary_not_sufficient: true` plus structured `approval_summary_requirements` and `request_update_requirements`. A passing test run alone is NEVER sufficient for approval; the approval summary MUST cite packet.yaml contract, implementation.md requirements mapped to the diff, review-context.md focus areas inspected, and issue Acceptance Criteria / Out-of-Scope boundaries honored, plus at least one related intent/spec/rule reference (or explicit ""none applied""). Related intent paths come from `guide review` `intent_reference_paths` for the resolved domain — never read the full intent tree per PR. When tests pass but the PR cannot show packet/intent conformance evidence, classify the gap as an `implementation-finding` and request-update; never approve.
- `automation host-review-diagnostics` is read-only and must not be used to mutate labels or parent files.
- Process at most one PR review and one new child issue per wake.
- Do not create a cron, monitor, scheduler, reminder, or new thread after completing this wake.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeHostOneshot,
            Kind = KindOneshot,
            Target = TargetHost,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json",
                $"intent-cli intent status --domain {domainPlaceholder} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json"
            ],
            Prompt = prompt,
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // G349: local Copilot coding-agent guidance blocks.
    //
    // copilot-local differs from the cloud/assignment path (G345) in one
    // key way: when running in a HOST cwd the local Copilot agent CAN exec
    // `intent-cli` commands for host-owned metadata mutation — intent
    // interview, clarification question generation/recording, packet draft,
    // issue publish, and bug-to-intent repair. When running in a CHILD
    // implementation cwd it remains host-state-free (same restrictions as
    // cloud Copilot child work).
    //
    // The four rendered blocks are:
    //   1. BuildCopilotLocalHostLoop   — host recurring loop with full host
    //      intent-cli surfaces available.
    //   2. BuildCopilotLocalHostOneshot — one-shot host action (same surfaces,
    //      no recurring wake).
    //   3. BuildCopilotLocalChildLoop   — child cwd stays host-state-free;
    //      returns structured child guidance matching the cloud path.
    //   4. BuildCopilotLocalChildOneshot — child cwd one-shot; host-state-free.
    // ─────────────────────────────────────────────────────────────────────

    private static GuidePromptMatrixEntry BuildCopilotLocalHostLoop(
        string domainPlaceholder,
        string targetRepoPlaceholder,
        string baseBranchPolicy)
    {
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Closeout / merge expectation honors");
        var prompt =
$@"Local Copilot coding-agent host-loop guidance (G349). This is a local Copilot agent running in the HOST repo cwd. Unlike the cloud/assignment Copilot path, a local Copilot agent CAN exec `intent-cli` to perform host-owned intent/clarification/packet/issue-publish workflows. This is a RECURRING loop — the operator or scheduler drives subsequent wakes. Run the loop body exactly once per wake.

{basePolicyBlock}

If the installed CLI surface is stale or any required automation command is missing, abort the wake before any mutation: `intent-cli automation doctor --format json` (or `automation host-review-preflight` reporting `stale-host-cli`) is the canonical signal — refresh the installed CLI before continuing.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domainPlaceholder} --format json` — current baseline / WIP / queued / clarifications.

Host-cwd workflow surfaces available to local Copilot (pick the relevant one per wake):

**Intent interview / design**:
- `intent-cli guide workflow task intent-interview --format json` — structured intent-interview guidance.
- `intent-cli intent next-slice --dry-run --domain {domainPlaceholder} --target-repo {targetRepoPlaceholder} --format json` — verify WIP cap and clarification gates.

**Clarification**:
- `intent-cli clarification next --domain {domainPlaceholder} --format markdown` — fetch the next open structured clarification question.
- `intent-cli clarification answer --domain {domainPlaceholder} --id <id> --choice <option-id> [--note ""<text>""] --write` — record clarification durably (G302).

**Packet draft**:
- `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --dry-run --format markdown` — preview packet.
- `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --format json` — create packet artifacts.

**Issue publish**:
- `intent-cli issue publish-flow <id> --repo {targetRepoPlaceholder} --write --format json` — publish issue.
- `intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <n> --write --format json` — apply `intent-target` label after durable state is pushed.

**Bug-to-intent repair**:
- `intent-cli guide workflow task bug-to-intent-repair --format json` — structured bug-to-intent repair guidance.

**Host review / closeout / next-slice** (full host loop surface):
- `intent-cli automation host-review-preflight --repo {targetRepoPlaceholder} --format json`
- `intent-cli review closeout-plan --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json`
- `intent-cli guide review --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json`

Hard rules:
- Local Copilot MUST NOT launch AI providers from intent-cli (`never call intent-cli run`).
- All host metadata mutations go through installed `intent-cli` command surfaces; never hand-edit queue-state, runs.jsonl, packet artifacts, or submodule pointers.
- Do NOT read `intents/rules/**`, local skill files, or copied prompt files for routine work. Use `intent-cli guide ...` instead.
- Process at most one action per wake. Do NOT create a new thread, remote scheduler, or external cron.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeHostLoop,
            Kind = KindLoop,
            Target = TargetHost,
            FrequencyGuidance = FrequencyGuidanceRecurring,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json",
                $"intent-cli intent status --domain {domainPlaceholder} --format json"
            ],
            Prompt = prompt,
            Agent = AgentCopilotLocal,
            AgentClassification = "local_host_agent",
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    private static GuidePromptMatrixEntry BuildCopilotLocalHostOneshot(
        string domainPlaceholder,
        string targetRepoPlaceholder,
        string baseBranchPolicy)
    {
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Closeout / merge expectation honors");
        var prompt =
$@"Local Copilot coding-agent host one-shot guidance (G349). This is a local Copilot agent running in the HOST repo cwd, executing exactly ONE host-side action. Unlike the cloud/assignment Copilot path, a local Copilot agent CAN exec `intent-cli` for host-owned metadata mutation. Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup.

{basePolicyBlock}

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json`
2. `intent-cli guide onboarding --format json`
3. `intent-cli guide commands list --format json`
4. `intent-cli automation summary --domain {domainPlaceholder} --format json`
5. `intent-cli intent status --domain {domainPlaceholder} --format json`

Pick exactly one host action per invocation:
- Intent interview: `intent-cli guide workflow task intent-interview --format json`
- Clarification next: `intent-cli clarification next --domain {domainPlaceholder} --format markdown`
- Clarification answer: `intent-cli clarification answer --domain {domainPlaceholder} --id <id> --choice <option-id> --write`
- Packet draft: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepoPlaceholder} --format json`
- Issue publish: `intent-cli issue publish-flow <id> --repo {targetRepoPlaceholder} --write --format json` then `intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <n> --write --format json`
- Bug-to-intent repair: `intent-cli guide workflow task bug-to-intent-repair --format json`
- Review/closeout: `intent-cli review closeout-plan --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json` and `intent-cli guide review --pr <n> --repo {targetRepoPlaceholder} --domain {domainPlaceholder} --format json`

Hard rules:
- Local Copilot MUST NOT launch AI providers from intent-cli.
- All host metadata mutations go through installed `intent-cli` command surfaces.
- Do NOT read `intents/rules/**`, local skill files, or copied prompt files. Use `intent-cli guide ...`.
- Do not create a cron, monitor, scheduler, reminder, or new thread after completing this wake.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeHostOneshot,
            Kind = KindOneshot,
            Target = TargetHost,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json",
                $"intent-cli intent status --domain {domainPlaceholder} --format json"
            ],
            Prompt = prompt,
            Agent = AgentCopilotLocal,
            AgentClassification = "local_host_agent",
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    private static GuidePromptMatrixEntry BuildCopilotLocalChildLoop(string baseBranchPolicy, bool isSameRepo = false)
    {
        // G349: in a child implementation cwd, local Copilot is host-state-free —
        // same restrictions as cloud Copilot child work (G345). The key difference
        // from the cloud path is the agent tag so consumers can distinguish.
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Honor");
        var sameRepoBlock = isSameRepo ? $"\n{RenderSameRepoTopologyBlock()}" : string.Empty;
        var prompt =
$@"Local Copilot coding-agent child-loop guidance (G349). This is a local Copilot agent running in a CHILD implementation cwd. Even though this is a local agent, the child cwd is GitHub-contract-only — the agent must NOT read, write, or mutate host metadata (`.intent-cli/`, `intents/`) from the implementation side.

{basePolicyBlock}{sameRepoBlock}

{CopilotChildContractParagraph}

{CopilotAllowedDoBullets}

{CopilotForbiddenDoBullets}

Note: if this local Copilot agent needs to perform HOST-side intent/clarification/packet/publish work, cd to the HOST repo cwd and use `intent-cli guide prompt-matrix --mode host-loop --agent copilot-local` (or `--mode host-oneshot --agent copilot-local`) instead. The host-cwd path allows full intent-cli access. This child-loop path is strictly GitHub-contract-only.

Hard rules:
- From a child implementation cwd, local Copilot has the same restrictions as cloud Copilot: no `.intent-cli/` mutation, no host queue-state, no workflow label edits from the implementation side.
- The PR body MUST include a deterministic closing reference (`Closes #<issue>`, `Fixes #<issue>`, or `Resolves #<issue>`) (G311).
- Create PRs as ready-for-review (non-draft) by default.
- Process at most one assignment / PR-mention per wake. Do NOT create a recurring loop from a child cwd.";

        var forbiddenSources = isSameRepo
            ? [.. ForbiddenSources, .. SameRepoAdditionalForbiddenSources]
            : (IReadOnlyList<string>)ForbiddenSources;

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildLoop,
            Kind = KindLoop,
            Target = TargetChild,
            FrequencyGuidance = "N/A — local Copilot in a child cwd is GitHub-contract-only; use host-loop / host-oneshot with copilot-local for recurring host design work (G349).",
            ForbiddenSources = forbiddenSources,
            FirstCalls =
            [
                "gh auth status",
                "gh issue view <N> --repo <owner>/<repo> --json number,title,body,labels,assignees",
                "gh pr view <PR> --repo <owner>/<repo> --json number,isDraft,body,reviews,comments"
            ],
            Prompt = prompt,
            Agent = AgentCopilotLocal,
            AgentClassification = "local_child_agent_host_state_free",
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
            Topology = isSameRepo ? TopologySameRepo : null,
        };
    }

    private static GuidePromptMatrixEntry BuildCopilotLocalChildOneshot(string baseBranchPolicy, bool isSameRepo = false)
    {
        // G349: same child-cwd restrictions as cloud Copilot child-oneshot.
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Honor");
        var sameRepoBlock = isSameRepo ? $"\n{RenderSameRepoTopologyBlock()}" : string.Empty;
        var prompt =
$@"Local Copilot coding-agent child one-shot guidance (G349). Child implementation cwd — host-state-free. Even though this is a local agent, from a child cwd it must NOT touch host metadata.

{basePolicyBlock}{sameRepoBlock}

{CopilotChildContractParagraph}

{CopilotAllowedDoBullets}

{CopilotForbiddenDoBullets}

If host-side intent/clarification/packet/publish work is needed, switch to the HOST cwd and use `--agent copilot-local --mode host-oneshot`.

Hard rules:
- No `.intent-cli/` mutation from child cwd. No host queue-state reads.
- PR body MUST include a closing reference (`Closes #<issue>` etc.) (G311).
- Create PRs as ready-for-review (non-draft) by default.
- Do not create a cron, monitor, scheduler, or new thread.";

        var forbiddenSources = isSameRepo
            ? [.. ForbiddenSources, .. SameRepoAdditionalForbiddenSources]
            : (IReadOnlyList<string>)ForbiddenSources;

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildOneshot,
            Kind = KindOneshot,
            Target = TargetChild,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = forbiddenSources,
            FirstCalls =
            [
                "gh auth status",
                "gh issue view <N> --repo <owner>/<repo> --json number,title,body,labels,assignees",
                "gh pr view <PR> --repo <owner>/<repo> --json number,isDraft,body,reviews,comments"
            ],
            Prompt = prompt,
            Agent = AgentCopilotLocal,
            AgentClassification = "local_child_agent_host_state_free",
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
            Topology = isSameRepo ? TopologySameRepo : null,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // G345: GitHub Copilot guidance blocks.
    //
    // Copilot is assignment-oriented: it works from a published GitHub issue
    // or a mention on a PR; it can create a branch / open a PR; it can
    // respond to PR review comments; and Copilot CLI supports a one-shot
    // `--prompt` mode when explicitly installed. Copilot does NOT map to
    // the local 5-minute heartbeat / `/loop` model that Claude / Codex run
    // and must NOT touch host `.intent-cli/` metadata.
    //
    // The three rendered blocks are:
    //   1. BuildCopilotChildOneshot — supported: assignment / PR mention /
    //      Copilot CLI one-shot, plus the GitHub-only contract.
    //   2. BuildCopilotChildLoop    — unsupported local loop: returns
    //      structured "unsupported_local_loop" guidance pointing at an
    //      assignment-oriented controller.
    //   3. BuildCopilotUnsupportedHostMode — host-loop / host-oneshot are
    //      not Copilot's job; host review/closeout/next-slice stays with
    //      intent-cli host automation.
    // ─────────────────────────────────────────────────────────────────────

    private const string CopilotChildContractParagraph =
        "GitHub-only Copilot child contract (G345): Copilot must work from GitHub issue / PR / comment facts only. The implementation repo MUST NOT contain `.intent-cli/` and Copilot MUST NOT read, write, or mutate host queue-state (`.intent-cli/queue-state.json`), append to `runs.jsonl`, edit packet artifacts (`packet.yaml`, `implementation.md`, `review-context.md`, `github-body.md`), or apply workflow labels (`intent-target`, `intent-pr-created`, `intent-issue-in-progress`, `intent-pr-update-in-progress`, `intent-pr-reviewing`, `intent-pr-request-update`, etc.). Workflow labels are applied by the host via the installed host CLI (intent-cli's `automation` / `worker` surfaces), never from the implementation side. Host review, closeout, and next-slice publish remain with the intent-cli host loop; Copilot's job ends at opening / updating the PR.";

    private const string CopilotMinimalPromptAssignIssue =
        "Assign a published GitHub issue to the Copilot cloud agent — copy/paste the following minimal human prompt to the operator (replace `<owner>/<repo>` and `<N>` with the published issue's repo and number):\n\n```\ngh issue edit <N> --repo <owner>/<repo> --add-assignee copilot\n```\n\nCopilot cloud agent picks the issue up from the GitHub assignment event, creates its own branch, and opens a PR that includes `Closes #<N>`. The operator does not run Copilot CLI for this path; no local install is required. The host intent-cli loop still owns review, approval, merge, and next-slice publish.";

    private const string CopilotMinimalPromptPrComment =
        "Ask Copilot to update an existing PR from the latest review comments — copy/paste the following minimal human prompt to the operator (replace `<owner>/<repo>` and `<PR>` with the PR's repo and number). Use `gh pr comment` or the GitHub UI to post the mention so Copilot cloud agent picks it up:\n\n```\ngh pr comment <PR> --repo <owner>/<repo> --body \"@copilot please address the latest review comments on this PR.\"\n```\n\nCopilot reads the PR review comments, pushes a follow-up commit to the same PR branch, and posts a reply summarizing the changes. The operator does not run Copilot CLI for this path. The host intent-cli loop still owns the next review/approval cycle and label transitions.";

    private const string CopilotMinimalPromptCli =
        "Use Copilot CLI one-shot if available — only if the operator has GitHub Copilot CLI installed AND has explicitly chosen this path (no installation is required for the assignment / PR-mention workflows above). The CLI's programmatic prompt entry point runs in non-interactive mode against a single contract:\n\n```\ngh copilot suggest -t shell \"<contract text describing one self-contained change>\"\n```\n\nor, when the standalone `copilot` CLI is available:\n\n```\ncopilot --prompt \"<contract text describing one self-contained change>\"\n```\n\nThis is a one-shot invocation. Do NOT wrap it in a local cron, scheduler, monitor, or recurring wakeup, and do NOT use it to mutate host `.intent-cli/` metadata.";

    private const string CopilotAllowedDoBullets =
        "Copilot is ALLOWED to:\n- read the published GitHub issue body, labels, and `Closes #<n>` reference;\n- read the existing PR diff, PR review comments, and PR body;\n- open a branch and a PR that includes the deterministic closing reference (`Closes #<issue>`);\n- push follow-up commits to the same PR branch in response to a `@copilot` mention from a reviewer;\n- post a PR reply summarizing what changed.";

    private const string CopilotForbiddenDoBullets =
        "Copilot is NOT ALLOWED to:\n- write or commit `.intent-cli/` in the implementation repo (the implementation repo's steady state is no `.intent-cli/` at all — G300 / G330 / G333);\n- read or mutate parent host queue-state (`.intent-cli/queue-state.json`), append to `runs.jsonl`, edit packet artifacts (`packet.yaml`, `implementation.md`, `review-context.md`, `github-body.md`), or change submodule pointers from the implementation side;\n- apply or remove workflow labels (`intent-target`, `intent-pr-created`, `intent-issue-in-progress`, `intent-pr-update-in-progress`, `intent-pr-reviewing`, `intent-pr-request-update`, etc.) — those are host-owned through the installed host CLI (intent-cli's `automation` / `worker` surfaces);\n- run host review / closeout / next-slice / publish steps;\n- run local cron, heartbeats, schedulers, or Claude/Codex `/loop`-style recurring wakeups (Copilot has no local heartbeat thread).";

    private static GuidePromptMatrixEntry BuildCopilotChildOneshot(string baseBranchPolicy)
    {
        var basePolicyBlock = RenderCopilotBaseBranchPolicyBlock(baseBranchPolicy);
        var prompt =
$@"GitHub Copilot one-shot child guidance (G345). Copilot is an assignment-oriented agent — it works from a published GitHub issue or a mention on a PR, opens / updates a PR, and stops. It does NOT run a local 5-minute heartbeat loop, does NOT exec the host's `intent-cli`, and does NOT touch host `.intent-cli/` metadata.

Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup. This is a one-shot execution. Frequency is forbidden.

{basePolicyBlock}

{CopilotChildContractParagraph}

Minimal human prompts for the operator (pick exactly one path per wake):

1. {CopilotMinimalPromptAssignIssue}

2. {CopilotMinimalPromptPrComment}

3. {CopilotMinimalPromptCli}

{CopilotAllowedDoBullets}

{CopilotForbiddenDoBullets}

Hard rules:
- The implementation repo (the repo Copilot pushes commits to) MUST NOT contain `.intent-cli/`. Absence of `.intent-cli/` is the expected steady state for child work (G300 / G330 / G333) and Copilot must not introduce it.
- Copilot MUST NOT call `intent-cli` at all from the implementation side. Workflow label transitions (`intent-target` apply, `intent-issue-in-progress`, `intent-pr-created`, `intent-pr-update-in-progress`, etc.) are host-owned via the installed host CLI (intent-cli's `automation` / `worker` surfaces); the host intent-cli loop applies them when it reviews the PR.
- Copilot MUST NOT edit `.intent-cli/queue-state.json`, `runs.jsonl`, packet `packet.yaml` / `implementation.md` / `review-context.md` / `github-body.md`, or submodule pointers from the implementation side.
- Copilot MUST NOT post review/approval comments that claim host-side label transitions; only the host intent-cli review loop applies approval / merge / closeout.
- The PR body MUST include a deterministic GitHub closing reference to the source issue — `Closes #<issue>`, `Fixes #<issue>`, or `Resolves #<issue>` (G311). Copilot's PR template / generated body already meets this when the assignment was made via `gh issue edit <N> --add-assignee copilot`; verify before merge.
- Create the PR as ready-for-review (non-draft) by default. Do not mark a Copilot PR as draft unless the operator explicitly asks for one.
- Process at most one assignment / PR-mention / CLI one-shot per wake.
- Do NOT create a cron, monitor, scheduler, reminder, or new thread after completing this wake.
- Host review / closeout / next-slice publish remains with the intent-cli host loop; the host loop will pick up the Copilot-opened PR via its host-review preflight on its own cadence.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildOneshot,
            Kind = KindOneshot,
            Target = TargetChild,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "gh auth status",
                "gh issue view <N> --repo <owner>/<repo> --json number,title,body,labels,assignees",
                "gh pr view <PR> --repo <owner>/<repo> --json number,isDraft,body,reviews,comments"
            ],
            Prompt = prompt,
            Agent = AgentCopilot,
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    // G345: limited safe host-oneshot guidance for Copilot. The host
    // operator runs ONE safe intent-cli host-side action — publishing one
    // already-prepared issue — and then assigns that newly-published issue
    // to the Copilot cloud agent for implementation. Copilot itself never
    // executes intent-cli, never enters a host loop, and never mutates host
    // metadata. Host review / closeout / next-slice remain with the
    // intent-cli host loop running under --agent claude|codex|generic.
    private static GuidePromptMatrixEntry BuildCopilotHostOneshot(string targetRepoPlaceholder, string baseBranchPolicy)
    {
        var prompt =
$@"GitHub Copilot host one-shot guidance (G345). This is a one-shot, human / operator-driven host action — NOT a host loop, NOT a recurring same-thread wakeup, and NOT something Copilot executes. The operator runs a single safe intent-cli host-side step on the HOST, then delegates the implementation work to the Copilot cloud agent via GitHub issue assignment. Process at most one issue per wake.

Limited safe scope:
- Publish exactly one prepared issue on the HOST side using `intent-cli automation issue-publish`.
- Assign that newly-published issue to the Copilot cloud agent.
- Stop. Do NOT enter a host review / closeout / next-slice loop here. Host review, approval, merge, and next-slice publish remain with the intent-cli host loop running under `--agent claude` / `--agent codex` / `--agent generic` on its own cadence (see `intent-cli guide prompt-matrix --mode host-loop --agent claude`).

Base branch policy: `{baseBranchPolicy}` (expected base branch: `{BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy)}`).
- {BaseBranchPolicyContract.DescribePolicy(baseBranchPolicy)}
- Base-branch enforcement is HOST / operator-owned. The host intent-cli loop runs `intent-cli automation base-branch-check` on its own cadence; this host-oneshot wake only publishes one issue and hands it off.

Operator, on the HOST (these commands run from the parent intent host repo, NOT from the implementation side; NOT from Copilot):

1. Publish one prepared issue:

```
intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <N> --write --format json
```

Replace `<N>` with the prepared issue number. This applies the `intent-target` host-owned publish boundary label only after parent durable state is committed and pushed. Do NOT use raw `gh issue edit --add-label intent-target` to bypass the gate.

2. After step 1 succeeds, assign the published issue to the Copilot cloud agent so Copilot picks it up via the GitHub assignment event:

```
gh issue edit <N> --repo {targetRepoPlaceholder} --add-assignee copilot
```

Copilot cloud agent then creates its own branch and opens a PR that includes `Closes #<N>`. The operator does NOT run Copilot CLI for this path; no local Copilot install is required.

{CopilotChildContractParagraph}

Copilot is NOT ALLOWED to:
- write or commit `.intent-cli/` in the implementation repo (the implementation repo's steady state is no `.intent-cli/` at all — G300 / G330 / G333);
- read or mutate parent host queue-state (`.intent-cli/queue-state.json`), append to `runs.jsonl`, edit packet artifacts (`packet.yaml`, `implementation.md`, `review-context.md`, `github-body.md`), or change submodule pointers from the implementation side;
- apply or remove workflow labels (`intent-target`, `intent-pr-created`, `intent-issue-in-progress`, `intent-pr-update-in-progress`, `intent-pr-reviewing`, `intent-pr-request-update`, etc.) — those are host-owned through installed `intent-cli automation` / `intent-cli worker` commands;
- run host review / closeout / next-slice / publish steps;
- run local cron, schedulers, or recurring same-thread wakeups (Copilot has no local recurring primitive).

Hard rules:
- This wake is a SINGLE safe operator host action followed by one GitHub assignment. Do NOT schedule a recurring wakeup, do NOT wrap this in a cron, monitor, scheduler, reminder, or new thread.
- Copilot itself does NOT invoke `intent-cli`, does NOT mutate `.intent-cli/`, and does NOT run host loops. The single `intent-cli automation issue-publish` call is the HOST operator's responsibility — it runs from the host intent repo, not from Copilot's implementation side.
- Host review, approval, merge, closeout, and next-slice publish stay with the intent-cli host loop running under `--agent claude` / `--agent codex` / `--agent generic`. Use `intent-cli guide prompt-matrix --mode host-loop --agent claude` (or `--agent codex` / `--agent generic`) for the canonical recurring host body.
- For recurring host modes (`host-loop`), Copilot remains unsupported: `intent-cli guide prompt-matrix --mode host-loop --agent copilot` returns structured `unsupported_mode_agent_combination` guidance.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeHostOneshot,
            Kind = KindOneshot,
            Target = TargetHost,
            FrequencyGuidance = FrequencyGuidanceOneshot,
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "gh auth status",
                $"intent-cli automation issue-publish --repo {targetRepoPlaceholder} --issue <N> --write --format json",
                $"gh issue edit <N> --repo {targetRepoPlaceholder} --add-assignee copilot"
            ],
            Prompt = prompt,
            Agent = AgentCopilot,
            AgentClassification = "host_oneshot_human_driven",
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    private static GuidePromptMatrixEntry BuildCopilotChildLoop(string baseBranchPolicy)
    {
        var basePolicyBlock = RenderBaseBranchPolicyBlock(baseBranchPolicy, "Honor");
        var prompt =
$@"GitHub Copilot does NOT map to the local heartbeat / `/loop` child implementation loop (G345). This entry returns structured `unsupported_local_loop` guidance and an assignment-oriented controller recommendation; it is intentionally NOT a Claude/Codex-style recurring loop body.

Why this combination is unsupported:
- Copilot is assignment-oriented (cloud agent triggered by GitHub events: issue assignment to `copilot`, `@copilot` PR mention, or one-shot Copilot CLI `--prompt`). It does not host a local 5-minute heartbeat thread, cannot exec the operator's local `intent-cli` against the host repo, and cannot reach local `.intent-cli/` paths.
- The Claude same-thread `/loop` primitive and the Codex current-thread heartbeat are LOCAL same-session primitives that reuse the active thread's filesystem paths; Copilot has no equivalent primitive. The operator cannot point Copilot at a local cron either, because Copilot will not run host commands from the implementation side.
- Workflow label transitions (`intent-target`, `intent-pr-created`, `intent-issue-in-progress`, etc.) are host-owned through installed `intent-cli automation` / `intent-cli worker` commands. They are not Copilot's job; running a recurring Copilot loop would either no-op on those transitions or, worse, push raw `gh label` edits that bypass the host's invariants.

Assignment-oriented controller recommendation (replaces the local heartbeat loop):
- The host intent-cli loop continues to drive queue selection, review, closeout, and next-slice publish on its own cadence. That host loop runs with `--agent claude` or `--agent codex` (or `--agent generic`), NOT `--agent copilot`. See `intent-cli guide prompt-matrix --mode host-loop --agent <claude|codex|generic>` for the host body.
- For each child issue the host publishes (or each PR that needs a follow-up commit), an external assigner — the operator clicking, or a small assignment script triggered by a GitHub Action / webhook — performs ONE of the three minimal prompts below per ready item. Each is a single GitHub event; none is a recurring local heartbeat.

{basePolicyBlock}

{CopilotChildContractParagraph}

Three minimal human prompts (reused from `child-oneshot`; pick exactly one per ready item):

1. {CopilotMinimalPromptAssignIssue}

2. {CopilotMinimalPromptPrComment}

3. {CopilotMinimalPromptCli}

{CopilotAllowedDoBullets}

{CopilotForbiddenDoBullets}

Hard rules:
- Do NOT substitute the Claude same-thread `/loop` body or the Codex current-thread heartbeat body for Copilot. The local heartbeat / `next-action` / `claim` / `complete` worker sequence is host- and local-agent-only.
- Do NOT create a local cron, monitor, scheduler, reminder, or new thread that wraps Copilot. Copilot has no local heartbeat surface to wrap.
- Do NOT touch host `.intent-cli/` metadata from the implementation side. The host loop reconciles workflow labels and queue-state on its own cadence.
- If the operator insists on a recurring Copilot cadence, surface this `unsupported_local_loop` classification and the assignment-oriented controller recommendation instead of inventing one.";

        return new GuidePromptMatrixEntry
        {
            Mode = ModeChildLoop,
            Kind = KindLoop,
            Target = TargetChild,
            FrequencyGuidance = "N/A — GitHub Copilot has no local heartbeat / recurring loop primitive; the local 5-minute / 20-minute guidance does not apply (G345).",
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "gh auth status",
                "gh issue view <N> --repo <owner>/<repo> --json number,title,body,labels,assignees",
                "gh pr view <PR> --repo <owner>/<repo> --json number,isDraft,body,reviews,comments"
            ],
            Prompt = prompt,
            Agent = AgentCopilot,
            AgentClassification = "unsupported_local_loop",
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    private static GuidePromptMatrixEntry BuildCopilotUnsupportedHostMode(string mode, string kind, string target, string baseBranchPolicy)
    {
        var prompt =
$@"GitHub Copilot is NOT a host-side agent (G345). Mode `{mode}` covers host review / closeout / next-slice publish and remains with intent-cli host automation. Copilot is implementation-side only — it works from a published GitHub issue or a `@copilot` PR mention and stops at opening / updating a PR. This entry returns structured `unsupported_mode_agent_combination` guidance.

Why this combination is unsupported:
- The host review / closeout / next-slice loops read parent durable state (`.intent-cli/queue-state.json`, `.intent-cli/runs.jsonl`, packet artifacts under `.intent-cli/issues/<execution-unit>/`) and mutate workflow labels through installed `intent-cli automation pr-transition` / `automation issue-publish` / `automation reconcile` commands. None of that is Copilot's job; the implementation side (where Copilot operates) does not own host metadata or workflow labels.
- Host review approval is gated by `intent-cli guide review` (packet/intent conformance evidence, Acceptance Criteria check, Out-of-Scope honored, etc.) and by intent-cli's draft-aware approval, host-sync preflight, publish-recovery, reconcile, and host-review-diagnostics lanes. Copilot cannot exec those commands and cannot supply structured approval-summary evidence in the form the host loop requires.
- Workflow labels (`intent-target`, `intent-pr-created`, `intent-issue-in-progress`, `intent-pr-update-in-progress`, `intent-pr-reviewing`, `intent-pr-request-update`, etc.) are host-owned. A recurring Copilot host loop would either no-op on these transitions or push raw `gh label` mutations that bypass the host's invariants.

Available agents for the recurring host mode (`host-loop`):
- `claude` — Claude Code same-thread `/loop` for recurring host review/next-slice;
- `codex` — Codex current-thread heartbeat for recurring host review/next-slice;
- `generic` — generic same-thread/current-thread agent (no Claude/Codex-specific scheduling primitive);
- `copilot` — NOT supported for `host-loop` (the recurring host body).

Recommended next call: `intent-cli guide prompt-matrix --mode {mode} --agent claude` (or `--agent codex` / `--agent generic`) for the canonical host body. For Copilot child work, use `intent-cli guide prompt-matrix --mode child-oneshot --agent copilot`. For a limited safe one-shot host action (publish one prepared issue and assign it to Copilot cloud), use `intent-cli guide prompt-matrix --mode host-oneshot --agent copilot`.

Hard rules:
- Do NOT run the host review / closeout / next-slice body under `--agent copilot`. Host loops require local exec of `intent-cli automation` / `intent-cli review closeout-plan` / `intent-cli intent next-slice` / `intent-cli packet draft` / `intent-cli issue publish-flow` against the parent host repo's local `.intent-cli/` directory; Copilot has no surface for that.
- Do NOT have Copilot mutate workflow labels, queue-state, runs.jsonl, packet artifacts, or submodule pointers under the guise of ""hosting"" review. Those mutations are reserved for the host intent-cli loop running under `claude` / `codex` / `generic`.
- Do NOT replace the host review / next-slice loop with a Copilot assignment cadence. The host loop's responsibilities (draft-aware approval, host-sync preflight, publish-recovery, reconcile, intent-and-packet-aware review, structured clarification) cannot be delegated to an external assignment workflow.";

        return new GuidePromptMatrixEntry
        {
            Mode = mode,
            Kind = kind,
            Target = target,
            FrequencyGuidance =
                string.Equals(kind, KindOneshot, StringComparison.Ordinal)
                    ? FrequencyGuidanceOneshot
                    : "N/A — GitHub Copilot is not a host-side agent; host review/closeout/next-slice remains with intent-cli host automation under --agent claude|codex|generic (G345).",
            ForbiddenSources = ForbiddenSources,
            FirstCalls =
            [
                "intent-cli guide prompt-matrix --mode " + mode + " --agent claude",
                "intent-cli guide prompt-matrix --mode " + mode + " --agent codex",
                "intent-cli guide prompt-matrix --mode " + mode + " --agent generic"
            ],
            Prompt = prompt,
            Agent = AgentCopilot,
            AgentClassification = "unsupported_mode_agent_combination",
            BaseBranchPolicy = baseBranchPolicy,
            ExpectedBaseBranch = BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy),
        };
    }

    private static void WriteMarkdown(TextWriter writer, IReadOnlyList<GuidePromptMatrixEntry> entries)
    {
        writer.WriteLine("# Guide prompt matrix");
        writer.WriteLine();
        writer.WriteLine("Canonical matrix of the four operational modes.");
        writer.WriteLine();

        foreach (var entry in entries)
        {
            writer.WriteLine($"## Mode: {entry.Mode}");
            writer.WriteLine();
            writer.WriteLine($"- kind: {entry.Kind}");
            writer.WriteLine($"- target: {entry.Target}");
            writer.WriteLine($"- frequency_guidance: {entry.FrequencyGuidance}");
            writer.WriteLine();

            writer.WriteLine("### First-call sequence (read-only)");
            foreach (var call in entry.FirstCalls)
            {
                writer.WriteLine($"- `{call}`");
            }
            writer.WriteLine();

            writer.WriteLine("### Forbidden rule sources");
            foreach (var src in entry.ForbiddenSources)
            {
                writer.WriteLine($"- {src}");
            }
            writer.WriteLine();

            writer.WriteLine("### Prompt");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(entry.Prompt);
            writer.WriteLine("```");
            writer.WriteLine();
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? mode,
        out string format,
        out string? domain,
        out string? targetRepo,
        out string? agent,
        out string? frequency,
        out string? baseBranchPolicy,
        out string? topology,
        out string error)
    {
        mode = null;
        format = FormatMarkdown;
        domain = null;
        targetRepo = null;
        agent = null;
        frequency = null;
        baseBranchPolicy = null;
        topology = null;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--mode":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--mode requires a value (child-loop, host-loop, child-oneshot, host-oneshot).";
                        return false;
                    }

                    var requestedMode = args[index + 1];
                    if (!string.Equals(requestedMode, ModeChildLoop, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeHostLoop, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeChildOneshot, StringComparison.Ordinal)
                        && !string.Equals(requestedMode, ModeHostOneshot, StringComparison.Ordinal))
                    {
                        error = $"--mode must be 'child-loop', 'host-loop', 'child-oneshot', or 'host-oneshot' (got '{requestedMode}').";
                        return false;
                    }

                    mode = requestedMode;
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domain = args[index + 1];
                    index++;
                    break;

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
                    index++;
                    break;

                case "--agent":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--agent requires a value (claude, codex, generic, copilot, copilot-cloud, or copilot-local).";
                        return false;
                    }
                    var requestedAgent = args[index + 1].Trim().ToLowerInvariant();
                    if (!string.Equals(requestedAgent, AgentClaude, StringComparison.Ordinal)
                        && !string.Equals(requestedAgent, AgentCodex, StringComparison.Ordinal)
                        && !string.Equals(requestedAgent, AgentGeneric, StringComparison.Ordinal)
                        && !string.Equals(requestedAgent, AgentCopilot, StringComparison.Ordinal)
                        && !string.Equals(requestedAgent, AgentCopilotCloud, StringComparison.Ordinal)
                        && !string.Equals(requestedAgent, AgentCopilotLocal, StringComparison.Ordinal))
                    {
                        error = $"--agent must be 'claude', 'codex', 'generic', 'copilot', 'copilot-cloud', or 'copilot-local' (got '{requestedAgent}').";
                        return false;
                    }
                    agent = requestedAgent;
                    index++;
                    break;

                case "--frequency":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--frequency requires a value (e.g. 5m, 20m, 1h).";
                        return false;
                    }
                    var requestedFrequency = args[index + 1].Trim();
                    if (!FrequencyPattern.IsMatch(requestedFrequency))
                    {
                        error = $"--frequency must match <NNm|NNh> (positive integer followed by 'm' for minutes or 'h' for hours; e.g. 5m, 20m, 1h). Got '{requestedFrequency}'.";
                        return false;
                    }
                    frequency = requestedFrequency;
                    index++;
                    break;

                case "--base-branch-policy":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"--base-branch-policy requires a value ('{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}').";
                        return false;
                    }
                    var requestedPolicy = args[index + 1].Trim();
                    if (!BaseBranchPolicyContract.IsKnownPolicy(requestedPolicy))
                    {
                        error = $"--base-branch-policy must be '{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}' (got '{requestedPolicy}').";
                        return false;
                    }
                    baseBranchPolicy = requestedPolicy;
                    index++;
                    break;

                case "--topology":
                    // G348: only 'same-repo' is a recognized topology value.
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"--topology requires a value ({TopologySameRepo}).";
                        return false;
                    }
                    var requestedTopology = args[index + 1].Trim();
                    if (!string.Equals(requestedTopology, TopologySameRepo, StringComparison.Ordinal))
                    {
                        error = $"--topology must be '{TopologySameRepo}' (got '{requestedTopology}').";
                        return false;
                    }
                    topology = requestedTopology;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide prompt-matrix");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only canonical matrix of the four operational modes with paste-ready prompt text.");
        writer.WriteLine();
        writer.WriteLine("Modes:");
        writer.WriteLine($"- {ModeChildLoop}    recurring child implement/update loop");
        writer.WriteLine($"- {ModeHostLoop}     recurring host review/next-slice loop");
        writer.WriteLine($"- {ModeChildOneshot} one-shot child implement/update");
        writer.WriteLine($"- {ModeHostOneshot}  one-shot host review/next-slice");
        writer.WriteLine();
        writer.WriteLine("Omit --mode to get all four entries.");
        writer.WriteLine("--domain, --target-repo, --agent, --frequency, and --topology are optional; provide them to render a concrete paste-ready prompt instead of one with placeholders.");
        writer.WriteLine("--agent values: claude (same-thread `/loop`), codex (current-thread heartbeat), generic, copilot / copilot-cloud (G345 — cloud/assignment-oriented; supported for child-oneshot, returns structured unsupported-loop guidance for child-loop, structured host-oneshot-human-driven guidance for host-oneshot, and structured unsupported-mode-agent-combination for host-loop), copilot-local (G349 — local Copilot coding-agent in a host cwd; can exec intent-cli for host operations; child cwd usage remains host-state-free).");
        writer.WriteLine("--frequency examples: 5m, 20m, 1h. Omit to keep the rendered prompt's ask-the-operator instruction.");
        writer.WriteLine($"--base-branch-policy values: {CliRuntimeContracts.DirectMainBaseBranchPolicy} (default; child PRs target `{CliRuntimeContracts.DirectMainBaseBranch}`), {CliRuntimeContracts.MainAiBaseBranchPolicy} (child PRs target `{CliRuntimeContracts.MainAiIntegrationBaseBranch}`).");
        writer.WriteLine($"--topology values: {TopologySameRepo} (G348 — adds same-repo forbidden-path guidance to child prompts; host intent metadata paths `.intent-cli/**` and `intents/**` are visible but forbidden for implementation agents).");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuidePromptMatrixEntry
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("frequency_guidance")]
    public required string FrequencyGuidance { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("first_calls")]
    public required IReadOnlyList<string> FirstCalls { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    [JsonPropertyName("frequency")]
    public string? Frequency { get; init; }

    [JsonPropertyName("base_branch_policy")]
    public string? BaseBranchPolicy { get; init; }

    [JsonPropertyName("expected_base_branch")]
    public string? ExpectedBaseBranch { get; init; }

    /// <summary>
    /// G348: topology hint; <c>"same-repo"</c> when <c>--topology same-repo</c>
    /// was passed, null otherwise.
    /// </summary>
    [JsonPropertyName("topology")]
    public string? Topology { get; init; }

    /// <summary>
    /// G345: structured classification when the requested agent does not
    /// map cleanly to the requested mode. Possible values:
    /// <c>"unsupported_local_loop"</c> (copilot + child-loop — Copilot does
    /// not provide the local 5-minute heartbeat loop; assignment-oriented
    /// controller is recommended instead),
    /// <c>"host_oneshot_human_driven"</c> (copilot + host-oneshot — limited
    /// safe human / operator-driven host action: one `intent-cli automation
    /// issue-publish` plus one `gh issue edit --add-assignee copilot`, no
    /// host loop), and
    /// <c>"unsupported_mode_agent_combination"</c> (copilot + host-loop —
    /// recurring host review/closeout/next-slice stays with intent-cli host
    /// automation and must use claude / codex / generic). Null when the
    /// requested combination is supported.
    /// </summary>
    [JsonPropertyName("agent_classification")]
    public string? AgentClassification { get; init; }
}
