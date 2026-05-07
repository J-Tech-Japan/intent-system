# G283 Guarantee PR review publication during worker issue-to-pr completion

## Why this slice

PR #668 was created and merged for G282, source issue #667 received
`intent-pr-created`, but the PR itself had no `intent-target` label. Host
review automation selects PRs through PR-side `intent-target`, so the PR
bypassed the intended review queue. This linkage requirement must be
guaranteed by `intent-cli` completion, not by prompts remembering label
rules.

## What changed

`intent-cli worker complete --kind issue --outcome pr-created` now requires
the new `--pr <n>` argument and atomically publishes the created PR to the
host review queue:

1. **Source issue update (existing)** — adds `intent-pr-created`, removes
   `intent-issue-in-progress` on the source issue.
2. **PR review publication (new)** — applies `intent-target` to the
   created PR through the existing `IGitHubLabelMutator`. The PR-side
   write happens only after the issue-side write succeeded.
3. **Parent metadata sync (new)** — patches `linked_pr` in
   `.intent-cli/queue-state.json` for the execution unit whose
   `linked_issue.number` matches the source issue. Re-runs are idempotent
   on an already-matching `linked_pr`.

If `--pr` is missing on `--kind issue --outcome pr-created`, the command
returns a usage error with no GitHub mutation. If the PR-side
`intent-target` write fails, the result reports `proceed: false`,
`applied: false`, `pr_target_applied: false`, exit code `2`, and a clear
error pointing the operator at `intent-cli automation reconcile`. If the
PR write succeeds but the queue-state sync fails (queue-state.json
missing or no matching execution unit), the result keeps
`proceed: true` / `pr_target_applied: true`, sets `linked_pr_synced:
false`, and emits a structured warning so the operator can repair the
host's parent state without re-running the GitHub side.

`intent-pr-created` continues to be issue-only. The mutator's defensive
guard already rejects adding it to a PR; this slice adds the explicit
PR-side `intent-target` application that PR review preflight depends on.

## Result schema additions

`WorkerCompleteResult` gains three optional fields populated only on
`--kind issue --outcome pr-created`:

- `pr_number` — the PR number paired with the issue completion.
- `pr_target_applied` — `true` when PR-side `intent-target` was applied;
  `false` when the PR write failed (errors include the failure detail).
- `linked_pr_synced` — `true` when queue-state.json was successfully
  updated; `false` with a warning when the file is absent or has no
  matching execution unit.

## Boundaries

- No raw `gh` label fallback; everything routes through the existing
  `IGitHubLabelMutator`.
- No PR merging, closing, or branch creation.
- `intent-pr-created` never lands on a PR.
- Issue-side completion is applied first; PR-side `intent-target` only
  follows on success. Failure paths surface the partial state instead
  of claiming success.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~WorkerCompleteCommandTests"

git diff --check
```

Five new focused tests cover: `--pr` missing returns usage error and
no GitHub call; full success applies issue-side swap, PR-side
`intent-target`, and queue-state `linked_pr`; PR-side mutator failure
reports incomplete with reconcile guidance; queue-state absent emits
warning but keeps the PR-side success; idempotent rerun does not double-
sync `linked_pr`. Existing pr-created tests were updated to pass `--pr
<n>` per the new contract.
