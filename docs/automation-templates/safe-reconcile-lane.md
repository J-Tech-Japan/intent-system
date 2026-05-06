# Host-Side Safe Reconcile Lane

The **safe reconcile lane** is a host-only stage that runs *before* a host
review/next-slice wake declares `idle` or `clarification-required`. It repairs
mechanically provable intent-cli metadata drift (label misplacement and a small
set of derivable bookkeeping gaps) using evidence from GitHub state plus the
existing intent-cli artifacts. Anything that is not mechanically provable still
stops with structured clarification — reconcile never guesses.

This template is the host-side counterpart to
[`host-review-loop.md`](./host-review-loop.md) and
[`host-next-slice-loop.md`](./host-next-slice-loop.md). It is **not** part of
[`coding-automation-loop.md`](./coding-automation-loop.md); child implementation
loops MUST NOT call `automation reconcile`.

> **Hard rules** (do not paraphrase):
> - Host-only. The command refuses to run from a child loop context.
> - `--write` mutates labels via the host-owned reconcile mutator only;
>   advisory entries point at the appropriate existing surface
>   (`intent-cli closeout pr`, `intent-cli packet draft`,
>   `intent-cli intent next-slice`).
> - High-confidence repairs cover **label drift only**. Queue-state mutations,
>   PR body edits, and clarification record updates remain owned by their
>   existing host surfaces.
> - `intent-pr-created` is issue-only. The reconcile mutator allows removing
>   a misplaced one from a PR but rejects adding it.
> - Ambiguous links and any case lacking deterministic evidence stop with
>   `unsafe_stops[]` instead of being repaired.

## When to invoke

Run reconcile only when Stage 1 (review/closeout) and Stage 2 (next-slice)
would otherwise stop without action:

- `automation host-review-preflight` returned `no-actionable-item`,
- `intent next-slice --dry-run` returned `clarification-required` or
  `inspect-manually`, **or**
- a PR is open in the target repo without expected workflow labels.

## Lanes

- `--lane host-review` — inspects open PRs and intent-target issues, looking
  for label drift on the review boundary.
- `--lane next-slice` — inspects whether the next-slice classifier is stuck
  in `clarification-required` while the source clarifications report no open
  blockers.
- `--lane all` (default) — both.

## Detected drift types

| Type | Confidence | Mutation |
|------|-----------|----------|
| `missing-pr-intent-target` — PR closes a published intent-target issue but lacks the label | high | add `intent-target` to the PR |
| `misplaced-pr-intent-pr-created` — PR carries `intent-pr-created` (issue-only by policy) | high | remove `intent-pr-created` from the PR |
| `missing-issue-intent-pr-created` — open PR closes a published intent-target issue but the issue lacks the completion marker | high | add `intent-pr-created` to the issue |
| `missing-linked-pr-metadata` — PR closing-issue references uniquely identify a published issue, so `linked_pr` could be filled | advisory | followup: `intent-cli closeout pr ...` |
| `stale-next-slice-candidate-cache` — classifier returned `clarification-required` but no open Hard Clarification exists | advisory | followup: `intent-cli intent next-slice --dry-run --format json` |

`unsafe_stops[]` examples:

- `ambiguous-issue-link` — a PR is `intent-target` but has no
  `Closes` / `Fixes` / `Resolves` keyword and no closing-issue reference.
- `open-clarification-present` — the next-slice classifier returned
  `clarification-required` and at least one open Hard Clarification is still
  present in the source files.
- `stale-host-cli` — the installed CLI is missing or stale for the required
  automation command surfaces.
- `child-loop-prohibited` — emitted when `--child-loop-context` is passed,
  to make the prohibition deterministically testable.

## Dry-run plan

```bash
intent-cli automation reconcile \
  --lane host-review \
  --repo "$TARGET_REPO" \
  --format json
```

The result has shape:

```json
{
  "lane": "host-review",
  "repo": "owner/repo",
  "mode": "dry-run",
  "host_only": true,
  "safe_repairs": [
    {
      "type": "missing-pr-intent-target",
      "target_kind": "pr",
      "target_number": 420,
      "add_labels": ["intent-target"],
      "remove_labels": [],
      "evidence": [
        "PR #420 body or closing-issue references include #559",
        "issue #559 carries 'intent-target' and 'intent-pr-created' (published intent-target issue)"
      ],
      "confidence": "high",
      "applied": false,
      "summary": "Add intent-target to PR #420 (links published intent-target issue #559).",
      "requires_followup_command": null
    }
  ],
  "unsafe_stops": [],
  "warnings": [],
  "summary": "reconcile dry-run: 1 high-confidence repair(s), 0 advisory entry(ies), 0 unsafe stop(s)."
}
```

## Write mode

```bash
intent-cli automation reconcile \
  --lane host-review \
  --repo "$TARGET_REPO" \
  --write \
  --format json
```

Only `confidence: high` entries are applied. Advisory entries are emitted
unchanged and must be addressed through their `requires_followup_command`.
If the reconcile mutator rejects a transition (for example because of a hard
policy like adding `intent-pr-created` to a PR), the entry stays with
`applied: false` and a warning records the reason — the command does not fall
back to raw `gh ... edit --add-label`.

## Child-loop prohibition

`automation reconcile` does not appear in the `child-loop` or `child-oneshot`
prompt-matrix entries. The runtime guard `--child-loop-context` lets the test
suite assert that calling it from a child surface returns exit code `2` with a
`child-loop-prohibited` unsafe stop and never invokes the lister or mutator.

## Hard out-of-scope

- Semantic intent decisions.
- Modifying child implementation code.
- Guessing missing issue/PR links without deterministic evidence.
- Broad label cleanup unrelated to intent-cli workflows.
- Running from child implementation loops.
- Hiding real contract gaps as automatic repairs.
