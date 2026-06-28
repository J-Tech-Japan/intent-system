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
mode"` routes to the orchestrator setup guidance, and `guide orchestrator-thread`
returns the concrete setup checklist. The shape:

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
7. **Schedule only the orchestrator** — Codex automation 5m or Claude
   `/loop 5m`; receivers stay loopless.
8. **Cleanup** — on teardown, leave/despawn the roles through the agmsg scripts
   (`leave.sh` / `despawn.sh`) and stop any inbox watchers.

> **Warning:** never edit the agmsg database or team files directly — register,
> message, and clean up only through the agmsg scripts. Hand-editing agmsg state
> corrupts delivery.

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
| **orchestrator-message mode** | a fourth orchestrator thread | Opt-in. The orchestrator paces the implementation/review threads over agmsg instead of timers. |

Do **not** run both modes for the same domain/repo. In orchestrator-message
mode, do not also launch the implementation/review recurring timer loops — two
drivers would race on the same GitHub state.

## Scheduled orchestrator cadence

In orchestrator-message mode the orchestrator thread is the **single recurring
driver**. Schedule **only** the orchestrator; the implementation and review
threads are long-lived but **loopless receivers** — they act only when the
orchestrator delegates and never start their own recurring timer for the same
domain/repo. This keeps a periodic driver (so design progress, agmsg replies,
completed CI, and approved PRs are noticed without the operator poking stalled
work) while avoiding the mixed-mode timer race.

Schedule the orchestrator one of two ways:

- **Codex automation (every 5m)** — run one orchestrator wake per fire: check
  design progress and replies, ask intent-cli for state, verify the GitHub
  facts, then send at most one message and exit.
- **Claude same-thread `/loop 5m`** — in the orchestrator thread run
  `/loop 5m` so the same thread re-wakes every 5 minutes for one pass each.

Do **not** also run `/loop` or a Codex automation in the implementation or
review threads — those are loopless receivers.

### Each orchestrator wake

Generate the authoritative wake prompt from intent-cli; each wake should:

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
