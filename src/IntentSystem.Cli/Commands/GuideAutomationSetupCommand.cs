using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G260: Read-only <c>intent-cli guide automation setup</c>. Returns
/// paste-ready, worktree-friendly setup prompts an AI coding agent
/// (Codex/Claude) consumes when an operator says "set up the
/// implementation loop" or "set up the host review/next-slice loop".
/// The generated prompts always instruct the AI agent to call
/// <c>guide model</c> / <c>guide onboarding</c> / <c>guide commands
/// list</c> / <c>automation summary</c> first; forbid reading
/// <c>intents/rules/**</c>, local skill files, or copied prompts; and
/// delegate label transitions to installed <c>intent-cli automation</c>
/// / <c>worker</c> commands. Never mutates state. Never launches an AI
/// provider.
/// </summary>
internal static class GuideAutomationSetupCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string KindChildImplement = "child-implement";
    private const string KindHostReviewNextSlice = "host-review-next-slice";

    // G320: agent-specific scheduling contract. `--agent claude` resolves to
    // same-thread `/loop <frequency>`; `--agent codex` resolves to a
    // current-thread local-automation heartbeat at the requested frequency;
    // `--agent unknown` (or any other value) MUST surface the required
    // mechanism instead of guessing.
    private const string AgentClaude = "claude";
    private const string AgentCodex = "codex";
    private const string AgentUnknown = "unknown";

    private const string SchedulingClaudeLoopSameThread = "claude-loop-same-thread";
    private const string SchedulingCodexHeartbeatSameThread = "codex-heartbeat-same-thread";
    private const string SchedulingUnknownAskOperator = "unknown-ask-operator";

    private const string UsageLine =
        "Usage: intent-cli guide automation setup --kind|--purpose <child-implement|host-review-next-slice|alias> "
        + "[--repo <owner/repo>] [--domain <name>] [--target-repo <owner/repo>] "
        + "[--agent claude|codex|claude code|codex-cli|unknown] [--cwd <path>] [--cwd-role host|child] "
        + "[--host-role design|review-runtime|child-worker|ambiguous] [--frequency <NNm|NNh>] "
        + "[--format markdown|json]";

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

        if (!TryParseArguments(
                args,
                out var rawKind,
                out var repo,
                out var domain,
                out var targetRepo,
                out var rawAgent,
                out var cwd,
                out var rawCwdRole,
                out var rawHostRole,
                out var frequency,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        // G321: normalize the operator-supplied purpose/agent vocabulary
        // BEFORE any downstream validation so Japanese / English / hyphen
        // / case variants ("実装", "Claude Code", "review & next slice")
        // all hash to the canonical kind + agent names. Unknown purpose
        // falls through to the existing "--kind must be ..." usage error;
        // unknown agent resolves to `unknown` (surfaced via
        // <see cref="ResolveScheduling"/> as the operator ask).
        var canonicalPurpose = GuideAutomationSetupAliasResolver.ResolvePurpose(rawKind);
        var kind = canonicalPurpose ?? rawKind;
        var canonicalAgent = string.IsNullOrWhiteSpace(rawAgent)
            ? null
            : GuideAutomationSetupAliasResolver.ResolveAgent(rawAgent);
        var agent = canonicalAgent;

        // G321: cwd-role resolution. When the operator did not supply
        // `--cwd-role`, we infer it from the canonical purpose. When they
        // did, we validate it and surface a structured conflict if it
        // contradicts the canonical purpose's host/child semantic.
        string? cwdRoleConflict = null;
        string? cwdRoleCanonical = null;
        if (!string.IsNullOrWhiteSpace(rawCwdRole))
        {
            cwdRoleCanonical = GuideAutomationSetupAliasResolver.ResolveCwdRole(rawCwdRole);
            if (cwdRoleCanonical is null)
            {
                writer.WriteLine(
                    $"--cwd-role must be '{GuideAutomationSetupAliasResolver.CanonicalCwdRoleHost}' or '{GuideAutomationSetupAliasResolver.CanonicalCwdRoleChild}' (got '{rawCwdRole}').");
                writer.WriteLine(UsageLine);
                return 1;
            }
        }
        if (canonicalPurpose is not null)
        {
            var inferredCwdRole = GuideAutomationSetupAliasResolver.InferCwdRole(canonicalPurpose);
            if (cwdRoleCanonical is null)
            {
                cwdRoleCanonical = inferredCwdRole;
            }
            else if (!string.Equals(cwdRoleCanonical, inferredCwdRole, StringComparison.Ordinal))
            {
                // Structured conflict (G321): host purpose with child cwd
                // (or vice versa) cannot be satisfied without picking one
                // side. Refuse to generate a contract that silently buries
                // the contradiction.
                cwdRoleConflict =
                    $"--cwd-role '{cwdRoleCanonical}' conflicts with --purpose '{canonicalPurpose}' (expected cwd-role '{inferredCwdRole}'). Re-run with a matching --cwd-role or change --purpose.";
            }
        }

        if (cwdRoleConflict is not null)
        {
            writer.WriteLine(cwdRoleConflict);
            writer.WriteLine(UsageLine);
            return 1;
        }

        // G326: resolve the host role (design / review-runtime /
        // child-worker / ambiguous). When the operator did not pass
        // `--host-role`, infer from the canonical cwd-role: child cwd
        // implies `child-worker`; host cwd cannot be disambiguated
        // between design and review-runtime without an explicit flag, so
        // we surface `ambiguous` and let the operator decide. This keeps
        // the durable-state ownership decision explicit per G326.
        string hostRoleCanonical;
        if (!string.IsNullOrWhiteSpace(rawHostRole))
        {
            hostRoleCanonical = HostOwnershipModel.ResolveRole(rawHostRole);
            if (string.Equals(hostRoleCanonical, HostOwnershipModel.RoleAmbiguous, StringComparison.Ordinal)
                && !string.Equals(rawHostRole, HostOwnershipModel.RoleAmbiguous, StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteLine(
                    $"--host-role '{rawHostRole}' did not resolve to a known role (design / review-runtime / child-worker / ambiguous).");
                writer.WriteLine(UsageLine);
                return 1;
            }
        }
        else if (string.Equals(cwdRoleCanonical, GuideAutomationSetupAliasResolver.CanonicalCwdRoleChild, StringComparison.Ordinal))
        {
            hostRoleCanonical = HostOwnershipModel.RoleChildWorker;
        }
        else
        {
            // Host cwd without an explicit --host-role flag: surface
            // ambiguous so the operator names design vs review-runtime
            // before any role-scoped mutation guidance is acted on.
            hostRoleCanonical = HostOwnershipModel.RoleAmbiguous;
        }

        // G320: agent + frequency are coupled. If the operator supplies an
        // agent we know how to handle (claude/codex) we also require a
        // frequency so the generated contract can pin the exact same-thread
        // loop cadence instead of inventing one.
        if (!string.IsNullOrWhiteSpace(agent)
            && (string.Equals(agent, AgentClaude, StringComparison.Ordinal)
                || string.Equals(agent, AgentCodex, StringComparison.Ordinal))
            && string.IsNullOrWhiteSpace(frequency))
        {
            writer.WriteLine(
                $"--frequency is required when --agent is '{agent}' so the generated contract can name the exact same-thread loop cadence.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        switch (kind)
        {
            case KindChildImplement:
                return EmitResult(writer, format, BuildChildImplement(repo, domain, agent, cwd, frequency, cwdRoleCanonical, rawKind, rawAgent, hostRoleCanonical));

            case KindHostReviewNextSlice:
                if (string.IsNullOrWhiteSpace(domain))
                {
                    writer.WriteLine("--domain is required for --kind host-review-next-slice.");
                    writer.WriteLine(UsageLine);
                    return 1;
                }
                if (string.IsNullOrWhiteSpace(targetRepo))
                {
                    writer.WriteLine("--target-repo is required for --kind host-review-next-slice.");
                    writer.WriteLine(UsageLine);
                    return 1;
                }

                return EmitResult(writer, format, BuildHostReviewNextSlice(domain!, targetRepo!, agent, cwd, frequency, cwdRoleCanonical, rawKind, rawAgent, hostRoleCanonical));

            default:
                writer.WriteLine(
                    $"--kind must be '{KindChildImplement}' or '{KindHostReviewNextSlice}' (got '{kind}').");
                writer.WriteLine(UsageLine);
                return 1;
        }
    }

    /// <summary>
    /// G320: resolve the operator-supplied agent into a stable scheduling
    /// mechanism identifier + a human-readable contract block. Unknown
    /// agents MUST NOT silently default to claude/codex behavior — the
    /// contract surfaces the required ask instead.
    /// </summary>
    private static (string Mechanism, string ContractBlock) ResolveScheduling(string? agent, string? frequency)
    {
        var normalizedAgent = string.IsNullOrWhiteSpace(agent) ? null : agent!.Trim().ToLowerInvariant();
        var freq = string.IsNullOrWhiteSpace(frequency) ? null : frequency!.Trim();

        if (string.Equals(normalizedAgent, AgentClaude, StringComparison.Ordinal))
        {
            return (
                SchedulingClaudeLoopSameThread,
                $@"Agent: Claude (Claude Code).
Scheduling mechanism: Claude Code same-thread `/loop {freq} <prompt>` (G314). Each wake re-enters the same chat thread; do NOT spawn a new chat, a new Claude Code session, a remote/cloud scheduler, an external cron, or an out-of-process monitor — those cannot reach this thread's local filesystem paths or `.intent-cli` packages. If the current Claude Code session lacks `/loop`, surface the gap to the operator instead of substituting a non-local schedule.");
        }

        if (string.Equals(normalizedAgent, AgentCodex, StringComparison.Ordinal))
        {
            return (
                SchedulingCodexHeartbeatSameThread,
                $@"Agent: Codex.
Scheduling mechanism: Codex current-thread local automation / heartbeat at {freq} (G314). Each wake re-enters the same Codex session; do NOT open a new Codex session, a remote/cloud scheduler, or any out-of-process monitor — those cannot reach this thread's local paths. If the active Codex session has no heartbeat surface, surface the gap to the operator instead of substituting a non-local schedule.");
        }

        // Unknown / unsupplied agent: refuse to guess.
        var nameForBlock = string.IsNullOrWhiteSpace(normalizedAgent) ? "<unspecified>" : normalizedAgent;
        return (
            SchedulingUnknownAskOperator,
            $@"Agent: {nameForBlock}.
Scheduling mechanism: unknown — ASK the operator which same-thread / local-automation mechanism `{nameForBlock}` exposes before creating any cron, monitor, scheduler, or recurring wakeup. Do NOT guess `/loop`, a heartbeat, a remote cron, or a cloud scheduler. The chosen mechanism MUST reach this thread's local filesystem paths and `.intent-cli` packages; remote/cloud schedulers are not acceptable substitutes.");
    }

    private static int EmitResult(TextWriter writer, string format, GuideAutomationSetupResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    private static GuideAutomationSetupResult BuildChildImplement(
        string? repo,
        string? domain,
        string? agent,
        string? cwd,
        string? frequency,
        string? cwdRoleCanonical = null,
        string? rawPurpose = null,
        string? rawAgent = null,
        string? hostRoleCanonical = null)
    {
        var repoLabel = string.IsNullOrWhiteSpace(repo) ? "the repo in the current worktree" : $"`{repo}`";
        var domainPlaceholder = string.IsNullOrWhiteSpace(domain) ? "<DOMAIN>" : domain;
        var (schedulingMechanism, schedulingBlock) = ResolveScheduling(agent, frequency);
        var cwdLine = string.IsNullOrWhiteSpace(cwd)
            ? string.Empty
            : $"\n\nOperator-supplied cwd: `{cwd}`. Confirm the current directory matches this path before running the loop body; if not, stop with `wrong-cwd` and surface to the operator instead of guessing.";
        var schedulingSuffix = string.IsNullOrWhiteSpace(agent)
            ? string.Empty
            : $"\n\nScheduling for this agent (G320):\n{schedulingBlock}";

        var prompt =
$@"Set up the child implementation and PR-comment-update loop for {repoLabel} once. Operator minimal trigger phrase: ""intent-cli に聞いて、この repo の実装と PR comment update loop を設定してください"" — the detailed procedure below is what intent-cli emits. Do not register any cron, monitor, reminder, scheduler, or recurring wakeup as part of this setup unless the operator explicitly asks.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — `primary` vs `support` vs `advanced` (`run`) vs `experimental` classification.
4. `intent-cli automation summary --domain {domainPlaceholder} --format json` — canonical label-driven contract and capability JSON for the parent intent domain.

Loop body (single wake; the operator drives subsequent wakes if any):
1. Save the child worktree path: `CHILD_WORKTREE=""$PWD""`. Confirm it is a git worktree root. Stop with `wrong-worktree` if not.
2. Resolve `<OWNER>/<REPO>` from the child cwd: `gh repo view --json nameWithOwner --jq .nameWithOwner` (fall back to `git remote get-url origin`).
3. `git fetch --all --prune` and `git status --short`. If dirty in a dedicated automation worktree, clean local residue (`git reset --hard`, `git clean -fd`, submodule reset). Never `git clean -fdx`. Never clean a personal/shared checkout.
4. From the parent host root (NOT the child cwd), run `intent-cli worker next-action --repo <OWNER>/<REPO> --workdir $CHILD_WORKTREE --format json`. Dispatch on `action`:
   - `none` → stop with `idle`.
   - `issue-to-pr` → claim with `intent-cli worker claim --kind issue --number <n> --write --format json`, run the issue-to-PR workflow on the returned URL only, classify outcome, then `worker result-summary --kind issue-to-pr ...` and `worker complete --kind issue --number <n> --outcome <outcome> --write --format json`.
   - `pr-comment-fix` → The PR URL returned by `worker next-action` (or supplied directly by the operator) is the authoritative work input; do not look up queue-state or linked PR to decide what to repair. Claim with `intent-cli worker claim --kind pr --number <n> --write --format json`. Check out the existing PR head branch: `gh pr checkout <n> --repo <OWNER>/<REPO>`. Apply only the narrow change requested in review comments. Run targeted tests. Push to the same branch: `git push`. From the parent host root, run `worker result-summary --kind pr-comment-fix --pr <n> --repo <OWNER>/<REPO> --format json`, then `worker complete --kind pr --number <n> --outcome <outcome> --write --format json`.

Hard rules:
- Do not read `intents/rules/**`, local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, etc.), or copied prompt files for routine collaboration. Use `intent-cli guide ...` instead.
- Do not call `intent-cli run` from this loop. `run` is advanced runtime (integration smoke / replay / dogfooding), not the chat-first path.
- If `intent-cli automation doctor` reports `stale-host-cli` or a missing required surface, **abort the wake** before any mutation and refresh the installed CLI. Never fall back to direct DLL invocation or `dotnet run`.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- All label transitions go through installed `intent-cli automation` / `intent-cli worker` commands. No manual `gh ... edit --add-label` / `--remove-label` fallback for workflow labels.
- Never apply `intent-target` from the child loop; it is host-owned.
- Never apply `intent-pr-created` to a PR; it is an issue-side completion marker.
- Process at most one action per wake.
- For `pr-comment-fix` turns: never edit `queue-state.json`, `linked_issue`, or `linked_pr`; those are host-owned durable bookkeeping and must not be repaired from the child loop.
- For `pr-comment-fix` turns: never run `intent-cli automation issue-publish`; that command is for publishing child issues, not for resolving PR comment repairs.

Frequency policy (applies only when a recurring local loop is explicitly requested; the default is one-wake execution):
- This setup prompt describes a single-wake run, not a recurring loop. One-wake execution does not create any scheduler.
- If the operator asks for a recurring loop, ask for the frequency before creating any cron, monitor, or scheduler. Never guess or use a tool-default interval.
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): about 20 minutes.
- Local same-thread loops are the baseline for workflows that depend on local paths or local `.intent-cli` packages. Cloud or new-thread schedulers cannot access local paths.{cwdLine}{schedulingSuffix}";

        return new GuideAutomationSetupResult
        {
            Kind = KindChildImplement,
            CanonicalPurpose = KindChildImplement,
            RawPurpose = NullIfBlank(rawPurpose),
            Repo = string.IsNullOrWhiteSpace(repo) ? null : repo,
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            // G321: keep `agent` as the operator-facing string. For known
            // agents (claude / codex) raw and canonical agree; for unknown
            // agents we surface the raw string ("robot") so the operator
            // sees their own input, while `canonical_agent` carries the
            // normalized "unknown" identifier.
            Agent = NullIfBlank(rawAgent) ?? (string.IsNullOrWhiteSpace(agent) ? null : agent),
            CanonicalAgent = string.IsNullOrWhiteSpace(agent) ? null : agent,
            RawAgent = NullIfBlank(rawAgent),
            Cwd = string.IsNullOrWhiteSpace(cwd) ? null : cwd,
            CwdRole = string.IsNullOrWhiteSpace(cwdRoleCanonical)
                ? GuideAutomationSetupAliasResolver.CanonicalCwdRoleChild
                : cwdRoleCanonical,
            Frequency = string.IsNullOrWhiteSpace(frequency) ? null : frequency,
            SchedulingMechanism = string.IsNullOrWhiteSpace(agent) ? null : schedulingMechanism,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domainPlaceholder} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files (gh-issue-to-pr, gh-fix-pr-comment, etc.)",
                "copied prompt files"
            },
            LabelOwnership = "All label transitions delegated to installed intent-cli automation / worker commands. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt resolves the repo from the child worktree's `gh` / `git remote` and runs the selector from the parent host root with --workdir; no hard-coded paths, and the same prompt works across local worktrees.",
            Prohibitions = GuidanceProhibitionCatalog.All,
            HostRole = hostRoleCanonical
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static GuideAutomationSetupResult BuildHostReviewNextSlice(
        string domain,
        string targetRepo,
        string? agent,
        string? cwd,
        string? frequency,
        string? cwdRoleCanonical = null,
        string? rawPurpose = null,
        string? rawAgent = null,
        string? hostRoleCanonical = null)
    {
        var (schedulingMechanism, schedulingBlock) = ResolveScheduling(agent, frequency);
        var cwdLine = string.IsNullOrWhiteSpace(cwd)
            ? string.Empty
            : $"\n\nOperator-supplied parent host root: `{cwd}`. Confirm cwd matches this path before running the loop body; if not, stop with `wrong-host-root` and surface to the operator instead of guessing.";
        var schedulingSuffix = string.IsNullOrWhiteSpace(agent)
            ? string.Empty
            : $"\n\nScheduling for this agent (G320):\n{schedulingBlock}";
        var prompt =
$@"Set up the host review and next-slice loop for domain `{domain}` against `{targetRepo}` once, in this existing chat thread.

IMPORTANT — do not create a new chat, a new Claude Code session, a cron job, a monitor, a cloud schedule, or any other recurring wakeup unless the operator explicitly asks for one. Run the loop body exactly once, in the current thread, then stop.

Cloud and new-thread schedulers CANNOT access local paths (e.g. `/Users/.../.intent-cli`) or local dotnet packages. Only use a remote/cloud scheduler if the operator explicitly provides a cloud-compatible intent-cli endpoint.

First-call sequence (read-only; required before any mutation):
1. `intent-cli guide model --format json` — confirm chat-first / CLI-internal collaboration model.
2. `intent-cli guide onboarding --format json` — first-call sequence for a fresh agent.
3. `intent-cli guide commands list --format json` — surface `primary` / `support` / `advanced` / `experimental` buckets.
4. `intent-cli automation summary --domain {domain} --format json` — canonical label-driven contract and capability JSON.
5. `intent-cli intent status --domain {domain} --format json` — current baseline / WIP / queued / clarifications.
6. `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — verify WIP cap and clarification gates.

Loop body (single wake):
1. Confirm cwd is the parent host repo root.
2. Stage 1 — review/closeout:
   Run `intent-cli automation host-review-preflight --repo {targetRepo} --format json` and dispatch on `action`:
   - `stale-host-cli` → Abort the wake immediately. Refresh or reinstall `intent-cli` on PATH before the next wake. Do NOT fall back to direct DLL invocation, raw `gh` label mutation, or report `no-actionable-item`. Missing or stale CLI surfaces are an infrastructure error, not an idle state.
   - `skip-next-slice-due-to-wip` → WIP cap is active. Skip Stage 2. Stop the wake. Do not publish a new child issue while open `intent-target` items remain in `{targetRepo}`.
   - `review-pr` → Review the selected PR: `intent-cli review closeout-plan --pr <n> --repo {targetRepo} --domain {domain} --format json` and `intent-cli guide review --pr <n> --repo {targetRepo} --domain {domain} --format json`. If review passes: `intent-cli automation pr-transition --transition approved --repo {targetRepo} --pr <n> --write --format json`, merge via the host's existing merge step, then `intent-cli closeout pr --pr <n> --repo {targetRepo} --write --format json`. If review needs repair: leave an actionable PR comment, then `intent-cli automation pr-transition --transition request-update --repo {targetRepo} --pr <n> --write --format json`. After Stage 1 closes out or requests repair, proceed to Stage 2 only if the WIP cap is clear.
   - `no-actionable-item` or `candidate-ready` → No eligible PR found in Stage 1. This is NOT the final idle decision. Proceed directly to Stage 2.
3. Stage 2 — next-slice (run when Stage 1 result is `no-actionable-item`, `candidate-ready`, or when Stage 1 review/closeout has cleared the WIP):
   - `intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json` — read `recommended_outcome` and dispatch:
   - `issue-cut-ready` → proceed to packet draft and publication below. Queued unpublished packets with satisfied dependencies must be published here rather than ignored.
   - `clarification-required` → stop and surface the open blocker or ambiguous question to the operator. Do NOT declare idle; the operator must unblock before the next-slice can proceed.
   - `no-actionable-item` → stop with truly `no-actionable-item`. This is the only valid idle stop: Stage 1 found no eligible PR AND Stage 2 found no actionable packet.
   - Any other outcome → stop and surface it as a blocker to the operator. Do NOT declare idle.
   - `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --dry-run --format markdown` — preview the packet.
   - With operator acceptance: `intent-cli packet draft --execution-unit <id> --target-repo {targetRepo} --format json` then `intent-cli issue publish-flow <id> --repo {targetRepo} --write --format json`.
   - After parent durable state is pushed: `intent-cli automation issue-publish --repo {targetRepo} --issue <n> --write --format json`.

Hard rules:
- Do not read `intents/rules/**`, local skill files, or copied prompt files for routine review/closeout. Use `intent-cli guide ...` and `intent-cli automation ...` instead.
- Do not call `intent-cli run`. `run` is advanced runtime, not the host review/closeout path.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Do not open a new chat, session, cron, monitor, or scheduler. Run one wake in this thread; the operator controls subsequent wakes.
- Every label transition (`intent-target`, `intent-pr-reviewing`, `intent-pr-request-update`, `intent-pr-approved`, `intent-pr-rereview-ready`, `intent-pr-update-in-progress`, `intent-issue-in-progress`, `intent-pr-created`) goes through installed `intent-cli automation pr-transition` / `intent-cli automation issue-publish` / `intent-cli worker claim` / `intent-cli worker complete`. No manual `gh ... edit --add-label` / `--remove-label` fallback.
- Never apply `intent-pr-created` to a PR.
- Honor the WIP cap: do not cut a new child issue while any open `intent-target` issue/PR remains in `{targetRepo}`.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.
- Process at most one PR review and one new child issue per wake.
- `no-actionable-item` from Stage 1 preflight is NOT idle; it means no PR was found. Stage 2 must still run before declaring the wake truly idle.

Frequency policy (applies only when a recurring local loop is explicitly requested; the default is one-wake execution):
- This setup prompt describes a single-wake run, not a recurring loop. One-wake execution does not create any scheduler.
- If the operator asks for a recurring loop, ask for the frequency before creating any cron, monitor, or scheduler. Never guess or use a tool-default interval.
- High-frequency local loops (active development): 5 minutes.
- Low-frequency local loops (background / idle polling): about 20 minutes.
- Local same-thread loops are the baseline for workflows that depend on local paths or local `.intent-cli` packages. Cloud or new-thread schedulers cannot access local paths.{cwdLine}{schedulingSuffix}";

        return new GuideAutomationSetupResult
        {
            Kind = KindHostReviewNextSlice,
            CanonicalPurpose = KindHostReviewNextSlice,
            RawPurpose = NullIfBlank(rawPurpose),
            Domain = domain,
            TargetRepo = targetRepo,
            // G321: keep `agent` as the operator-facing string. For known
            // agents (claude / codex) raw and canonical agree; for unknown
            // agents we surface the raw string ("robot") so the operator
            // sees their own input, while `canonical_agent` carries the
            // normalized "unknown" identifier.
            Agent = NullIfBlank(rawAgent) ?? (string.IsNullOrWhiteSpace(agent) ? null : agent),
            CanonicalAgent = string.IsNullOrWhiteSpace(agent) ? null : agent,
            RawAgent = NullIfBlank(rawAgent),
            Cwd = string.IsNullOrWhiteSpace(cwd) ? null : cwd,
            CwdRole = string.IsNullOrWhiteSpace(cwdRoleCanonical)
                ? GuideAutomationSetupAliasResolver.CanonicalCwdRoleHost
                : cwdRoleCanonical,
            Frequency = string.IsNullOrWhiteSpace(frequency) ? null : frequency,
            SchedulingMechanism = string.IsNullOrWhiteSpace(agent) ? null : schedulingMechanism,
            Prompt = prompt,
            FirstCalls = new[]
            {
                "intent-cli guide model --format json",
                "intent-cli guide onboarding --format json",
                "intent-cli guide commands list --format json",
                $"intent-cli automation summary --domain {domain} --format json",
                $"intent-cli intent status --domain {domain} --format json",
                $"intent-cli intent next-slice --dry-run --domain {domain} --target-repo {targetRepo} --format json"
            },
            ForbiddenSources = new[]
            {
                "intents/rules/**",
                "local skill files",
                "copied prompt files"
            },
            LabelOwnership = "All review-side and issue-side label transitions delegated to installed `intent-cli automation pr-transition` / `automation issue-publish` / `worker claim` / `worker complete`. Manual `gh ... edit --label` fallback is forbidden.",
            WorktreeFriendly = "The prompt names the parent host root as cwd but does not hardcode any operator-specific path beyond that; the same prompt works across host-side checkouts.",
            Prohibitions = GuidanceProhibitionCatalog.All,
            HostRole = hostRoleCanonical
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideAutomationSetupResult result)
    {
        writer.WriteLine($"# Guide automation setup — {result.Kind}");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Repo))
        {
            writer.WriteLine($"- repo: {result.Repo}");
        }
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
        }
        if (!string.IsNullOrWhiteSpace(result.TargetRepo))
        {
            writer.WriteLine($"- target repo: {result.TargetRepo}");
        }
        if (!string.IsNullOrWhiteSpace(result.Agent))
        {
            writer.WriteLine($"- agent: {result.Agent}");
        }
        if (!string.IsNullOrWhiteSpace(result.Cwd))
        {
            writer.WriteLine($"- cwd: {result.Cwd}");
        }
        if (!string.IsNullOrWhiteSpace(result.Frequency))
        {
            writer.WriteLine($"- frequency: {result.Frequency}");
        }
        if (!string.IsNullOrWhiteSpace(result.SchedulingMechanism))
        {
            writer.WriteLine($"- scheduling mechanism: {result.SchedulingMechanism}");
        }
        if (!string.IsNullOrWhiteSpace(result.CwdRole))
        {
            writer.WriteLine($"- cwd role: {result.CwdRole}");
        }
        if (!string.IsNullOrWhiteSpace(result.HostRole))
        {
            // G326 review fix (PR #756): render the resolved host role
            // (design / review-runtime / child-worker / ambiguous) in
            // the markdown contract operators paste and follow. JSON
            // exposes `host_role` separately; this line ensures markdown
            // does not silently lose the role binding that decides what
            // the cwd is allowed to mutate.
            writer.WriteLine($"- host role: {result.HostRole}");
        }
        if (!string.IsNullOrWhiteSpace(result.CanonicalPurpose)
            && !string.IsNullOrWhiteSpace(result.RawPurpose)
            && !string.Equals(result.CanonicalPurpose, result.RawPurpose, StringComparison.Ordinal))
        {
            writer.WriteLine($"- canonical purpose: {result.CanonicalPurpose} (raw: {result.RawPurpose})");
        }
        if (!string.IsNullOrWhiteSpace(result.RawAgent)
            && !string.IsNullOrWhiteSpace(result.CanonicalAgent)
            && !string.Equals(result.RawAgent, result.CanonicalAgent, StringComparison.Ordinal))
        {
            writer.WriteLine($"- canonical agent: {result.CanonicalAgent} (raw: {result.RawAgent})");
        }
        writer.WriteLine();

        writer.WriteLine("## First-call sequence (read-only)");
        foreach (var call in result.FirstCalls)
        {
            writer.WriteLine($"- `{call}`");
        }
        writer.WriteLine();

        writer.WriteLine("## Forbidden rule sources");
        foreach (var src in result.ForbiddenSources)
        {
            writer.WriteLine($"- {src}");
        }
        writer.WriteLine();

        writer.WriteLine("## Label ownership");
        writer.WriteLine();
        writer.WriteLine(result.LabelOwnership);
        writer.WriteLine();

        writer.WriteLine("## Worktree-friendly assumption");
        writer.WriteLine();
        writer.WriteLine(result.WorktreeFriendly);
        writer.WriteLine();

        writer.WriteLine("## Prompt");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string? repo,
        out string? domain,
        out string? targetRepo,
        out string? agent,
        out string? cwd,
        out string? cwdRole,
        out string? hostRole,
        out string? frequency,
        out string format,
        out string error)
    {
        kind = null;
        repo = null;
        domain = null;
        targetRepo = null;
        agent = null;
        cwd = null;
        cwdRole = null;
        hostRole = null;
        frequency = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                // G320: `--purpose` is the new operator-facing flag name; it is
                // an exact alias of the existing `--kind` value so prior tests,
                // host-loop callers, and copy-pasted automation summaries keep
                // working without churn.
                case "--kind":
                case "--purpose":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"{argument} requires a value.";
                        return false;
                    }

                    kind = args[index + 1];
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
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
                        error = "--agent requires a value (claude, codex, or unknown).";
                        return false;
                    }

                    agent = args[index + 1];
                    index++;
                    break;

                case "--cwd":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--cwd requires a value.";
                        return false;
                    }

                    cwd = args[index + 1];
                    index++;
                    break;

                case "--cwd-role":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--cwd-role requires a value (host or child).";
                        return false;
                    }

                    cwdRole = args[index + 1];
                    index++;
                    break;

                case "--host-role":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--host-role requires a value (design, review-runtime, child-worker, or ambiguous).";
                        return false;
                    }

                    hostRole = args[index + 1];
                    index++;
                    break;

                case "--frequency":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--frequency requires a value (e.g. 5m, 20m, 1h).";
                        return false;
                    }

                    frequency = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind (or --purpose) is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide automation setup");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready setup prompts for the child implementation loop or the host review / next-slice loop.");
        writer.WriteLine();
        writer.WriteLine("For --kind child-implement:");
        writer.WriteLine("  --repo is optional; omit to derive the repo from the current child worktree via gh/git.");
        writer.WriteLine("  --domain is optional; omit to emit a <DOMAIN> placeholder in the generated prompt.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideAutomationSetupResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>
    /// G321: canonical purpose name (<c>child-implement</c> /
    /// <c>host-review-next-slice</c>) — equal to <see cref="Kind"/> but
    /// emitted as its own controller-facing field so downstream consumers
    /// can dispatch on a stable identifier independent of the legacy
    /// <c>kind</c> wire vocabulary.
    /// </summary>
    [JsonPropertyName("canonical_purpose")]
    public string? CanonicalPurpose { get; init; }

    /// <summary>
    /// G321: original operator-supplied purpose / kind phrase before
    /// alias resolution (e.g. <c>実装</c>, <c>Claude Code</c>,
    /// <c>review &amp; next slice</c>). <c>null</c> when the operator
    /// already passed the canonical value.
    /// </summary>
    [JsonPropertyName("raw_purpose")]
    public string? RawPurpose { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    /// <summary>
    /// G320: operator-supplied agent identifier. <c>null</c> when the caller
    /// did not name an agent (backward-compat path); explicit values are
    /// <c>claude</c>, <c>codex</c>, or any other string (treated as unknown
    /// in <see cref="SchedulingMechanism"/>).
    /// </summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    /// <summary>
    /// G321: canonical agent identifier (<c>claude</c>, <c>codex</c>,
    /// <c>unknown</c>). Identical to <see cref="Agent"/> after
    /// normalization; kept as a separate field so controllers can switch
    /// on a stable name even if a future refactor renames
    /// <see cref="Agent"/>.
    /// </summary>
    [JsonPropertyName("canonical_agent")]
    public string? CanonicalAgent { get; init; }

    /// <summary>
    /// G321: original operator-supplied agent phrase before alias
    /// resolution (e.g. <c>Claude Code</c>, <c>codex-cli</c>).
    /// <c>null</c> when the operator already passed the canonical value
    /// or no agent at all.
    /// </summary>
    [JsonPropertyName("raw_agent")]
    public string? RawAgent { get; init; }

    /// <summary>
    /// G321: cwd-role inference. <c>host</c> for host review/next-slice
    /// loops (cwd is the parent host repo root). <c>child</c> for child
    /// implementation/PR-comment-update loops (cwd is a child worktree
    /// with a parent host root reference). When the operator passes an
    /// explicit <c>--cwd-role</c> that conflicts with the canonical
    /// purpose, the command refuses (structured conflict) instead of
    /// silently picking one side.
    /// </summary>
    [JsonPropertyName("cwd_role")]
    public string? CwdRole { get; init; }

    /// <summary>
    /// G320: operator-supplied cwd hint (parent host root for host-loop,
    /// child worktree path for child-loop). Pure documentation: the loop
    /// body still resolves runtime cwd via <c>git rev-parse</c>.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    /// <summary>
    /// G320: operator-requested loop cadence (e.g. <c>5m</c>, <c>20m</c>).
    /// Pinned into the generated contract instead of letting agents
    /// invent one.
    /// </summary>
    [JsonPropertyName("frequency")]
    public string? Frequency { get; init; }

    /// <summary>
    /// G320: stable identifier for the scheduling mechanism the generated
    /// contract names. Controller-friendly equivalent of the rendered
    /// "Scheduling for this agent" block. <c>null</c> when no agent was
    /// supplied (backward-compat).
    /// </summary>
    [JsonPropertyName("scheduling_mechanism")]
    public string? SchedulingMechanism { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("first_calls")]
    public required IReadOnlyList<string> FirstCalls { get; init; }

    [JsonPropertyName("forbidden_sources")]
    public required IReadOnlyList<string> ForbiddenSources { get; init; }

    [JsonPropertyName("label_ownership")]
    public required string LabelOwnership { get; init; }

    [JsonPropertyName("worktree_friendly")]
    public required string WorktreeFriendly { get; init; }

    /// <summary>
    /// G323: structured prohibitions list. Every generated setup
    /// contract advertises the same canonical safety prohibitions
    /// (no local rules / skill / copied prompt fallback, no stale
    /// memory fallback, no `dotnet run`, no raw `gh` label edits, no
    /// AI provider launch, no `intent-cli run` from chat-first loops,
    /// abort on stale-cli / missing command surface). Controllers can
    /// dispatch on the structured <c>id</c> values without parsing
    /// prose.
    /// </summary>
    [JsonPropertyName("prohibitions")]
    public IReadOnlyList<GuidanceProhibition>? Prohibitions { get; init; }

    /// <summary>
    /// G326: structured host role identifier (<c>design</c>,
    /// <c>review-runtime</c>, <c>child-worker</c>, or <c>ambiguous</c>).
    /// Inferred from <see cref="CwdRole"/> when the operator did not
    /// pass <c>--host-role</c> explicitly: a <c>child</c> cwd resolves
    /// to <c>child-worker</c>; a <c>host</c> cwd surfaces
    /// <c>ambiguous</c> because design and review-runtime cannot be
    /// disambiguated without an explicit flag (see
    /// <see cref="HostOwnershipModel"/> for the may-write /
    /// must-not-write matrix).
    /// </summary>
    [JsonPropertyName("host_role")]
    public string? HostRole { get; init; }
}
