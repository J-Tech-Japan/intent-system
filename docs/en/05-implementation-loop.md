# Implementation loop setup

> ← [docs index](index.md)

This is **child-implementation** work and is **GitHub-contract-only &
metadata-free**: the issue/PR and repo-local code are the only source of truth.
Never read or mutate host `.intent-cli/`, queue-state, metadata branches, or
`intents/**`.

## Design-thread prompt

Paste this into your AI agent (Claude, Codex, Copilot, etc.):

> Run the child implementation loop for `<owner>/<repo>` from this worktree.
> Get the full loop prompt via:
> `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>`
> and follow it exactly. Select work only via `intent-cli worker next-action`;
> process at most one action per wake. All label transitions go through
> `intent-cli worker` — never raw `gh ... --add-label`.

## What the agent will run (reference)

```bash
# 1. Select exactly one target (no manual label-walking)
intent-cli worker next-action --repo <owner>/<repo> --github-only --format json

# 2. issue-to-pr: claim, implement smallest change, open ready-for-review PR
intent-cli worker claim --kind issue --number <n> --repo <owner>/<repo> --github-only --write --format json
#    PR body MUST contain `Closes #<n>`; start from origin/main.
intent-cli worker result-summary --kind issue-to-pr --repo <owner>/<repo> --issue <n> --pr <pr> --outcome <outcome> --format json
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> --github-only --outcome <outcome> --pr <pr> --write --format json
```

`pr-comment-fix` targets follow the same claim → repair → result-summary →
complete shape on the existing PR branch.

## Metadata / label safety

- All workflow-label transitions go through `intent-cli worker` — no raw
  `gh ... --add-label`.
- A child agent never applies `intent-target` (host-owned) or `intent-pr-created`
  (issue-side marker) to a PR.
- `linked_pr_synced: false` from `worker complete` is the expected child-cwd
  warning — record it and move on.

## Next

[Review / next-slice loop setup](06-review-next-slice-loop.md).
