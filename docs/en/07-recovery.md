# Recover when a loop looks wrong

← [docs index](index.md) | → [Review / next-slice loop setup](06-review-next-slice-loop.md)

When a loop looks stuck, the first step is to describe the symptom — not to run commands.
Do not edit labels or metadata directly. Tell the AI agent what looks wrong and ask it
to consult intent-cli for the safe repair path.

## Describe the symptom first

In your design thread or the relevant automation thread, say something like:

**When you have a specific symptom:**

```text
The PR exists and shows review-in-progress, but the AI agent is not working on it.
Something may have gone wrong — please ask intent-cli how to repair it safely.
```

**When something is stuck but you don't know why:**

```text
This isn't moving forward. Please ask intent-cli how to fix it and apply the safe repair.
```

The agent will check the current intent-cli guidance and apply only repairs that are
marked safe. Manual state edits and hand-applied labels are not the normal recovery path.

## Common symptoms and prompt examples

| Symptom | What to say |
|---|---|
| PR stuck in `review-in-progress`, agent not acting | `PR #<n> shows review-in-progress but nothing is happening. Ask intent-cli how to repair it.` |
| Issue published but implementation never starts | `Issue #<n> has intent-target but the implementation loop isn't picking it up. Ask intent-cli.` |
| PR comment fix not starting | `PR #<n> has request-update but no repair is starting. Ask intent-cli how to fix it.` |
| Next issue not cut after merge | `PR #<n> merged but no next issue appeared. Ask intent-cli what's missing.` |
| Loop reports idle when work seems available | `The loop reports idle but issue #<n> looks open. Ask intent-cli to check the state.` |
| Metadata state looks inconsistent | `The state looks wrong. Ask intent-cli to diagnose and apply the safe repair.` |

## Recovery principles

- **Do not hand-edit state**: never directly modify `queue-state.json`, labels, or metadata
- **Delegate to the agent**: intent-cli determines which command owns each repair
- **One at a time**: apply at most one guided repair per recovery cycle
- **Stop if operator judgment is needed**: if intent-cli returns `host-artifact-repair-required` or `clarification-required`, report to the operator and stop

## Command reference (for agents, maintainers, and troubleshooting)

> **Note:** The commands below are run by the AI agent internally. You do not normally need to run them manually. Refer to this section for workflow debugging or host automation maintenance.

```bash
# Is this PR's review feedback a safe, in-scope child repair?
intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr <n> --format json

# Is this issue safe to (re)claim as issue-to-pr?
intent-cli worker issue-preflight --repo <owner>/<repo> --issue <n> --format json

# CLI freshness / host-state resolution
intent-cli automation doctor --format json
```

Reading the result: `actionable` / `safe_repair_available` / `repair_category` tell
you whether a child-loop-owned repair exists. Host-owned categories surface as
`host-artifact-repair-required` and return to the host loop.

### Repeated-stall recovery (G408)

When an automation loop hits the same blocker on the same target for **two or more
consecutive wakes** without progress, it should self-recover rather than reporting
the same stop indefinitely. Recovery flow:

```bash
intent-cli guide model --format json
intent-cli guide onboarding --format json
intent-cli automation summary --domain <domain> --format json

# Child loop: run the matching preflight for the stuck target
intent-cli worker issue-preflight      --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr    <n> --format json

# Host loop: check freshness and state
intent-cli automation doctor --format json
```

| Result | Action |
|--------|--------|
| `safe_repair_category: child-selector-label-gap` | Apply the one repair `intent-cli` marks safe; retry once |
| `host-artifact-repair-required` | Stop. Report a structured operator stop. Do not hand-fix |
| `clarification-required` | Stop. Report what is ambiguous; wait for operator input |
| Stall persists after one repair | Escalate to operator stop — do not retry indefinitely |

## Next

[docs index](index.md) | [Review / next-slice loop setup](06-review-next-slice-loop.md)
