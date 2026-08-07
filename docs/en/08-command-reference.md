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

## Design-thread improve / realignment

A periodic reflection step for a design thread: step back and check whether
recent work still aligns with the original mission, vision, values, ADR/design
notes, and intent tree. In a design thread you can simply paste the
natural-language request and let the agent run the guide internally:

```text
intent-cli で improve プロセスを実行してください。
```

The agent then fetches the current guidance and produces a structured report.
`improve` is a first-class top-level command (with `guide improve` as the
equivalent guide-namespaced form):

```bash
intent-cli improve --domain <domain> --format markdown
intent-cli guide improve --domain <domain> --format markdown
```

Both forms return identical guidance and are discoverable via `intent-cli
--help` and `intent-cli guide commands list`. If the installed CLI has no
`improve` surface, report `improve guidance unavailable` and update the CLI —
do **not** substitute `bug-to-intent-repair`, host-loop recovery,
`state-doctor`, or dirty-state repair.

By default `improve` is **implementation-aware**: when evidence is available it
also inspects related GitHub issues/PRs, implementation diffs, tests, review
findings, and product evidence to find the current top blocker and propose a
corrective backlog (`Implementation Reality Check`, `Blocker Cluster Analysis`,
`Corrective Backlog Candidates`) — intent-tree cleanup alone is not enough when
packet history suggests an unresolved product blocker. Pass `--light` for a
quick intent-only reflection:

```bash
intent-cli improve --domain <domain> --light --format markdown
```

After operator approval the agent may create the proposed corrective packets and
publish **at most one** first GitHub issue unless asked for more.

`improve` is a **safety net**, not the first line of defense. The normal path is
**packet-time intent maintenance** (G461): when a packet is drafted, the agent is
already prompted to consider intent placement, ADR candidates, diagram candidates,
docs updates, and closeout knowledge writeback — see `intent-cli guide workflow task
packet-draft`. That metadata is optional and backward-compatible (legacy packets
without it stay valid), but capturing it while the design context is fresh keeps the
intent tree, ADRs, and diagrams from drifting behind packet history in the first
place. `improve` then catches whatever the packet-time check missed.

`guide improve` is a design-thread reflection process — **not** a scheduler, a
provider launcher, or a routine host-loop / worker-loop recovery diagnostic.
Operational metadata/label/queue repair stays in the existing operational
surfaces (`automation reconcile`, `automation publish-recovery`,
`review closeout-plan`). It inspects MVV, ADR/design notes, the intent tree,
recent packet history, clarification history, and short-term-loop signals, and
classifies the outcome as one of `aligned`,
`intent-strengthening-recommended`, `clarification-recommended`,
`corrective-packet-recommended`, `adr-update-recommended`,
`short-term-loop-detected`, or `operator-policy-required`. Mutations are
proposed first and applied only after operator agreement through supported
intent-cli / repo paths.

## Grill — persistent interview mode

A user-facing **persistent interview mode** (G463). Once you ask to grill a
topic, the design thread STAYS in grill mode: it generates an open-question
backlog from the current intent context (intents, packets, ADR/design notes,
docs, and relevant implementation evidence) and keeps asking **one question at a
time**, continuing after each answer without you repeating `grill`, until a
structured stop condition is reached.

```bash
intent-cli grill --domain <domain> --format markdown
intent-cli guide grill --domain <domain> --format markdown
```

Both forms return identical guidance and are discoverable via `intent-cli
--help` and `intent-cli guide commands list`. grill is built on the existing
`interview` artifacts (it records answers through `intent-cli interview
record-answer` and reads pending questions through `intent-cli interview
next-question`); it is **not** `clarification` (blocker resolution) and **not**
`improve` (retrospective realignment), and it never auto-publishes packets or
issues.

Stop conditions: `no-more-questions` (returns `今のところ追加質問はありません`
only when the backlog is empty and rediscovery finds nothing), `packet-ready`,
`intent-update-ready`, `clarification-needed`, `blocked-by-user-decision`, and
`too-broad-split-needed`. Packet / issue / intent-update actions are proposed at
a stop condition and applied only after explicit operator acceptance.

## Inspect — evidence-backed observation

