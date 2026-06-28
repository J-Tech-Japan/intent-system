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
                    "On a no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check: send one non-destructive status-request, check read-only intent-cli/GitHub facts, keep watching if there is progress, treat waiting-permission as an operator notice (never auto-clear), and only after repeated no-reply with no progress send one idempotent re-entry or escalate.",
                    "If intent-cli reports an `issue-cut-ready` candidate and all gates pass (same-domain or routed, complete contract, no open clarification, dependencies satisfied, under WIP, clean host-sync/preflight), publish ONE issue this wake via canonical publish-flow / issue-publish, then verify — do not ask the operator to create it.",
                    "If the candidate has unmet dependencies, plan the chain instead of pausing: act on the EARLIEST unmet resolvable dependency (publish or route it), keep the dependent held, and escalate only ambiguous/cycle/cross-domain-unrouted cases.",
                    "Decide the single action for this wake: publish one ready next-slice issue, delegate the next slice/PR, send one repair message, or escalate one operator decision.",
                    "Apply the design-thread escalation filter: keep routine progress / CI-wait / success / closeout / idle internal; surface to the design thread ONLY human-needed decisions, with structured evidence and the exact decision needed. Never hide a failure that needs a human.",
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
            DependencyPlanning = new OrchestratorDependencyPlanning
            {
                Summary =
                    "Unmet dependencies are NORMAL orchestration work when explicit and resolvable — not an operator "
                    + "stop. When a candidate depends on work that is not yet complete, do NOT pause for operator "
                    + "judgment; plan the dependency chain deterministically: hold the dependent candidate and route this "
                    + "wake's action to the earliest unmet dependency.",
                SelectionRule =
                    "Select the EARLIEST unmet same-domain dependency first (or an explicitly routed multi-domain "
                    + "dependency). Act on that dependency this wake — publish or route it — rather than on the dependent "
                    + "candidate. Walk the chain from its root, not from the visible leaf candidate.",
                Statuses = new[]
                {
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-publish-ready",
                        Action =
                            "The earliest unmet dependency is `issue-cut-ready` and has no GitHub issue — publish it this "
                            + "wake under the next-slice publication gates (one issue per wake), then keep the dependent "
                            + "candidate held.",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-actionable",
                        Action =
                            "The dependency already has an issue or PR that can move forward — route it (delegate "
                            + "implementation, review, closeout, or repair) using intent-cli / GitHub facts, not the "
                            + "dependent candidate.",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-waiting",
                        Action =
                            "The dependency is published and in flight (e.g. PR CI pending) — wait and re-check on the "
                            + "next wake; do not ask the operator. Keep the dependent candidate held.",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-ambiguous",
                        Action =
                            "The dependency cannot be resolved deterministically (missing dependency packet, conflicting "
                            + "GitHub linkage, or a cross-domain dependency with no route mapping) — escalate one operator "
                            + "decision (fail closed).",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-cycle",
                        Action =
                            "The dependencies form a cycle — escalate (fail closed); never pick a node arbitrarily to "
                            + "break the cycle.",
                    },
                },
                DependentHold =
                    "Keep the dependent candidate held — do not publish or delegate it — until every dependency is "
                    + "completed/cut. Re-evaluate the chain on each wake.",
                EscalationCases = new[]
                {
                    "A dependency packet is missing (referenced but no packet exists).",
                    "The dependencies form a cycle.",
                    "A cross-domain dependency has no explicit route mapping.",
                    "Conflicting GitHub linkage (two issues/PRs claim the same dependency, or linkage disagrees with the packet).",
                    "Destructive recovery would be required to proceed.",
                    "Credentials / security are involved.",
                    "A human product/design judgment is required.",
                },
            },
            StaleThreadHealthCheck = new OrchestratorStaleThreadHealthCheck
            {
                Summary =
                    "The implementation/review receivers are loopless, so silence is ambiguous — a receiver may be "
                    + "working, waiting for CI, waiting for a permission prompt, blocked, completed-without-reply, or "
                    + "truly stale. When a receiver has had no reply past the threshold, run a SAFE liveness check: ask "
                    + "before acting, verify authoritative facts, and never auto-cancel work, auto-clear a permission "
                    + "prompt, or duplicate a task.",
                NoReplyThreshold =
                    "Default 30 minutes since the receiver's last reply (configurable). Below the threshold, treat "
                    + "silence as normal in-progress work — do not poke.",
                Procedure = new[]
                {
                    "On no reply for >= the threshold, send ONE non-destructive status-request (see status_request_template) — ask, do not retry, cancel, or reset yet.",
                    "Check read-only intent-cli / GitHub facts: `worker next-action`, the issue/PR state, CI conclusion, and labels. Silence plus visible progress means the thread is working.",
                    "If authoritative facts show progress (new commits, PR opened/updated, CI running), keep watching — do not re-send the work.",
                    "If the receiver replies `waiting-permission`, treat it as an OPERATOR NOTICE — surface it; never auto-clear the permission/credential prompt.",
                    "Only after repeated no-reply AND no observable progress, send AT MOST ONE idempotent re-entry prompt that references the same issue/PR so it cannot duplicate work.",
                    "Escalate to the operator after repeated silence with no progress, or any unsafe case (would require cancel/reset, destructive git, or credentials).",
                },
                StatusRequestTemplate =
                    "{\"type\":\"status-request\",\"to\":\"<thread>\",\"ref\":\"issue#<n>|pr#<n>\",\"ask\":"
                    + "\"non-destructive liveness check: reply with one of working / waiting-ci / waiting-permission / "
                    + "blocked / completed / idle; do not start new work\"}",
                ReceiverStatuses = new[]
                {
                    new OrchestratorReceiverStatus { Status = "working", Meaning = "actively implementing/reviewing — keep watching, take no action." },
                    new OrchestratorReceiverStatus { Status = "waiting-ci", Meaning = "waiting on CI to conclude — wait and re-check; pending CI is normal progress." },
                    new OrchestratorReceiverStatus { Status = "waiting-permission", Meaning = "blocked on a permission/credential prompt — OPERATOR NOTICE; never auto-clear, surface it to the operator." },
                    new OrchestratorReceiverStatus { Status = "blocked", Meaning = "blocked on a clarification or external dependency — read the structured blocker and route repair or escalate." },
                    new OrchestratorReceiverStatus { Status = "completed", Meaning = "work finished — verify against intent-cli / GitHub; a completed-without-reply thread may just need its reply confirmed." },
                    new OrchestratorReceiverStatus { Status = "idle", Meaning = "no current task — safe to delegate the next item." },
                },
                Safety = new[]
                {
                    "Never auto-clear a permission/credential prompt — `waiting-permission` is an operator notice only.",
                    "Never auto-cancel or reset a receiver's work as part of a health check.",
                    "No destructive git operations and no raw label mutation in a health check.",
                    "Re-entry is idempotent: reference the existing issue/PR so a re-sent prompt cannot duplicate work.",
                    "Ask (status-request) and verify authoritative facts before any retry or escalation.",
                },
            },
            DesignThreadEscalation = new OrchestratorDesignThreadEscalation
            {
                Summary =
                    "The design thread is the PRIMARY human communication surface. Humans mainly talk to the design "
                    + "thread; implementation and review run through the orchestrator, and only human-needed decisions "
                    + "return to the design thread. Keep routine orchestration internal — this is a NOISE filter, not a "
                    + "failure filter: never hide a failure that needs a human.",
                KeptInternal = new[]
                {
                    "Normal progress, accepted, and in-flight delegations.",
                    "CI waiting — pending checks are an active wait state, not a design-thread event.",
                    "Successful implementation (PR opened, CI green).",
                    "Successful review / approval.",
                    "Closeout of an already-approved PR.",
                    "Idle wakes with no actionable change.",
                },
                EscalateWhen = new[]
                {
                    "Clarification required — the issue/packet contract is ambiguous.",
                    "Product intent ambiguity or a design decision the orchestrator cannot make.",
                    "Permission / credentials / security.",
                    "A destructive action would be required to proceed.",
                    "Repeated no-reply / no-progress after the safe stale-thread health check.",
                    "Unresolved canonical state — intent-cli / GitHub facts conflict or are missing.",
                    "Release / public publish decision.",
                    "An explicit policy decision the operator owns.",
                },
                EscalationMessageTemplate =
                    "{\"to\":\"design\",\"type\":\"escalation\",\"ref\":\"issue#<n>|pr#<n>\",\"reason\":\"<clarification|"
                    + "product-ambiguity|permission|destructive|no-progress|canonical-conflict|release|policy>\","
                    + "\"evidence\":\"<intent-cli/GitHub facts>\",\"decision_needed\":\"<the exact decision or action "
                    + "requested>\"}",
            },
            Setup = new OrchestratorSetup
            {
                Summary =
                    "Design-thread setup for starting orchestrator-message mode. The orchestrator is the single "
                    + "scheduled driver; the implementation and review threads are loopless receivers. Decide the "
                    + "inputs, register the agmsg roles under one team, paste the role prompts, run one read-only first "
                    + "wake, ping-test the inbox, then schedule only the orchestrator. agmsg is a signal layer only; "
                    + "intent-cli and GitHub stay authoritative.",
                Decisions = new[]
                {
                    Apply("domain (`<domain>`) and target repo (`<owner/repo>`)"),
                    "host / orchestrator / implementation / review paths — each role runs from its own folder, clone, or worktree",
                    "base branch policy (e.g. direct-main)",
                    Apply("per-role agents (e.g. orchestrator=`<agent>`, implementation=claude, review=codex)"),
                    "agmsg team name",
                    "delivery mode — how each role receives messages (e.g. a streamed inbox watch per role)",
                },
                Checklist = new[]
                {
                    "Record the decisions above (domain, repo, paths, base branch policy, agents, team, delivery mode).",
                    "Register each role with agmsg under ONE team: orchestrator, implementation, review (agmsg `join.sh`).",
                    "Set the delivery mode so each role receives messages — e.g. a streamed inbox watch per role (agmsg `delivery.sh` / `watch.sh`).",
                    "Paste the role prompts from the `Thread prompts` section into the matching thread (orchestrator / implementation / review).",
                    "Run ONE read-only first wake in the orchestrator (see `Orchestrator first wake`) — confirm only, send nothing.",
                    "Ping-test the inbox before real delegation (see ping_test).",
                    "Schedule ONLY the orchestrator (Codex automation 5m, or Claude same-thread `/loop 5m`); leave the receivers loopless.",
                    "On teardown, clean up the agmsg roles through the agmsg scripts (see cleanup).",
                },
                AgmsgCommands = new[]
                {
                    "register a role / join the team — agmsg `join.sh`",
                    "set the delivery mode — agmsg `delivery.sh`",
                    "watch a role's inbox (receiver delivery) — agmsg `watch.sh`",
                    "send a message / ping — agmsg `send.sh`",
                    "list the team / identities — agmsg `team.sh`",
                    "leave / clean up a role — agmsg `leave.sh` / `despawn.sh`",
                },
                PingTest =
                    "Send one agmsg message from the orchestrator to each receiver and confirm it appears in that "
                    + "receiver's inbox/stream before any real delegation. If the ping does not arrive, fix "
                    + "delivery/registration first — do not start delegating against a broken channel.",
                Cleanup = new[]
                {
                    "Leave the team / despawn each role through the agmsg scripts (`leave.sh` / `despawn.sh`).",
                    "Stop any inbox watchers started for the roles.",
                },
                Warning =
                    "Never edit the agmsg database or team files directly — register, message, and clean up ONLY through "
                    + "the agmsg scripts. Hand-editing agmsg state corrupts delivery.",
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
                        + "escalate one operator decision. Unmet dependencies are normal work, not a stop: if the next "
                        + "candidate depends on incomplete work, act on the EARLIEST unmet resolvable dependency "
                        + "(publish or route it) and keep the dependent held, rather than pausing for the operator. For a "
                        + "no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check — "
                        + "send one non-destructive status-request and verify read-only intent-cli/GitHub facts before any "
                        + "retry; never auto-clear a permission prompt, auto-cancel work, or duplicate a task. Report to "
                        + "the human-facing DESIGN thread ONLY human-needed decisions (clarification, product ambiguity, "
                        + "permission/credentials, destructive action, repeated no-progress, unresolved canonical state, "
                        + "release/publish, explicit policy); keep routine progress / CI-wait / success / closeout / idle "
                        + "internal — but never hide a failure that needs a human. Do "
                        + "NOT "
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

        writer.WriteLine("## Setup (starting orchestrator mode)");
        writer.WriteLine();
        writer.WriteLine(guide.Setup.Summary);
        writer.WriteLine();
        writer.WriteLine("### Decide / record");
        writer.WriteLine();
        foreach (var decision in guide.Setup.Decisions)
        {
            writer.WriteLine($"- {decision}");
        }
        writer.WriteLine();
        writer.WriteLine("### Setup checklist");
        writer.WriteLine();
        for (var i = 0; i < guide.Setup.Checklist.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.Setup.Checklist[i]}");
        }
        writer.WriteLine();
        writer.WriteLine("### agmsg commands");
        writer.WriteLine();
        foreach (var command in guide.Setup.AgmsgCommands)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **ping test** — {guide.Setup.PingTest}");
        writer.WriteLine();
        writer.WriteLine("### Cleanup");
        writer.WriteLine();
        foreach (var step in guide.Setup.Cleanup)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Warning:** {guide.Setup.Warning}");
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

        writer.WriteLine("## Dependency planning");
        writer.WriteLine();
        writer.WriteLine(guide.DependencyPlanning.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **selection rule** — {guide.DependencyPlanning.SelectionRule}");
        writer.WriteLine($"- **dependent hold** — {guide.DependencyPlanning.DependentHold}");
        writer.WriteLine();
        writer.WriteLine("### Dependency statuses");
        writer.WriteLine();
        foreach (var status in guide.DependencyPlanning.Statuses)
        {
            writer.WriteLine($"- **{status.Status}** — {status.Action}");
        }
        writer.WriteLine();
        writer.WriteLine("### Escalate only when");
        writer.WriteLine();
        foreach (var escalation in guide.DependencyPlanning.EscalationCases)
        {
            writer.WriteLine($"- {escalation}");
        }
        writer.WriteLine();

        writer.WriteLine("## Stale-thread health check");
        writer.WriteLine();
        writer.WriteLine(guide.StaleThreadHealthCheck.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **no-reply threshold** — {guide.StaleThreadHealthCheck.NoReplyThreshold}");
        writer.WriteLine();
        writer.WriteLine("### Procedure");
        writer.WriteLine();
        for (var i = 0; i < guide.StaleThreadHealthCheck.Procedure.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.StaleThreadHealthCheck.Procedure[i]}");
        }
        writer.WriteLine();
        writer.WriteLine("### Status-request message");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.StaleThreadHealthCheck.StatusRequestTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Receiver statuses");
        writer.WriteLine();
        foreach (var status in guide.StaleThreadHealthCheck.ReceiverStatuses)
        {
            writer.WriteLine($"- **{status.Status}** — {status.Meaning}");
        }
        writer.WriteLine();
        writer.WriteLine("### Health-check safety");
        writer.WriteLine();
        foreach (var rule in guide.StaleThreadHealthCheck.Safety)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();

        writer.WriteLine("## Design-thread escalation filter");
        writer.WriteLine();
        writer.WriteLine(guide.DesignThreadEscalation.Summary);
        writer.WriteLine();
        writer.WriteLine("### Kept internal (no design-thread message by default)");
        writer.WriteLine();
        foreach (var item in guide.DesignThreadEscalation.KeptInternal)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Escalate to the design thread when");
        writer.WriteLine();
        foreach (var item in guide.DesignThreadEscalation.EscalateWhen)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Design escalation message");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DesignThreadEscalation.EscalationMessageTemplate);
        writer.WriteLine("```");
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

    [JsonPropertyName("dependency_planning")]
    public required OrchestratorDependencyPlanning DependencyPlanning { get; init; }

    [JsonPropertyName("stale_thread_health_check")]
    public required OrchestratorStaleThreadHealthCheck StaleThreadHealthCheck { get; init; }

    [JsonPropertyName("design_thread_escalation")]
    public required OrchestratorDesignThreadEscalation DesignThreadEscalation { get; init; }

    [JsonPropertyName("setup")]
    public required OrchestratorSetup Setup { get; init; }

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

internal sealed record OrchestratorDesignThreadEscalation
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("kept_internal")]
    public required IReadOnlyList<string> KeptInternal { get; init; }

    [JsonPropertyName("escalate_when")]
    public required IReadOnlyList<string> EscalateWhen { get; init; }

    [JsonPropertyName("escalation_message_template")]
    public required string EscalationMessageTemplate { get; init; }
}

