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

    // G500: setup-intake outcomes and the existing-loop stop policy values.
    private const string IntakeMissingInputs = "missing-inputs";
    private const string IntakeSetupReady = "setup-ready";
    private const string IntakeBlocked = "blocked";

    private const string ExistingLoopNone = "none";
    private const string ExistingLoopWillStop = "will-stop";
    private const string ExistingLoopKeep = "keep";

    private const string UsageLine =
        "Usage: intent-cli guide orchestrator-thread [--domain <name>] [--target-repo <owner/repo>] [--agent <agent>] "
        + "[--mode single-domain|multi-domain] [--orchestrator-path <p>] [--implementation-path <p>] [--review-path <p>] "
        + "[--orchestrator-agent <a>] [--implementer-agent <a>] [--reviewer-agent <a>] [--team <name>] "
        + "[--delivery-mode <mode>] [--existing-loop-policy none|will-stop|keep] [--format markdown|json]";

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
            SetupIntake = BuildSetupIntake(values),
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
            RoleBoundary = new OrchestratorRoleBoundary
            {
                Summary =
                    "ROLE BOUNDARY: DESIGN creates packets; the ORCHESTRATOR moves READY packets through the workflow. "
                    + "The orchestrator must NOT silently become the product/release/design author. When a needed packet "
                    + "is absent, incomplete, or would require product/release/design judgment, ask design to create or "
                    + "update it — do NOT draft it yourself.",
                DesignOwns = new[]
                {
                    "Intent shaping and clarifications.",
                    "ADRs and design decisions.",
                    "Release scope and version selection.",
                    "Packet content and acceptance criteria (the durable packet files).",
                },
                OrchestratorOwns = new[]
                {
                    "Inspect canonical intent-cli / GitHub state.",
                    "Publish exactly ONE already-authored, `issue-cut-ready` packet per wake (via canonical publish surfaces — see Next-slice publication).",
                    "Delegate implementation/review to the loopless receivers.",
                    "Wait for CI / review and track review state.",
                    "Close out approved PRs through the canonical review surfaces.",
                    "Report blockers and missing packets back to design.",
                },
                MissingPacketResponse =
                    "When a needed packet is absent or incomplete, or a vague goal would require authoring intent / "
                    + "acceptance criteria / release scope, the orchestrator does NOT invent the packet. It sends a "
                    + "structured request to DESIGN describing what is needed and WAITS for design to author/update the "
                    + "packet (or give an explicit instruction). The orchestrator only publishes/coordinates a packet "
                    + "that already exists and is `issue-cut-ready`.",
                MissingPacketMessageTemplate = Apply(
                    "{\"to\":\"design\",\"type\":\"packet-needed\",\"domain\":\"<domain>\",\"need\":\"<what is needed: "
                    + "e.g. a release-prep packet for vX.Y.Z, or acceptance criteria for <topic>>\",\"reason\":\"<why the "
                    + "orchestrator cannot proceed: requires product/release/design judgment, or the packet is absent/"
                    + "incomplete>\",\"blocking\":\"<the work that is waiting on it>\"}"),
                ReleasePrepRule =
                    "Release-prep is design-owned: DESIGN decides the release version and scope and AUTHORS the "
                    + "release-prep packet. The orchestrator may publish and coordinate that release-prep packet ONLY "
                    + "after it exists and is `issue-cut-ready` — it must not pick the version, decide scope, or author "
                    + "the release notes/packet itself from a vague \"prepare a release\" instruction.",
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
                    "In orchestrator-message mode the normal steady state is MESSAGE-DRIVEN: implementation/review "
                    + "receivers already send accepted/progress/completed/blocked replies to the orchestrator, and those "
                    + "replies wake the orchestrator path — routine fast polling is NOT required. An orchestrator timer "
                    + "(Codex automation every 5m, or Claude same-thread `/loop 5m`) remains SUPPORTED but only as an "
                    + "explicit FALLBACK/LEGACY polling option for an operator who intentionally wants scheduled "
                    + "polling instead of message-driven wakes. Either way the implementation and review threads stay "
                    + "long-lived LOOPLESS receivers. The recommended safety net for message-driven steady state is a "
                    + "LOW-frequency design-side watchdog (see Design-side watchdog), not a fast orchestrator loop.",
                ScheduledThread = "orchestrator",
                ReceiverNote =
                    "Implementation and review threads are loopless receivers: do NOT start a recurring timer/loop in a "
                    + "receiver thread for this domain/repo. A receiver waits for an agmsg delegation, acts once, replies "
                    + "once, and waits again. Receivers are NEVER scheduled; when an explicit fallback/legacy timer is "
                    + "used (message-driven wakes are the default), the orchestrator is the only thread ever scheduled.",
                CodexSetupPrompt = Apply(
                    "OPTIONAL fallback/legacy polling — Codex automation (run every 5 minutes) for the ORCHESTRATOR "
                    + "thread, domain `<domain>` against `<owner/repo>` using `<agent>`: on each run perform exactly "
                    + "ONE orchestrator wake — check design-side progress and agmsg replies, ask intent-cli for state "
                    + "(`intent status`, `worker next-action --github-only`, `automation host-review-preflight`), "
                    + "verify the GitHub facts (CI/approval/merge/closeout), then send AT MOST ONE message (one "
                    + "delegation, one repair, or one escalation) and exit. Prefer the message-driven steady state "
                    + "(implementation/review agmsg replies already wake the orchestrator); use this timer only when "
                    + "the operator explicitly wants scheduled fallback/legacy polling. Do not run implementation/"
                    + "review loops; they are loopless receivers."),
                ClaudeLoopSetupPrompt = Apply(
                    "OPTIONAL fallback/legacy polling — Claude same-thread setup for the ORCHESTRATOR thread, domain "
                    + "`<domain>` against `<owner/repo>`: in the orchestrator thread run `/loop 5m` with the "
                    + "orchestrator prompt so the same thread re-wakes every 5 minutes. Each wake does exactly one "
                    + "orchestrator pass (read replies, check intent-cli / GitHub state, send AT MOST ONE message). "
                    + "Prefer the message-driven steady state (implementation/review agmsg replies already wake the "
                    + "orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy "
                    + "polling. Do NOT also launch `/loop` in the implementation or review threads — those are "
                    + "loopless receivers driven only by your delegations."),
                WakeResponsibilities = new[]
                {
                    "A wake is triggered either by an incoming agmsg reply from implementation/review (the message-driven steady state) or by the optional fallback/legacy timer firing — either trigger runs exactly one orchestrator pass below.",
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
            DraftPrReviewability =
                "A DRAFT PR may still be reviewable depending on domain guidance — a reviewer MAY perform review "
                + "feedback on a draft when the domain's review policy allows it. But the reviewer must use the canonical "
                + "intent-cli review surfaces (`review closeout-plan`, `guide review`, `automation pr-transition`, "
                + "`closeout pr`); merge/approval stays gated by those surfaces. A draft is never approved/merged by hand "
                + "or by raw label edits, and never via host-metadata editing.",
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
                    + "failure filter: never hide a failure that needs a human. A design escalation carries a concise "
                    + "reason, the current AUTHORITATIVE state read from intent-cli/GitHub, the supporting evidence, "
                    + "options only when useful, and the exact decision needed — so the human can decide without "
                    + "re-deriving the state.",
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
                    + "\"current_state\":\"<the current AUTHORITATIVE state read from intent-cli/GitHub: labels, PR/CI/"
                    + "review/merge state, queue position>\",\"evidence\":\"<the intent-cli/GitHub facts that establish "
                    + "that state>\",\"options\":\"<OPTIONAL: candidate choices, only when useful>\",\"decision_needed\":"
                    + "\"<the exact decision or action requested from the human>\"}",
                MessageFields = new[]
                {
                    "reason — which human-needed category triggered the escalation.",
                    "current_state — the current AUTHORITATIVE state, read from intent-cli / GitHub (labels, PR/CI/review/merge state, queue position). REQUIRED: the receiver must not have to re-derive it.",
                    "evidence — the intent-cli / GitHub facts that establish the current state (do not pass generic wording as a substitute for the explicit state).",
                    "options — OPTIONAL candidate choices, included only when they help the human decide.",
                    "decision_needed — the exact decision or action requested from the human.",
                },
            },
            DesignReceiver = new OrchestratorDesignReceiver
            {
                Summary =
                    "When human-needed escalations should be deliverable over agmsg, add a FOURTH logical role: a "
                    + "DESIGN / human-facing receiver. Routine progress stays internal to orchestrator / implementation "
                    + "/ review; only human-needed decisions go to the design thread (see the design-thread escalation "
                    + "filter). The design receiver is OPTIONAL for routine operation but RECOMMENDED so escalations "
                    + "reach the human reliably — and it may receive manually by checking its inbox.",
                Optional = true,
                Roles = new[]
                {
                    "orchestrator — paces the other roles over agmsg; message-driven by default, with an explicit timer only as a fallback/legacy option.",
                    "implementation receiver — LOOPLESS; acts on delegations only, never starts its own timer.",
                    "review receiver — LOOPLESS; acts on delegations only, never starts its own timer.",
                    "design / human receiver — OPTIONAL; receives ONLY human-needed escalations and is also loopless (the human reads on demand, e.g. via `inbox.sh`).",
                },
                Setup = new[]
                {
                    "Register the design role in the SAME agmsg team — `agmsg join.sh <team> design <agent> <design-folder>` — or simply address escalation messages to the existing design thread.",
                    "Optional streamed delivery: `agmsg delivery.sh set <mode> <agent> <design-folder>`; otherwise the design thread reads on demand with `inbox.sh`.",
                    "The design receiver does NOT need a recurring loop — like implementation/review it is loopless; the human reads when prompted.",
                },
                ManualInboxTriggerPrompt =
                    "agmsg の inbox を確認してください。あなたは `<team>` の design です。 "
                    + "(Check your agmsg inbox — you are the `design` role of team `<team>`. Read pending escalations "
                    + "with `inbox.sh`; routine progress is intentionally not sent here.)",
                PreStartNote =
                    "Messages sent BEFORE the design receiver's monitor started may be in agmsg history but not visibly "
                    + "delivered — the design thread should read its inbox with `inbox.sh` to catch earlier escalations, "
                    + "exactly like the other receivers (see Receiver readiness / startup order).",
            },
            DesignHandoff = new OrchestratorDesignHandoff
            {
                Summary =
                    "Setup does not stop at role registration. After the agmsg roles are registered and ready, the "
                    + "DESIGN thread starts (or resumes) orchestration by sending ONE message to the orchestrator; the "
                    + "orchestrator then drives the loop autonomously and returns to design only for human decisions.",
                FirstMessageTemplate = Apply(
                    "{\"to\":\"orchestrator\",\"type\":\"start\",\"domain\":\"<domain>\",\"target_repo\":"
                    + "\"<owner/repo>\",\"requested_action\":\"<e.g. publish the next ready slice and drive it to a PR>\","
                    + "\"constraints\":\"one action per wake; escalate to design ONLY for human decisions (product/"
                    + "clarification, release/credentials/security, destructive actions, unresolved blockers)\"}"),
                AutonomousPublishRule =
                    "If `intent-cli` reports the next slice `issue-cut-ready` and all publish gates pass (see Next-slice "
                    + "publication), the orchestrator creates/publishes ONE GitHub issue ITSELF via canonical intent-cli "
                    + "commands (`issue publish-flow` / `automation issue-publish`) — it does NOT ask design to do each "
                    + "step. At most one issue per wake; verify after publishing before delegating implementation.",
                EscalationBoundary =
                    "Routine delegation (publish, delegate, CI wait, review, closeout) stays orchestrator↔receivers and "
                    + "does NOT go to design. Return to DESIGN only for human decisions — product/design clarification, "
                    + "release/credentials/security, destructive actions, or an unresolved blocker — using the structured "
                    + "escalation message (reason / current_state / evidence / decision_needed).",
                DesignInboxWorkflow =
                    "The design thread is a loopless receiver and reads on demand. To pick up escalations, the human (or "
                    + "the design thread) checks the design inbox with `inbox.sh` — especially when monitor delivery did "
                    + "not appear live or the design session started after the orchestrator sent. Read, decide/reply, "
                    + "then the orchestrator continues.",
            },
            DesignWatchdog = new OrchestratorDesignWatchdog
            {
                Summary =
                    "In the message-driven steady state, implementation/review replies already wake the orchestrator, "
                    + "so a fast orchestrator loop is redundant. The recommended safety net instead is an OPTIONAL, "
                    + "LOW-frequency watchdog run from the DESIGN thread: it checks whether HITL (human-in-the-loop) "
                    + "messages arrived and whether the orchestrator looks stalled, then sends AT MOST ONE canonical "
                    + "repair/status request — it never drives routine orchestration itself.",
                Optional = true,
                Frequency =
                    "LOW frequency only (e.g. tens of minutes to hours, not every 5m) — the watchdog is a safety net "
                    + "for the message-driven steady state, not a replacement driver. A fast watchdog loop recreates "
                    + "the same churn the message-driven model removes.",
                Checks = new[]
                {
                    "Check the design/HITL inbox for unread human-facing escalations or unanswered questions (`inbox.sh` on the design role).",
                    Apply("Check orchestrator staleness: read-only intent-cli / GitHub facts (`worker next-action --repo <owner/repo> --github-only --format json`, open PR/CI/label state) compared against the last known orchestrator activity."),
                },
                Action =
                    "When staleness or an unanswered HITL message is detected, send AT MOST ONE canonical repair/status "
                    + "request to the orchestrator (the same non-destructive status-request shape as the stale-thread "
                    + "health check) — never more than one per watchdog wake, and never a batch of repairs.",
                RepairStatusRequestTemplate =
                    "{\"type\":\"status-request\",\"to\":\"orchestrator\",\"from\":\"design-watchdog\",\"ask\":"
                    + "\"non-destructive liveness check: reply with current state and next action, or confirm idle\"}",
                StopCondition =
                    "Stop or archive the watchdog once both the backlog and the human-decision (HITL) queues are "
                    + "drained — an idle orchestrator with nothing queued and no pending human decisions needs no "
                    + "further watchdog wakes until new work appears.",
                SafetyRules = new[]
                {
                    "PROHIBITED: duplicate delegation — the watchdog never re-sends or re-creates a delegation itself; only the orchestrator delegates.",
                    "PROHIBITED: clearing a permission prompt — `waiting-permission` stays an operator notice; the watchdog never auto-clears it.",
                    "PROHIBITED: cancelling or resetting in-flight work.",
                    "PROHIBITED: force-closing an issue/PR or any other terminal action.",
                    "PROHIBITED: speculative durable-state surgery — no hand-editing labels, queue-state, or any host metadata; the watchdog only sends a message and reads read-only facts.",
                },
                FallbackTimerNote =
                    "An explicit orchestrator timer (Codex automation every 5m, or Claude same-thread `/loop 5m`) "
                    + "remains SUPPORTED as fallback/legacy polling when an operator intentionally wants scheduled "
                    + "polling instead of the message-driven steady state — the design-side watchdog and the "
                    + "orchestrator fallback timer are alternative safety nets, not both required together.",
            },
            MonitorRecovery = new[]
            {
                new OrchestratorTroubleshooting
                {
                    Symptom = "Monitor did not start",
                    Action = "Restart the receiver session so the monitor/watch hook attaches on a fresh turn; verify with `delivery.sh status` and a ping/ack. Until then, read with `inbox.sh`.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Message not visible",
                    Action = "It may be queued but not delivered live — read the role's queue with `inbox.sh`; if still missing, re-confirm registration (`team.sh`) and delivery, then resend after an ack.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Receiver started after the message was sent",
                    Action = "Earlier messages are in history but not delivered live to the new session — read them with `inbox.sh`, or have the sender resend after the receiver acks.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Orchestrator idle despite a packet existing",
                    Action = "Confirm the orchestrator received the design start/resume message (`inbox.sh` on the orchestrator) and that `worker next-action` / `intent status` report an actionable item for THIS domain/repo (not another domain visible in the host repo). If issue-cut-ready and safe, the orchestrator should publish one issue itself rather than wait.",
                },
            },
            MonitorToolDistinction = new OrchestratorMonitorDistinction
            {
                Summary =
                    "Claude Code's `Monitor` is a generic Claude Code tool — the real mechanism that streams the agmsg "
                    + "inbox into a receiver. agmsg attaches it by launching `watch.sh` from the Claude Code SessionStart "
                    + "directive; the running Monitor task is what turns incoming agmsg lines into live transcript events. "
                    + "The word \"monitor\" is overloaded — do NOT confuse this Claude Code `Monitor` tool with agmsg's "
                    + "delivery-mode config or with unrelated `Azure Monitor` / other MCP `monitor` tools.",
                DeliveryModeNote =
                    "agmsg `delivery.sh status` `mode=monitor` is configuration only and is NOT proof that a Monitor tool "
                    + "is attached and streaming — a receiver can report `mode=monitor` while no Claude Code `Monitor` is "
                    + "running and nothing is delivered live. Confirm live attachment with the success markers below, not "
                    + "with the delivery mode alone.",
                SuccessMarkers = new[]
                {
                    "`ToolSearch select:Monitor` resolves Monitor in the receiver session (the tool is available).",
                    "the transcript shows `Monitor(agmsg inbox stream)` — the Monitor tool attached to the inbox stream.",
                    "the Claude Code footer shows `1 monitor` (a live Monitor task is attached).",
                    "the transcript shows `Monitor event` lines as inbox messages arrive (the stream is live).",
                },
                FailureMarkers = new[]
                {
                    "delivery falls back to a plain `Bash` / background `watch.sh` task instead of an attached Monitor — no live stream.",
                    "the footer shows `1 shell` instead of `1 monitor` (a background shell is running, not a Monitor).",
                    "confusion with `Azure Monitor` / other MCP `monitor` tools — those are unrelated to agmsg inbox streaming and never prove attachment.",
                },
                TrustRepair = new[]
                {
                    "Root cause: the exact-cwd project key in `~/.claude.json` with `hasTrustDialogAccepted=false` suppresses the SessionStart directive that launches Monitor, so no Monitor attaches and the inbox never streams (the receiver still reports `mode=monitor`).",
                    "Repair (operator action only): repair Claude project trust for that exact cwd, restart the receiver session, then re-verify the success markers above. intent-cli never auto-detects or edits `~/.claude.json`.",
                },
                WindowsGuidance = new[]
                {
                    "On Windows, start the monitor-mode Claude Code receiver from **Git Bash**. Dogfooding showed PowerShell / native-Windows startup may not attach the agmsg Monitor reliably (the SessionStart `watch.sh` directive assumes a bash environment), so the receiver can report `mode=monitor` yet never stream.",
                    "If Git Bash is unavailable or the Monitor still does not attach on Windows, fall back to `turn` delivery or manual `inbox.sh` polling (see the fallback ladder) — do NOT report the receiver ready on `mode=monitor` alone.",
                },
                FallbackLadder = new[]
                {
                    "Realtime Monitor delivery is NOT required for orchestrator mode — it is a convenience. When the success markers are missing, work the bounded ladder below and then keep going with an explicit fallback; do not silently claim a live monitor.",
                    "Restart the receiver Claude Code session so the SessionStart directive re-launches `watch.sh`/Monitor on a fresh turn, then re-check the success markers.",
                    "Verify project trust / session: the exact-cwd `~/.claude.json` project key must have trust accepted (see the trust-repair runbook); confirm `ToolSearch select:Monitor` resolves the generic Monitor tool in that session.",
                    "On Windows, relaunch the receiver from Git Bash (see Windows guidance) rather than PowerShell / a native shell.",
                    "Compare against a known-good receiver project (one already showing `1 monitor` / `Monitor event`) to isolate whether the break is this cwd's config or the environment.",
                    "If it still will not attach, fall back to `turn` delivery or manual `inbox.sh` polling and say so explicitly, or escalate to the operator. A Bash/background `watch.sh` (`1 shell`) is diagnostic/fallback only — never a substitute for the Claude Code Monitor, and never a reason to report the receiver as live-monitored.",
                    "See the agmsg monitor-delivery docs (https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md) for backend-specific delivery/watch details; intent-cli does not own or modify agmsg internals or Claude Code tool availability.",
                },
                ProjectSettingsDiagnosis = new[]
                {
                    "When `ToolSearch select:Monitor` finds NO generic `Monitor` tool at all (not `1 shell` vs `1 monitor` — the tool is simply absent), treat it as a Claude Code TOOL-SURFACE problem FIRST, before debugging agmsg delivery — regardless of what `delivery.sh status` `mode=monitor` reports. agmsg cannot stream through a Monitor tool that Claude Code is not exposing.",
                    "Known-good comparison checklist — diff this project's Claude Code config against a folder where `1 monitor` already works: `.claude/settings.json`, `.claude/settings.local.json`, `~/.claude.json` project trust/onboarding flags, the enabled/disabled MCP server lists, and project-level `env` settings.",
                    "Suspect project-level `env` overrides observed in dogfooding (in `.claude/settings.json` `env`) that can suppress the tool surface: `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true`, `CLAUDE_CODE_ENABLE_TELEMETRY=false`, `DISABLE_ERROR_REPORTING=true`, `DISABLE_TELEMETRY=true`. Removing/isolating these project `env` overrides (agmsg hooks preserved) restored `ToolSearch select:Monitor` in the affected folders.",
                    "Safe remediation (operator action, does NOT touch agmsg): close the Claude Code sessions → remove or isolate the suspect project-level `env` settings while PRESERVING the agmsg SessionStart hooks → reopen Claude Code → run `ToolSearch select:Monitor` → verify `Monitor(agmsg inbox stream)`, the footer `1 monitor`, and `Monitor event` as inbox messages arrive.",
                    "This is a Claude Code project-config repair, not an agmsg change — intent-cli never edits `.claude/settings.json`, `~/.claude.json`, or agmsg internals. Preserve the G516 distinction throughout: `1 monitor` = live success, `1 shell` = diagnostic/fallback only, never success.",
                },
            },
            IntakeForm = new OrchestratorIntakeForm
            {
                Summary =
                    "When the user asks only \"I want to use orchestrator mode\", elicit or infer the setup facts, then "
                    + "produce the concrete commands/messages (see Setup intake / Setup). Ask for what is missing; apply "
                    + "the recommended defaults for the rest.",
                Questions = new[]
                {
                    "domain and target repo",
                    "orchestrator cwd + agent type",
                    "implementation receiver cwd + agent type",
                    "review receiver cwd + agent type",
                    "design cwd + agent type, and whether design is manual-inbox or monitored",
                    "delivery mode per role (monitored / streamed inbox watch, or manual inbox)",
                },
                Defaults = new[]
                {
                    "orchestrator = Claude",
                    "implementer = Claude",
                    "reviewer = Codex",
                    "design = manual-inbox Codex",
                    "runtime / implementation / review receivers = monitor (when supported)",
                },
                DesignDeliveryNote =
                    "Design may be a manual-inbox receiver (reads with `inbox.sh` on demand) or a monitored receiver; "
                    + "either way it receives ONLY human-decision escalations or explicit summaries, never routine "
                    + "progress.",
                RoleStartupMessages = new[]
                {
                    new OrchestratorRoleStartup
                    {
                        AgentType = "claude",
                        ActasInvocation = "/agmsg actas <role>",
                        Note = "In a Claude session, paste the slash-command form to assume the agmsg role, then paste that role's prompt from the Thread prompts section.",
                    },
                    new OrchestratorRoleStartup
                    {
                        AgentType = "codex",
                        ActasInvocation = "$agmsg actas <role>",
                        Note = "In a Codex session, paste the `$agmsg actas <role>` form to assume the agmsg role, then paste that role's prompt from the Thread prompts section.",
                    },
                },
            },
            DesignTrafficController = new OrchestratorDesignTrafficController
            {
                Summary =
                    "The design thread acts as a TRAFFIC CONTROLLER, not an implementer. It coordinates through the "
                    + "orchestrator and only surfaces human-needed items — it does not drive implementation/review or "
                    + "mutate workflow state itself.",
                Playbook = new[]
                {
                    "Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.",
                    "Check intent-cli / GitHub READ-ONLY state (`intent status`, `worker next-action`, PR/issue/labels) to ground any decision — never trust an agmsg message as state.",
                    "Send the orchestrator a state update or a nudge (start/resume); do not drive implementation/review yourself.",
                    "Do NOT directly mutate implementation/review work, labels, or host metadata — that is the orchestrator/receivers' job through intent-cli.",
                    "Summarize ONLY human-needed items to the human; keep routine progress internal.",
                },
                IdleDiagnostic = new[]
                {
                    "Confirm the orchestrator is actually scheduled and on a fresh turn (its `/loop` or Codex automation is running).",
                    "Confirm it received your last message (`inbox.sh` on the orchestrator) — a pre-monitor send may be queued, not delivered live; resend after an ack.",
                    "Confirm intent-cli actually reports an actionable item for THIS domain/repo (`worker next-action` / `intent status`) — idle may be correct (nothing to do).",
                    "Only after these, escalate to the human as a structured decision.",
                },
                ContextOnlyRule =
                    "The design thread MAY send context to a receiver thread, but MUST mark it context-only (e.g. "
                    + "`context-only: <text>`) unless the orchestrator delegated the action — receivers act only on "
                    + "orchestrator delegations, not on design context.",
            },
            WorktreeManagement = new OrchestratorWorktreeManagement
            {
                Summary =
                    "Orchestrated work creates temporary worktrees for implementation and review. Allocate them under a "
                    + "managed, allowlisted root inside the workspace and clean them up with `git worktree remove` — "
                    + "NEVER a raw `rm -rf` of an arbitrary `/tmp/intent-review-...` path. Safe cleanup design, not "
                    + "disabling approvals, is the right default: a destructive `rm -rf` approval prompt is the symptom "
                    + "of an unmanaged workspace.",
                ManagedRoot =
                    "Allocate temporary worktrees under a repo/workspace-scoped managed root — the `[project] "
                    + "worktree_root` (default `.intent-cli/worktrees/`), git-ignored — not arbitrary `/tmp/"
                    + "intent-review-...` paths. A managed root is allowlisted, predictable, and removable with `git "
                    + "worktree remove`.",
                Allocation = new[]
                {
                    "Create each worktree under the managed root: `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`.",
                    "Keep the managed root git-ignored so it never pollutes the tree.",
                    "One worktree per role/unit; do not reuse a dirty worktree across units.",
                },
                SafeCleanup = new[]
                {
                    "Remove a worktree only with `git worktree remove` (it refuses a dirty worktree) — never raw `rm -rf`.",
                    "Validate the target path is INSIDE the allowlisted managed root before removal.",
                    "Confirm the path is a registered git worktree (it appears in `git worktree list`).",
                    "Confirm the worktree state is clean (no uncommitted or untracked user work) before removing.",
                    "Prune stale registrations with `git worktree prune` after removal.",
                },
                RefuseWhen = new[]
                {
                    "The target is OUTSIDE the allowlisted managed root.",
                    "The target is the repo root, `$HOME`, or a system path (`/`, `/tmp` root, etc.).",
                    "The path is not a registered git worktree.",
                    "The worktree has uncommitted or untracked user work — STOP and surface it; do not delete user work.",
                },
                ApprovalPolicyNote =
                    "`approval_policy=never` / `danger-full-access` is NOT a substitute for safe cleanup design. Keep "
                    + "least-privilege approvals as the default; the goal is to never need a destructive `rm -rf` prompt, "
                    + "not to suppress the prompt.",
            },
            ReviewDelegationContract = new OrchestratorReviewDelegationContract
            {
                Summary =
                    "Review delegation must carry the managed-worktree policy and require design-alignment evidence up "
                    + "front — not leave the reviewer to discover it. Dogfooding showed a reviewer allocate a raw "
                    + "`/tmp/...review...` worktree and Codex correctly ask to approve a destructive `rm -rf` — the "
                    + "RIGHT safety behavior for the WRONG workflow. The fix is a managed root, NOT weakening approval "
                    + "settings.",
                ManagedWorktreeRoot =
                    "Review worktrees use the SAME managed, workspace-local root as the rest of orchestrated work — the "
                    + "`[project] worktree_root` (default `.intent-cli/worktrees/`), e.g. "
                    + "`.intent-cli/worktrees/review-<unit>` — NEVER an arbitrary `/tmp/...review...` path.",
                ProhibitedPattern =
                    "PROHIBITED as the normal path: a raw `/tmp/...` review worktree, and a `rm -rf /tmp/... && git "
                    + "worktree add ...` cleanup chain. Reaching for this pattern is the signal to STOP and allocate "
                    + "under the managed root instead — not to ask the operator to approve the `rm -rf`.",
                CleanupRule =
                    "Cleanup is `git worktree remove <managed-path>` for a REGISTERED, CLEAN worktree only — confirmed "
                    + "via `git worktree list` and a clean `git status` first.",
                UnsafeStalePathRule =
                    "A stale path that is NOT a registered git worktree, is OUTSIDE the managed root, or is dirty/"
                    + "unsafe is NEVER an operator `rm -rf` approval prompt — it is a STRUCTURED BLOCKER agmsg reply to "
                    + "the orchestrator (`status: blocked`) so the orchestrator can route the repair, not something the "
                    + "reviewer resolves by force-deleting an unmanaged path.",
                DelegationExample =
                    "{\"delegate\":{\"domain\":\"<domain>\",\"execution_unit\":\"<unit>\",\"target_repo\":"
                    + "\"<owner/repo>\",\"pr\":\"<n>\",\"review_cwd\":\"/review/<domain>\",\"managed_worktree_policy\":"
                    + "\"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never "
                    + "/tmp\",\"design_alignment_required\":true,\"destination_thread\":\"review@<domain>\"}}",
                DesignAlignmentSources = new[]
                {
                    "packet — the authored packet content and acceptance criteria.",
                    "review-context — the review-context artifact for this PR/unit.",
                    "intent tree — the relevant intent-tree entries for the touched domain.",
                    "ADR / decision notes — any linked architecture or design-decision records.",
                    "relevant docs — user-facing or developer docs the change touches.",
                },
            },
            Setup = new OrchestratorSetup
            {
                Summary =
                    "Design-thread setup for starting orchestrator-message mode. The steady state is MESSAGE-DRIVEN: "
                    + "implementation/review receivers reply over agmsg and those replies wake the orchestrator, so no "
                    + "fast recurring driver is required by default; the implementation and review threads are always "
                    + "loopless receivers. Decide the inputs, register the agmsg roles under one team, paste the role "
                    + "prompts, run one read-only first wake, ping-test the inbox, then either rely on message-driven "
                    + "wakes or schedule the orchestrator only as an explicit fallback/legacy timer (see Design-side "
                    + "watchdog for the recommended low-frequency safety net). agmsg is a signal layer only; intent-cli "
                    + "and GitHub stay authoritative.",
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
                    "Message-driven steady state is the default — implementation/review replies wake the orchestrator; schedule an orchestrator timer (Codex automation 5m, or Claude same-thread `/loop 5m`) only as an explicit fallback/legacy option, and leave the receivers loopless either way.",
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
            Preflight = new OrchestratorPreflight
            {
                Summary =
                    "Before mutating anything, preflight ALL THREE checkouts (the orchestrator, implementation, and "
                    + "review cwds). A receiver acting in the wrong repo, on the wrong branch, or over dirty user work is "
                    + "the most common orchestration failure — catch it before the first delegation.",
                Checks = new[]
                {
                    "For each cwd (orchestrator / implementation / review), confirm `git status` is clean — no uncommitted or untracked work that a checkout/branch switch would clobber.",
                    "Confirm each cwd's git remote is the EXPECTED repo for its role — the implementation/review receivers must point at the delegated target repo, not a sibling clone.",
                    "Confirm each cwd is on the expected branch/base (e.g. the base-branch policy's base, or a fresh work branch) — a receiver on a stale branch implements against the wrong base.",
                    "Confirm the host repo / domain context: if a checkout exposes multiple domains, the orchestrator MUST filter by the requested domain/target repo before publishing or delegating (visibility is not authorization).",
                    "Existing-loop conflict check: confirm no timer-loop (implement/review recurring timer) is already running for this domain/repo — orchestrator-message mode and timer-loop mode must not run together for the same route.",
                },
            },
            Troubleshooting = new[]
            {
                new OrchestratorTroubleshooting
                {
                    Symptom = "Message not received by a receiver",
                    Action = "Confirm the role is registered (`team.sh`) and delivery is set (`delivery.sh status`). The receiver may have missed it (monitor not yet active) — have it read its queue with `inbox.sh`, or resend after a ping/ack.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Monitor/delivery configured AFTER the session started",
                    Action = "A session started before its monitor/watch path was active will not pick up earlier messages live — restart the receiver session (or read with `inbox.sh`) so the monitor hook attaches, then re-confirm with a ping/ack before delegating.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Codex Desktop app thread is the receiver",
                    Action = "Codex Desktop app threads are NOT agmsg monitor receivers by default — they receive manually only. Use a CLI session as the receiver, or have the Desktop thread read its queue with `inbox.sh`.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Receiver cwd sees a different repo/domain than delegated",
                    Action = "STOP — do not claim. The receiver's cwd/worktree, git remote, and delegated domain must match the routing; reply blocked and re-route. An execution-unit ID prefix mismatch alone is NOT the signal — compare packet/domain metadata and the routing context.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Codex asks to approve `rm -rf /tmp/...review...`",
                    Action = "This is the RIGHT safety behavior for the WRONG workflow — the review worktree was allocated at an unmanaged `/tmp` path instead of the managed root. The fix is the managed root (`.intent-cli/worktrees/review-<unit>`), NOT weakening approval settings: re-allocate under the managed root (see Review delegation — managed worktrees and design alignment), and do NOT approve the `rm -rf` for the stale `/tmp` path — reply blocked to the orchestrator so it can route the cleanup as a repair instead.",
                },
            },
            ReceiverReadiness = new OrchestratorReceiverReadiness
            {
                Summary =
                    "Monitor configuration is NOT enough. A registered team plus a configured delivery mode does NOT mean "
                    + "a receiver will see your message — a newly launched or restarted session may not pick up messages "
                    + "sent before its monitor/watch path was active. Confirm each receiver is READY with a ping/ack "
                    + "before sending real work.",
                StartupOrder = new[]
                {
                    "Join the three roles to the team (`join.sh`).",
                    "Set the delivery mode for each role (`delivery.sh set`).",
                    "Launch or restart the receiver CLI sessions (implementation, review, and the orchestrator).",
                    "Wait for the monitor/bridge to attach in each receiver session before sending anything.",
                    "Send a ping to each receiver only AFTER its session is active.",
                    "Require an ack from each receiver — or confirm receipt manually with `inbox.sh` — before proceeding.",
                    "Only then send the first real delegation.",
                },
                SendBeforeReadyWarning =
                    "Messages sent BEFORE a receiver is ready may be stored in agmsg history but NOT visibly delivered "
                    + "to a freshly launched/restarted session. A send is not a delivery: an unacked message is "
                    + "receiver-NOT-READY, not a successful delegation. Recover by resending after the ack, or have the "
                    + "receiver read its queue with `inbox.sh`.",
                RecoveryMessageTemplate =
                    "Heads up: your session started AFTER I sent earlier messages, so they may be in agmsg history but "
                    + "not visibly delivered to you. Read your queue now with `inbox.sh` to catch anything you missed. "
                    + "Any prior unacked message is receiver-not-ready (NOT a delegation you must act on) — reply `ack` "
                    + "to this ping and I will (re)send the current delegation.",
                States = new[]
                {
                    new OrchestratorReadinessState { State = "registered", Meaning = "the role joined the team (it appears in `team.sh`)." },
                    new OrchestratorReadinessState { State = "delivery-configured", Meaning = "the delivery mode is set for the role (`delivery.sh status`)." },
                    new OrchestratorReadinessState { State = "watcher-alive", Meaning = "the monitor / watch process is running for the role." },
                    new OrchestratorReadinessState { State = "receiver-session-active", Meaning = "a launched/restarted receiver session is actually attached to the monitor path — a session started before delivery was active may not receive earlier messages." },
                    new OrchestratorReadinessState { State = "ping-acknowledged", Meaning = "the receiver replied to a ping — the only proof the channel works end to end." },
                },
                PingAckRequired =
                    "Before ANY real delegation, send a ping to the orchestrator, implementer, and reviewer and require "
                    + "an ack from each. Treat a missing ack as NOT-READY and do not send real work until every receiver "
                    + "has acked. Re-do the ping/ack after any receiver launch or restart.",
                NotReadyRecovery = new[]
                {
                    "Messages sent before readiness may not have been received — resend them after the ack.",
                    "Read what is already queued for a role with `inbox.sh` (a session can pull messages it missed).",
                    "Re-confirm registration (`team.sh`) and delivery (`delivery.sh status`) before resending.",
                },
                WatchNote =
                    "Manual `watch.sh` streams a role's inbox live but OCCUPIES a terminal — it is a debug / fallback "
                    + "streaming option, not the default setup requirement. The normal path is the monitor delivery hook; "
                    + "use `watch.sh` to diagnose, not as the standing receiver.",
                CodexDesktopNote =
                    "Codex Desktop app threads are NOT agmsg monitor receivers by default — they are a different "
                    + "execution surface from a CLI session. Do not assume a Desktop-app thread will receive agmsg "
                    + "messages; use a CLI session as the receiver (or read with `inbox.sh`).",
                DiagnosticCommands = new[]
                {
                    "agmsg team.sh — confirm the role is registered in the team.",
                    "agmsg delivery.sh status — confirm the delivery mode is configured/active for the role.",
                    "agmsg inbox.sh — read what is queued for a role (catch messages sent before readiness).",
                    "agmsg send.sh — send a ping; the receiver's ack proves the channel end to end.",
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
                        + "with GitHub/intent-cli facts, STOP and escalate rather than guessing. Your normal steady "
                        + "state is MESSAGE-DRIVEN: implementation/review agmsg replies wake you, so you do NOT need a "
                        + "fast recurring timer. An orchestrator timer (Codex automation every 5m, or Claude "
                        + "same-thread `/loop 5m`) remains SUPPORTED only as an explicit FALLBACK/LEGACY polling "
                        + "option; the implementation/review receivers stay loopless and act only on your delegations "
                        + "either way."
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
                        + "wait again (receivers are never scheduled; the orchestrator is message-driven by default, with an explicit fallback/legacy timer as the only case where it is scheduled). When delegated an item, run "
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
                        + "wait again (receivers are never scheduled; the orchestrator is message-driven by default, with an explicit fallback/legacy timer as the only case where it is scheduled). When delegated a PR, run the "
                        + "official host review/closeout through intent-cli surfaces (`review closeout-plan`, `guide "
                        + "review`, `automation pr-transition`, `closeout pr`) — agmsg never replaces semantic review or "
                        + "authorizes a merge. Perform semantic review only when you are the packet `review_role` or "
                        + "explicitly assigned (G480); otherwise orchestrate the merge/closeout of an already-approved "
                        + "PR. If you need a review worktree, allocate it under the MANAGED root "
                        + "(`.intent-cli/worktrees/review-<unit>`) — NEVER a raw `/tmp/...review...` path, and NEVER "
                        + "`rm -rf /tmp/... && git worktree add ...`; remove it only with `git worktree remove` once it "
                        + "is a registered, clean worktree. A non-registered, dirty, or otherwise unsafe stale path is a "
                        + "STRUCTURED BLOCKER reply to the orchestrator, not an operator `rm -rf` approval prompt (see "
                        + "Review delegation — managed worktrees and design alignment). Your review must be grounded in "
                        + "design intent, not only diff/CI: check the packet, review-context, intent tree, ADR/decision "
                        + "notes, and relevant docs, and your reply must set `design_alignment_checked: true` plus which "
                        + "of those sources you checked — the orchestrator treats a reply missing that evidence as an "
                        + "INCOMPLETE review unless an authoritative prior approval state already proves equivalent "
                        + "review. Report ONE structured agmsg reply (accepted / progress / completed / blocked) citing "
                        + "the intent-cli/GitHub facts. intent-cli and GitHub stay authoritative."),
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
                ReviewCompletedExample =
                    "{\"status\":\"completed\",\"thread\":\"review\",\"ref\":\"pr#<n>\",\"note\":\"approved; closeout done\","
                    + "\"design_alignment_checked\":true,\"design_alignment_sources_checked\":[\"packet\","
                    + "\"review-context\",\"intent-tree\",\"adr-decision-notes\",\"relevant-docs\"],"
                    + "\"managed_worktree_policy\":\"compliant — .intent-cli/worktrees/review-<unit>, removed after review\"}",
                ReviewIncompleteRule =
                    "A review `completed` reply that omits `design_alignment_checked: true` and the checked-source list "
                    + "is INCOMPLETE — the orchestrator does not route merge/closeout on that reply alone. The only "
                    + "exception is when an authoritative PRIOR approval state already proves equivalent design-alignment "
                    + "review (the orchestrator must point to that specific prior evidence, not assume equivalence).",
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
                "Allocate temporary worktrees under an allowlisted managed root and remove them with `git worktree remove`; never raw `rm -rf` of arbitrary temp paths, and `approval_policy=never`/`danger-full-access` is not a substitute for safe cleanup.",
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

    // G500: turn an orchestrator setup request into a concrete, operational
    // intake. The visible outcome is one of missing-inputs / setup-ready /
    // blocked. When inputs are complete the intake emits copy-paste agmsg
    // join/delivery commands and first role prompts per role; when a field is
    // missing it lists ONLY the missing fields; when an existing loop would
    // race it is blocked. The orchestrator is the only scheduled thread —
    // implementation/review are loopless receivers.
    private static OrchestratorSetupIntake BuildSetupIntake(IReadOnlyDictionary<string, string> values)
    {
        var domain = values["<domain>"];
        var repo = values["<owner/repo>"];
        var agent = values["<agent>"];
        var orchestratorPath = values["<orchestrator-path>"];
        var implementationPath = values["<implementation-path>"];
        var reviewPath = values["<review-path>"];
        var team = values["<team>"];
        var deliveryMode = values["<delivery-mode>"];
        var loopPolicy = values["<existing-loop-policy>"];

        // The three role agents fall back to --agent when not explicitly set.
        string ResolveAgent(string key) =>
            values[key].Length > 0 ? values[key] : (string.Equals(agent, "<agent>", StringComparison.Ordinal) ? string.Empty : agent);

        var orchestratorAgent = ResolveAgent("<orchestrator-agent>");
        var implementerAgent = ResolveAgent("<implementer-agent>");
        var reviewerAgent = ResolveAgent("<reviewer-agent>");

        bool Supplied(string value, string placeholder) =>
            value.Length > 0 && !string.Equals(value, placeholder, StringComparison.Ordinal);

        // Required fields, in a stable order; a field is "missing" when unset.
        var required = new (string Label, bool Present)[]
        {
            ("domain", Supplied(domain, "<domain>")),
            ("target repo", Supplied(repo, "<owner/repo>")),
            ("orchestrator folder", Supplied(orchestratorPath, "<orchestrator-path>")),
            ("implementation folder", Supplied(implementationPath, "<implementation-path>")),
            ("review folder", Supplied(reviewPath, "<review-path>")),
            ("orchestrator agent", orchestratorAgent.Length > 0),
            ("implementer agent", implementerAgent.Length > 0),
            ("reviewer agent", reviewerAgent.Length > 0),
            ("agmsg team name", Supplied(team, "<team>")),
            ("delivery mode", Supplied(deliveryMode, "<delivery-mode>")),
            ("existing-loop stop policy", loopPolicy.Length > 0),
        };

        var missing = required.Where(f => !f.Present).Select(f => f.Label).ToArray();

        var inputs = new OrchestratorSetupInputs
        {
            Domain = domain,
            TargetRepo = repo,
            OrchestratorFolder = orchestratorPath,
            ImplementationFolder = implementationPath,
            ReviewFolder = reviewPath,
            OrchestratorAgent = orchestratorAgent.Length > 0 ? orchestratorAgent : null,
            ImplementerAgent = implementerAgent.Length > 0 ? implementerAgent : null,
            ReviewerAgent = reviewerAgent.Length > 0 ? reviewerAgent : null,
            Team = team,
            DeliveryMode = deliveryMode,
            ExistingLoopPolicy = loopPolicy.Length > 0 ? loopPolicy : null,
        };

        if (missing.Length > 0)
        {
            return new OrchestratorSetupIntake
            {
                Status = IntakeMissingInputs,
                Headline = $"missing-inputs — supply the {missing.Length} missing field(s) below to get a setup-ready plan.",
                MissingFields = missing,
                Inputs = inputs,
                LooplessReceiverNote = LooplessReceiverNote,
            };
        }

        // All inputs present. A kept existing loop for the same route would race
        // the orchestrator (mixed-mode timers) — block until the operator stops it.
        if (string.Equals(loopPolicy, ExistingLoopKeep, StringComparison.Ordinal))
        {
            return new OrchestratorSetupIntake
            {
                Status = IntakeBlocked,
                Headline =
                    "blocked — existing implementation/review timer loops for this domain/repo would race the "
                    + "orchestrator (mixed-mode). Stop the existing loops (or re-run with --existing-loop-policy "
                    + "will-stop) before starting orchestrator mode; receivers are never scheduled — orchestrator "
                    + "wakes are message-driven by default, with an explicit fallback/legacy timer as the only case "
                    + "where the orchestrator itself is scheduled.",
                MissingFields = Array.Empty<string>(),
                Inputs = inputs,
                LooplessReceiverNote = LooplessReceiverNote,
            };
        }

        // setup-ready: emit copy-paste agmsg commands + first role prompts.
        var agmsgCommands = new[]
        {
            $"agmsg join.sh {team} orchestrator {orchestratorAgent} {orchestratorPath}",
            $"agmsg delivery.sh set {deliveryMode} {orchestratorAgent} {orchestratorPath}",
            $"agmsg join.sh {team} implementation {implementerAgent} {implementationPath}",
            $"agmsg delivery.sh set {deliveryMode} {implementerAgent} {implementationPath}",
            $"agmsg join.sh {team} review {reviewerAgent} {reviewPath}",
            $"agmsg delivery.sh set {deliveryMode} {reviewerAgent} {reviewPath}",
        };

        string RolePrompt(string role, string roleAgent, string folder) =>
            $"You are the {role.ToUpperInvariant()} thread for domain `{domain}` against `{repo}` using `{roleAgent}`, "
            + $"running from `{folder}` as part of agmsg team `{team}` (delivery: {deliveryMode}). "
            + (string.Equals(role, "orchestrator", StringComparison.Ordinal)
                ? "Your steady state is MESSAGE-DRIVEN — implementation/review agmsg replies wake you; an orchestrator "
                  + "timer (Codex automation 5m or Claude `/loop 5m`) is an OPTIONAL fallback/legacy polling mode, not "
                  + "the default. You pace the implementation/review receivers over agmsg and never run their timers. "
                  + "See the full orchestrator prompt in the Thread prompts section."
                : "You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator "
                  + "delegation, act once, reply once, then wait. Your worker target comes from `intent-cli worker "
                  + "next-action`, not the agmsg text. See the full prompt in the Thread prompts section.");

        var rolePrompts = new[]
        {
            new OrchestratorThreadPrompt { Role = "orchestrator", Purpose = "First prompt — paste into the scheduled orchestrator thread.", Prompt = RolePrompt("orchestrator", orchestratorAgent, orchestratorPath) },
            new OrchestratorThreadPrompt { Role = "implementation", Purpose = "First prompt — paste into the loopless implementation receiver.", Prompt = RolePrompt("implementation", implementerAgent, implementationPath) },
            new OrchestratorThreadPrompt { Role = "review", Purpose = "First prompt — paste into the loopless review receiver.", Prompt = RolePrompt("review", reviewerAgent, reviewPath) },
        };

        return new OrchestratorSetupIntake
        {
            Status = IntakeSetupReady,
            Headline = "setup-ready — register the three roles with the agmsg commands, paste the first prompts, then run the first validation.",
            MissingFields = Array.Empty<string>(),
            Inputs = inputs,
            AgmsgCommands = agmsgCommands,
            RolePrompts = rolePrompts,
            FirstValidation = new[]
            {
                $"Preflight all three cwds BEFORE mutating: `{orchestratorPath}` (orchestrator), `{implementationPath}` (implementation), `{reviewPath}` (review) — clean `git status`, expected git remote/repo, expected branch/base, and no existing timer-loop for this domain/repo (see Preflight).",
                "Existing-loop conflict check: confirm no implementation/review recurring timer is running for this domain/repo (implementation/review stay loopless whether the orchestrator runs message-driven or on an explicit fallback/legacy timer).",
                "First read-only wake: run ONE confirm-only orchestrator wake — read state, send nothing.",
                "Receiver readiness: ping each receiver and require an ack BEFORE any real delegation — a registered+configured role is not ready until it acks (see the Receiver readiness section). A session launched before delivery was active may have missed earlier messages; resend or read with `inbox.sh`.",
            },
            LooplessReceiverNote = LooplessReceiverNote,
        };
    }

    private const string LooplessReceiverNote =
        "The implementation and review threads are loopless agmsg receivers — they must NOT run their own `/loop` or "
        + "recurring timer for the same domain/repo, whether the orchestrator runs message-driven (the default, woken "
        + "by agmsg replies) or on an explicit fallback/legacy timer (Codex 5m / Claude `/loop 5m`).";

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
            // G500: setup-intake inputs. Unset = the placeholder token (treated
            // as "missing" by the intake); the three role agents fall back to
            // <agent> when not explicitly supplied.
            ["<orchestrator-path>"] = "<orchestrator-path>",
            ["<implementation-path>"] = "<implementation-path>",
            ["<review-path>"] = "<review-path>",
            ["<orchestrator-agent>"] = string.Empty,
            ["<implementer-agent>"] = string.Empty,
            ["<reviewer-agent>"] = string.Empty,
            ["<team>"] = "<team>",
            ["<delivery-mode>"] = "<delivery-mode>",
            ["<existing-loop-policy>"] = string.Empty,
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
                case "--orchestrator-path":
                    parsed["<orchestrator-path>"] = value;
                    break;
                case "--implementation-path":
                    parsed["<implementation-path>"] = value;
                    break;
                case "--review-path":
                    parsed["<review-path>"] = value;
                    break;
                case "--orchestrator-agent":
                    parsed["<orchestrator-agent>"] = value;
                    break;
                case "--implementer-agent":
                    parsed["<implementer-agent>"] = value;
                    break;
                case "--reviewer-agent":
                    parsed["<reviewer-agent>"] = value;
                    break;
                case "--team":
                    parsed["<team>"] = value;
                    break;
                case "--delivery-mode":
                    parsed["<delivery-mode>"] = value;
                    break;
                case "--existing-loop-policy":
                    parsed["<existing-loop-policy>"] = value;
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

        var loopPolicy = parsed["<existing-loop-policy>"];
        if (loopPolicy.Length > 0
            && !string.Equals(loopPolicy, ExistingLoopNone, StringComparison.Ordinal)
            && !string.Equals(loopPolicy, ExistingLoopWillStop, StringComparison.Ordinal)
            && !string.Equals(loopPolicy, ExistingLoopKeep, StringComparison.Ordinal))
        {
            values = parsed;
            error = $"Unknown --existing-loop-policy '{loopPolicy}'. Supported: none, will-stop, keep.";
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
        || string.Equals(arg, "--mode", StringComparison.Ordinal)
        || string.Equals(arg, "--orchestrator-path", StringComparison.Ordinal)
        || string.Equals(arg, "--implementation-path", StringComparison.Ordinal)
        || string.Equals(arg, "--review-path", StringComparison.Ordinal)
        || string.Equals(arg, "--orchestrator-agent", StringComparison.Ordinal)
        || string.Equals(arg, "--implementer-agent", StringComparison.Ordinal)
        || string.Equals(arg, "--reviewer-agent", StringComparison.Ordinal)
        || string.Equals(arg, "--team", StringComparison.Ordinal)
        || string.Equals(arg, "--delivery-mode", StringComparison.Ordinal)
        || string.Equals(arg, "--existing-loop-policy", StringComparison.Ordinal);

    // G500: render the operational setup intake at the top of the guide.
    private static void WriteSetupIntake(TextWriter writer, OrchestratorSetupIntake intake)
    {
        writer.WriteLine("## Setup intake");
        writer.WriteLine();
        writer.WriteLine($"- **status: `{intake.Status}`**");
        writer.WriteLine($"- {intake.Headline}");
        writer.WriteLine($"- {intake.LooplessReceiverNote}");
        writer.WriteLine();

        if (intake.MissingFields.Count > 0)
        {
            writer.WriteLine("### Missing inputs (supply only these)");
            writer.WriteLine();
            foreach (var field in intake.MissingFields)
            {
                writer.WriteLine($"- {field}");
            }
            writer.WriteLine();
        }

        if (intake.AgmsgCommands is { Count: > 0 })
        {
            writer.WriteLine("### agmsg registration + delivery (copy-paste)");
            writer.WriteLine();
            writer.WriteLine("```bash");
            foreach (var command in intake.AgmsgCommands)
            {
                writer.WriteLine(command);
            }
            writer.WriteLine("```");
            writer.WriteLine();
        }

        if (intake.RolePrompts is { Count: > 0 })
        {
            writer.WriteLine("### First role prompts");
            foreach (var prompt in intake.RolePrompts)
            {
                writer.WriteLine();
                writer.WriteLine($"#### {prompt.Role}");
                writer.WriteLine();
                writer.WriteLine("```text");
                writer.WriteLine(prompt.Prompt);
                writer.WriteLine("```");
            }
            writer.WriteLine();
        }

        if (intake.FirstValidation is { Count: > 0 })
        {
            writer.WriteLine("### First validation");
            writer.WriteLine();
            foreach (var step in intake.FirstValidation)
            {
                writer.WriteLine($"- {step}");
            }
            writer.WriteLine();
        }
    }

    private static void WriteMarkdown(TextWriter writer, OrchestratorThreadGuide guide)
    {
        writer.WriteLine("# Guide — agmsg-backed orchestrator thread (G487)");
        writer.WriteLine();

        // G500: the setup intake comes FIRST — a design-thread agent must land on
        // an operational outcome (missing-inputs / setup-ready / blocked) before
        // the long reference material below.
        WriteSetupIntake(writer, guide.SetupIntake);

        writer.WriteLine(guide.Summary);
        writer.WriteLine();

        writer.WriteLine("## Mode separation");
        writer.WriteLine();
        writer.WriteLine($"- **timer-loop mode** — {guide.ModeSeparation.TimerLoopMode}");
        writer.WriteLine($"- **orchestrator-message mode** — {guide.ModeSeparation.OrchestratorMessageMode}");
        writer.WriteLine($"- **mixed-mode warning** — {guide.ModeSeparation.MixedModeWarning}");
        writer.WriteLine();

        writer.WriteLine("## Role boundary (design authors; orchestrator coordinates)");
        writer.WriteLine();
        writer.WriteLine(guide.RoleBoundary.Summary);
        writer.WriteLine();
        writer.WriteLine("### Design owns");
        writer.WriteLine();
        foreach (var item in guide.RoleBoundary.DesignOwns)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Orchestrator owns");
        writer.WriteLine();
        foreach (var item in guide.RoleBoundary.OrchestratorOwns)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **missing packet** — {guide.RoleBoundary.MissingPacketResponse}");
        writer.WriteLine($"- **release-prep** — {guide.RoleBoundary.ReleasePrepRule}");
        writer.WriteLine();
        writer.WriteLine("Structured packet-needed message (orchestrator → design):");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.RoleBoundary.MissingPacketMessageTemplate);
        writer.WriteLine("```");
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

        writer.WriteLine("## Setup intake form");
        writer.WriteLine();
        writer.WriteLine(guide.IntakeForm.Summary);
        writer.WriteLine();
        writer.WriteLine("### Ask for / infer");
        writer.WriteLine();
        foreach (var question in guide.IntakeForm.Questions)
        {
            writer.WriteLine($"- {question}");
        }
        writer.WriteLine();
        writer.WriteLine("### Recommended defaults (when inputs are incomplete)");
        writer.WriteLine();
        foreach (var fallback in guide.IntakeForm.Defaults)
        {
            writer.WriteLine($"- {fallback}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **design delivery** — {guide.IntakeForm.DesignDeliveryNote}");
        writer.WriteLine();
        writer.WriteLine("### Role startup messages (assume the agmsg role)");
        writer.WriteLine();
        foreach (var startup in guide.IntakeForm.RoleStartupMessages)
        {
            writer.WriteLine($"- **{startup.AgentType}** — `{startup.ActasInvocation}` — {startup.Note}");
        }
        writer.WriteLine();

        writer.WriteLine("## Preflight (all three cwds)");
        writer.WriteLine();
        writer.WriteLine(guide.Preflight.Summary);
        writer.WriteLine();
        foreach (var check in guide.Preflight.Checks)
        {
            writer.WriteLine($"- {check}");
        }
        writer.WriteLine();

        writer.WriteLine("## Receiver readiness");
        writer.WriteLine();
        writer.WriteLine(guide.ReceiverReadiness.Summary);
        writer.WriteLine();
        writer.WriteLine("### Startup order");
        writer.WriteLine();
        for (var i = 0; i < guide.ReceiverReadiness.StartupOrder.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.ReceiverReadiness.StartupOrder[i]}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Send-before-ready:** {guide.ReceiverReadiness.SendBeforeReadyWarning}");
        writer.WriteLine();
        writer.WriteLine("Copy-paste operator message when receivers were launched after the initial messages were sent:");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.ReceiverReadiness.RecoveryMessageTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Readiness states");
        writer.WriteLine();
        foreach (var state in guide.ReceiverReadiness.States)
        {
            writer.WriteLine($"- **{state.State}** — {state.Meaning}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **ping/ack required** — {guide.ReceiverReadiness.PingAckRequired}");
        writer.WriteLine();
        writer.WriteLine("### If a receiver is not ready");
        writer.WriteLine();
        foreach (var item in guide.ReceiverReadiness.NotReadyRecovery)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **`watch.sh`** — {guide.ReceiverReadiness.WatchNote}");
        writer.WriteLine($"- **Codex Desktop app** — {guide.ReceiverReadiness.CodexDesktopNote}");
        writer.WriteLine();
        writer.WriteLine("### Diagnostic commands (agmsg scripts only)");
        writer.WriteLine();
        foreach (var command in guide.ReceiverReadiness.DiagnosticCommands)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();

        writer.WriteLine("## Troubleshooting");
        writer.WriteLine();
        foreach (var entry in guide.Troubleshooting)
        {
            writer.WriteLine($"- **{entry.Symptom}** — {entry.Action}");
        }
        writer.WriteLine();

        writer.WriteLine("## Monitor recovery");
        writer.WriteLine();
        foreach (var entry in guide.MonitorRecovery)
        {
            writer.WriteLine($"- **{entry.Symptom}** — {entry.Action}");
        }
        writer.WriteLine();

        writer.WriteLine("## Monitor tool vs delivery-mode (G511)");
        writer.WriteLine();
        writer.WriteLine(guide.MonitorToolDistinction.Summary);
        writer.WriteLine();
        writer.WriteLine(guide.MonitorToolDistinction.DeliveryModeNote);
        writer.WriteLine();
        writer.WriteLine("Live-attachment success markers — verify all four to confirm the inbox stream is live:");
        writer.WriteLine();
        foreach (var marker in guide.MonitorToolDistinction.SuccessMarkers)
        {
            writer.WriteLine($"- {marker}");
        }
        writer.WriteLine();
        writer.WriteLine("Failure markers — a receiver reporting `mode=monitor` may still be silently broken:");
        writer.WriteLine();
        foreach (var marker in guide.MonitorToolDistinction.FailureMarkers)
        {
            writer.WriteLine($"- {marker}");
        }
        writer.WriteLine();
        writer.WriteLine("Trust-repair runbook (when the success markers are missing):");
        writer.WriteLine();
        foreach (var step in guide.MonitorToolDistinction.TrustRepair)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine("Windows guidance:");
        writer.WriteLine();
        foreach (var note in guide.MonitorToolDistinction.WindowsGuidance)
        {
            writer.WriteLine($"- {note}");
        }
        writer.WriteLine();
        writer.WriteLine("Fallback ladder — orchestrator mode stays usable without realtime Monitor:");
        writer.WriteLine();
        foreach (var step in guide.MonitorToolDistinction.FallbackLadder)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine("Missing-Monitor project-settings diagnosis (G517) — when `ToolSearch select:Monitor` finds no Monitor tool at all:");
        writer.WriteLine();
        foreach (var step in guide.MonitorToolDistinction.ProjectSettingsDiagnosis)
        {
            writer.WriteLine($"- {step}");
        }
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
        writer.WriteLine($"- scheduled thread when an explicit timer is used: `{guide.Scheduling.ScheduledThread}` (the only thread ever scheduled)");
        writer.WriteLine($"- **receivers are loopless** — {guide.Scheduling.ReceiverNote}");
        writer.WriteLine();
        writer.WriteLine("### Codex automation (5m) — orchestrator (fallback/legacy, optional)");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.Scheduling.CodexSetupPrompt);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Claude `/loop 5m` — orchestrator (fallback/legacy, optional)");
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

        writer.WriteLine("## Draft PR reviewability");
        writer.WriteLine();
        writer.WriteLine(guide.DraftPrReviewability);
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
        foreach (var field in guide.DesignThreadEscalation.MessageFields)
        {
            writer.WriteLine($"- {field}");
        }
        writer.WriteLine();

        writer.WriteLine("## Design / human receiver (optional)");
        writer.WriteLine();
        writer.WriteLine(guide.DesignReceiver.Summary);
        writer.WriteLine();
        writer.WriteLine($"- optional for routine operation: {(guide.DesignReceiver.Optional ? "yes" : "no")} (recommended for escalation delivery)");
        writer.WriteLine();
        writer.WriteLine("### Four logical roles (when design receiving is enabled)");
        writer.WriteLine();
        foreach (var role in guide.DesignReceiver.Roles)
        {
            writer.WriteLine($"- {role}");
        }
        writer.WriteLine();
        writer.WriteLine("### Design receiver setup");
        writer.WriteLine();
        foreach (var step in guide.DesignReceiver.Setup)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine("### Minimal manual inbox trigger prompt (paste into the design thread)");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.DesignReceiver.ManualInboxTriggerPrompt);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"> **Pre-start messages:** {guide.DesignReceiver.PreStartNote}");
        writer.WriteLine();

        writer.WriteLine("## Design handoff (start / resume)");
        writer.WriteLine();
        writer.WriteLine(guide.DesignHandoff.Summary);
        writer.WriteLine();
        writer.WriteLine("First message — design → orchestrator (paste into the design thread):");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DesignHandoff.FirstMessageTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"- **autonomous publish** — {guide.DesignHandoff.AutonomousPublishRule}");
        writer.WriteLine($"- **escalation boundary** — {guide.DesignHandoff.EscalationBoundary}");
        writer.WriteLine($"- **design inbox workflow** — {guide.DesignHandoff.DesignInboxWorkflow}");
        writer.WriteLine();

        writer.WriteLine("## Design-side watchdog (optional safety net)");
        writer.WriteLine();
        writer.WriteLine(guide.DesignWatchdog.Summary);
        writer.WriteLine();
        writer.WriteLine($"- optional: {(guide.DesignWatchdog.Optional ? "yes" : "no")}");
        writer.WriteLine($"- **frequency** — {guide.DesignWatchdog.Frequency}");
        writer.WriteLine();
        writer.WriteLine("### Watchdog checks");
        writer.WriteLine();
        foreach (var check in guide.DesignWatchdog.Checks)
        {
            writer.WriteLine($"- {check}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **action** — {guide.DesignWatchdog.Action}");
        writer.WriteLine();
        writer.WriteLine("Repair/status-request template:");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DesignWatchdog.RepairStatusRequestTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"- **stop condition** — {guide.DesignWatchdog.StopCondition}");
        writer.WriteLine();
        writer.WriteLine("### Watchdog safety rules");
        writer.WriteLine();
        foreach (var rule in guide.DesignWatchdog.SafetyRules)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **fallback timer** — {guide.DesignWatchdog.FallbackTimerNote}");
        writer.WriteLine();

        writer.WriteLine("## Design traffic-controller playbook");
        writer.WriteLine();
        writer.WriteLine(guide.DesignTrafficController.Summary);
        writer.WriteLine();
        writer.WriteLine("### Playbook");
        writer.WriteLine();
        for (var i = 0; i < guide.DesignTrafficController.Playbook.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.DesignTrafficController.Playbook[i]}");
        }
        writer.WriteLine();
        writer.WriteLine("### \"Orchestrator appears idle\" diagnostic (before escalating)");
        writer.WriteLine();
        for (var i = 0; i < guide.DesignTrafficController.IdleDiagnostic.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.DesignTrafficController.IdleDiagnostic[i]}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Context-only:** {guide.DesignTrafficController.ContextOnlyRule}");
        writer.WriteLine();

        writer.WriteLine("## Managed worktree cleanup");
        writer.WriteLine();
        writer.WriteLine(guide.WorktreeManagement.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **managed root** — {guide.WorktreeManagement.ManagedRoot}");
        writer.WriteLine($"- **approval policy** — {guide.WorktreeManagement.ApprovalPolicyNote}");
        writer.WriteLine();
        writer.WriteLine("### Allocation");
        writer.WriteLine();
        foreach (var item in guide.WorktreeManagement.Allocation)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Safe cleanup");
        writer.WriteLine();
        foreach (var item in guide.WorktreeManagement.SafeCleanup)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Refuse cleanup when");
        writer.WriteLine();
        foreach (var item in guide.WorktreeManagement.RefuseWhen)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Review delegation — managed worktrees and design alignment");
        writer.WriteLine();
        writer.WriteLine(guide.ReviewDelegationContract.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **managed worktree root** — {guide.ReviewDelegationContract.ManagedWorktreeRoot}");
        writer.WriteLine($"- **prohibited pattern** — {guide.ReviewDelegationContract.ProhibitedPattern}");
        writer.WriteLine($"- **cleanup rule** — {guide.ReviewDelegationContract.CleanupRule}");
        writer.WriteLine($"- **unsafe/stale path rule** — {guide.ReviewDelegationContract.UnsafeStalePathRule}");
        writer.WriteLine();
        writer.WriteLine("Review delegation example (orchestrator → review):");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.ReviewDelegationContract.DelegationExample);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("Design-alignment sources a review reply may cite as checked:");
        writer.WriteLine();
        foreach (var source in guide.ReviewDelegationContract.DesignAlignmentSources)
        {
            writer.WriteLine($"- {source}");
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
        writer.WriteLine("Review `completed` reply — must include design-alignment evidence:");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.AgmsgReplyContract.ReviewCompletedExample);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"- **review-incomplete rule** — {guide.AgmsgReplyContract.ReviewIncompleteRule}");
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
        writer.WriteLine();
        writer.WriteLine("Setup intake (rendered first): supply --domain, --target-repo, --orchestrator-path,");
        writer.WriteLine("--implementation-path, --review-path, --orchestrator-agent, --implementer-agent,");
        writer.WriteLine("--reviewer-agent, --team, --delivery-mode, and --existing-loop-policy (none|will-stop|keep) to");
        writer.WriteLine("get a setup-ready plan with copy-paste agmsg commands and first role prompts. Missing fields");
        writer.WriteLine("yield status missing-inputs (only the missing fields are listed); a kept existing loop yields");
        writer.WriteLine("status blocked. The role agents default to --agent when not set individually.");
    }
}

