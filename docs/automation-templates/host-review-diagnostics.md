# Host Review/Next-Slice Recovery Diagnostics

Recent host loop failures became hard to interpret: PRs could retain
`intent-pr-reviewing`, the preflight could time out or abort, and operators
could not distinguish a correct **idle** wake from **stale metadata** or an
**in-progress recovery need**. The new
`intent-cli automation host-review-diagnostics` command exposes a read-only
classification so the operator can tell why the host loop did not advance
before the wake reports a no-actionable / idle outcome.

This template is a host-only diagnostic surface. It pairs with
[`host-review-loop.md`](./host-review-loop.md) (which actually selects a
target) and [`safe-reconcile-lane.md`](./safe-reconcile-lane.md) (which
repairs label drift). The diagnostic command itself never mutates labels,
queue state, runs.jsonl, packet files, or any GitHub state.

> **Hard rules**:
> - Read-only. The command must never mutate labels or parent files.
> - Host-only by intent: the new Stage 4 wording in the host-loop / host-oneshot
>   prompt is the only place the agent loop is told to call it.
> - When `classification` is anything other than `true-idle`, the loop MUST
>   surface the classification (and any `recommended_next_command` /
>   `structured_clarification`) instead of saying "no-actionable".

## Classifications

| Classification | Meaning | Recommended next surface |
|----------------|---------|--------------------------|
| `true-idle` | No host review work, no in-flight intent-target item, no next-slice candidate. The host loop is correctly idle. | (none) |
| `stale-host-cli` | Installed CLI surface is missing required automation commands. | `intent-cli automation doctor --format json` and refresh the installed CLI |
| `stuck-reviewing` | A PR carries `intent-pr-reviewing` with no exit-transition label. | `intent-cli automation pr-transition --transition request-update --write` (or `--transition approved`) |
| `missing-target-on-pr` | A PR closes a published intent-target issue but lacks `intent-target`. | `intent-cli automation reconcile --lane host-review --write` |
| `request-update-rereview-conflict` | A PR carries both `intent-pr-request-update` and `intent-pr-rereview-ready`. | structured clarification: operator picks which state is intent |
| `clarification-required` | The host preflight was told a clarification is required. | resolve the source clarification |
| `wip-cap-blocked` | An open intent-target issue or PR is still in flight; new next-slice publication is blocked. | wait for closeout or operate on the in-flight item |
| `review-pr-actionable` | An eligible intent-target PR is present; preflight should not have returned no-actionable. | `intent-cli automation host-review-preflight --format json` |
| `candidate-ready` | No host review work and no WIP; a next-slice candidate was supplied. | `intent-cli packet draft --execution-unit <id> --target-repo ... --dry-run` |

## Usage

```bash
intent-cli automation host-review-diagnostics \
  --repo "$TARGET_REPO" \
  --format json
```

Optional inputs:

- `--workdir <path>` — inferred when omitted from the current cwd.
- `--candidate <execution-unit>` — when the host loop has a next-slice
  candidate, surfaces `candidate-ready` instead of `true-idle`.
- `--clarification-required` — explicit operator hint that a clarification
  is open; renders `clarification-required`.

The result has shape:

```json
{
  "repo": "owner/repo",
  "classification": "stuck-reviewing",
  "summary": "PR #490 appears to be stuck mid-review...",
  "read_only": true,
  "recommended_next_command": "intent-cli automation pr-transition --transition request-update --repo owner/repo --pr 490 --write --format json (or --transition approved when review is complete)",
  "structured_clarification": null,
  "details": [
    {
      "kind": "stuck-reviewing",
      "target_kind": "pr",
      "target_number": 490,
      "target_url": "https://github.com/owner/repo/pull/490",
      "description": "PR #490 carries 'intent-pr-reviewing' with no exit-transition label..."
    }
  ],
  "warnings": []
}
```
