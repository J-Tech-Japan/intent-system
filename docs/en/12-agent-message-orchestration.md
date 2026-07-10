# Agent-message orchestration (single-domain vs multi-domain)

← [Review standing policy](11-review-standing-policy.md) | [docs index](README.md)

This page describes the **optional** agmsg-backed orchestrator thread and, in
particular, how it stays safe when a single host repository holds **several
intent domains**. The authoritative, paste-ready prompts come from installed
intent-cli guidance — do not copy prompts from this page by hand. Generate the
current prompts with:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --mode single-domain|multi-domain --format markdown
```

## Starting orchestrator mode (design-thread setup)

A design thread that wants to run orchestration can ask intent-cli directly —
`intent-cli guide workflow suggest --goal "I want to start agmsg orchestrator
mode"` (and natural-language variants like `orchestrator を使いたい` /
`新しい intent-cli オーケストレーションを使ってみたい`) routes to the orchestrator
setup guidance.

`guide orchestrator-thread` renders a **setup intake** first, whose visible
outcome is one of `missing-inputs`, `setup-ready`, or `blocked`:

- **missing-inputs** — supply only the missing fields among domain, target repo,
  orchestrator/implementation/review folder, orchestrator/implementer/reviewer
  agent, agmsg team name, delivery mode, and existing-loop stop policy.
- **setup-ready** — the intake emits copy-paste agmsg `join.sh` / `delivery.sh`
  commands and first prompts for all three roles, plus the first validation
  (existing-loop conflict check, read-only first wake, ping/inbox test).
- **blocked** — an existing implementation/review timer loop for this domain/repo
  would race the orchestrator; stop it (or pass `--existing-loop-policy
  will-stop`) before starting. Receivers are never scheduled; when an explicit
  fallback/legacy timer is used (message-driven wakes are the default), the
  orchestrator is the only thread ever scheduled.

```bash
intent-cli guide orchestrator-thread --domain <d> --target-repo <owner/repo> \
  --orchestrator-path <o> --implementation-path <i> --review-path <r> \
  --orchestrator-agent <a> --implementer-agent <a> --reviewer-agent <a> \
  --team <team> --delivery-mode <mode> --existing-loop-policy none --format markdown
```

The full reference checklist follows the intake:

1. **Decide / record** — domain and target repo; host / orchestrator /
   implementation / review paths (each role runs from its own folder, clone, or
   worktree); base branch policy; per-role agents; agmsg team name; delivery
   mode.
2. **Register roles** — register orchestrator, implementation, and review under
   one agmsg team (`join.sh`).
3. **Set delivery** — give each role a way to receive messages, e.g. a streamed
   inbox watch (`delivery.sh` / `watch.sh`).
4. **Paste role prompts** — copy the orchestrator / implementation / review
   prompts from `guide orchestrator-thread` into the matching threads.
5. **First read-only wake** — run one confirm-only orchestrator wake; send
   nothing.
6. **Ping test** — send one agmsg message and confirm it lands in the target
   role's inbox before any real delegation.
