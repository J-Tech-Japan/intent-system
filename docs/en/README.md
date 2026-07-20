# intent-cli documentation (English)

> 日本語版は [`../ja/README.md`](../ja/README.md) を参照してください.

> **Official service site:** [intent-driven-development.com](https://www.intent-driven-development.com/) — the Intent-Driven Development concept & intent-system service site, operated by J-Tech Japan, covering the broader Intent-Driven Development concept and intent-system overview. This GitHub repository remains the source for code, releases, installation, and detailed docs.

`intent-cli` is **deterministic support tooling** for an intent-driven development workflow on top of GitHub.

Once installed, open a design thread in your AI agent and paste a prompt like:

> I want to work on `<owner>/<repo>` with intent-cli.
> Ask intent-cli what phase I'm in and what I should decide next.

The agent runs `intent-cli` internally and brings back questions or results. You do not need to memorize commands.

## Pages

1. [Install](01-install.md)
2. [Start a project](02-project-start.md)
3. [Intent Storming & organize intents](03-intents.md)
4. [Create packets & publish issues](04-packets-issues.md)
4a. [GitHub workflow labels and what they mean](04a-workflow-labels.md) — label meanings and how to read them
12. [Agent-message orchestration](12-agent-message-orchestration.md) — **primary**: the four-thread (design/orchestrator/implementation/review) agmsg orchestrator model; single-domain vs multi-domain routing
5. [Implementation loop setup](05-implementation-loop.md) — timer-loop **alternative** setup
6. [Review / next-slice loop setup](06-review-next-slice-loop.md) — timer-loop **alternative** setup
7. [Recover when a loop looks wrong](07-recovery.md)
8. [Command reference](08-command-reference.md) — agent-facing and power-user command surfaces
9. [Developer reference](09-developer-reference.md) — packaged invocation, preview channel, version flow

## What is Intent Storming?

**Intent Storming** is the practice of working with an AI agent before coding to clarify what you want to build and why — capturing the result in a structured intent tree. The AI agent asks structured questions with background, options, pros/cons, and a recommendation. Your answers are organized into an **intent tree** that feeds packets and GitHub issues.

See [Intent Storming & organize intents](03-intents.md) for the full guide.

**The one rule behind the prompts:** before any label/metadata change, the AI agent should run the appropriate `intent-cli` command rather than editing files or applying GitHub labels by hand. See the [command reference](08-command-reference.md).

## Two agent roles (read this once)

| Role | Source of truth | Responsibilities |
| --- | --- | --- |
| **Host / review agent** | parent host `.intent-cli/` state + intent tree | publish issues, apply `intent-target`, review/approve/merge, cut next slices, label transitions via `intent-cli automation` |
| **Child implementation agent** | the **GitHub issue/PR + repo-local code** (not host metadata) | implement the issue contract, open/update the PR, record outcomes via `intent-cli worker` |

Child implementation agents are **GitHub-contract-only**: they must not read or mutate host `.intent-cli/`, queue-state, metadata branches, or `intents/**`.

The host can live in a **separate host repository** or in the **same repository on a dedicated metadata branch** (e.g. `main-metadata`). See [Start a project → Repository topology choices](02-project-start.md#repository-topology-choices) for guidance.

## Community

Join the [J-Tech JAPAN OSS Discord](https://discord.gg/kMdv978X) for community discussion and questions. For bugs or actionable feature requests, open a [GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) instead. Security reports go to [SECURITY.md](../../SECURITY.md), not Discord.
