using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G487: read-only guide surface for an OPTIONAL agmsg-backed orchestrator
/// thread (ADR-012 / spec-26). Renders paste-ready prompts for an orchestrator
/// thread plus the implementation/review threads it delegates to, and pins the
/// operating contract: agmsg is a message/progress/completion signal layer
/// ONLY; <c>intent-cli</c> and GitHub remain authoritative for domain status,
/// queue-state, issue/PR facts, labels, CI, and closeout. The existing
/// timer-loop mode stays valid; orchestrator-message mode is opt-in and MUST
/// NOT also launch implement/review recurring timer loops for the same
/// domain/repo (no mixed-mode timer races). Host-state-free; never launches an
/// AI provider; never sends agmsg messages itself.
///
/// G489: a host repo can legitimately hold several intent domains (e.g.
/// <c>sekiban-as-a-service</c>, <c>sekiban-wasm-runtime</c>, <c>intent-cli</c>),
/// and more than one domain may target the same GitHub repository. The guide
/// therefore distinguishes a SINGLE-DOMAIN orchestrator (only one domain in
/// scope even though other-domain metadata is visible) from a MULTI-DOMAIN
/// orchestrator (intentionally coordinates several domains and must carry
/// explicit per-delegation routing). <c>--mode single-domain|multi-domain</c>
/// selects which contract the generated prompts emphasize. An execution-unit
/// ID prefix mismatch alone is NOT a wrong-repo signal — packet/domain metadata
/// and routing context decide.
/// </summary>
internal static class GuideOrchestratorThreadCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeSingleDomain = "single-domain";
    private const string ModeMultiDomain = "multi-domain";

    private const string UsageLine =
        "Usage: intent-cli guide orchestrator-thread [--domain <name>] [--target-repo <owner/repo>] [--agent <agent>] [--mode single-domain|multi-domain] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

        if (!TryParseArguments(args, out var format, out var values, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var guide = BuildGuide(values);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(guide, JsonOptions));
            writer.WriteLine();
            return 0;
        }

        WriteMarkdown(writer, guide);
        return 0;
    }

    private static OrchestratorThreadGuide BuildGuide(IReadOnlyDictionary<string, string> values)
    {
        var domain = values["<domain>"];
        var repo = values["<owner/repo>"];
        var agent = values["<agent>"];
        var mode = values["<mode>"];
        var multiDomain = string.Equals(mode, ModeMultiDomain, StringComparison.Ordinal);

        string Apply(string template) => template
            .Replace("<domain>", domain, StringComparison.Ordinal)
            .Replace("<owner/repo>", repo, StringComparison.Ordinal)
            .Replace("<agent>", agent, StringComparison.Ordinal);

        // G489: the orchestrator prompt carries a mode-specific routing clause —
        // single-domain orchestrators stay scoped to one domain; multi-domain
        // orchestrators must attach explicit routing metadata to each delegation.
        var routingClause = multiDomain
            ? Apply(
                " You are in MULTI-DOMAIN mode: you intentionally coordinate several domains, and a single host repo "
                + "can hold several domains while one target repo (`<owner/repo>`) may receive work from more than one "
                + "domain. Before EACH delegation you MUST attach explicit routing metadata — domain, execution unit, "
                + "target repo, implementation cwd/worktree, review cwd/worktree, base branch policy, and destination "
                + "thread — and send each execution unit only to the thread that owns that domain's checkout. Never "
                + "delegate without complete routing. An execution-unit ID prefix that differs from the domain name is "
                + "NOT by itself a wrong-repo signal — compare packet/domain metadata and the routing context, not the "
                + "prefix.")
            : Apply(
                " You are in SINGLE-DOMAIN mode: only domain `<domain>` is in scope. A host checkout can expose other "
                + "domains' metadata in the same repo; those other-domain items are OUT OF SCOPE — do NOT delegate, "
                + "publish, or repair them, even if they target `<owner/repo>`. Escalate to the operator to switch "
                + "domain/mode instead of treating a visible other-domain item as delegable.");

        return new OrchestratorThreadGuide
        {
            Summary =
                "Optional agmsg-backed orchestrator thread (ADR-012 / spec-26). agmsg carries natural-language "
                + "delegation / progress / completion / blocker signals between threads; it is NOT workflow state. "
                + "intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, "
                + "CI, and closeout.",
            ModeSeparation = new OrchestratorModeSeparation
            {
                TimerLoopMode =
                    "Existing mode and still fully supported: implementation and review threads run on recurring "
                    + "timers and use intent-cli `worker next-action` / host review-next-slice as their source of truth. "
                    + "Use `intent-cli guide prompt-matrix` / `guide prompt-template` to set these up. No orchestrator "
                    + "thread is required.",
                OrchestratorMessageMode =
                    "Opt-in mode: a fourth orchestrator thread delegates to implementation/review threads over agmsg "
                    + "instead of relying on independent timers. Choose ONE mode per domain/repo.",
                MixedModeWarning =
                    "Do NOT run both modes for the same domain/repo. In orchestrator-message mode, do NOT launch the "
                    + "implementation/review recurring timer loops for that domain/repo — two drivers (a timer AND the "
                    + "orchestrator) would race on the same GitHub state. The orchestrator paces those threads; they do "
                    + "not also self-schedule.",
            },
            DomainRouting = new OrchestratorDomainRouting
            {
                Mode = mode,
                SingleDomainRule = Apply(
                    "Single-domain orchestrator: only domain `<domain>` is in scope. A host repo can hold several "
                    + "domains, so other-domain queue items may be VISIBLE in the same checkout — they are OUT OF SCOPE "
                    + "unless the operator switches domain/mode. Do not publish, delegate, or repair another domain's "
                    + "item just because it is visible or targets the same repo; escalate instead."),
                MultiDomainRule = Apply(
                    "Multi-domain orchestrator: intentionally coordinates several domains. One target repo can receive "
                    + "work from more than one domain, so visibility is not authorization. Require explicit routing "
                    + "metadata for EACH delegation before publishing, delegating, reviewing, or repairing, and route "
                    + "each execution unit to the thread that owns that domain's checkout."),
                RoutingMetadataFields = new[]
                {
                    "domain",
                    "execution unit",
                    "target repo",
                    "implementation cwd/worktree",
                    "review cwd/worktree",
                    "base branch policy",
                    "destination thread",
                },
                DelegationExample =
                    "{\"delegate\":{\"domain\":\"sekiban-as-a-service\",\"execution_unit\":\"G491\","
                    + "\"target_repo\":\"J-Tech-Japan/intent-system\",\"impl_cwd\":\"/work/sekiban-saas\","
                    + "\"review_cwd\":\"/review/sekiban-saas\",\"base_branch_policy\":\"direct-main\","
                    + "\"destination_thread\":\"implementation@sekiban-as-a-service\"}}",
                PrefixMismatchNote =
                    "Do NOT treat an execution-unit ID prefix that differs from the domain name as a wrong-repo signal "
                    + "on its own (a host repo can hold several domains, and one repo can serve several domains). Compare "
                    + "the packet/domain metadata and the routing context to decide ownership, not the prefix string.",
            },
            Scheduling = new OrchestratorScheduling
            {
                Summary =
                    "In orchestrator-message mode the orchestrator thread is the SINGLE recurring driver. Schedule ONLY "
                    + "the orchestrator (Codex automation every 5m, or Claude same-thread `/loop 5m`); the implementation "
                    + "and review threads are long-lived but LOOPLESS receivers. This keeps a periodic driver — so "
                    + "design progress, agmsg replies, completed CI, and approved PRs are noticed without the operator "
                    + "poking stalled work — while avoiding the mixed-mode timer race.",
                ScheduledThread = "orchestrator",
                ReceiverNote =
                    "Implementation and review threads are loopless receivers: do NOT start a recurring timer/loop in a "
                    + "receiver thread for this domain/repo. A receiver waits for an agmsg delegation, acts once, replies "
                    + "once, and waits again. Only the orchestrator is scheduled.",
                CodexSetupPrompt = Apply(
                    "Codex automation (run every 5 minutes) for the ORCHESTRATOR thread, domain `<domain>` against "
                    + "`<owner/repo>` using `<agent>`: on each run perform exactly ONE orchestrator wake — check "
                    + "design-side progress and agmsg replies, ask intent-cli for state (`intent status`, `worker "
                    + "next-action --github-only`, `automation host-review-preflight`), verify the GitHub facts "
                    + "(CI/approval/merge/closeout), then send AT MOST ONE message (one delegation, one repair, or one "
                    + "escalation) and exit. Do not run implementation/review loops; they are loopless receivers."),
                ClaudeLoopSetupPrompt = Apply(
                    "Claude same-thread setup for the ORCHESTRATOR thread, domain `<domain>` against `<owner/repo>`: in "
                    + "the orchestrator thread run `/loop 5m` with the orchestrator prompt so the same thread re-wakes "
                    + "every 5 minutes. Each wake does exactly one orchestrator pass (read replies, check intent-cli / "
                    + "GitHub state, send AT MOST ONE message). Do NOT also launch `/loop` in the implementation or "
                    + "review threads — those are loopless receivers driven only by your delegations."),
                WakeResponsibilities = new[]
                {
                    Apply("Check design-side progress: newly published packets/issues and intent status changes via `intent-cli intent status --domain <domain> --format json`."),
                    "Read pending agmsg replies from the implementation/review receivers (signals only — re-verify against intent-cli / GitHub).",
                    Apply("Ask intent-cli for worker state: `intent-cli worker next-action --repo <owner/repo> --github-only --format json`."),
                    Apply("Check host review readiness: `intent-cli automation host-review-preflight --repo <owner/repo> --format json`."),
                    "Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge state, and closeout/label state.",
                    "Classify each open PR's CI: pending = wait-and-recheck next wake (no message); green = delegate review/closeout; red = repair or escalate by ownership; stuck = escalate. Pending CI is normal progress, not a reason to message the operator.",
                    "Detect stale blockers and no-reply receivers: a delegation with no accepted/progress reply within the expected window, or a thread stuck off the official workflow.",
                    "If intent-cli reports an `issue-cut-ready` candidate and all gates pass (same-domain or routed, complete contract, no open clarification, dependencies satisfied, under WIP, clean host-sync/preflight), publish ONE issue this wake via canonical publish-flow / issue-publish, then verify — do not ask the operator to create it.",
                    "Decide the single action for this wake: publish one ready next-slice issue, delegate the next slice/PR, send one repair message, or escalate one operator decision.",
                },
                RepairVsEscalate = new OrchestratorRepairEscalate
                {
                    Repair =
                        "REPAIR routine off-rail states yourself by messaging the appropriate thread back onto the "
                        + "official intent-cli workflow — e.g. a receiver that stalled, skipped `worker complete`, "
                        + "applied a label by hand, or has not replied. Routine recovery is a repair message, not an "
                        + "escalation.",
                    Escalate =
                        "ESCALATE to the operator ONLY for: product/design judgment, credentials or security, a "
                        + "destructive local action, or an unresolved canonical ambiguity (intent-cli/GitHub facts "
                        + "genuinely conflict or are missing). Do not escalate states you can repair by message.",
                },
            },
            CiWaitState = new OrchestratorCiWaitState
            {
                Summary =
                    "A PR with pending/running CI is an ACTIVE WAIT STATE, not a blocker. GitHub checks are "
                    + "authoritative for CI state. Re-check the required checks on each scheduled wake; pending CI is "
                    + "normal progress and by itself NEVER triggers a request-update label, a repair message, or an "
                    + "operator question. Always re-verify the required checks immediately before delegating review, "
                    + "merge, or closeout — a green status read on an earlier wake can go stale.",
                States = new[]
                {
                    new OrchestratorCiState
                    {
                        State = "pending",
                        Routing =
                            "PENDING / RUNNING — wait and re-check on the next wake. Do not send a message, do not apply "
                            + "request-update, and do not ask the operator. Track the PR as in-flight and move on; the "
                            + "scheduled cadence re-evaluates it.",
                    },
                    new OrchestratorCiState
                    {
                        State = "green",
                        Routing =
                            "GREEN — all required checks passed. Route to review/closeout: delegate the PR to the review "
                            + "thread (or orchestrate merge/closeout of an already-approved PR) through intent-cli review "
                            + "surfaces. Re-verify the checks are still green at delegation time.",
                    },
                    new OrchestratorCiState
                    {
                        State = "red",
                        Routing =
                            "RED — a required check failed. Route by ownership: if the implementation thread can fix it "
                            + "(test/build/lint failure on the PR branch), send ONE repair message to that thread; if it "
                            + "needs product/design or canonical judgment, escalate. Never delegate merge/closeout while "
                            + "a required check is red.",
                    },
                    new OrchestratorCiState
                    {
                        State = "stuck",
                        Routing =
                            "STUCK / AMBIGUOUS — checks never started, hung well past a reasonable window, or report a "
                            + "conflicting/unknown status that intent-cli and GitHub cannot resolve. Escalate one operator "
                            + "decision (fail closed); do not guess green or force a merge.",
                    },
                },
            },
            NextSlicePublication = new OrchestratorNextSlicePublication
            {
                Summary =
                    "Routine next-slice issue publication is an ORCHESTRATOR responsibility, not an operator question. "
                    + "When intent-cli reports a candidate as `issue-cut-ready` and ALL safety gates pass, the "
                    + "orchestrator publishes it itself through canonical intent-cli commands instead of stopping to ask "
                    + "the operator to create the GitHub issue. Publish AT MOST ONE issue per wake, then verify before "
                    + "delegating implementation.",
                OnePerWake = true,
                Preconditions = new[]
                {
                    Apply("Same-domain context (`<domain>`), or an explicitly routed multi-domain delegation (domain, target repo, destination thread) — never publish a cross-domain candidate without explicit routing."),
                    "The packet contract is complete: no missing required sections (goal, in/out of scope, acceptance criteria, base-branch policy).",
                    "No open clarification or contract ambiguity on the candidate.",
                    "Dependencies are satisfied — every dependency execution unit is completed or already cut; never publish ahead of an uncut dependency.",
                    "Under the WIP cap — no in-progress blocker that should pace the queue first.",
                    Apply("Clean host-sync / preflight: `intent-cli automation host-review-preflight --repo <owner/repo> --format json` and the publish preflight report no blocker, and the target repo/domain is unambiguous."),
                },
                Blockers = new[]
                {
                    "Missing contract sections — hold, do not publish.",
                    "Open clarification / ambiguous contract — hold or escalate one operator decision.",
                    "Dependency mismatch — an uncut or incomplete dependency; hold (publishing ahead would violate the dependency contract).",
                    "WIP cap reached — let the in-progress work drain first.",
                    "Host-sync blocker or failed preflight — fix the sync via intent-cli, do not force the publish.",
                    "Ambiguous target repo or domain (no explicit routing in multi-domain) — escalate rather than guess.",
                },
                CanonicalCommands = new[]
                {
                    Apply("intent-cli issue publish-flow <execution-unit> --repo <owner/repo> --write --format json"),
                    "intent-cli automation issue-publish --write --format json",
                    "Never raw `gh issue create` or `gh ... --add-label`; publication and the `intent-target` label go through the canonical intent-cli surfaces only.",
                },
                PostPublishVerification = new[]
                {
                    "Confirm via intent-cli / GitHub (not chat) that the issue exists with the expected execution-unit body and the `intent-target` label.",
                    "Confirm the durable workflow state (queue-state / linkage / label) reflects the publish through intent-cli surfaces.",
                    "Only after verification, delegate implementation over agmsg — and the implementation receiver still derives its target from `intent-cli worker next-action`, not the agmsg text.",
                },
            },
            Threads = new[]
            {
                new OrchestratorThreadPrompt
                {
                    Role = "orchestrator",
                    Purpose =
                        "Coordinate implementation/review threads for domain `" + domain + "` via agmsg; never mutate "
                        + "workflow state directly.",
                    Prompt = Apply(
                        "You are the ORCHESTRATOR thread for domain `<domain>` against `<owner/repo>` using `<agent>`. "
                        + "You coordinate the implementation and review threads over agmsg; you do NOT implement code, "
                        + "perform semantic review, or mutate GitHub/intent-cli workflow state yourself. agmsg is a "
                        + "signal layer only — intent-cli and GitHub are authoritative. Per wake: read pending agmsg "
                        + "replies, ask intent-cli for the real state (`intent-cli intent status --domain <domain> "
                        + "--format json`, `intent-cli worker next-action --repo <owner/repo> --github-only --format "
                        + "json`, `intent-cli automation host-review-preflight --repo <owner/repo> --format json`), "
                        + "verify the GitHub facts that an agmsg reply claims (merged PR, CI, labels). Treat pending/"
                        + "running CI as an active wait state — re-check it on a later wake rather than asking the "
                        + "operator; delegate review/closeout only after required checks are green, route red checks to "
                        + "repair or escalation by ownership, and escalate only stuck/ambiguous CI. Then take AT MOST "
                        + "ONE forward action: publish one ready next-slice issue (when intent-cli reports it "
                        + "`issue-cut-ready` and all gates pass — via canonical `intent-cli issue publish-flow` / "
                        + "`automation issue-publish`, then verify), send one delegation (assign the next slice/PR), send "
                        + "one repair request (point a stalled thread back to the official intent-cli workflow), or "
                        + "escalate one operator decision. Do NOT "
                        + "launch recurring implement/review timers for this domain/repo while orchestrating. Fail "
                        + "closed: if you detect a second orchestrator for this domain/repo, or agmsg replies conflict "
                        + "with GitHub/intent-cli facts, STOP and escalate rather than guessing. In "
                        + "orchestrator-message mode YOU are the single recurring driver: schedule only this "
                        + "orchestrator thread (Codex automation every 5m, or Claude same-thread `/loop 5m`); the "
                        + "implementation/review receivers stay loopless and act only on your delegations."
                        + routingClause),
                },
                new OrchestratorThreadPrompt
                {
                    Role = "implementation",
                    Purpose =
                        "Implement exactly one delegated item, then report a structured agmsg reply.",
                    Prompt = Apply(
                        "You are the IMPLEMENTATION thread for domain `<domain>` against `<owner/repo>` using `<agent>`, "
                        + "driven by orchestrator agmsg delegations. You are a LOOPLESS receiver: do NOT start your own "
                        + "recurring timer/loop for this domain/repo — wait for a delegation, act once, reply once, then "
                        + "wait again (only the orchestrator is scheduled). When delegated an item, run "
                        + "the normal child implementation workflow: the issue/PR number comes from `intent-cli worker "
                        + "next-action --repo <owner/repo> --github-only`, NOT from the agmsg text. Before claiming, "
                        + "verify your local checkout context matches the delegation: your cwd/worktree, the git remote "
                        + "repo, and the delegated domain must line up with the routing you were handed. If the checkout "
                        + "does not match the delegated repo/domain, STOP and reply blocked instead of claiming. An "
                        + "execution-unit ID prefix that differs from the domain name is NOT by itself a wrong-repo "
                        + "signal — confirm via packet/domain metadata and the routing context, not the prefix. Then "
                        + "claim, implement, open the PR with a `Closes #<issue>` reference, and `worker complete` — all "
                        + "label transitions through intent-cli worker/automation only. intent-cli and GitHub remain "
                        + "authoritative; agmsg is only how you receive the delegation and send back your reply. When "
                        + "done or blocked, send ONE structured agmsg reply (accepted / progress / completed / blocked) "
                        + "citing the GitHub facts (PR number, CI). Do NOT read host metadata (`.intent-cli/**`, "
                        + "`intents/**`)."),
                },
                new OrchestratorThreadPrompt
                {
                    Role = "review",
                    Purpose =
                        "Review/closeout exactly one delegated PR through intent-cli, then report a structured agmsg reply.",
                    Prompt = Apply(
                        "You are the REVIEW thread for domain `<domain>` against `<owner/repo>` using `<agent>`, driven "
                        + "by orchestrator agmsg delegations. You are a LOOPLESS receiver: do NOT start your own "
                        + "recurring timer/loop for this domain/repo — wait for a delegation, act once, reply once, then "
                        + "wait again (only the orchestrator is scheduled). When delegated a PR, run the "
                        + "official host review/closeout through intent-cli surfaces (`review closeout-plan`, `guide "
                        + "review`, `automation pr-transition`, `closeout pr`) — agmsg never replaces semantic review or "
                        + "authorizes a merge. Perform semantic review only when you are the packet `review_role` or "
                        + "explicitly assigned (G480); otherwise orchestrate the merge/closeout of an already-approved "
                        + "PR. Report ONE structured agmsg reply (accepted / progress / completed / blocked) citing the "
                        + "intent-cli/GitHub facts. intent-cli and GitHub stay authoritative."),
                },
            },
            AgmsgReplyContract = new OrchestratorReplyContract
            {
                Description =
                    "Implementation/review threads reply to a delegation with exactly one structured agmsg message. "
                    + "The reply is a SIGNAL; the orchestrator re-verifies every claim against intent-cli / GitHub "
                    + "before acting on it.",
                Accepted = "{\"status\":\"accepted\",\"thread\":\"implementation\",\"ref\":\"issue#<n>\",\"note\":\"claimed; starting\"}",
                Progress = "{\"status\":\"progress\",\"thread\":\"implementation\",\"ref\":\"issue#<n>\",\"note\":\"branch pushed; CI running\"}",
                Completed = "{\"status\":\"completed\",\"thread\":\"implementation\",\"ref\":\"pr#<n>\",\"note\":\"PR opened, Closes #<n>, CI green\"}",
                Blocked = "{\"status\":\"blocked\",\"thread\":\"review\",\"ref\":\"pr#<n>\",\"classification\":\"clarification-required\",\"note\":\"one operator action: <text>\"}",
            },
            OrchestratorFirstWake = new[]
            {
                "Confirm you are the ONLY orchestrator for this domain/repo; if a second is detected, STOP and escalate (fail closed).",
                Apply("Confirm domain scope: in single-domain mode, treat other-domain items visible in the host repo as OUT OF SCOPE (escalate, never delegate); in multi-domain mode, attach full routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree, base branch policy, destination thread) before each delegation. Visibility is not authorization, and an execution-unit prefix mismatch alone is not a wrong-repo signal."),
                "Read pending agmsg replies from the implementation/review threads (signals only — do not trust them as state).",
                Apply("Ask intent-cli for the real state: `intent-cli intent status --domain <domain> --format json` and `intent-cli worker next-action --repo <owner/repo> --github-only --format json`."),
                "Verify every GitHub fact an agmsg reply claims (PR merged, CI concluded, labels) before acting on it.",
                "Send AT MOST ONE message this wake: one delegation, one repair request, or one operator escalation — never a batch.",
                "Do not launch implement/review recurring timers for this domain/repo while orchestrating.",
            },
            SafetyBoundaries = new[]
            {
                "agmsg is a message/progress/completion signal layer only; intent-cli and GitHub are authoritative for all workflow state.",
                "No raw label mutation (`gh ... --add-label`/`--remove-label`); every label transition goes through intent-cli worker/automation.",
                "No hand-editing queue-state, runs.jsonl, packets, or any host metadata (`.intent-cli/**`, `intents/**`).",
                "agmsg never replaces semantic review or authorizes a merge; review/closeout decisions run through intent-cli review surfaces (G480).",
                "Process at most one delegation/repair/escalation per orchestrator wake; one delegated item per implementation/review wake.",
                "Domain isolation: a host repo can hold several domains and one repo can serve several domains, so visibility is not authorization. Single-domain orchestrators ignore/escalate other-domain items; multi-domain orchestrators require explicit per-delegation routing. An execution-unit prefix mismatch alone is not a wrong-repo signal.",
                "Fail closed on duplicate orchestrators for the same domain/repo, or when an agmsg reply conflicts with intent-cli/GitHub facts — STOP and escalate, never guess.",
                "Never ask intent-cli to launch Claude/Codex/Copilot or any AI provider; intent-cli only emits text the human agent acts on.",
            },
            DetailedGuideCommands = new[]
            {
                Apply("intent-cli guide prompt-matrix --mode child-loop --target-repo <owner/repo> --agent <agent> --format markdown"),
                Apply("intent-cli guide prompt-matrix --mode host-loop --domain <domain> --target-repo <owner/repo> --agent <agent> --format markdown"),
                Apply("intent-cli automation summary --domain <domain> --format json"),
            },
        };
    }

    private static bool TryParseArguments(
        string[] args,
        out string format,
        out IReadOnlyDictionary<string, string> values,
        out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<domain>"] = "<domain>",
            ["<owner/repo>"] = "<owner/repo>",
            ["<agent>"] = "<agent>",
            ["<mode>"] = ModeSingleDomain,
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!RequiresValue(arg))
            {
                values = parsed;
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                values = parsed;
                error = $"{arg} requires a value.";
                return false;
            }

            var value = args[++i];
            switch (arg)
            {
                case "--format":
                    format = value;
                    break;
                case "--domain":
                    parsed["<domain>"] = value;
                    break;
                case "--target-repo":
                    parsed["<owner/repo>"] = value;
                    break;
                case "--agent":
                    parsed["<agent>"] = value;
                    break;
                case "--mode":
                    parsed["<mode>"] = value;
                    break;
            }
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            values = parsed;
            error = $"Unknown --format '{format}'. Supported: markdown, json.";
            return false;
        }

        var modeValue = parsed["<mode>"];
        if (!string.Equals(modeValue, ModeSingleDomain, StringComparison.Ordinal)
            && !string.Equals(modeValue, ModeMultiDomain, StringComparison.Ordinal))
        {
            values = parsed;
            error = $"Unknown --mode '{modeValue}'. Supported: single-domain, multi-domain.";
            return false;
        }

        values = parsed;
        return true;
    }

    private static bool RequiresValue(string arg) =>
        string.Equals(arg, "--format", StringComparison.Ordinal)
        || string.Equals(arg, "--domain", StringComparison.Ordinal)
        || string.Equals(arg, "--target-repo", StringComparison.Ordinal)
        || string.Equals(arg, "--agent", StringComparison.Ordinal)
        || string.Equals(arg, "--mode", StringComparison.Ordinal);

    private static void WriteMarkdown(TextWriter writer, OrchestratorThreadGuide guide)
    {
        writer.WriteLine("# Guide — agmsg-backed orchestrator thread (G487)");
        writer.WriteLine();
        writer.WriteLine(guide.Summary);
        writer.WriteLine();

        writer.WriteLine("## Mode separation");
        writer.WriteLine();
        writer.WriteLine($"- **timer-loop mode** — {guide.ModeSeparation.TimerLoopMode}");
        writer.WriteLine($"- **orchestrator-message mode** — {guide.ModeSeparation.OrchestratorMessageMode}");
        writer.WriteLine($"- **mixed-mode warning** — {guide.ModeSeparation.MixedModeWarning}");
        writer.WriteLine();

        writer.WriteLine("## Domain routing — single-domain vs multi-domain");
        writer.WriteLine();
        writer.WriteLine($"- selected mode: `{guide.DomainRouting.Mode}`");
        writer.WriteLine($"- **single-domain** — {guide.DomainRouting.SingleDomainRule}");
        writer.WriteLine($"- **multi-domain** — {guide.DomainRouting.MultiDomainRule}");
        writer.WriteLine($"- **execution-unit prefix** — {guide.DomainRouting.PrefixMismatchNote}");
        writer.WriteLine();
        writer.WriteLine("Routing metadata required for every multi-domain delegation:");
        writer.WriteLine();
        foreach (var field in guide.DomainRouting.RoutingMetadataFields)
        {
            writer.WriteLine($"- {field}");
        }
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DomainRouting.DelegationExample);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## Scheduled orchestrator cadence");
        writer.WriteLine();
        writer.WriteLine(guide.Scheduling.Summary);
        writer.WriteLine();
        writer.WriteLine($"- scheduled thread: `{guide.Scheduling.ScheduledThread}` (the only scheduled thread)");
        writer.WriteLine($"- **receivers are loopless** — {guide.Scheduling.ReceiverNote}");
        writer.WriteLine();
        writer.WriteLine("### Codex automation (5m) — orchestrator");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.Scheduling.CodexSetupPrompt);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Claude `/loop 5m` — orchestrator");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.Scheduling.ClaudeLoopSetupPrompt);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Each orchestrator wake");
        writer.WriteLine();
        foreach (var responsibility in guide.Scheduling.WakeResponsibilities)
        {
            writer.WriteLine($"- {responsibility}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **repair** — {guide.Scheduling.RepairVsEscalate.Repair}");
        writer.WriteLine($"- **escalate** — {guide.Scheduling.RepairVsEscalate.Escalate}");
        writer.WriteLine();

        writer.WriteLine("## CI wait state");
        writer.WriteLine();
        writer.WriteLine(guide.CiWaitState.Summary);
        writer.WriteLine();
        foreach (var state in guide.CiWaitState.States)
        {
            writer.WriteLine($"- **{state.State}** — {state.Routing}");
        }
        writer.WriteLine();

        writer.WriteLine("## Next-slice publication");
        writer.WriteLine();
        writer.WriteLine(guide.NextSlicePublication.Summary);
        writer.WriteLine();
        writer.WriteLine($"- one_per_wake: {(guide.NextSlicePublication.OnePerWake ? "yes" : "no")}");
        writer.WriteLine();
        writer.WriteLine("### Publish only when ALL hold");
        writer.WriteLine();
        foreach (var precondition in guide.NextSlicePublication.Preconditions)
        {
            writer.WriteLine($"- {precondition}");
        }
        writer.WriteLine();
        writer.WriteLine("### Blocked by (hold or escalate)");
        writer.WriteLine();
        foreach (var blocker in guide.NextSlicePublication.Blockers)
        {
            writer.WriteLine($"- {blocker}");
        }
        writer.WriteLine();
        writer.WriteLine("### Canonical publish commands");
        writer.WriteLine();
        foreach (var command in guide.NextSlicePublication.CanonicalCommands)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();
        writer.WriteLine("### Post-publish verification");
        writer.WriteLine();
        foreach (var step in guide.NextSlicePublication.PostPublishVerification)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Thread prompts");
        foreach (var thread in guide.Threads)
        {
            writer.WriteLine();
            writer.WriteLine($"### {thread.Role}");
            writer.WriteLine();
            writer.WriteLine($"- purpose: {thread.Purpose}");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(thread.Prompt);
            writer.WriteLine("```");
        }
        writer.WriteLine();

        writer.WriteLine("## agmsg reply contract");
        writer.WriteLine();
        writer.WriteLine(guide.AgmsgReplyContract.Description);
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.AgmsgReplyContract.Accepted);
        writer.WriteLine(guide.AgmsgReplyContract.Progress);
        writer.WriteLine(guide.AgmsgReplyContract.Completed);
        writer.WriteLine(guide.AgmsgReplyContract.Blocked);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## Orchestrator first wake");
        writer.WriteLine();
        foreach (var step in guide.OrchestratorFirstWake)
        {
            writer.WriteLine($"1. {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Safety boundaries");
        writer.WriteLine();
        foreach (var boundary in guide.SafetyBoundaries)
        {
            writer.WriteLine($"- {boundary}");
        }
        writer.WriteLine();

        writer.WriteLine("## Detailed guide commands");
        writer.WriteLine();
        foreach (var command in guide.DetailedGuideCommands)
        {
            writer.WriteLine($"- `{command}`");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide orchestrator-thread");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Renders paste-ready prompts for an OPTIONAL agmsg-backed orchestrator thread plus the");
        writer.WriteLine("implementation/review threads it delegates to. agmsg is a signal layer only; intent-cli and");
        writer.WriteLine("GitHub remain authoritative. Existing timer-loop mode stays valid and is not replaced.");
        writer.WriteLine();
        writer.WriteLine("--mode single-domain (default) scopes the orchestrator to one domain and treats other-domain");
        writer.WriteLine("items visible in a shared host repo as out of scope. --mode multi-domain requires explicit");
        writer.WriteLine("routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree,");
        writer.WriteLine("base branch policy, destination thread) for each delegation, since one repo may serve several");
        writer.WriteLine("domains. An execution-unit prefix mismatch alone is not treated as a wrong-repo signal.");
    }
}