internal sealed record OrchestratorStaleThreadHealthCheck
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("no_reply_threshold")]
    public required string NoReplyThreshold { get; init; }

    [JsonPropertyName("procedure")]
    public required IReadOnlyList<string> Procedure { get; init; }

    [JsonPropertyName("status_request_template")]
    public required string StatusRequestTemplate { get; init; }

    [JsonPropertyName("receiver_statuses")]
    public required IReadOnlyList<OrchestratorReceiverStatus> ReceiverStatuses { get; init; }

    [JsonPropertyName("safety")]
    public required IReadOnlyList<string> Safety { get; init; }
}

internal sealed record OrchestratorReceiverStatus
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}

internal sealed record OrchestratorDependencyPlanning
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("selection_rule")]
    public required string SelectionRule { get; init; }

    [JsonPropertyName("statuses")]
    public required IReadOnlyList<OrchestratorDependencyStatus> Statuses { get; init; }

    [JsonPropertyName("dependent_hold")]
    public required string DependentHold { get; init; }

    [JsonPropertyName("escalation_cases")]
    public required IReadOnlyList<string> EscalationCases { get; init; }
}

internal sealed record OrchestratorDependencyStatus
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }
}

internal sealed record OrchestratorSetup
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("decisions")]
    public required IReadOnlyList<string> Decisions { get; init; }

    [JsonPropertyName("checklist")]
    public required IReadOnlyList<string> Checklist { get; init; }

    [JsonPropertyName("agmsg_commands")]
    public required IReadOnlyList<string> AgmsgCommands { get; init; }

    [JsonPropertyName("ping_test")]
    public required string PingTest { get; init; }

    [JsonPropertyName("cleanup")]
    public required IReadOnlyList<string> Cleanup { get; init; }

    [JsonPropertyName("warning")]
    public required string Warning { get; init; }
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