/// <summary>
/// G500: operational orchestrator setup intake. Status is one of
/// <c>missing-inputs</c> / <c>setup-ready</c> / <c>blocked</c>; setup-ready
/// carries copy-paste agmsg commands and first role prompts.
/// </summary>
internal sealed record OrchestratorSetupIntake
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("headline")]
    public required string Headline { get; init; }

    [JsonPropertyName("missing_fields")]
    public required IReadOnlyList<string> MissingFields { get; init; }

    [JsonPropertyName("inputs")]
    public required OrchestratorSetupInputs Inputs { get; init; }

    [JsonPropertyName("agmsg_commands")]
    public IReadOnlyList<string>? AgmsgCommands { get; init; }

    [JsonPropertyName("role_prompts")]
    public IReadOnlyList<OrchestratorThreadPrompt>? RolePrompts { get; init; }

    [JsonPropertyName("first_validation")]
    public IReadOnlyList<string>? FirstValidation { get; init; }

    [JsonPropertyName("loopless_receiver_note")]
    public required string LooplessReceiverNote { get; init; }
}

internal sealed record OrchestratorSetupInputs
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("orchestrator_folder")]
    public required string OrchestratorFolder { get; init; }

    [JsonPropertyName("implementation_folder")]
    public required string ImplementationFolder { get; init; }

    [JsonPropertyName("review_folder")]
    public required string ReviewFolder { get; init; }

    [JsonPropertyName("orchestrator_agent")]
    public string? OrchestratorAgent { get; init; }

    [JsonPropertyName("implementer_agent")]
    public string? ImplementerAgent { get; init; }

    [JsonPropertyName("reviewer_agent")]
    public string? ReviewerAgent { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("delivery_mode")]
    public required string DeliveryMode { get; init; }

    [JsonPropertyName("existing_loop_policy")]
    public string? ExistingLoopPolicy { get; init; }
}