A named process for **observing the real product** before cutting tasks (G466).
`inspect` guides an agent to exercise the actual app / CLI / UI / logs / tests,
strictly separate observed evidence from inference, compare against expected
intent, and turn the gaps into packet candidates.

```bash
intent-cli inspect --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide inspect --domain <domain> --target-repo <owner/repo> --format markdown
```

The **Inspect Report** separates `observed_behavior`, `expected_intent`,
`evidence`, `gaps`, `risk_severity`, `recommended_next_action`, and
`packet_candidates`. The first pass is **read-only by default** — it never runs
destructive interactions or auto-publishes — and it *guides how to use*
browser / computer-use / log / test tooling rather than replacing it. Based on
what it finds, an inspect pass routes to **stack** (package the gaps),
**grill** (extract unclear intent), **improve** (systemic drift), **recovery**
(broken operational state), or **no-action** (behavior matches intent). It is
distinct from grill, stack, and improve.

## Next — design-side action advisor

The simplest design-thread question — "what should I do next?" — answered by
`intent-cli next` (G465). It lays out the catalog of design-side processes and
recommends one, so users do not have to remember every command name.

```bash
intent-cli next --domain <domain> --team <team> --target-repo <owner/repo> --format markdown
intent-cli guide next --domain <domain> --team <team> --target-repo <owner/repo> --format markdown
```

Ask it in natural language: `intent-cli に聞いて、次に何をしたらいいか教えてくださ
い。`. It checks the evidence (current intents, open questions, packet backlog,
open PRs / review state, CLI / queue health) and recommends exactly one of
**grill** (extract open questions), **stack** (create packet backlog + publish
first issue), **improve** (retrospective realignment), **inspect**
(evidence-backed observation of real app/CLI/UI/log/test behavior, not just
status checking), **issue-publish** (publish a ready packet), **review** (review
an open PR), **recovery** (repair stale CLI / queue), or **idle** (nothing
actionable).
The output includes the recommended action, the reason, the evidence checked, a
paste-ready suggested prompt, and the safety boundary. `next` is **read-only by
default** and never auto-executes the chosen action — the user decides whether
to run the suggested prompt.

When `--domain` and `--team` are supplied, `next` also reads the team's recorded
supervision cycle. No recorded cycle adds the `supervision-setup` recommendation;
an existing cycle leaves that recommendation silent. The host-init and
design-side loop guides carry the deployment step and link the
[orchestration reference](12-agent-message-orchestration.md); this command only
detects the missing record and never starts or manages the background process.
Supervision remains a preview through 1.x under the compatibility promise.

## Stack — packet backlog creation + first issue publish

A named **forward planning** process (G464). `stack` reads the current intents,
creates an ordered backlog of the packets that are ready now (often around ten),
commits and pushes that durable state, and by default publishes **at most the
first** GitHub issue — leaving the rest as a deferred backlog.

```bash
intent-cli stack --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide stack --domain <domain> --target-repo <owner/repo> --format markdown
```

`stack` matches タスクを積む. It is distinct from `improve` (retrospective
realignment from a drift / loop crisis), `grill` (persistent open-question
interview), `clarification` (blocker resolution), and runtime `queue`
transitions. It respects open questions, work-in-progress, and host-only packet
boundaries, commits/pushes durable packet state before issue-publish, and never
hand-applies `intent-target` (the host publish boundary applies it). The output
shape lists `created_packets`, `recommended_first_issue`, `published_issue`, and
`deferred_items`.

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
intent-cli automation doctor --domain <domain> --team <team> --format json
```

The bare doctor keeps an empty anonymous root unjudged. Supply `--domain` and
`--team` together to require the shared record-first session-layer preflight for
that named team; an unrecorded mode is configuration-incomplete, never
not-required.

---

## Command group overview

`intent-cli guide commands list` is a **role-based catalog** (G467): every
command group carries an operator-role category — **design** (improve / grill /
stack / next / inspect / intent / interview / packet / clarify), **host-review**
(review / closeout / automation / issue), **child-implementation** (worker),
**recovery-diagnostics** (automation doctor / metadata / queue), and
**advanced-developer** (task) — alongside its `primary`/`support` lifecycle
classification. `intent-cli guide help` explains the same role buckets and points
to the loop-prompt generators (`guide workflow task implementation-loop` /
`review-next-slice-loop`).

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
