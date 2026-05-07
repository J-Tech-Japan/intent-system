# G291 Promote unambiguous closing-issue linked_pr repair to high confidence

## Why this slice

PR #682 for G289 was selected and review-actionable, but the host wake
stopped because parent queue-state lacked `linked_pr` for the G289 row.
The PR body said `Closes #681` and queue-state had G289 linked to issue
#681, yet `automation reconcile --lane host-review` did not produce an
unambiguous, writeable repair. G284 had already laid the groundwork for
the high-confidence path, but the analyzer's branch handling did not
distinguish "linked_pr is empty" (safe to write) from "linked_pr points
at a different PR" (unsafe — would silently clobber review state).

## What changed

### `AutomationReconcileAnalyzer` — three-way branch on existing linked_pr

The G284 high-confidence promotion now splits the matched-queue-item
case into three deterministic branches:

1. **`match.LinkedPrUrl == prUrl`** — no drift. The queue row already
   points at this PR. Skip silently (no advisory, no high-confidence
   repair, no unsafe stop). Idempotent on reruns.
2. **`match.LinkedPrUrl` is empty / whitespace** — high-confidence
   write. PR closes the source issue uniquely AND queue-state has
   exactly one matching row AND `linked_pr` is unset. The repair
   carries `confidence: high`, `target_kind: queue-state`, and the
   evidence line now explicitly reads `no current linked_pr (empty)`
   so the deterministic conditions are visible in dry-run output.
3. **`match.LinkedPrUrl` is non-empty and ≠ `prUrl`** — new
   `conflicting-linked-pr` unsafe stop. Two PRs claiming the same
   queue row is unsafe to repair without operator confirmation;
   overwriting could lose review history. The unsafe stop's
   `missing_evidence` lists both the existing `linked_pr` URL and the
   incoming closing-PR URL so the operator can decide which row should
   stay.

`AutomationReconcileUnsafeStopKinds.ConflictingLinkedPr` is the new
constant. Existing G284 behavior for empty `linked_pr` and the
multi-queue-item ambiguous case is unchanged.

## Boundaries

- Read-only on the analyzer side; the existing `--write` path still
  refuses to overwrite a non-empty linked_pr because the analyzer no
  longer emits a high-confidence repair in that case (it emits an
  unsafe stop instead).
- Backward compatible for the G284 empty-linked_pr case: that path
  still produces the same high-confidence write.
- Idempotent for the matching-linked_pr case (no repair, no warning).
- Multi-closing-issue and multi-queue-item ambiguous cases are
  unchanged (G284 already handled them).
- No raw `gh` mutation. Host-loop guide already routes
  `unsafe_stops` to a structured operator stop (G286).

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationReconcile"

git diff --check
```

Three new focused `AutomationReconcileCommandTests` cover:

- PR #682 / issue #681 / G289 shape with empty `linked_pr` →
  high-confidence repair with the new
  `no current linked_pr (empty)` evidence line. No
  `conflicting-linked-pr` unsafe stop.
- Conflicting linked_pr (queue item pointed at PR #600 but PR #682
  closes the same issue) → `conflicting-linked-pr` unsafe stop with
  both URLs in `missing_evidence`. No high-confidence repair emitted.
- Idempotent regression: queue item's linked_pr already equals the
  closing PR URL → neither repair nor unsafe stop.

Existing G284 tests (high-confidence empty case, ambiguous-queue-linkage,
already-matching, advisory backward-compat) continue to pass.

Full suite: 2104 passed, 1 skipped.
