# PR Comment-Fix Execution Handoff

Used when the combined loop's [`coding-automation-loop.md`](./coding-automation-loop.md)
returned `action: "pr-comment-fix"`. The AI worker takes the returned
PR URL, applies the narrow repair, pushes, and a post-run
`result-summary` step is recorded.

> **Hard rules** (do not paraphrase):
> - The PR URL/number is taken from the upstream `worker next-action`
>   result. Do NOT pick a different PR.
> - The repair MUST be narrow: address only the host's most recent
>   repair-request comment. Do NOT widen scope.
> - At most ONE PR is touched per wake.

## Inputs (from the upstream `next-action` result)

- `repo`        — `<owner/repo>`
- `number`      — PR number
- `url`         — PR URL
- `recommended_workflow` — should be `pr-comment-fix`
- `source_classification` — should be `repair-required`

## Step 1: hand off to the AI worker

Provide the AI worker with the PR URL and the most recent host repair
comment text. The worker:

1. checks out the PR's head branch,
2. applies the narrow fix the comment requested,
3. runs the project's verification (e.g. `dotnet test ... --filter ...`),
4. pushes the branch,
5. posts a single "Update — fix applied" comment on the PR.

## Step 2: capture the outcome

Classify the outcome as one of (G205 schema):

| Outcome                  | Means                                                          |
|--------------------------|----------------------------------------------------------------|
| `repair-pushed`          | A new commit was pushed; PR ready for re-review.               |
| `no-actionable-comments` | No actionable repair feedback remains.                         |
| `already-resolved`       | The PR is already at the resolved state on `origin/main`.      |
| `clarification-required` | The repair-request was ambiguous; cooldown stop, no push.      |
| `failed`                 | The worker errored before pushing.                             |
| `label-cleanup-required` | Stale labels left behind after a partial run.                  |

## Step 3: normalize via `automation complete`

```bash
intent-cli automation complete \
  --kind pr-comment-fix \
  --pr "$PR_NUMBER" \
  --outcome "$OUTCOME" \
  --format json
```

For the happy `repair-pushed` path the recommended advice swaps
`intent-pr-update-in-progress` → `intent-pr-rereview-ready` and clears
the stale `intent-pr-request-update` state on the PR. The controlling
automation applies those edits only by calling `automation complete --write`;
this prompt MUST NOT call `gh pr edit` directly.

For `clarification-required`, also render the cooldown stop:

```bash
intent-cli automation clarification-stop \
  --kind pr-comment-fix \
  --pr "$PR_NUMBER" \
  --url "$PR_URL" \
  --reason "$REASON" \
  --recommended-owner-action "$OWNER_ACTION" \
  --format json
```

## Verification (deterministic)

- `intent-cli worker next-action --format json` → a PR URL.
- AI worker pushes a single repair commit and posts the update note.
- `intent-cli automation complete` returns JSON for the
  outcome.
- The host's recurring deterministic-rereview comment is no longer
  classified as `repair-required` once the label-state advances to
  `intent-pr-rereview-ready`.

## What this template forbids

- Selecting a different PR than the one returned by `next-action`.
- Widening the repair beyond the latest host repair-request comment.
- Posting more than one "Update — fix applied" comment per wake.
- Calling `intent-cli run`.
- Asking `intent-cli` to launch the AI worker.
- Adding/removing `intent-target` on the PR (this loop never owns
  `intent-target`).
