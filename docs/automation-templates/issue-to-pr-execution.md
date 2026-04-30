# Issue-to-PR Execution Handoff

Used when the combined loop's [`coding-automation-loop.md`](./coding-automation-loop.md)
returned `action: "issue-to-pr"`. The AI worker (Claude / Codex) takes
the returned issue URL and runs the `gh-issue-to-pr` workflow against
it, then a post-run `result-summary` step is recorded.

> **Hard rules** (do not paraphrase):
> - The issue URL/number is taken from the upstream
>   `worker next-action` result. Do NOT pick a different target.
> - `intent-pr-created` belongs on the source ISSUE, not on the PR
>   that gets created. The post-run `result-summary` will surface this
>   as a warning if it appears on the PR.
> - At most ONE PR is created per wake.

## Inputs (from the upstream `next-action` result)

- `repo`        — `<owner/repo>` of the child repo
- `number`      — issue number
- `url`         — issue URL
- `recommended_workflow` — should be `gh-issue-to-pr`
- `source_classification` — should be `ready-to-implement`

## Step 1: hand off to the AI worker

The external AI worker runs the `gh-issue-to-pr` workflow on the
returned issue URL. The prompt to the AI worker should include:

- the issue URL verbatim,
- a reminder that this worker MUST NOT add `intent-target` to the
  created PR,
- a reminder that `intent-pr-created` (when applied) belongs on the
  source issue, not on the PR.

## Step 2: capture the outcome

After the AI worker returns, classify the outcome as one of (G205
schema):

| Outcome                          | Means                                                  |
|----------------------------------|--------------------------------------------------------|
| `pr-created`                     | New draft PR opened against `origin/main`.             |
| `declined-contract-incomplete`   | The issue body was insufficient; AI worker stopped.    |
| `clarification-required`         | Contract was ambiguous; cooldown stop.                 |
| `already-resolved`               | Already on `origin/main`; nothing to do.               |
| `failed`                         | Worker stopped with an error mid-run.                  |
| `label-cleanup-required`         | Stale labels left behind after a partial run.          |

## Step 3: normalize via `result-summary`

```bash
intent-cli worker result-summary \
  --kind issue-to-pr \
  --repo "$REPO" \
  --issue "$ISSUE_NUMBER" \
  [--pr "$PR_NUMBER"] \
  --outcome "$OUTCOME" \
  --format json
```

This emits stable `recommended_label_actions[]` advice — for the
happy `pr-created` path it will recommend:

- remove `intent-issue-in-progress` from the source issue,
- add `intent-pr-created` to the source issue.

The controlling automation applies those edits via its own gh layer.
The prompt here MUST NOT call `gh issue edit` or `gh pr edit` directly.

## Verification (deterministic)

- `intent-cli worker next-action --format json` → an issue URL.
- AI worker runs `gh-issue-to-pr` against that URL.
- `intent-cli worker result-summary` returns `valid: true` and an
  outcome whose `status` is one of `completed` / `declined` /
  `clarification-required` / `failed` / `label-cleanup-required`.
- No additional GitHub mutations from this prompt.

## What this template forbids

- Selecting a different issue than the one returned by `next-action`.
- Adding `intent-target` to the created PR.
- Adding `intent-pr-created` to the PR (it belongs on the source issue).
- Calling `intent-cli run`.
- Asking `intent-cli` to launch the AI worker.
