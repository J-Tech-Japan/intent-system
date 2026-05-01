# Combined Coding Automation Loop

Use this prompt at the top of one wake of the local coding automation
loop. It picks at most ONE target via `intent-cli automation check`
and dispatches to the appropriate execution-handoff template.

> **Hard rules** (do not paraphrase):
> - Use `intent-cli automation check --format json` to choose work.
>   Do NOT walk labels manually in this prompt.
> - At most ONE branch/PR per wake.
> - `intent-cli` MUST NOT launch any AI provider. The external AI
>   worker runs separately on the URL this command returns.
> - Do NOT call `intent-cli run` from this path.

## Inputs

- `--repo <owner/repo>` — optional child repo override. When omitted,
  `intent-cli automation check` infers it from the current worktree or
  `--workdir`.
- `--workdir <path>` — optional child worktree path.

## Step 1: pick the next action

```bash
intent-cli automation check --format json
```

When the controlling loop runs outside the child worktree, pass the
child path explicitly:

```bash
intent-cli automation check --workdir "$WORKDIR" --format json
```

The output has a stable shape (G206):

```json
{
  "action": "pr-comment-fix" | "issue-to-pr" | "none",
  "repo": "...",
  "number": <int|null>,
  "url": "<github URL|null>",
  "reason": "...",
  "recommended_workflow": "pr-comment-fix" | "gh-issue-to-pr" | null,
  "recommendedWorkflow": "...",
  "warnings": [...],
  "source_classification": "...",
  "sourceClassification": "..."
}
```

## Step 2: dispatch on `action`

| `action`           | Handoff template                                 |
|--------------------|--------------------------------------------------|
| `pr-comment-fix`   | [`pr-comment-fix-execution.md`](./pr-comment-fix-execution.md) — repair the returned PR URL |
| `issue-to-pr`      | [`issue-to-pr-execution.md`](./issue-to-pr-execution.md) — implement the returned issue URL via the `gh-issue-to-pr` workflow |
| `none`             | End the wake. NO push. NO label mutation. Emit a `status: idle` summary line. |

If `warnings[]` is non-empty, surface each warning verbatim in the wake
summary so the operator sees label-policy issues such as
`intent-pr-created` being misplaced on a PR.

## Step 3: post-run completion (after the AI worker returns)

When the dispatched handoff has finished and produced an outcome string
(e.g. `pr-created`, `repair-pushed`, `clarification-required`, `failed`),
ask the operator (or the controlling automation) to run:

```bash
intent-cli automation complete \
  --kind <issue-to-pr|pr-comment-fix> \
  [--issue <n>] [--pr <n>] \
  --outcome <outcome> \
  --format json
```

By default this is dry-run and emits `recommended_label_actions[]`,
`planned_label_actions[]`, `applied_label_actions[]`, `warnings[]`,
and `summary`. To apply the supported completion label transition,
the controlling automation must pass `--write` explicitly. Do NOT
mutate labels outside the `automation complete --write` path.

If the outcome is `clarification-required`, also emit an owner-facing
cooldown summary:

```bash
intent-cli automation clarification-stop \
  --kind <issue-to-pr|pr-comment-fix> \
  --number <n> \
  --url "$TARGET_URL" \
  --reason "$REASON" \
  --recommended-owner-action "$OWNER_ACTION" \
  --format json
```

This helper is read-only. It exists so the wake summary has stable
fields instead of free-form prompt prose.

## Idempotency

This template is fully idempotent: running it again on a state where
`next-action` returns `none` is a no-op. Running it during a wake whose
selected work is already in progress (in-progress label set) returns
`none` and ends the wake.

## What this template forbids

- Manually selecting a target by reading labels in natural language.
- Calling `intent-cli run` instead of an external AI worker.
- Asking `intent-cli` to launch Claude or Codex.
- Adding `intent-pr-created` to a PR (it belongs on the source issue).
- Touching more than one branch/PR per wake.
