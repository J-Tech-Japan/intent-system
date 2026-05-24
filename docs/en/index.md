# intent-cli documentation (English)

> 日本語版は [`../ja/index.md`](../ja/index.md) を参照してください.

`intent-cli` is **deterministic support tooling** for an intent-driven
development workflow on top of GitHub. These pages give a little more structure
than the [root README](../../README.md) without requiring you to read the
internal design notes.

## The one rule: ask intent-cli first

Before any intent / packet / issue / review / implementation-loop work, run:

```bash
intent-cli guide start
```

It points you at the exact `intent-cli guide …` command for your phase. Don't
start from memory, copied prompts, or ordinary GitHub habits, and don't
hand-edit metadata or labels when an `intent-cli` command owns the transition.
Every page below repeats this rule because it is the difference between a smooth
loop and a broken one.

## Pages

1. [Install](01-install.md)
2. [Start a project](02-project-start.md)
3. [Organize & maintain intents](03-intents.md)
4. [Create packets & publish issues](04-packets-issues.md)
5. [Implementation loop setup](05-implementation-loop.md)
6. [Review / next-slice loop setup](06-review-next-slice-loop.md)
7. [Recover when a loop looks wrong](07-recovery.md)

## Two agent roles (read this once)

| Role | Source of truth | Responsibilities |
| --- | --- | --- |
| **Host / review agent** | parent host `.intent-cli/` state + intent tree | publish issues, apply `intent-target`, review/approve/merge, cut next slices, label transitions via `intent-cli automation` |
| **Child implementation agent** | the **GitHub issue/PR + repo-local code** (not host metadata) | implement the issue contract, open/update the PR, record outcomes via `intent-cli worker` |

Child implementation agents are **GitHub-contract-only**: they must not read or
mutate host `.intent-cli/`, queue-state, metadata branches, or `intents/**`.
Host/review agents may operate on metadata, but ask `intent-cli` for the current
command first and prefer its transitions over hand edits.
