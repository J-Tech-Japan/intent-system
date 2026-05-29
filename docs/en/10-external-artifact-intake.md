# External artifact intake (G438)

← [docs index](README.md) | → [Recovery](07-recovery.md)

This page covers how AI agents and host operators handle **external GitHub issues and PRs** — artifacts that arrive from outside the normal intent-packet publish flow. The intended user experience is:

> この外部 Issue / PR を intent-cli に聞いて、正規の方法で扱ってください。

The AI agent asks intent-cli for guidance and follows the returned steps. Humans do not need to learn intent-cli commands directly.

## Why metadata is required

Simply commenting on an external artifact and applying a workflow label is **not** a valid import. The value of bringing an artifact into the intent workflow is durable traceability: the host must know *why* the artifact was accepted, *which intent* it supports, *which packets* it relates to, and *what review should verify*.

Without this metadata, agents in subsequent loops cannot perform packet-aware review or closeout without manual repair.

## Three lanes

intent-cli distinguishes three intake scenarios:

| Lane | Trigger | Key gate |
|---|---|---|
| `external-issue` | An external GitHub issue should enter the intent queue | Lightweight packet metadata before `intent-target` |
| `external-pr-review` | An external PR should be reviewed as part of the intent workflow | Packet/review-context metadata + linked issue before review transitions |
| `external-pr-adopt` | Host explicitly adopts an external PR into the intent workflow | Full provenance + shadow issue + explicit operator confirmation |

## Ask-intent-cli prompt templates

### External Issue intake

> There is an external issue at `<url>`. Ask intent-cli how to properly bring it into the intent workflow.

The AI agent will run:

```
intent-cli guide artifact-intake --lane external-issue --repo <owner/repo> --format markdown
```

and follow the returned guidance.

### External PR review

> There is an external PR at `<url>` that I want reviewed under the intent workflow. Ask intent-cli how to proceed.

The AI agent will run:

```
intent-cli guide artifact-intake --lane external-pr-review --repo <owner/repo> --format markdown
```

### External PR adopt/import

> I want to formally adopt PR `<url>` into the intent workflow. Ask intent-cli for the adoption steps.

The AI agent will run:

```
intent-cli guide artifact-intake --lane external-pr-adopt --repo <owner/repo> --format markdown
```

## Metadata fields

Each lane requires the host to record specific metadata before label mutations. The `artifact-intake` guide command returns the required fields for the requested lane. Key fields common to all lanes:

- **source_artifact** — the external GitHub issue/PR URL and number
- **relevant_intents** — links to existing intent documents this artifact supports
- **related_packets** — execution units of any related packets
- **expected_outcome** — what acceptance or implementation must achieve
- **constraints** — scope, compatibility, or sequencing constraints

PR-specific additional fields:

- **linked_issue** — a suitable linked issue (or shadow issue) that anchors intent context
- **review_focus** — what the host review should verify
- **provenance** (adopt only) — original author, origin repo, prior discussion references
- **operator_confirmation** (adopt only) — explicit host sign-off

## Shadow issues

When an external PR has no suitable linked issue, the host must create a **shadow issue** before any review transition or adoption step. The shadow issue:

- Records the same metadata fields as the intake lane requires
- Becomes the intent anchor for the PR in subsequent review/closeout steps
- Must be created by the host operator (not automatically)

If the PR raises unresolved product or technical questions, run the interview/clarification flow **before** creating the shadow issue:

```
intent-cli guide workflow task intent-interview --format markdown
```

## What contributors need to know

Nothing. External contributors do not need to understand intent labels, queue-state, packet YAML, or closeout mechanics. The host agent handles all metadata creation and label transitions through intent-cli.

## Guard rails

- `intent-target` is never applied by comment or label alone
- `intent-pr-reviewing` is never started before metadata and a linked issue exist
- Ambiguous mappings stop for operator clarification rather than guessing
- AI agents must not proceed past operator confirmation gates without an explicit decision

## Command reference

```
intent-cli guide artifact-intake --lane external-issue [--repo <owner/repo>] [--format markdown|json]
intent-cli guide artifact-intake --lane external-pr-review [--repo <owner/repo>] [--format markdown|json]
intent-cli guide artifact-intake --lane external-pr-adopt [--repo <owner/repo>] [--format markdown|json]
```