internal sealed record OrchestratorThreadGuide
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("mode_separation")]
    public required OrchestratorModeSeparation ModeSeparation { get; init; }

    [JsonPropertyName("domain_routing")]
    public required OrchestratorDomainRouting DomainRouting { get; init; }

    [JsonPropertyName("scheduling")]
    public required OrchestratorScheduling Scheduling { get; init; }

    [JsonPropertyName("ci_wait_state")]
    public required OrchestratorCiWaitState CiWaitState { get; init; }

    [JsonPropertyName("next_slice_publication")]
    public required OrchestratorNextSlicePublication NextSlicePublication { get; init; }

    [JsonPropertyName("threads")]
    public required IReadOnlyList<OrchestratorThreadPrompt> Threads { get; init; }

    [JsonPropertyName("agmsg_reply_contract")]
    public required OrchestratorReplyContract AgmsgReplyContract { get; init; }

    [JsonPropertyName("orchestrator_first_wake")]
    public required IReadOnlyList<string> OrchestratorFirstWake { get; init; }

    [JsonPropertyName("safety_boundaries")]
    public required IReadOnlyList<string> SafetyBoundaries { get; init; }

    [JsonPropertyName("detailed_guide_commands")]
    public required IReadOnlyList<string> DetailedGuideCommands { get; init; }
}

