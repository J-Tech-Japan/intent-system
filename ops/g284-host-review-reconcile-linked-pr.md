# G284 Recover selected PR review when host linked_pr metadata is missing

## Why this slice

SekibanAsAService PR #492 was selected for review by
`automation host-review-preflight`, but `intent-cli review closeout-plan`
returned `ready: false` because no parent queue item linked to PR #492.
`automation reconcile --lane host-review` flagged the missing
`linked_pr` as advisory only, and the host-loop prompt only ran reconcile
at the idle / clarification-required path. A selected PR linkage gap
must be recoverable mid-review without aborting and without manual
queue-state edits.

## What changed

### Reconcile analyzer

`AutomationReconcileAnalyzer.AnalyzeHostReview` now accepts an optional
queue-state snapshot (`IReadOnlyList<ReconcileQueueLink>`). The
`missing-linked-pr-metadata` repair is now classified as:

- **High confidence** — when the selected PR uniquely closes one source
  issue AND queue-state has exactly one item with
  `linked_issue.number` matching that issue AND its current `linked_pr`
  does not already point at the selected PR. The repair carries the
  resolved `queue_state_execution_unit`, `pr_number_to_link`, and
  `queue_state_linked_pr_url`.
- **Advisory** — backward-compat path when the host did not pass
  queue-state evidence. Still surfaces the existing
  `intent-cli closeout pr` follow-up.

A new unsafe stop kind, `ambiguous-queue-linkage`, fires when more than
one queue item references the same source issue. The host loop must
stop with structured clarification rather than guess which row should
receive `linked_pr`.

### Reconcile command

`AutomationReconcileCommand.Execute` now reads `.intent-cli/queue-state.json`
and projects the analyzer-needed slice (execution unit, source issue
repo/number, current `linked_pr`). On `--write`, high-confidence
queue-state repairs invoke a new `PatchQueueStateLinkedPr` helper that
locates the matching execution unit and writes `linked_pr` (idempotent
on an already-matching value). Existing label-drift repairs still flow
through `IGitHubLabelMutator.ApplyReconcileTransitions` unchanged.

### Host-loop guide

`guide prompt-matrix --mode host-loop` Stage 1 now includes an explicit
**Selected-PR linkage recovery** step:

> When `closeout-plan` or `guide review` returns `ready: false` because
> the parent queue has no item with `linked_pr` matching the selected
> PR, do NOT abort the review yet. Run
> `intent-cli automation reconcile --lane host-review`; if a
> high-confidence `missing-linked-pr-metadata` repair targets the
> selected PR, re-run with `--write` and **retry the same selected PR
> exactly once**. If the post-reconcile retry still returns
> `ready: false`, surface the gap as a structured operator stop. If
> reconcile reports `ambiguous-queue-linkage` or any other
> `unsafe_stop`, stop with structured clarification.

## Boundaries

- Read-only by default; `--write` mutates queue-state only when
  evidence is deterministic (single source issue + single queue item).
- Ambiguous multi-issue / multi-queue linkage stops with structured
  clarification — never writes parent state.
- Child implementation loops still cannot invoke this (G277 child-loop
  prohibition unchanged).
- No raw `gh` label fallback or manual queue-state editing in prompt
  instructions.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationReconcileCommandTests"

git diff --check
```

Six new focused tests: PR-492-shaped scenario promotes
`missing-linked-pr-metadata` to High when the queue has a unique
matching item; `--write` patches queue-state.json and marks the repair
applied; ambiguous multi-queue case emits `ambiguous-queue-linkage`
unsafe stop and never mutates queue-state; queue item already pointing
at the matching PR emits no repair; missing queue-state.json keeps the
backward-compat advisory; host-loop guide mentions selected-PR linkage
recovery, retry-once wording, and `ambiguous-queue-linkage`.