internal sealed record OrchestratorThreadGuide
{
    [JsonPropertyName("setup_intake")]
    public required OrchestratorSetupIntake SetupIntake { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("mode_separation")]
    public required OrchestratorModeSeparation ModeSeparation { get; init; }

    [JsonPropertyName("role_boundary")]
    public required OrchestratorRoleBoundary RoleBoundary { get; init; }

    [JsonPropertyName("domain_routing")]
    public required OrchestratorDomainRouting DomainRouting { get; init; }

    [JsonPropertyName("scheduling")]
    public required OrchestratorScheduling Scheduling { get; init; }

    [JsonPropertyName("ci_wait_state")]
    public required OrchestratorCiWaitState CiWaitState { get; init; }

    [JsonPropertyName("draft_pr_reviewability")]
    public required string DraftPrReviewability { get; init; }

    [JsonPropertyName("next_slice_publication")]
    public required OrchestratorNextSlicePublication NextSlicePublication { get; init; }

    [JsonPropertyName("dependency_planning")]
    public required OrchestratorDependencyPlanning DependencyPlanning { get; init; }

    [JsonPropertyName("stale_thread_health_check")]
    public required OrchestratorStaleThreadHealthCheck StaleThreadHealthCheck { get; init; }

    [JsonPropertyName("design_thread_escalation")]
    public required OrchestratorDesignThreadEscalation DesignThreadEscalation { get; init; }