internal sealed record OrchestratorModeSeparation
{
    [JsonPropertyName("timer_loop_mode")]
    public required string TimerLoopMode { get; init; }

    [JsonPropertyName("orchestrator_message_mode")]
    public required string OrchestratorMessageMode { get; init; }

    [JsonPropertyName("mixed_mode_warning")]
    public required string MixedModeWarning { get; init; }
}

internal sealed record OrchestratorDomainRouting
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("single_domain_rule")]
    public required string SingleDomainRule { get; init; }

    [JsonPropertyName("multi_domain_rule")]
    public required string MultiDomainRule { get; init; }

    [JsonPropertyName("routing_metadata_fields")]
    public required IReadOnlyList<string> RoutingMetadataFields { get; init; }

    [JsonPropertyName("delegation_example")]
    public required string DelegationExample { get; init; }

    [JsonPropertyName("prefix_mismatch_note")]
    public required string PrefixMismatchNote { get; init; }
}

internal sealed record OrchestratorScheduling
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("scheduled_thread")]
    public required string ScheduledThread { get; init; }

    [JsonPropertyName("receiver_note")]
    public required string ReceiverNote { get; init; }

    [JsonPropertyName("codex_setup_prompt")]
    public required string CodexSetupPrompt { get; init; }

    [JsonPropertyName("claude_loop_setup_prompt")]
    public required string ClaudeLoopSetupPrompt { get; init; }

    [JsonPropertyName("wake_responsibilities")]
    public required IReadOnlyList<string> WakeResponsibilities { get; init; }

    [JsonPropertyName("repair_vs_escalate")]
    public required OrchestratorRepairEscalate RepairVsEscalate { get; init; }
}

