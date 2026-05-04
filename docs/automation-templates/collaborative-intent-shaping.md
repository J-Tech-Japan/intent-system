# Collaborative intent shaping — smoke guide

This guide shows the minimal prompt-to-intent flow an AI agent runs inside
Codex/Claude when a product owner says something like
`intent-cli に以下の機能を追加したいから一緒に作業して` (or any similar
intake request). It uses only installed `intent-cli` commands; no
`intents/rules` files or local skill files are required during normal
operation.

## Caller model

- The product owner interacts through chat.
- An AI agent (Codex / Claude) calls `intent-cli` internally to retrieve
  rules, questions, context, drafts, and validation.
- `intent-cli` is the deterministic guide/artifact authority. It does
  not launch any AI provider.
- The operator decides; the AI agent drafts and summarizes; `intent-cli`
  records canonical state.
- Canonical data lives under `intents/<domain>` and `.intent-cli` in the
  host git repository.

## Smoke flow (read-only by default)

Each step below is read-only unless `--write` is explicitly passed.
Operator acceptance is required before any source-of-truth mutation.

### 1. Pull the collaboration boundaries

The AI agent first asks intent-cli for the canonical responsibility
boundaries and suggested command sequence so it does not need to read
`intents/rules` files:

```bash
intent-cli guide collaborate --kind feature-intake --domain intent-cli --format markdown
```

The output names: who guides, who interviews, who decides; the suggested
command sequence; interview rules; and draft handoff rules.

### 2. Pull current state and prior art

Before asking the operator deeper questions, the AI agent surfaces
current state and prior art so its questions stay anchored:

```bash
intent-cli intent status --domain intent-cli --format json
intent-cli intent search --domain intent-cli --query "<keyword>" --format json
intent-cli intent explain <execution-unit> --domain intent-cli --format json
```

The agent also checks the `intent-cli` rule references for the topics
that come up in conversation:

```bash
intent-cli guide rules --topic label-ownership --format markdown
intent-cli guide rules --topic child-issue-contract --format markdown
intent-cli guide rules --topic clarification --format markdown
```

### 3. Run the interview against a durable session

The AI agent forms each question, asks the operator, and records the
answer in the per-domain interview store. This is the only step that
typically writes during the smoke flow — and only with `--write`:

```bash
intent-cli interview next-question --session alpha --domain intent-cli --format json

intent-cli interview record-answer \
  --session alpha \
  --domain intent-cli \
  --question q1 \
  --prompt "What is the goal of this feature?" \
  --from-file /tmp/answer-q1.txt \
  --write \
  --format json
```

`record-answer` adds a new question entry when `--question` is unknown
and `--prompt` is provided; otherwise the unknown id is rejected. The
session file lives at `intents/<domain>/interviews/<session>.json` and
is resume-safe across calls.

The agent can also peek at the durable interview state at any time:

```bash
intent-cli interview compile --session alpha --domain intent-cli --format markdown
```

This emits the accepted baseline (answered Qs), open questions
(pending), and a placeholder for candidate execution units.

### 4. Verify next-slice readiness before proposing a draft

Before suggesting a draft, the AI agent verifies that the WIP cap and
clarification gates allow a new slice:

```bash
intent-cli intent next-slice --dry-run --domain intent-cli --target-repo J-Tech-Japan/intent-system --format json
```

The `recommended_outcome` field will be one of
`clarification-required`, `skip-next-slice-due-to-wip`,
`no-actionable-item`, or `issue-cut-ready`. The agent must not propose a
draft if a clarification or WIP block applies.

### 5. Compile a draft (operator decision gate)

When the operator is ready to see a candidate intent draft, the AI
agent compiles the accepted answers without publishing anything:

```bash
intent-cli intent draft-from-interview \
  --session alpha \
  --domain intent-cli \
  --format markdown
```

This is read-only by default. The draft contains an Accepted baseline
(per-question summaries), Open questions, and a Candidate execution
units placeholder. The operator reviews this draft. **Acceptance is the
operator's decision.**

When the operator accepts the draft, the AI agent writes it to disk:

```bash
intent-cli intent draft-from-interview \
  --session alpha \
  --domain intent-cli \
  --write \
  --format json
```

The draft lands at `intents/<domain>/drafts/<session>.md`. The
collaborative-shaping smoke ends here. **No GitHub issue is created in
this flow.**

## Where operator decisions are required

The flow has three explicit operator decision gates. Each gate must be
crossed by the operator (in chat), not by the AI agent:

1. **Acceptance of each interview answer** — the AI agent only records
   answers the operator confirmed.
2. **Acceptance of the compiled draft** — `intent draft-from-interview
   --write` runs only after the operator accepts the dry-run draft.
3. **Promotion to a published child issue** — that step is intentionally
   out of scope for this smoke flow. It happens later via
   `intent-cli packet draft` and `intent-cli issue publish-flow`, both
   of which keep `intent-target` apply behind the parent host's publish
   boundary (`intent-cli automation issue-publish --write`).

## Skill-file independence

This entire smoke flow runs without opening:

- `intents/rules/*.md`
- local skill files (`gh-issue-to-pr`, `gh-fix-pr-comment`, `intent-*`)
- copied prompt fragments

If the AI agent needs current rules for any topic, it asks
`intent-cli guide rules --topic <name>` instead of reading rule files
directly. If it needs the canonical label contract, it asks
`intent-cli automation summary --format json`.

## Failure modes (deterministic stops)

- The intake selector returns nothing actionable: stop with status
  `idle`. Do not invent a slice.
- `intent next-slice --dry-run` returns `clarification-required`: stop
  with status `clarification-required` and report background, question,
  options, pros/cons, recommendation.
- `intent next-slice --dry-run` returns `skip-next-slice-due-to-wip`:
  do not draft a new slice; finish the in-flight WIP first.
- `record-answer` with an unknown question id and no `--prompt`: stop
  and ask the operator to confirm the new question text first.
- `draft-from-interview` against a session with no accepted answers:
  stop and continue the interview.

## Related installed surfaces

| Slice | Command | Role |
|-------|---------|------|
| G249  | `intent-cli guide collaborate` | Boundaries + suggested command sequence |
| G250  | `intent-cli interview next-question` / `interview record-answer` | Durable Q/A store |
| G251  | `intent-cli interview compile` / `intent draft-from-interview` | Compile accepted answers; write draft |
| G252  | `intent-cli guide rules --topic` | Current operational rules by topic |
| G241  | `intent-cli intent status` | Latest baseline, WIP, queued, clarifications |
| G242  | `intent-cli intent search` / `intent explain` | Discovery and execution-unit explainer |
| G243  | `intent-cli intent next-slice --dry-run` | Next-slice planning facts |

## What this guide is not

- It is not a replacement for the host review/closeout loop.
- It does not publish a GitHub issue. Promotion happens later via
  `intent-cli packet draft` (G244) → `intent-cli issue publish-flow`
  (G245) → host `intent-cli automation issue-publish --write` (G226).
- It does not include any `intent-cli run` invocation. `intent-cli run`
  is for integration smoke / deterministic replay / local dogfooding,
  not for collaborative intent shaping.
