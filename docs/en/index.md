# intent-cli documentation (English)

> 日本語版は [`../ja/index.md`](../ja/index.md) を参照してください.

`intent-cli` is **deterministic support tooling** for an intent-driven
development workflow on top of GitHub. These pages give a little more structure
than the [root README](../../README.md) without requiring you to read the
internal design notes.

## How to use intent-cli

`intent-cli` is designed to be driven by an AI agent — Claude, Codex,
Copilot, or any capable coding assistant with repository access. You do not
need to memorize or run its commands directly.

**The typical human path:**

1. Install `intent-cli` and verify with `intent-cli --version`.
2. Open a design thread in your AI agent.
3. Paste a prompt such as:

> Ask intent-cli what phase I'm in for `<owner>/<repo>` domain `<name>`.
> Run `intent-cli guide start` and `intent-cli intent status`, then tell me
> what I should decide next.

The agent runs `intent-cli` internally and brings back questions or results.
You focus on intent, priorities, and approval decisions — not on memorizing
command sequences.

**The one rule behind the prompts:** before any label/metadata change, the AI
agent should run the appropriate `intent-cli` command rather than editing files
or applying GitHub labels by hand. Every guide and automation page below
enforces this rule.

See the [command reference](08-command-reference.md)
for the full list of commands the agent will use on your behalf.

## Pages

1. [Install](01-install.md)
2. [Start a project](02-project-start.md)
3. [Organize & maintain intents](03-intents.md)
4. [Create packets & publish issues](04-packets-issues.md)
5. [Implementation loop setup](05-implementation-loop.md)
6. [Review / next-slice loop setup](06-review-next-slice-loop.md)
7. [Recover when a loop looks wrong](07-recovery.md)
8. [Command reference](08-command-reference.md) — agent-facing and power-user command surfaces
9. [Developer reference](09-developer-reference.md) — packaged invocation, preview channel, version flow

## Two agent roles (read this once)

| Role | Source of truth | Responsibilities |
| --- | --- | --- |
| **Host / review agent** | parent host `.intent-cli/` state + intent tree | publish issues, apply `intent-target`, review/approve/merge, cut next slices, label transitions via `intent-cli automation` |
| **Child implementation agent** | the **GitHub issue/PR + repo-local code** (not host metadata) | implement the issue contract, open/update the PR, record outcomes via `intent-cli worker` |

Child implementation agents are **GitHub-contract-only**: they must not read or
mutate host `.intent-cli/`, queue-state, metadata branches, or `intents/**`.
Host/review agents may operate on metadata, but ask `intent-cli` for the current
command first and prefer its transitions over hand edits.

The host can live in a **separate host repository** or in the **same repository
on a dedicated metadata branch** (e.g. `main-metadata`). Both topologies are
fully supported. See [Start a project → Repository topology choices](02-project-start.md#repository-topology-choices)
for guidance on which to choose.

## Community

Join the [J-Tech Japan Discord](https://discord.gg/kMdv978X) for community
discussion and questions. For bugs or actionable feature requests, open a
[GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) instead.
Security reports go to [SECURITY.md](../../SECURITY.md), not Discord.