    [JsonPropertyName("design_receiver")]
    public required OrchestratorDesignReceiver DesignReceiver { get; init; }

    [JsonPropertyName("design_handoff")]
    public required OrchestratorDesignHandoff DesignHandoff { get; init; }

    [JsonPropertyName("design_watchdog")]
    public required OrchestratorDesignWatchdog DesignWatchdog { get; init; }

    [JsonPropertyName("monitor_recovery")]
    public required IReadOnlyList<OrchestratorTroubleshooting> MonitorRecovery { get; init; }

    [JsonPropertyName("monitor_tool_distinction")]
    public required OrchestratorMonitorDistinction MonitorToolDistinction { get; init; }

    [JsonPropertyName("intake_form")]
    public required OrchestratorIntakeForm IntakeForm { get; init; }

    [JsonPropertyName("design_traffic_controller")]
    public required OrchestratorDesignTrafficController DesignTrafficController { get; init; }

    [JsonPropertyName("worktree_management")]
    public required OrchestratorWorktreeManagement WorktreeManagement { get; init; }

    [JsonPropertyName("review_delegation_contract")]
    public required OrchestratorReviewDelegationContract ReviewDelegationContract { get; init; }

    [JsonPropertyName("setup")]
    public required OrchestratorSetup Setup { get; init; }

