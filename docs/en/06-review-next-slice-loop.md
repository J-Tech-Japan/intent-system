# Review / next-slice loop setup

← [Implementation loop setup](05-implementation-loop.md) | → [Recover when a loop looks wrong](07-recovery.md)

Do not copy the steps from this page directly to create a review/next-slice loop.
The authoritative conditions come from installed intent-cli guidance.
Ask the AI agent in your design thread to generate the current loop creation prompt.

## Folder separation (understand this first)

Run the review loop from a **dedicated review folder**.
Do not share a folder with the implementation loop or the design thread.

| Folder | Role |
|---|---|
| **Design/host folder** | Stores intent metadata and packets. The design thread runs here. |
| **Implementation folder** | The child implementation loop edits code and creates/updates PRs here. |
| **Review folder** | The host review/next-slice loop reviews PRs and publishes the next issue here. |

> **Note:** The review folder is not optional in normal operation.
> It exists so review/next-slice automation can pull and mutate host metadata
> without interfering with the design thread or the implementation worker checkout.

**Same-repository metadata topology** (using a `main-metadata` branch): even when
all three roles target the same repository, run each loop from a **separate
folder, clone, or worktree**.

## How to create a loop

1. **In the design thread**, ask the AI agent to ask intent-cli for the current review/next-slice loop creation prompt.
2. Provide the domain, target repo, **path to the review folder**, and the PR base branch.
3. Paste the generated prompt into a separate thread opened in the **review folder**.

## Design-thread prompt (to request a loop creation prompt)

Paste this into your design thread (the AI agent running in the design/host folder):

> Ask intent-cli to generate the prompt I need to create a host review / next-slice loop
> for `<owner>/<repo>` using Codex 5m automation.
> The domain is `<domain>`, the working folder is `<review-folder>`,
> and the implementation PR base branch is `<branch>`.
> The review targets, next issue, workflow labels, and durable metadata
> should all defer to intent-cli guidance as the source of truth.

Paste the generated prompt into a **separate thread opened in the review folder**.
The loop's detailed conditions come from intent-cli guidance — you do not need to
copy a long loop body from this document.

## Host review / next-slice loop principles

- This is **host/review** work: review PRs against the packet/intent contract, request updates, approve/merge, and cut the next slice
- It may operate on host metadata, but always via `intent-cli`-supported transitions
- Tie approvals to packet/intent evidence, not just green tests
- All label transitions go through `intent-cli automation` — never apply labels by hand

## Metadata / label safety

- Review label transitions (`intent-pr-reviewing`, `intent-pr-request-update`, `intent-pr-approved`, …) are applied by `intent-cli automation`, never by hand
- Passing tests is **necessary but not sufficient** — approval requires packet/intent conformance evidence (see `guide review`)
- Current-PR acceptance-criterion blockers get a durable PR comment before completing as request-update/clarification (see [recovery](07-recovery.md))

## Command reference (for agents, maintainers, and troubleshooting)

> **Note:** The commands below are run by the AI agent internally. The authoritative
> loop conditions come from `intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>`.
> You do not normally need to run these commands manually.

```bash
# Get the authoritative review/next-slice prompt
intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>

# PR-specific review guidance (checklist, packet refs, approval/request-update reqs)
intent-cli guide review --pr <n> --repo <owner>/<repo> --format json

# Label transitions (review-start, request-update, approve, …) — never by hand
intent-cli automation pr-transition --transition <name> --write --format json
```

## Next

[Recover when a loop looks wrong](07-recovery.md).
