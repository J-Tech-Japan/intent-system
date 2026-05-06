# G277 Host-side safe reconcile lane

## Why this slice

Host review/next-slice loops were correctly stopping when intent-cli
metadata was inconsistent, but the behavior was too rigid for obvious
intent-cli-generated drift: missing `intent-target` on an intent-cli-created
PR, a misplaced `intent-pr-created` on a PR, a missing source-issue
completion marker, or a next-slice candidate cache that says
`clarification-required` when no open Hard Clarification exists. These are
host-owned bookkeeping problems, not child implementation problems, and
should be repaired host-side before the wake declares idle / hard
clarification.

## What changed

- `intent-cli automation reconcile` is the new host-only command that
  produces a structured dry-run plan of mechanically provable label drift
  and applies high-confidence label repairs through the host-owned
  reconcile mutator on `--write`.
- `IGitHubLabelMutator.ApplyReconcileTransitions` is the reconcile-only
  mutator entry point: it allows removing a misplaced `intent-pr-created`
  from a PR but still rejects adding it.
- `intent-cli guide prompt-matrix --mode host-loop` now includes a
  Stage 3 "safe reconcile" entry. `child-loop` and `child-oneshot`
  prompts are unchanged — child implementation loops MUST NOT invoke
  reconcile.
- New template:
  [`docs/automation-templates/safe-reconcile-lane.md`](../docs/automation-templates/safe-reconcile-lane.md).

## Boundaries

- Mechanically provable label drift only.
  - `missing-pr-intent-target` — high
  - `misplaced-pr-intent-pr-created` — high
  - `missing-issue-intent-pr-created` — high
- Advisory entries do not mutate; they emit a `requires_followup_command`
  pointing at the existing surface that owns the underlying mutation:
  - `missing-linked-pr-metadata` → `intent-cli closeout pr`
  - `stale-next-slice-candidate-cache` → `intent-cli intent next-slice --dry-run`
- Ambiguous cases stop with `unsafe_stops[]` rather than guess.
- Child-loop prohibition is testable via `--child-loop-context` (exit `2`,
  no GitHub or mutator side-effects), and is also enforced by the
  prompt-matrix omission for `child-loop` / `child-oneshot`.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationReconcileCommandTests"

git diff --check
```

The focused tests cover safe repair plans, dry-run vs `--write` gating,
child-loop prohibition, ambiguous-link unsafe stops, next-slice clarified
vs open-clarification handling, stale-host-cli refusal, command router
registration, and the `child-loop` / `child-oneshot` prompt omission of the
new command.
