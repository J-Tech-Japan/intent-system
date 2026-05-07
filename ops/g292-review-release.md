# G292 Make review-start a guarded lease releasable on host-metadata blockers

## Why this slice

PR #682 became stuck with `intent-pr-reviewing` while review was
blocked by missing parent `linked_pr` metadata. The label had been
applied via `pr-transition --transition review-start`, but the
subsequent `closeout-plan` was host-metadata-blocked (G287), so the
host loop had no way to drop the lease without falsely claiming an
implementation repair was needed. The PR sat with `intent-pr-reviewing`
across multiple wakes even though no review was in progress.

## What changed

### `intent-cli automation pr-transition` — new `review-release` kind

A fourth transition joins `review-start`, `request-update`, and
`approved`:

- **Add labels:** none.
- **Remove labels:** `intent-pr-reviewing` (defensive — only removed
  when actually present, mirroring the G292-style hardening
  `review-start` already applies in write mode).

The transition is for the host loop to release the reviewer lease
when host-owned metadata blocks closeout. It never adds
`intent-pr-request-update`, `intent-pr-approved`, or
`intent-pr-rereview-ready`, so the implementer is never told to
repair host metadata they cannot reach (G287).

The argument validator, error messages, and help text now accept
`review-start | request-update | approved | review-release`.

### Host-loop guide

Stage 1 of `guide prompt-matrix --mode host-loop` and `--mode
host-oneshot` adds the explicit release step right after the G287
host-metadata-blocked block:

> **Release the review lease on host-metadata blockers (G292)**: if
> `review-start` was already applied to the PR (so
> `intent-pr-reviewing` is on it) and host metadata then blocks the
> wake, run `intent-cli automation pr-transition --transition
> review-release --repo <r> --pr <n> --write --format json` to drop
> `intent-pr-reviewing` cleanly without adding
> `intent-pr-request-update`. The next wake reselects the PR after
> reconcile completes. Never leave a PR stuck with
> `intent-pr-reviewing` while no review is in progress.

## Boundaries

- `review-release` only changes labels — no merge, no comment, no
  durable-state mutation, no PR title/body change.
- The implementer never receives a PR comment or `request-update` from
  this path. It is host-side cleanup only.
- Backward compatible: existing `review-start` / `request-update` /
  `approved` transitions are unchanged.
- The transition is idempotent — re-running on a PR that no longer
  carries `intent-pr-reviewing` is a no-op (label list is empty).
- WIP cap, Hard Clarification, and the no-raw-`gh` rule are unchanged.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationPrTransition|FullyQualifiedName~GuidePromptMatrix"

git diff --check
```

Three new focused `AutomationPrTransitionCommandTests` cover:

- Stuck PR (intent-target + intent-pr-reviewing) → `review-release`
  with `--write` removes only `intent-pr-reviewing`. No
  `intent-pr-request-update`/`intent-pr-approved`/`intent-pr-rereview-ready`
  is added. Critical regression guard.
- Dry-run `review-release` reports the same plan: empty `add_labels`,
  `intent-pr-reviewing` in `remove_labels`.
- Idempotent: writing `review-release` on a PR without
  `intent-pr-reviewing` succeeds and produces an empty mutation plan.

One new `GuidePromptMatrixCommandTests` confirms the host-loop prompt
mentions `review-release`, the
`Release the review lease on host-metadata blockers (G292)` heading,
and `intent-pr-reviewing`.

Full suite: 2108 passed, 1 skipped.
