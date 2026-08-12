# Implementation loop setup

← [docs index](README.md) | → [Review / next-slice loop setup](06-review-next-slice-loop.md)

Do not copy the steps from this page directly to create an implementation loop.
The authoritative conditions come from installed intent-cli guidance.
Ask the AI agent in your design thread to generate the current loop creation prompt.

## Folder separation (understand this first)

Before starting an implementation loop, understand the three folder roles:

| Folder | Role |
|---|---|
| **Design/host folder** | Stores intent metadata and packets. The design thread runs here. |
| **Implementation folder** | The child implementation loop edits code and creates/updates PRs here. |
| **Review folder** | The host review/next-slice loop reviews PRs and publishes the next issue here. |

> **Warning — wrong cwd is a common failure mode.**
> Starting an implementation loop from the design/host folder, or a review loop
> from an implementation folder, causes misbehavior. Always verify your working
> directory before starting a loop.

**Same-repository metadata topology** (using a `main-metadata` branch): even when
all three roles target the same repository, run each loop from a **separate
folder, clone, or worktree**. Sharing a folder across roles causes branch
operations and metadata changes to interfere with each other.

## How to create a loop

1. **In the design thread**, ask the AI agent to ask intent-cli for the current implementation loop creation prompt.
2. Provide the domain, target repo, **path to the implementation folder**, and the PR base branch.
3. Paste the generated prompt into a separate thread opened in the **implementation folder**.

## Design-thread prompt (to request a loop creation prompt)

Paste this into your design thread (the AI agent running in the design/host folder):

> Ask intent-cli to generate the prompt I need to create a child implementation loop
> for `<owner>/<repo>` using Claude Code `/loop 5m`.
> The domain is `<domain>`, the working folder is `<implementation-folder>`,
> and the implementation PR base branch is `<branch>`.
> The generated prompt should delegate detailed conditions to intent-cli guidance.

Paste the generated prompt into a **separate thread opened in the implementation folder**.
The loop's detailed conditions come from intent-cli guidance — you do not need to
copy a long loop body from this document.

## Child implementation loop principles

- **GitHub-contract-only and metadata-free**: the issue/PR and repo-local code are the only source of truth
- Never read or mutate host `.intent-cli/`, queue-state, metadata branches, or `intents/**`
- Select work only via `intent-cli worker next-action`; process at most one action per wake
- All label transitions go through `intent-cli worker` — never raw `gh ... --add-label`

## Metadata / label safety

- A child agent never applies `intent-target` (host-owned) or `intent-pr-created` (issue-side marker) to a PR
- `linked_pr_synced: false` from `worker complete` is the expected child-cwd warning — record it and move on

## Preview: Git-backed cross-clone scope claims (G679)

The decision and its boundaries are recorded in
[ADR 0003](../adr/0003-git-push-cas-work-ownership.md).

`worker claim` remains the GitHub issue/PR lifecycle transition above. The
separate preview `claim` group coordinates one named unit of work across host
clones without a server:

```bash
intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json
intent-cli claim acquire --scope release-prep:<owner/repo>:<version> --actor <actor> --team <team> --write --format json
intent-cli claim verify --scope <scope> --team <team> --format json
```

Acquisition is exactly `git pull --ff-only` → create the immutable record under
`.intent-cli/claims/` → commit → plain push. Only push success means acquired.
If the same scope appeared after a rejected push, the result is `held` and names
the holder; an unrelated advance is reapplied from a fresh base with a bounded
retry. Release and takeover require explicit actor/team/reason attribution, and
takeover names the displaced holder. Age never expires or transfers a claim;
`automation stalled-work` only reports `claim-stale` with actor, team, scope,
age, and last evidence.

Fresh hosts put these exact lines in `.gitattributes`, in this order:

```gitattributes
.intent-cli/runs.jsonl merge=union
.intent-cli/**/*.jsonl merge=union
.intent-cli/claims/** -merge
```

Existing hosts are not migrated automatically. Add the final, more-specific
line after broad union rules only through an explicit reviewed commit.

### Preview: claim-aware start surfaces (G680)

Packet draft, queue seed/publish-flow, worker next-action, and release-prep use
the same `claim verify` judgment. A configured store requires the invoking
team to hold the matching scope; unheld and other-team refusals name scope,
holder, and holder team. Next-slice uses that same judgment in recommendation
mode: unheld and own-team units remain candidates, while claimed-elsewhere
units are excluded with holder evidence, so it never urges what start will
refuse.

Numbering is claim-then-draft. Claim `execution-unit:<N>` before scaffolding;
after losing N, fast-forward, recompute, and retry the next number exactly once.
The GitHub lifecycle label remains visible defence in depth but is not the
acquisition fact. Review/closeout gates and `worker complete` are unchanged.

## Command reference (for agents, maintainers, and troubleshooting)

> **Note:** The commands below are run by the AI agent internally. The authoritative
> loop conditions come from `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>`.
> You do not normally need to run these commands manually.

```bash
# Select exactly one target (no manual label-walking)
intent-cli worker next-action --repo <owner>/<repo> --team <team> --github-only --format json

# issue-to-pr: claim, implement smallest change, open ready-for-review PR
intent-cli worker claim --kind issue --number <n> --repo <owner>/<repo> --github-only --write --format json
#    PR body MUST contain `Closes #<n>`; start from origin/main.
intent-cli worker result-summary --kind issue-to-pr --repo <owner>/<repo> --issue <n> --pr <pr> --outcome <outcome> --format json
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> --github-only --outcome <outcome> --pr <pr> --write --format json
```

## Next

[Review / next-slice loop setup](06-review-next-slice-loop.md) | [docs index](README.md)
