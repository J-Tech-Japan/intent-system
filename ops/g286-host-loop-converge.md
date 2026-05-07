# G286 Host-loop convergence diagnostic

## Why this slice

Recent host wakes left complete next-slice candidates unpublished
because the loop reported `idle` or `clarification-required` even though
WIP was empty, the candidate contract was complete, and the only
"blocker" was metadata drift (stale `intent_state: open` front-matter,
unapplied safe reconcile repairs, or unsafe-stop ambiguity). The
operator had to step in for what should have been a deterministic
publish.

`automation host-review-diagnostics` already classified `true-idle` vs
the various stuck states, but it only knew about review-side and WIP
signals. It did not see the next-slice gate's stale-clarification flag,
reconcile's unsafe-stops, or unapplied high-confidence repairs, so the
host loop could not converge on a single terminal class.

## What changed

### `automation host-review-diagnostics`

Three new read-only flags collapse next-slice + reconcile signals into
the diagnostic:

- `--stale-clarification-metadata` — surfaces in `warnings` without
  flipping the terminal class. The host loop should re-stamp the file's
  front-matter to `clarified` after the publish, but the warning is not
  a stop signal (G285 + G286).
- `--reconcile-unsafe-stop <kind>` (repeatable) — when reconcile's
  dry-run output reports `unsafe_stops` (e.g. `ambiguous-queue-linkage`
  from G284), the host loop must stop with structured clarification.
- `--reconcile-repairs-available <N>` — when reconcile dry-run found N
  unapplied high-confidence repairs and no other terminal class fits,
  surface `repaired-and-retry` so the operator knows to run reconcile
  with `--write` and retry the wake.

Three new classifications join the existing set:

- `issue-publish-ready` — replaces `candidate-ready` (the constant is
  preserved for backward-compat callers but the analyzer no longer
  emits it). The `recommended_next_command` is now the deterministic
  publish chain: `intent-cli packet draft --execution-unit <id>
  --target-repo <r> --format json && intent-cli issue publish-flow
  <id> --repo <r> --write --format json && intent-cli automation
  issue-publish --repo <r> --issue <new#> --write --format json`. With
  this in place a host loop can converge from "complete queued
  candidate" to "issue published" without an extra acceptance prompt.
- `unsafe-metadata` — fires on any non-empty `reconcileUnsafeStopKinds`
  list, dominating every terminal class except
  `request-update-rereview-conflict` (which is a higher-precedence PR
  label conflict). Recommended next command is the read-only reconcile
  dry-run.
- `repaired-and-retry` — fires only when WIP is empty, no review PR is
  actionable, no clarification is required, no candidate is provided,
  and reconcile has unapplied high-confidence repairs. Recommended
  next command is `automation reconcile --write`.

Precedence (top → bottom, first match wins):

1. `stale-host-cli` (command-level surface probe)
2. `request-update-rereview-conflict`
3. `unsafe-metadata` (G286)
4. `stuck-reviewing`
5. `missing-target-on-pr`
6. `clarification-required`
7. `review-pr-actionable`
8. `wip-cap-blocked`
9. `issue-publish-ready` (G286, replaces `candidate-ready`)
10. `repaired-and-retry` (G286)
11. `true-idle`

### Host-loop / host-oneshot guide

Stage 4 of `guide prompt-matrix --mode host-loop` and `--mode
host-oneshot` now drives convergence through the new flags:

> Before reporting a no-actionable / idle wake, run
> `intent-cli automation host-review-diagnostics --repo <r> --candidate
> <id?> --format json` and pass `--clarification-required`,
> `--stale-clarification-metadata`, `--reconcile-unsafe-stop <kind>`
> (repeatable), and `--reconcile-repairs-available <N>` flags so the
> diagnostic converges on a single terminal class.

The prompt enumerates the new classifications and tells the operator
how each one routes:

- `issue-publish-ready` → run the deterministic publish chain.
- `repaired-and-retry` → reconcile with `--write` and retry.
- `unsafe-metadata` → stop with structured clarification.

## Boundaries

- Read-only: this slice only extends a read-only diagnostic surface.
  No new mutating flag.
- WIP cap unchanged.
- Hard Clarification (substantive blocker / question text) still wins
  over `issue-publish-ready`.
- Candidate contract completeness still gates `issue-publish-ready`
  (the gate stays in `intent next-slice --dry-run`).
- Existing callers of `candidate-ready` continue to compile (the
  constant is preserved); only new callers see `issue-publish-ready`.
- Raw `gh` label mutation is still forbidden in the host loop.
- `automation reconcile --write` still comes from the host loop only;
  child loops are unchanged.
- Child implementation loop behavior is unchanged.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationHostReviewDiagnostics|FullyQualifiedName~GuidePromptMatrix"

git diff --check
```

Five new focused `AutomationHostReviewDiagnosticsCommandTests` cover:

- candidate provided + no review/WIP/clarification → `issue-publish-ready`
  with the deterministic publish chain in `recommended_next_command`
  (the existing `candidate-ready` test was renamed and tightened)
- `--stale-clarification-metadata` surfaces in `warnings` without
  flipping the terminal class
- `--reconcile-unsafe-stop ambiguous-queue-linkage` →
  `unsafe-metadata`, even with a candidate and no other blocker
- `--reconcile-repairs-available 2` (no candidate) →
  `repaired-and-retry` with `automation reconcile --write` recommended
- candidate present + repairs available → `issue-publish-ready` wins
  (publish first; repairs are advisory follow-up)

One new `GuidePromptMatrixCommandTests` confirms the host-loop prompt
mentions `issue-publish-ready`, `unsafe-metadata`, `repaired-and-retry`,
and the three new diagnostic flags.

Full suite: 2081 passed, 1 skipped.
