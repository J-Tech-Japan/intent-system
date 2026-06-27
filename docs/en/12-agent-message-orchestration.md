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