    [JsonPropertyName("preflight")]
    public required OrchestratorPreflight Preflight { get; init; }

    [JsonPropertyName("troubleshooting")]
    public required IReadOnlyList<OrchestratorTroubleshooting> Troubleshooting { get; init; }

    [JsonPropertyName("receiver_readiness")]
    public required OrchestratorReceiverReadiness ReceiverReadiness { get; init; }

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

internal sealed record OrchestratorRoleBoundary
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("design_owns")]
    public required IReadOnlyList<string> DesignOwns { get; init; }

    [JsonPropertyName("orchestrator_owns")]
    public required IReadOnlyList<string> OrchestratorOwns { get; init; }

    [JsonPropertyName("missing_packet_response")]
    public required string MissingPacketResponse { get; init; }

    [JsonPropertyName("missing_packet_message_template")]
    public required string MissingPacketMessageTemplate { get; init; }

    [JsonPropertyName("release_prep_rule")]
    public required string ReleasePrepRule { get; init; }
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

internal sealed record OrchestratorWorktreeManagement
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("managed_root")]
    public required string ManagedRoot { get; init; }

    [JsonPropertyName("allocation")]
    public required IReadOnlyList<string> Allocation { get; init; }

    [JsonPropertyName("safe_cleanup")]
    public required IReadOnlyList<string> SafeCleanup { get; init; }

    [JsonPropertyName("refuse_when")]
    public required IReadOnlyList<string> RefuseWhen { get; init; }

    [JsonPropertyName("approval_policy_note")]
    public required string ApprovalPolicyNote { get; init; }
}

