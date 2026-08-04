# intent-cli documentation (English)

> 日本語版は [`../ja/README.md`](../ja/README.md) を参照してください.

> **Official service site:** [intent-driven-development.com](https://www.intent-driven-development.com/) — the Intent-Driven Development concept & intent-system service site, operated by J-Tech Japan, covering the broader Intent-Driven Development concept and intent-system overview. This GitHub repository remains the source for code, releases, installation, and detailed docs.

`intent-cli` is **deterministic support tooling** for an intent-driven development workflow on top of GitHub.

Start by choosing one [self-contained onboarding pattern](02a-getting-started-orchestration.md):
separate host repository or same-repository metadata branch, crossed with a
brand-new or existing project. Each pattern has two paste-ready initial prompts.
For a collocated single-machine team, `herdr-only` is the first supported
choice (PREVIEW is a maturity note); choose `agmsg` + herdr for distributed
teams or an existing agmsg investment. The primary thing is the four-thread
model, not either transport.

## Pages

1. [Install](01-install.md)
2. [Start a project](02-project-start.md)
2a. [Getting started: the road to the first packet](02a-getting-started-orchestration.md) — minimal start and the primary four-thread model; collocated `herdr-only` is supported (PREVIEW maturity note)
   - [Separate host × brand-new](02b-separate-host-brand-new.md)
   - [Separate host × existing](02c-separate-host-existing.md)
   - [Same repo × brand-new](02d-same-repo-brand-new.md)
   - [Same repo × existing](02e-same-repo-existing.md)
3. [Intent Storming & organize intents](03-intents.md)
4. [Create packets & publish issues](04-packets-issues.md)
4a. [GitHub workflow labels and what they mean](04a-workflow-labels.md) — label meanings and how to read them
12. [Agent-message orchestration](12-agent-message-orchestration.md) — four-thread contract reference; single-domain vs multi-domain routing

### Alternative paths

5. [Implementation loop setup](05-implementation-loop.md) — timer-loop **alternative** setup
6. [Review / next-slice loop setup](06-review-next-slice-loop.md) — timer-loop **alternative** setup

### Reference and recovery

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

Join the [J-Tech JAPAN OSS Discord](https://discord.gg/z9FnEgm6mp) for community discussion and questions. For bugs or actionable feature requests, open a [GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) instead. Security reports go to [SECURITY.md](../../SECURITY.md), not Discord.
