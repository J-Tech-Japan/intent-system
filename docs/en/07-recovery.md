# Recover when a loop looks wrong

> **Ask intent-cli first** — don't hand-fix state. Run `intent-cli guide start`,
> then the read-only preflight/doctor surfaces below. ← [docs index](index.md)

When a loop looks stuck, wrong, or you're unsure whether a fix is in scope, ask
`intent-cli` to classify it and tell you which command (if any) owns the repair —
instead of editing labels or metadata directly.

```bash
# Is this PR's review feedback a safe, in-scope child repair?
intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr <n> --format json

# Is this issue safe to (re)claim as issue-to-pr?
intent-cli worker issue-preflight --repo <owner>/<repo> --issue <n> --format json

# CLI freshness / host-state resolution
intent-cli automation doctor --format json
```

Read the result: `actionable` / `safe_repair_available` / `repair_category` tell
you whether a child-loop-owned repair exists. Host-owned categories surface as
`host-artifact-repair-required` and return to the host loop.

## Ask-intent-cli prompt template

> A loop looks wrong on `<owner>/<repo>` (PR/issue `<n>`). Before touching
> anything, run the matching `intent-cli worker …-preflight` and
> `intent-cli automation doctor`, and only apply a repair the CLI says is safe
> and in scope. Don't hand-edit labels or metadata.

## Repeated-stall recovery (G408)

When an automation loop selects the same target and hits the same blocker for
**two or more consecutive wakes** without making progress, it should self-recover
rather than reporting the same stop indefinitely.

**Recovery flow — re-read guidance first:**

```bash
intent-cli guide model --format json
intent-cli guide onboarding --format json
intent-cli guide commands list --format json
intent-cli automation summary --domain <domain> --format json

# Child loop: run the matching preflight for the stuck target
intent-cli worker issue-preflight     --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr    <n> --format json

# Host loop: check freshness and state
intent-cli automation doctor --format json
```

**What to do with the result:**

| Result | Action |
|--------|--------|
| `safe_repair_category: child-selector-label-gap` | Apply the one repair `intent-cli` marks safe; retry once. |
| `host-artifact-repair-required` | Stop. Report a structured operator stop. Do not hand-fix. |
| `clarification-required` | Stop. Report what is ambiguous; wait for operator input. |
| Stall persists after one repair | Escalate to operator stop — do not retry indefinitely. |

**Hard limits:**
- Apply at most **one guided repair** per recovery cycle.
- Never invent workarounds or raw `gh label` mutations as recovery.
- Never bypass `intent-cli worker` / `intent-cli automation` transition ownership.

## Metadata / label safety

- Recovery never means hand-editing `queue-state.json` or labels; the preflight
  surfaces are read-only and name the owning command.
- Child implementation agents only own `child-selector-label-gap` repairs;
  everything else is host/review-owned.
- Durable PR blocker comments (not chat-only) record current-PR AC blockers; a
  broader capability gap routes to a follow-up issue/packet/signal.

## Back to start

Return to the [docs index](index.md) or re-run `intent-cli guide start`.