internal sealed record OrchestratorReviewDelegationContract
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("managed_worktree_root")]
    public required string ManagedWorktreeRoot { get; init; }

    [JsonPropertyName("prohibited_pattern")]
    public required string ProhibitedPattern { get; init; }

    [JsonPropertyName("cleanup_rule")]
    public required string CleanupRule { get; init; }

    [JsonPropertyName("unsafe_stale_path_rule")]
    public required string UnsafeStalePathRule { get; init; }

    [JsonPropertyName("delegation_example")]
    public required string DelegationExample { get; init; }

    [JsonPropertyName("design_alignment_sources")]
    public required IReadOnlyList<string> DesignAlignmentSources { get; init; }
}

internal sealed record OrchestratorIntakeForm
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("questions")]
    public required IReadOnlyList<string> Questions { get; init; }

    [JsonPropertyName("defaults")]
    public required IReadOnlyList<string> Defaults { get; init; }

    [JsonPropertyName("design_delivery_note")]
    public required string DesignDeliveryNote { get; init; }

    [JsonPropertyName("role_startup_messages")]
    public required IReadOnlyList<OrchestratorRoleStartup> RoleStartupMessages { get; init; }
}

internal sealed record OrchestratorRoleStartup
{
    [JsonPropertyName("agent_type")]
    public required string AgentType { get; init; }