internal sealed record OrchestratorRepairEscalate
{
    [JsonPropertyName("repair")]
    public required string Repair { get; init; }

    [JsonPropertyName("escalate")]
    public required string Escalate { get; init; }
}

internal sealed record OrchestratorCiWaitState
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("states")]
    public required IReadOnlyList<OrchestratorCiState> States { get; init; }
}

internal sealed record OrchestratorCiState
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("routing")]
    public required string Routing { get; init; }
}

internal sealed record OrchestratorNextSlicePublication
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("one_per_wake")]
    public required bool OnePerWake { get; init; }

    [JsonPropertyName("preconditions")]
    public required IReadOnlyList<string> Preconditions { get; init; }

    [JsonPropertyName("blockers")]
    public required IReadOnlyList<string> Blockers { get; init; }

    [JsonPropertyName("canonical_commands")]
    public required IReadOnlyList<string> CanonicalCommands { get; init; }

    [JsonPropertyName("post_publish_verification")]
    public required IReadOnlyList<string> PostPublishVerification { get; init; }
}

internal sealed record OrchestratorThreadPrompt
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record OrchestratorReplyContract
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("accepted")]
    public required string Accepted { get; init; }

    [JsonPropertyName("progress")]
    public required string Progress { get; init; }

    [JsonPropertyName("completed")]
    public required string Completed { get; init; }

    [JsonPropertyName("blocked")]
    public required string Blocked { get; init; }
}
