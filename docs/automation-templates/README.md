# Local Coding Automation Prompt Templates

Operator-dogfooding-grade prompt templates that drive the local
Claude/Codex coding automation loop entirely through the deterministic
`intent-cli` worker and metadata commands. The templates are
intentionally narrow in scope: they capture the prompt text an operator
hands to the AI worker, not the AI worker behavior itself.

These templates assume:

- `intent-cli` is on the operator's `PATH`, or invoked through a pinned
  wrapper exposed by the operator's `MyIntentHost` setup.
- The local automation loop reads an `intent-cli worker next-action`
  classification and runs at most ONE workflow per wake.
- All target selection, label policy, and outcome normalization is done
  by `intent-cli`; prompts must NOT reimplement label-selection logic in
  natural language.

## Layered intent-cli surface (consumed by these templates)

| Slice | Command | Role |
|-------|---------|------|
| G202  | `intent-cli worker issue-preflight`     | "Is this issue actionable?" |
| G203  | `intent-cli worker pr-review-preflight` | "Is this PR ready to review?" |
| G204  | `intent-cli worker pr-comment-preflight`| "Does this PR need repair?" |
| G205  | `intent-cli worker result-summary`      | Normalize a worker run's outcome |
| G206  | `intent-cli worker next-action`         | Pick at most one target |
| G207  | `intent-cli metadata validate`          | Mechanical packet validator |
| G208  | `intent-cli metadata update`            | Bounded controlled writer |
| G213  | `intent-cli automation check`           | Worktree-aware next-action entrypoint |
| G214  | `intent-cli automation complete`        | Worktree-aware outcome completion entrypoint |
| G215  | `intent-cli automation clarification-stop` | Stable clarification-required stop summary |
| G226  | `intent-cli automation issue-publish`   | Host issue publish label transition |
| G227  | `intent-cli automation host-review-preflight` | Host PR review target preflight |
| G228  | `intent-cli automation pr-transition --transition review-start` | Host review-start PR transition |
| G229  | `intent-cli automation pr-transition --transition request-update` | Host request-update PR transition |
| G230  | `intent-cli automation pr-transition --transition review-start` | Robust absent-rereview-label cleanup |
| G232  | host transition smoke examples | Read-only local verification before command-only host runbooks |

## Installed host transition command adoption

Parent-host runbooks should use installed `intent-cli` transition
commands for supported mechanical label changes. Raw `gh issue edit` /
`gh pr edit` label mutation is not the normal path for these
transitions.

Before applying host-side label transitions, detect stale local CLI
installations with the read-only command surfaces:

```bash
intent-cli automation doctor --format json
intent-cli automation host-review-preflight --repo "$CHILD_REPO" --format json
```

Both commands must report the host-local installed CLI path and all
required installed surfaces as available: `automation summary`,
`automation host-review-preflight`, `automation issue-publish`, and
`automation pr-transition` for `review-start`, `request-update`, and
`approved`. If either command reports `stale-host-cli` or a missing
surface, abort and refresh the installed CLI; do not substitute raw
`gh` label mutation.

Supported installed transitions:

```bash
intent-cli automation issue-publish \
  --repo "$CHILD_REPO" \
  --issue "$ISSUE_NUMBER" \
  --write \
  --format json

intent-cli automation pr-transition \
  --repo "$CHILD_REPO" \
  --pr "$PR_NUMBER" \
  --transition review-start \
  --write \
  --format json

intent-cli automation pr-transition \
  --repo "$CHILD_REPO" \
  --pr "$PR_NUMBER" \
  --transition request-update \
  --write \
  --format json

intent-cli automation pr-transition \
  --repo "$CHILD_REPO" \
  --pr "$PR_NUMBER" \
  --transition approved \
  --write \
  --format json
```

`intent-pr-created` remains issue-only. It must never be added to a PR
by a template, fallback, or installed transition command.