    [JsonPropertyName("actas_invocation")]
    public required string ActasInvocation { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }
}

internal sealed record OrchestratorDesignTrafficController
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("playbook")]
    public required IReadOnlyList<string> Playbook { get; init; }

    [JsonPropertyName("idle_diagnostic")]
    public required IReadOnlyList<string> IdleDiagnostic { get; init; }

    [JsonPropertyName("context_only_rule")]
    public required string ContextOnlyRule { get; init; }
}

internal sealed record OrchestratorDesignHandoff
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("first_message_template")]
    public required string FirstMessageTemplate { get; init; }

    [JsonPropertyName("autonomous_publish_rule")]
    public required string AutonomousPublishRule { get; init; }

    [JsonPropertyName("escalation_boundary")]
    public required string EscalationBoundary { get; init; }

    [JsonPropertyName("design_inbox_workflow")]
    public required string DesignInboxWorkflow { get; init; }
}

internal sealed record OrchestratorDesignWatchdog
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("optional")]
    public required bool Optional { get; init; }

    [JsonPropertyName("frequency")]
    public required string Frequency { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<string> Checks { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("repair_status_request_template")]
    public required string RepairStatusRequestTemplate { get; init; }

    [JsonPropertyName("stop_condition")]
    public required string StopCondition { get; init; }

    [JsonPropertyName("safety_rules")]
    public required IReadOnlyList<string> SafetyRules { get; init; }

    [JsonPropertyName("fallback_timer_note")]
    public required string FallbackTimerNote { get; init; }
}

