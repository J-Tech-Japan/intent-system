# GitHub workflow labels and what they mean

← [docs index](README.md) | → [Implementation loop setup](05-implementation-loop.md)

When a packet becomes a GitHub Issue, the Intent System's workflow state becomes visible
through GitHub labels. Labels are the canonical visible state that humans use to understand
what is waiting on whom. **You do not manually add or remove workflow labels in normal operation.**
Label transitions are managed by intent-cli and automation commands.

## Core label reference

| Label | Meaning |
|---|---|
| `intent-target` | This item is selected as the current workflow target |
| `intent-issue-in-progress` | An implementation worker has claimed the issue and is working on it |
| `intent-pr-created` | The issue has produced a PR (applied to the issue, not the PR) |
| `intent-pr-reviewing` | The host review loop is actively reviewing the PR |
| `intent-pr-request-update` | The reviewer has requested changes |
| `intent-pr-update-in-progress` | An implementation worker has claimed the PR update |
| `intent-pr-rereview-ready` | A fix has been pushed; the review loop should run again |
| `intent-pr-approved` | The host review has approved the PR — waiting for merge or closeout |

## Reading label combinations

```text
intent-target + intent-issue-in-progress
→ This issue is the active implementation target; a worker is working on it.

intent-target + intent-pr-created (on the issue)
→ A PR has been created; waiting for the review loop to pick it up.

intent-target + intent-pr-reviewing (on the PR)
→ The host review loop is actively reviewing the PR.

intent-target + intent-pr-request-update (on the PR)
→ The PR has a change request; waiting for the implementation worker to respond.

intent-target + intent-pr-rereview-ready (on the PR)
→ A fix has been pushed; the review loop should re-review.

intent-target + intent-pr-approved (on the PR)
→ The PR is approved and waiting for merge.
```

## Important notes about labels

- **Do not manually add or remove workflow labels**: in normal operation, workflow labels are managed by intent-cli/automation. Manual changes can cause loops to enter incorrect states.
- **If the state looks wrong**: do not hand-fix labels. Describe the symptom to your AI agent and ask it to consult intent-cli for the safe repair path (see [Recover when a loop looks wrong](07-recovery.md)).

## Next

[Implementation loop setup](05-implementation-loop.md) | [Create packets & publish issues](04-packets-issues.md) | [docs index](README.md)