For local smoke testing before enabling command-only host runbooks, use
[`dry-run-checklist.md`](./dry-run-checklist.md#host-transition-command-smoke).

## Template index

The templates are split into two groups by **side** of the host ↔
child boundary. Child-side templates author or repair implementation
output; host-side templates review or plan. The two sides MUST NOT
be mixed inside a single wake — see each template's "Boundary
against …" section for the explicit comparison tables.

### Child-side (implementation repo)

| File | Path | What it covers |
|------|------|----------------|
| [`coding-automation-loop.md`](./coding-automation-loop.md)         | combined loop | One wake: choose between PR repair / issue-to-PR / none |
| [`issue-to-pr-execution.md`](./issue-to-pr-execution.md)           | issue handoff | Run `gh-issue-to-pr` workflow on the returned issue URL, summarize result |
| [`pr-comment-fix-execution.md`](./pr-comment-fix-execution.md)     | PR repair handoff | Apply repair on the returned PR URL, summarize result |
| [`metadata-safety.md`](./metadata-safety.md)                       | metadata boundaries | Validate before / after, controlled update only when explicit |
| [`dry-run-checklist.md`](./dry-run-checklist.md)                   | operator dry-run | Read-only pre-flight to confirm wrapper / commands / no-action / metadata wiring before arming the loop |

### Host-side (parent-host repo)

| File | Path | What it covers |
|------|------|----------------|
| [`host-review-loop.md`](./host-review-loop.md)                     | host review | Per-wake review verdict on a child PR (accept / request-update / accept-as-rereview-ready / clarification) with the matching label transition |
| [`host-next-slice-loop.md`](./host-next-slice-loop.md)             | host planning | Post-merge metadata closeout + pick at most one next-slice candidate (planning artifact only — no implementation, no provider launch) |

## Hard rules these templates enforce

- **Single source of truth for target selection**: prompts must call
  `intent-cli automation check --format json` (or the lower-level
  `worker next-action` wrapper path) and act on its result.
  No manual label-walking in the prompt body.
- **No provider launch from `intent-cli`**: `intent-cli` is deterministic
  support tooling; it must NEVER spawn Claude / Codex / any AI provider.
  External AI workers run separately and consume `intent-cli` JSON.
- **Do not call `intent-cli run`** from this local coding automation
  path. `intent-cli run` is a separate command family with different
  semantics and is out of scope here.
- **Label policy invariant**: `intent-pr-created` belongs on the source
  ISSUE, not on the PR. Prompts and post-run summaries that imply
  otherwise are a bug — `automation complete` / `worker result-summary`
  surface this as a warning automatically when they detect misuse.
- **Review-target propagation**: after an issue-to-PR success,
  `automation complete --write` is the supported path that marks the
  created PR with `intent-target` for host review. The completion call
  must pass the created PR number with `--pr`; if the worker reports a
  PR URL, resolve or extract the number before completion. Prompts must
  not add that PR label directly.
- **Single-target cap**: at most ONE branch/PR is touched per wake. If
  `worker next-action` returns `none`, the wake is idle and ends without
  pushing anything.

## Host-side hard rules (in addition to the rules above)

The host-side templates ([`host-review-loop.md`](./host-review-loop.md)
and [`host-next-slice-loop.md`](./host-next-slice-loop.md)) keep the
same `intent-cli` invariants and add:

- **Side discipline**: host-side templates run on the parent-host
  side. They MUST NOT author or push child-repo implementation
  changes. Coding-automation work belongs to
  [`coding-automation-loop.md`](./coding-automation-loop.md).
- **Bounded writes only into host packets**: any host-packet
  mutation goes through `intent-cli metadata update` with an
  explicit supported transition mode (currently
  `completed-closeout`). See
  [`metadata-safety.md`](./metadata-safety.md).
- **No cross-loop shortcuts**: the host review loop and the host
  next-slice loop run as separate wakes. Neither calls the
  child-side coding-automation loop directly; the implementation
  repo's child loop picks up newly-published `intent-target` issues
  via `intent-cli worker next-action` on its own next wake.

## What is intentionally out of scope here

- new worker commands;
- changes to `intent-cli run`;
- scheduler / cron registration;
- mutating GitHub issues or PRs from coding-automation prompts;
- writing into the parent-host `MyIntentHost` repository from
  child-side coding-automation prompts (host-side templates do
  write — but only via the bounded `metadata update` surface);
- distributing these templates as a public package.