internal sealed record OrchestratorDesignReceiver
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("optional")]
    public required bool Optional { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<string> Roles { get; init; }

    [JsonPropertyName("setup")]
    public required IReadOnlyList<string> Setup { get; init; }

    [JsonPropertyName("manual_inbox_trigger_prompt")]
    public required string ManualInboxTriggerPrompt { get; init; }

    [JsonPropertyName("pre_start_note")]
    public required string PreStartNote { get; init; }
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

    [JsonPropertyName("message_fields")]
    public required IReadOnlyList<string> MessageFields { get; init; }
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

internal sealed record OrchestratorPreflight
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<string> Checks { get; init; }
}

internal sealed record OrchestratorTroubleshooting
{
    [JsonPropertyName("symptom")]
    public required string Symptom { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }
}

internal sealed record OrchestratorMonitorDistinction
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("delivery_mode_note")]
    public required string DeliveryModeNote { get; init; }

    [JsonPropertyName("success_markers")]
    public required IReadOnlyList<string> SuccessMarkers { get; init; }

    [JsonPropertyName("failure_markers")]
    public required IReadOnlyList<string> FailureMarkers { get; init; }

    [JsonPropertyName("trust_repair")]
    public required IReadOnlyList<string> TrustRepair { get; init; }

    [JsonPropertyName("windows_guidance")]
    public required IReadOnlyList<string> WindowsGuidance { get; init; }

    [JsonPropertyName("fallback_ladder")]
    public required IReadOnlyList<string> FallbackLadder { get; init; }

    [JsonPropertyName("project_settings_diagnosis")]
    public required IReadOnlyList<string> ProjectSettingsDiagnosis { get; init; }
}

internal sealed record OrchestratorReceiverReadiness
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("startup_order")]
    public required IReadOnlyList<string> StartupOrder { get; init; }

    [JsonPropertyName("send_before_ready_warning")]
    public required string SendBeforeReadyWarning { get; init; }

    [JsonPropertyName("recovery_message_template")]
    public required string RecoveryMessageTemplate { get; init; }

    [JsonPropertyName("states")]
    public required IReadOnlyList<OrchestratorReadinessState> States { get; init; }

    [JsonPropertyName("ping_ack_required")]
    public required string PingAckRequired { get; init; }

    [JsonPropertyName("not_ready_recovery")]
    public required IReadOnlyList<string> NotReadyRecovery { get; init; }

    [JsonPropertyName("watch_note")]
    public required string WatchNote { get; init; }

    [JsonPropertyName("codex_desktop_note")]
    public required string CodexDesktopNote { get; init; }

    [JsonPropertyName("diagnostic_commands")]
    public required IReadOnlyList<string> DiagnosticCommands { get; init; }
}

internal sealed record OrchestratorReadinessState
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
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

    [JsonPropertyName("review_completed_example")]
    public required string ReviewCompletedExample { get; init; }

    [JsonPropertyName("review_incomplete_rule")]
    public required string ReviewIncompleteRule { get; init; }
}
