# Command reference (agent-facing / power users)

> English version. 日本語版: [`../ja/08-command-reference.md`](../ja/08-command-reference.md)

This page lists the `intent-cli` command surfaces that AI agents and power users
run on your behalf. You do not need to memorize these for routine use; the
[root README](../../README.md) Quickstart and `intent-cli guide start` cover the
typical path.

The commands below are what the AI agent runs internally. Run
`intent-cli guide commands list --format json` for the authoritative live catalog.

---

## Two agent roles

| Agent | Source of truth | Owns |
| --- | --- | --- |
| **Host / review agent** | parent host `.intent-cli/` state + intent tree | publishing issues, applying `intent-target`, review/approve/merge, next-slice planning, label transitions via `intent-cli automation` |
| **Child implementation agent** | the **GitHub issue/PR + repo-local code** (NOT host metadata) | implementing the issue contract, opening/updating the PR, recording outcomes via `intent-cli worker` |

Child implementation agents are **GitHub-contract-only**: they must not read or
mutate the parent host's queue-state, runs logs, packet directories, or intent
tree, and they treat the GitHub issue body as the standalone contract.

The host can live in a **separate host repository** or in the **same repository
on a dedicated metadata branch** (e.g. `main-metadata`). Both topologies are
fully supported — see [Start a project](02-project-start.md#repository-topology-choices).

---

## Project setup

```bash
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write
intent-cli intent status
intent-cli guide intent-work setup --format json
```

## Design / intents

```bash
intent-cli interview next-question
intent-cli interview record-answer ...
intent-cli interview compile
intent-cli guide workflow
```

## Packets / issues

```bash
intent-cli packet ...
intent-cli issue validate-body ...
intent-cli issue prepare ...
intent-cli issue publish-reviewed ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --write
```

## Implementation & review loops

```bash
# Fetch the complete loop prompt for an AI agent:
intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>
intent-cli guide oneshot --kind host-review-next-slice    --domain <name>
```

Operator-dogfooding prompt templates that wire these loops entirely through the
deterministic worker/metadata commands live under
[`docs/automation-templates/`](../automation-templates/README.md).

## Recovery

```bash
intent-cli worker issue-preflight       --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight  --repo <owner>/<repo> --pr <n>    --format json
intent-cli automation doctor --format json
```

---

## Command group overview

| Surface | Role |
|---------|------|
| `intent-cli guide …` | Ask-first guidance: collaboration model, workflow, prompt-template catalog, one-shot prompts |
| `intent-cli status brief` | Compact AI-thread context input |
| `intent-cli clarify draft` / `clarify record` | Owner clarification flow |
| `intent-cli issue validate-body` | Standalone Child Issue Contract enforcement |
| `intent-cli issue prepare` / `issue publish-reviewed` | Reviewed issue body publish boundary (never applies `intent-target`) |
| `intent-cli worker next-action` / `claim` / `result-summary` / `complete` | Child implementation loop selector + bounded label transitions |
| `intent-cli automation summary` | Provider-neutral label-driven automation contract emitter |
| `intent-cli safety nested-provider-handoff` | Artifact-only nested-provider safety guard (never spawns providers) |

---

## Rules of thumb

- **Use `intent-cli` transition commands, not raw edits.** Do not directly edit
  queue-state, workflow labels, packet publish metadata, or other host artifacts
  when an `intent-cli automation` / `intent-cli worker` command owns that
  transition. Apply labels through those commands, never `gh ... edit
  --add-label`.
- **Ask, don't read-and-guess.** Prefer `intent-cli guide ...` over reading
  local rule files; the guidance reflects the installed CLI's current contract.
- **`intent-cli` does not launch AI providers.** It emits deterministic
  guidance, validates contracts, and performs bounded GitHub/metadata
  transitions. The AI agent stays in the driver's seat.