7. **Message-driven steady state by default** — implementation/review replies
   over agmsg wake the orchestrator, so routine fast polling is not required;
   receivers stay loopless. Only schedule an orchestrator timer (Codex
   automation 5m or Claude `/loop 5m`) as an explicit fallback/legacy option
   (see [Design-side watchdog](#design-side-watchdog-optional-safety-net) for
   the recommended low-frequency safety net instead).
8. **Cleanup** — on teardown, leave/despawn the roles through the agmsg scripts
   (`leave.sh` / `despawn.sh`) and stop any inbox watchers.

> **Warning:** never edit the agmsg database or team files directly — register,
> message, and clean up only through the agmsg scripts. Hand-editing agmsg state
> corrupts delivery.

## Role boundary — design authors, orchestrator coordinates

**Design creates packets; the orchestrator moves ready packets through the
workflow.** The orchestrator must **not** silently become the product/release/
design author.

- **Design owns** — intent shaping and clarifications; ADRs and design
  decisions; release scope and version selection; packet content and acceptance
  criteria (the durable packet files).
- **Orchestrator owns** — inspect canonical intent-cli/GitHub state; publish
  exactly **one already-authored, `issue-cut-ready` packet per wake**; delegate
  implementation/review; wait for CI/review; close out approved PRs through the
  canonical review surfaces; report blockers and missing packets back to design.

When a needed packet is absent, incomplete, or would require product/release/
design judgment, the orchestrator does **not** invent it — it sends a structured
`packet-needed` message to design and **waits** for design to author/update the
packet (or give an explicit instruction):

```json
{"to":"design","type":"packet-needed","domain":"<domain>","need":"<what is needed>","reason":"<why the orchestrator cannot proceed>","blocking":"<the work that is waiting>"}
```

**Release-prep is design-owned:** design decides the release version and scope
and authors the release-prep packet; the orchestrator may publish and coordinate
it only **after** it exists and is `issue-cut-ready` — it must not pick the
version, decide scope, or author the release notes/packet from a vague "prepare a
release" instruction.

## What agmsg is (and is not)

agmsg is a **message / progress / completion / blocker signal layer only**. It
carries natural-language delegation and reply signals between threads.

`intent-cli` and GitHub remain **authoritative** for queue-state, issue/PR
facts, labels, CI, review, closeout, and recovery. A signal is never workflow
state: the orchestrator re-verifies every claim against intent-cli / GitHub
before acting on it. intent-cli never launches Claude/Codex or any AI provider.

## Two driver modes (pick one per domain/repo)

| Mode | Driver | Notes |
|---|---|---|
| **timer-loop mode** | recurring timers | Existing, fully supported. Implementation/review threads self-schedule and read `worker next-action` / host review-next-slice. No orchestrator required. |
| **orchestrator-message mode** | a fourth orchestrator thread | Opt-in. The orchestrator paces the implementation/review threads over agmsg; at steady state this is message-driven, with an optional low-frequency design-side watchdog as the safety net. An explicit orchestrator timer remains supported as a fallback/legacy option. |

Do **not** run both modes for the same domain/repo. In orchestrator-message
mode, do not also launch the implementation/review recurring timer loops — two
drivers would race on the same GitHub state.

## Scheduled orchestrator cadence

In orchestrator-message mode the normal steady state is **message-driven**:
implementation/review receivers already send accepted/progress/completed/
blocked replies to the orchestrator, and those replies wake the orchestrator
path — routine fast polling is **not** required. An orchestrator timer remains
**supported** but only as an explicit **fallback/legacy** polling option for an
operator who intentionally wants scheduled polling instead of message-driven
wakes. Either way, the implementation and review threads are long-lived but
**loopless receivers** — they act only when the orchestrator delegates and
never start their own recurring timer for the same domain/repo. The
recommended safety net for the message-driven steady state is a low-frequency
[design-side watchdog](#design-side-watchdog-optional-safety-net), not a fast
orchestrator loop.

If an explicit fallback/legacy timer is used, schedule the orchestrator one of
two ways:

- **Codex automation (every 5m, optional)** — run one orchestrator wake per
  fire: check design progress and replies, ask intent-cli for state, verify
  the GitHub facts, then send at most one message and exit.
- **Claude same-thread `/loop 5m` (optional)** — in the orchestrator thread run
  `/loop 5m` so the same thread re-wakes every 5 minutes for one pass each.

Do **not** also run `/loop` or a Codex automation in the implementation or
review threads — those are loopless receivers regardless of whether the
orchestrator runs message-driven or on a fallback/legacy timer.

### Each orchestrator wake

Generate the authoritative wake prompt from intent-cli. A wake is triggered
either by an incoming agmsg reply from implementation/review (the
message-driven steady state) or by the optional fallback/legacy timer firing —
either trigger runs exactly one pass:

- Check design-side progress (new packets/issues, intent status changes).
- Read pending agmsg replies (signals only — re-verify against intent-cli /
  GitHub).
- Ask intent-cli for worker state (`worker next-action --github-only`).
- Check host review readiness (`automation host-review-preflight`).
- Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge
  state, closeout/label state.
- Detect stale blockers and no-reply receivers.
- Decide the single action this wake: delegate the next slice/PR, send one
  repair message, or escalate one operator decision.

### Repair vs escalate

- **Repair** routine off-rail states yourself by messaging the appropriate
  thread back onto the official intent-cli workflow — a receiver that stalled,
  skipped `worker complete`, applied a label by hand, or has not replied.
  Routine recovery is a repair message, not an escalation.
- **Escalate** to the operator only for: product/design judgment, credentials
  or security, a destructive local action, or an unresolved canonical ambiguity
  (intent-cli/GitHub facts genuinely conflict or are missing).

### CI wait state

A PR with pending/running CI is an **active wait state**, not a blocker.
GitHub checks are authoritative. Re-check the required checks on each wake;
pending CI by itself never triggers a request-update label, a repair message,
or an operator question. Always re-verify required checks immediately before
delegating review, merge, or closeout — an earlier green read can go stale.

- **pending / running** — wait and re-check next wake. No message, no
  request-update, no operator question; track the PR as in-flight and move on.
- **green** — all required checks passed. Delegate review/closeout through
  intent-cli review surfaces; re-verify green at delegation time.
- **red** — a required check failed. Route by ownership: a test/build/lint
  failure the implementation thread can fix gets one repair message; anything
  needing product/design or canonical judgment escalates. Never delegate
  merge/closeout while a required check is red.
- **stuck / ambiguous** — checks never started, hung well past a reasonable
  window, or report conflicting/unknown status. Escalate one operator decision
  (fail closed); do not guess green.

## Next-slice publication

Routine next-slice issue publication is an **orchestrator responsibility**, not
an operator question. When intent-cli reports a candidate as `issue-cut-ready`
and all safety gates pass, the orchestrator publishes it itself through
canonical intent-cli commands rather than stopping to ask the operator to create
the GitHub issue. **At most one issue per wake.**

Publish only when **all** of these hold:

- same-domain context, or an explicitly routed multi-domain delegation (never
  publish a cross-domain candidate without explicit routing);
- the packet contract is complete (no missing required sections);
- no open clarification or contract ambiguity;
- dependencies are satisfied — never publish ahead of an uncut dependency;
- under the WIP cap;
- clean host-sync / preflight and an unambiguous target repo/domain.

Otherwise **hold or escalate** — missing contract sections, open clarification,
dependency mismatch, WIP cap reached, host-sync blocker, or ambiguous target
repo/domain are all blockers.

Publish through the canonical surfaces only — `intent-cli issue publish-flow`
and `intent-cli automation issue-publish` — never raw `gh issue create` or
`gh ... --add-label`. After publishing, verify via intent-cli / GitHub (not
chat) that the issue exists with the expected body and the `intent-target`
label and that durable state reflects it, then delegate implementation over
agmsg. The implementation receiver still derives its target from
`intent-cli worker next-action`, not the agmsg text.

## Dependency planning

Unmet dependencies are **normal orchestration work** when explicit and
resolvable — not an operator stop. When the next candidate depends on work that
is not yet complete, the orchestrator does **not** pause for operator judgment;
it plans the chain deterministically: hold the dependent candidate and route
this wake's action to the **earliest unmet** same-domain (or explicitly routed)
dependency.

Routing by dependency status:

- **dependency-publish-ready** — the earliest unmet dependency is
  `issue-cut-ready` with no GitHub issue → publish it this wake (one issue per
  wake, under the next-slice publication gates); keep the dependent held.
- **dependency-actionable** — the dependency already has an issue or PR that can
  move → route it (implementation, review, closeout, or repair) using
  intent-cli / GitHub facts.
- **dependency-waiting** — the dependency is in flight (e.g. PR CI pending) →
  wait and re-check next wake; keep the dependent held.
- **dependency-ambiguous** — cannot be resolved deterministically (missing
  dependency packet, conflicting GitHub linkage, cross-domain with no route
  mapping) → escalate one operator decision.
- **dependency-cycle** — the dependencies form a cycle → escalate (fail closed).

The dependent candidate stays held until every dependency is completed/cut.
**Escalate only** for: a missing dependency packet, a dependency cycle, a
cross-domain dependency without route mapping, conflicting GitHub linkage,
destructive recovery, credentials/security, or a human product/design judgment.

## Stale-thread health check

Receivers are loopless, so **silence is ambiguous** — a receiver may be working,
waiting for CI, waiting for a permission prompt, blocked, completed without a
reply, or truly stale. When a receiver has had no reply past the threshold
(default **30 minutes**, configurable), the orchestrator runs a **safe**
liveness check: ask before acting, verify authoritative facts, and never
auto-cancel work, auto-clear a permission prompt, or duplicate a task.

Procedure:

1. Send **one non-destructive status-request** — ask, do not retry/cancel/reset.
2. Check read-only intent-cli / GitHub facts (`worker next-action`, issue/PR
   state, CI, labels).
3. If facts show progress (new commits, PR updated, CI running), **keep
   watching** — do not re-send the work.
4. If the receiver replies `waiting-permission`, it is an **operator notice** —
   surface it; never auto-clear the prompt.
5. Only after repeated no-reply **and** no progress, send **at most one
   idempotent re-entry** referencing the same issue/PR.
6. Escalate after repeated silence with no progress, or any unsafe case
   (cancel/reset, destructive git, credentials).

The status-request asks the receiver to reply with one of: `working`,
`waiting-ci`, `waiting-permission`, `blocked`, `completed`, `idle`. A health
check never clears a permission prompt, cancels/resets work, mutates labels, or
runs destructive git. (Timer-loop mode is unaffected — this applies only to
orchestrator-message receivers.)

## Design-thread escalation filter

The **design thread** is the primary human communication surface. Humans mainly
talk to the design thread; implementation and review run through the
orchestrator, and only **human-needed** decisions return to the design thread.
This is a **noise filter, not a failure filter** — never hide a failure that
needs a human.

Kept internal by default (no design-thread message):

- normal progress / accepted / in-flight delegations;
- CI waiting (pending checks are an active wait state);
- successful implementation (PR opened, CI green);
- successful review / approval;
- closeout of an already-approved PR;
- idle wakes with no actionable change.

Escalate to the design thread only when:

- clarification is required (ambiguous issue/packet contract);
- product intent ambiguity or a design decision;
- permission / credentials / security;
- a destructive action would be required;
- repeated no-reply / no-progress after the safe stale-thread health check;
- unresolved canonical state (intent-cli / GitHub facts conflict or are missing);
- a release / public publish decision;
- an explicit policy decision the operator owns.

A design escalation carries a concise reason, the **current authoritative
state** read from intent-cli/GitHub, the supporting evidence, options only when
useful, and the exact decision needed — so the human can decide without
re-deriving the state:

```json
{"to":"design","type":"escalation","ref":"issue#<n>|pr#<n>","reason":"<clarification|product-ambiguity|permission|destructive|no-progress|canonical-conflict|release|policy>","current_state":"<the current AUTHORITATIVE state read from intent-cli/GitHub: labels, PR/CI/review/merge state, queue position>","evidence":"<the intent-cli/GitHub facts that establish that state>","options":"<OPTIONAL: candidate choices, only when useful>","decision_needed":"<the exact decision or action requested from the human>"}
```

- `reason` — which human-needed category triggered the escalation.
- `current_state` — the current **authoritative** state, read from intent-cli /
  GitHub (labels, PR/CI/review/merge state, queue position). **Required** — the
  receiver must not have to re-derive it; generic evidence wording does not
  substitute for the explicit state.
- `evidence` — the intent-cli / GitHub facts that establish the current state.
- `options` — **optional** candidate choices, included only when they help.
- `decision_needed` — the exact decision or action requested from the human.

## Managed worktree cleanup

Orchestrated work creates temporary worktrees for implementation and review.
Allocate them under a **managed, allowlisted root** inside the workspace and
clean them up with `git worktree remove` — **never** a raw `rm -rf` of an
arbitrary `/tmp/intent-review-...` path. Safe cleanup design, not disabling
approvals, is the right default: a destructive `rm -rf` approval prompt is the
symptom of an unmanaged workspace.

- **Managed root** — allocate under the `[project] worktree_root` (default
  `.intent-cli/worktrees/`, git-ignored), not arbitrary `/tmp` paths. Create
  each with `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`,
  one per role/unit.
- **Safe cleanup** — remove only with `git worktree remove` (it refuses a dirty
  worktree); validate the target is inside the allowlisted root, is a registered
  git worktree (`git worktree list`), and is clean; then `git worktree prune`.
- **Refuse cleanup** when the target is outside the allowlisted root, is the
  repo root / `$HOME` / a system path, is not a registered worktree, or has
  uncommitted/untracked user work — stop and surface it; never delete user work.
- **Approval policy** — `approval_policy=never` / `danger-full-access` is **not**
  a substitute for safe cleanup design. Keep least-privilege approvals as the
  default; the goal is to never need a destructive `rm -rf` prompt, not to
  suppress it.

## Review delegation — managed worktrees and design alignment

Review delegation must carry the managed-worktree policy and require
design-alignment evidence **up front** — not leave the reviewer to discover
it. Dogfooding showed a reviewer allocate a raw `/tmp/...review...` worktree
and Codex correctly ask to approve a destructive `rm -rf` — the **right**
safety behavior for the **wrong** workflow. The fix is a managed root, **not**
weakening approval settings.

- **Managed worktree root** — review worktrees use the **same** managed,
  workspace-local root as the rest of orchestrated work — the `[project]
  worktree_root` (default `.intent-cli/worktrees/`), e.g.
  `.intent-cli/worktrees/review-<unit>` — **never** an arbitrary
  `/tmp/...review...` path.
- **Prohibited pattern** — a raw `/tmp/...` review worktree, and a
  `rm -rf /tmp/... && git worktree add ...` cleanup chain, are **prohibited**
  as the normal path. Reaching for this pattern is the signal to stop and
  allocate under the managed root instead — not to ask the operator to
  approve the `rm -rf`.
- **Cleanup rule** — cleanup is `git worktree remove <managed-path>` for a
  **registered, clean** worktree only (confirm via `git worktree list` and a
  clean `git status` first).
- **Unsafe/stale path rule** — a stale path that is not a registered git
  worktree, is outside the managed root, or is dirty/unsafe is **never** an
  operator `rm -rf` approval prompt — it is a **structured blocker** agmsg
  reply to the orchestrator (`status: blocked`) so the orchestrator can route
  the repair, not something the reviewer resolves by force-deleting an
  unmanaged path.

Review delegation example (orchestrator → review):

```json
{"delegate":{"domain":"<domain>","execution_unit":"<unit>","target_repo":"<owner/repo>","pr":"<n>","review_cwd":"/review/<domain>","managed_worktree_policy":"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never /tmp","design_alignment_required":true,"destination_thread":"review@<domain>"}}
```

A review `completed` reply must include design-alignment evidence:

```json
{"status":"completed","thread":"review","ref":"pr#<n>","note":"approved; closeout done","design_alignment_checked":true,"design_alignment_sources_checked":["packet","review-context","intent-tree","adr-decision-notes","relevant-docs"],"managed_worktree_policy":"compliant — .intent-cli/worktrees/review-<unit>, removed after review"}
```

Design-alignment sources a review reply may cite as checked: the **packet**
(content and acceptance criteria), the **review-context** artifact for the
PR/unit, the relevant **intent tree** entries, any linked **ADR / decision
notes**, and **relevant docs** the change touches.

**Review-incomplete rule:** a review `completed` reply that omits
`design_alignment_checked: true` and the checked-source list is
**incomplete** — the orchestrator does not route merge/closeout on that reply
alone. The only exception is when an authoritative **prior** approval state
already proves equivalent design-alignment review (the orchestrator must
point to that specific prior evidence, not assume equivalence).

## Receiver readiness

Monitor configuration is **not enough**. A registered team plus a configured
delivery mode does **not** mean a receiver will see your message — a newly
launched or restarted session may not pick up messages sent before its
monitor/watch path was active. Confirm each receiver is **ready** with a
ping/ack before sending real work.

### Startup order

Follow this order strictly — a send is not a delivery:

1. Join the three roles to the team (`join.sh`).
2. Set the delivery mode for each role (`delivery.sh set`).
3. Launch or restart the receiver CLI sessions (implementation, review, and the
   orchestrator).
4. Wait for the monitor/bridge to attach in each receiver session before
   sending anything.
5. Send a ping to each receiver only **after** its session is active.
6. Require an ack — or confirm receipt manually with `inbox.sh` — before
   proceeding.
7. Only then send the first real delegation.

> **Send-before-ready:** messages sent before a receiver is ready may be stored
> in agmsg history but **not** visibly delivered to a freshly launched/restarted
> session. An unacked message is **receiver-not-ready**, not a successful
> delegation. Recover by resending after the ack, or have the receiver read its
> queue with `inbox.sh`.

Copy-paste operator message when receivers were launched **after** the initial
messages were sent:

```text
Heads up: your session started AFTER I sent earlier messages, so they may be in agmsg history but not visibly delivered to you. Read your queue now with `inbox.sh` to catch anything you missed. Any prior unacked message is receiver-not-ready (NOT a delegation you must act on) — reply `ack` to this ping and I will (re)send the current delegation.
```

Readiness states:

- **registered** — the role joined the team (it appears in `team.sh`).
- **delivery-configured** — the delivery mode is set (`delivery.sh status`).
- **watcher-alive** — the monitor/watch process is running for the role.
- **receiver-session-active** — a launched/restarted receiver session is
  actually attached to the monitor path (a session started before delivery was
  active may not receive earlier messages).
- **ping-acknowledged** — the receiver replied to a ping; the only end-to-end
  proof the channel works.

**Ping/ack is required** for the orchestrator, implementer, and reviewer before
any real delegation, and must be re-done after any launch/restart. A missing ack
is **not-ready** — do not send real work. If a receiver was not ready, messages
sent earlier may have been missed: resend after the ack, or read what is queued
with `inbox.sh`; re-confirm `team.sh` and `delivery.sh status` first.

Boundaries:

- **`watch.sh`** streams a role's inbox live but **occupies a terminal** — it is
  a debug/fallback option, not the default setup requirement. The normal path is
  the monitor delivery hook.
- **Codex Desktop app threads are not agmsg monitor receivers by default** — a
  different execution surface from a CLI session. Use a CLI session as the
  receiver (or read with `inbox.sh`).

Diagnose with agmsg scripts only: `team.sh` (registration), `delivery.sh status`
(delivery), `inbox.sh` (queued messages), `send.sh` (ping → ack).

## Design / human receiver (optional)

When human-needed escalations should be deliverable over agmsg, add a **fourth
logical role**: a **design / human receiver**. Routine progress stays internal
to orchestrator / implementation / review; only human-needed decisions go to the
design thread (see the design-thread escalation filter). The design receiver is
**optional** for routine operation but **recommended** so escalations reach the
human reliably, and it may receive manually by checking its inbox.

Four logical roles when design receiving is enabled:

- **orchestrator** — paces the other roles over agmsg; message-driven by
  default, with an explicit timer only as a fallback/legacy option.
- **implementation receiver** — loopless; acts on delegations only.
- **review receiver** — loopless; acts on delegations only.
- **design / human receiver** — optional; receives only human-needed escalations
  and is also loopless (the human reads on demand, e.g. via `inbox.sh`).

Setup:

- Register the design role in the **same** agmsg team —
  `agmsg join.sh <team> design <agent> <design-folder>` — or simply address
  escalation messages to the existing design thread.
- Optional streamed delivery: `agmsg delivery.sh set <mode> <agent> <design-folder>`;
  otherwise the design thread reads on demand with `inbox.sh`.
- The design receiver needs no recurring loop — like implementation/review it is
  loopless; the human reads when prompted.

Minimal manual inbox trigger prompt (paste into the design thread):

```text
agmsg の inbox を確認してください。あなたは `<team>` の design です。 (Check your agmsg inbox — you are the `design` role of team `<team>`. Read pending escalations with `inbox.sh`; routine progress is intentionally not sent here.)
```

> **Pre-start messages:** messages sent before the design receiver's monitor
> started may be in agmsg history but not visibly delivered — the design thread
> should read its inbox with `inbox.sh` to catch earlier escalations, exactly
> like the other receivers (see Receiver readiness / startup order).

## Setup intake form

When the user asks only "I want to use orchestrator mode", elicit or infer the
setup facts, then produce the concrete commands/messages. Ask for what is
missing; apply the recommended defaults for the rest.

Ask for / infer: domain and target repo; orchestrator cwd + agent type;
implementation receiver cwd + agent type; review receiver cwd + agent type;
design cwd + agent type (and whether design is manual-inbox or monitored);
delivery mode per role.

Recommended defaults when inputs are incomplete:

- orchestrator = Claude
- implementer = Claude
- reviewer = Codex
- design = manual-inbox Codex
- runtime / implementation / review receivers = monitor (when supported)

Design may be a manual-inbox receiver (reads with `inbox.sh` on demand) or a
monitored receiver; either way it receives **only** human-decision escalations or
explicit summaries, never routine progress.

Role startup messages — assume the agmsg role, then paste that role's prompt:

- **Claude**: `/agmsg actas <role>` (slash command)
- **Codex**: `$agmsg actas <role>`

## Design handoff (start / resume)

Setup does not stop at role registration. After the agmsg roles are registered
and ready, the **design thread** starts (or resumes) orchestration by sending
**one** message to the orchestrator; the orchestrator then drives the loop
autonomously and returns to design only for human decisions.

First message — design → orchestrator (paste into the design thread):

```json
{"to":"orchestrator","type":"start","domain":"<domain>","target_repo":"<owner/repo>","requested_action":"<e.g. publish the next ready slice and drive it to a PR>","constraints":"one action per wake; escalate to design ONLY for human decisions (product/clarification, release/credentials/security, destructive actions, unresolved blockers)"}
```

- **Autonomous publish** — if `intent-cli` reports the next slice
  `issue-cut-ready` and all publish gates pass, the orchestrator creates/
  publishes **one** GitHub issue itself via canonical intent-cli commands
  (`issue publish-flow` / `automation issue-publish`) — it does **not** ask
  design to do each step. At most one issue per wake; verify before delegating.
- **Escalation boundary** — routine delegation (publish, delegate, CI wait,
  review, closeout) stays orchestrator↔receivers. Return to **design** only for
  human decisions (product/design clarification, release/credentials/security,
  destructive actions, an unresolved blocker) using the structured escalation
  message.
- **Design inbox workflow** — the design thread is a loopless receiver and reads
  on demand; check the design inbox with `inbox.sh` to pick up escalations,
  especially when monitor delivery did not appear live or the design session
  started after the orchestrator sent.

## Design-side watchdog (optional safety net)

In the message-driven steady state, implementation/review replies already wake
the orchestrator, so a fast orchestrator loop is redundant. The recommended
safety net instead is an **optional**, **low-frequency** watchdog run from the
**design** thread: it checks whether HITL (human-in-the-loop) messages arrived
and whether the orchestrator looks stalled, then sends **at most one**
canonical repair/status request — it never drives routine orchestration
itself.

- **Frequency** — low only (e.g. tens of minutes to hours, not every 5m); a
  fast watchdog loop recreates the same churn the message-driven model
  removes.
- **Checks** — the design/HITL inbox for unread human-facing escalations
  (`inbox.sh` on the design role), and orchestrator staleness via read-only
  intent-cli/GitHub facts (`worker next-action --github-only`, open PR/CI/
  label state) compared against the last known orchestrator activity.
- **Action** — when staleness or an unanswered HITL message is detected, send
  at most one canonical repair/status request to the orchestrator:

  ```json
  {"type":"status-request","to":"orchestrator","from":"design-watchdog","ask":"non-destructive liveness check: reply with current state and next action, or confirm idle"}
  ```

- **Stop condition** — stop or archive the watchdog once both the backlog and
  the human-decision (HITL) queues are drained.

Watchdog safety rules **prohibit**:

- duplicate delegation — the watchdog never re-sends or re-creates a
  delegation itself; only the orchestrator delegates;
- clearing a permission prompt — `waiting-permission` stays an operator
  notice; the watchdog never auto-clears it;
- cancelling or resetting in-flight work;
- force-closing an issue/PR or any other terminal action;
- speculative durable-state surgery — no hand-editing labels, queue-state, or
  any host metadata.

An explicit orchestrator timer (Codex automation every 5m, or Claude
same-thread `/loop 5m`) remains supported as fallback/legacy polling when an
operator intentionally wants scheduled polling instead of the message-driven
steady state — the design-side watchdog and the orchestrator fallback timer
are alternative safety nets, not both required together.

## Monitor recovery

- **Monitor did not start** — restart the receiver session so the monitor/watch
  hook attaches on a fresh turn; verify with `delivery.sh status` and a
  ping/ack. Until then, read with `inbox.sh`.
- **Message not visible** — it may be queued but not delivered live; read the
  role's queue with `inbox.sh`, re-confirm `team.sh` / `delivery.sh status`, then
  resend after an ack.
- **Receiver started after the message was sent** — earlier messages are in
  history but not delivered live; read them with `inbox.sh`, or resend after the
  receiver acks.
- **Orchestrator idle despite a packet existing** — confirm the orchestrator
  received the design start/resume message (`inbox.sh`) and that `worker
  next-action` / `intent status` report an actionable item for **this**
  domain/repo (not another visible domain). If issue-cut-ready and safe, the
  orchestrator should publish one issue itself rather than wait.
- **`mode=monitor` but no live stream** — `delivery.sh status` `mode=monitor` is
  configuration only, not proof a Claude Code `Monitor` is attached. Verify the
  live-attachment success markers (`1 monitor` / `Monitor event`), check Windows
  Git Bash startup, and work the bounded fallback ladder (restart → verify trust
  → Git Bash on Windows → compare known-good → `turn`/manual `inbox.sh` or
  escalate). Full checklist:
  [Orchestrator-message mode — Monitor tool vs delivery-mode](orchestrator-message-mode.md).
- **`ToolSearch select:Monitor` finds no Monitor tool at all** — this is a Claude
  Code tool-surface problem *before* it is an agmsg problem, regardless of
  `mode=monitor`. Compare `.claude/settings.json` / `.claude/settings.local.json`
  / `~/.claude.json` against a known-good folder and remove suspect project-level
  `env` overrides (e.g. `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true`),
  preserving agmsg hooks, then restart and re-verify. See
  [Missing-Monitor project-settings diagnosis](orchestrator-message-mode.md#missing-monitor-project-settings-diagnosis).

## Codex monitor (beta) failure modes

The agmsg Codex monitor (beta) is a different delivery backend from the Claude
Code `Monitor` tool above — it bridges agmsg into a Codex CLI session instead.
intent-cli does not own or modify agmsg internals; this section only covers
what an operator needs to set up a Codex receiver and recognize/recover from
the two field-verified failure modes. See the
[agmsg codex-monitor-beta doc](https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md)
for implementation internals.

> Observed at agmsg 1.1.6 / Codex v0.144.1 (macOS, `codex()` shim launch) — the
> setup preflight, healthy-state markers, and troubleshooting entries below
> are observations from that tested environment, not a permanent bridge
> contract. Re-verify against the installed agmsg/Codex versions after an
> upgrade before trusting the exact mechanics (e.g. retry interval, thread
> attachment order) described here.

**Setup preflight** — before launching a Codex receiver, verify the (project,
codex) pair resolves to exactly **one** identity: `whoami.sh <project> codex`
should print a single `agent=` line. Clean up any stale registration first
(e.g. a leftover `actas` registering another role into the project) — more
than one identity blocks the bridge launcher silently.

**Healthy-state markers:**

- `delivery.sh status` shows `Codex bridge: <team>/<role> alive (pid N)`.
- The bridge arms on the **first turn** sent to the session, not at Codex
  startup — do not expect delivery before that first turn.
- An already-running Codex session stays unmonitored until it is restarted
  after the bridge is enabled.

**Troubleshooting:**

- **`mode: monitor` but the Codex bridge never starts** — the (project, codex)
  pair resolves to more than one identity; `codex-bridge-launcher.sh` proceeds
  only when there is exactly one, and otherwise retries silently every 0.3s
  forever (e.g. a stale `actas` registration for another role). Check the
  identity count with `whoami.sh <project> codex`, remove the stale
  registration, then relaunch.
- **Bridge alive (pid shown) but the Codex TUI never moves / never reacts to
  messages** — the shared Codex app-server accumulates loaded threads across
  sessions, and `codex-bridge.js` attaches to the FIRST (oldest) entry of
  `thread/loaded/list` — turns are injected into an old background thread
  while the visible TUI never reacts. Recover by: quit the TUI, stop the
  app-server/bridge/launcher processes, remove the recorded app-server/bridge
  state files (`codex-app-server.*.{pid,port,version}` and the bridge
  `{pid,appserver,meta}` files), relaunch codex, then send one turn to
  re-arm.
- **Responses to one message appear twice across a restart window** —
  suspect a doubled bridge; verify only one bridge pid exists
  (`delivery.sh status`) before relaunching.

## Design traffic-controller playbook

The design thread acts as a **traffic controller**, not an implementer. It
coordinates through the orchestrator and only surfaces human-needed items.

1. Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.
2. Check intent-cli / GitHub **read-only** state (`intent status`, `worker
   next-action`, PR/issue/labels) to ground any decision — never trust an agmsg
   message as state.
3. Send the orchestrator a state update or a nudge (start/resume); do not drive
   implementation/review yourself.
4. Do **not** directly mutate implementation/review work, labels, or host
   metadata — that is the orchestrator/receivers' job through intent-cli.
5. Summarize **only** human-needed items to the human; keep routine progress
   internal.

**"Orchestrator appears idle" diagnostic** (before escalating): confirm the
orchestrator is scheduled and on a fresh turn; confirm it received your last
message (`inbox.sh`) — a pre-monitor send may be queued, not live, so resend
after an ack; confirm intent-cli actually reports an actionable item for **this**
domain/repo (idle may be correct); only then escalate to the human.

> **Context-only:** the design thread may send context to a receiver thread but
> must mark it `context-only: <text>` unless the orchestrator delegated the
> action — receivers act only on orchestrator delegations, not on design context.

## Preflight (all three cwds)

Before mutating anything, preflight **all three checkouts** (orchestrator,
implementation, review cwds). A receiver acting in the wrong repo, on the wrong
branch, or over dirty user work is the most common orchestration failure.

- For each cwd, confirm `git status` is clean — no uncommitted/untracked work a
  checkout/branch switch would clobber.
- Confirm each cwd's git remote is the **expected repo** for its role (the
  implementation/review receivers must point at the delegated target repo).
- Confirm each cwd is on the **expected branch/base** — a receiver on a stale
  branch implements against the wrong base.
- If a checkout exposes multiple domains, the orchestrator must **filter by the
  requested domain/target repo** before publishing or delegating (visibility is
  not authorization).
- **Existing-loop conflict check** — no timer-loop may be running for this
  domain/repo; orchestrator-message mode and timer-loop mode must not run
  together for the same route.

## Troubleshooting

- **Message not received by a receiver** — confirm registration (`team.sh`) and
  delivery (`delivery.sh status`); the receiver may have missed it (monitor not
  yet active) — read its queue with `inbox.sh`, or resend after a ping/ack.
- **Monitor/delivery configured after the session started** — a session started
  before its monitor/watch path was active will not pick up earlier messages
  live; restart the receiver session (or read with `inbox.sh`), then re-confirm
  with a ping/ack before delegating.
- **Codex Desktop app thread is the receiver** — Codex Desktop app threads are
  not agmsg monitor receivers by default; they receive manually only. Use a CLI
  session, or have the Desktop thread read with `inbox.sh`.
- **Receiver cwd sees a different repo/domain than delegated** — stop; do not
  claim. The receiver's cwd/worktree, git remote, and delegated domain must
  match the routing; reply blocked and re-route. An execution-unit prefix
  mismatch alone is not the signal — compare packet/domain metadata.
- **Codex asks to approve `rm -rf /tmp/...review...`** — this is the **right**
  safety behavior for the **wrong** workflow: the review worktree was
  allocated at an unmanaged `/tmp` path instead of the managed root. The fix
  is the managed root (`.intent-cli/worktrees/review-<unit>`), **not**
  weakening approval settings — re-allocate under the managed root (see
  [Review delegation](#review-delegation--managed-worktrees-and-design-alignment)),
  and do not approve the `rm -rf` for the stale `/tmp` path; reply blocked to
  the orchestrator instead so it can route the cleanup as a repair.

## Draft PR reviewability

A **draft PR may still be reviewable** depending on domain guidance — a reviewer
may perform review feedback on a draft when the domain's review policy allows it.
But the reviewer must use the **canonical intent-cli review surfaces**
(`review closeout-plan`, `guide review`, `automation pr-transition`, `closeout
pr`); merge/approval stays gated by those surfaces. A draft is never
approved/merged by hand or by raw label edits, and never via host-metadata
editing.

## Single-domain vs multi-domain orchestration

A host checkout can legitimately contain **several** intent domains (for
example `sekiban-as-a-service`, `sekiban-wasm-runtime`, and `intent-cli`), and
**more than one domain may target the same GitHub repository**. Visibility is
not authorization. The orchestrator therefore operates in one of two modes.

### Single-domain orchestrator

- Only the selected domain is in scope.
- Other-domain queue items that are **visible** in the same host repo are
  **out of scope** — do not publish, delegate, or repair them, even when they
  target the same repository.
- Escalate to the operator to switch domain/mode instead of treating a visible
  other-domain item as delegable.

### Multi-domain orchestrator

- Intentionally coordinates several domains.
- Requires **explicit routing metadata for each delegation** before
  publishing, delegating, reviewing, or repairing.
- Routes each execution unit only to the thread that owns that domain's
  checkout.

Every multi-domain delegation must carry:

- domain
- execution unit
- target repo
- implementation cwd/worktree
- review cwd/worktree
- base branch policy
- destination thread

Example delegation payload (note one repo serving two domains):

```json
{"delegate":{"domain":"sekiban-as-a-service","execution_unit":"G491","target_repo":"J-Tech-Japan/intent-system","impl_cwd":"/work/sekiban-saas","review_cwd":"/review/sekiban-saas","base_branch_policy":"direct-main","destination_thread":"implementation@sekiban-as-a-service"}}
```

### Execution-unit prefix is not a routing signal

An execution-unit ID prefix that differs from the domain name (e.g. a `G###`
unit whose number does not encode the domain) is **not by itself** a wrong-repo
signal. Compare the **packet/domain metadata** and the **routing context** to
decide ownership — never the prefix string alone.

## Implementation thread: verify your checkout before claiming

The implementation thread is driven by orchestrator delegations, but the
worker target still comes from receiver-side
`intent-cli worker next-action --repo <owner/repo> --github-only` — **not** from
the agmsg text. Before claiming:

1. Verify your local checkout context — cwd/worktree, git remote repo, and the
   delegated domain — matches the routing you were handed.
2. If the checkout does not match the delegated repo/domain, **stop and reply
   blocked** instead of claiming.
3. Remember that a prefix mismatch alone is not a wrong-repo signal; confirm
   ownership via packet/domain metadata and the routing context.

Implementation threads stay **GitHub-contract-only**: they do not read or
mutate host metadata (`.intent-cli/**`, `intents/**`). All label transitions go
through `intent-cli worker` / `intent-cli automation`.

## Safety boundaries (summary)

- agmsg is a signal layer only; intent-cli and GitHub are authoritative for all
  workflow state.
- No raw label mutation; every transition runs through intent-cli
  worker/automation.
- No hand-editing of queue-state, runs logs, packets, or host metadata.
- agmsg never replaces semantic review or authorizes a merge.
- Domain isolation: visibility is not authorization. Single-domain
  orchestrators ignore/escalate other-domain items; multi-domain orchestrators
  require explicit per-delegation routing.
- Fail closed on duplicate orchestrators or when a signal conflicts with
  intent-cli/GitHub facts — stop and escalate, never guess.
