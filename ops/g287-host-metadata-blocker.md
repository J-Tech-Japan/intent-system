# G287 Do not convert host metadata blockers into PR repair comments

## Why this slice

PR #670 received an implementation-review comment asking the implementer
to repair parent-host metadata (`closeout-plan` could not find a queue
item with `linked_pr` matching the PR). The implementer cannot rewrite
the parent host's durable state from a PR branch; turning that gap into
`intent-pr-request-update` sends the child loop on impossible work and
creates unnecessary update cycles.

`review closeout-plan` already surfaced gaps, but every gap was a flat
string. The host loop had no deterministic way to tell host-metadata
drift apart from a real implementation finding, so its guide wording
turned every `ready: false` into a PR comment.

## What changed

### `review closeout-plan`

Each gap is now classified at write time:

- `host-metadata` — parent durable-state drift the implementer cannot
  repair: missing/invalid `queue-state.json`, no queue item with
  `linked_pr` matching the PR (the PR #670 case), missing
  `linked_issue` on the matched item, missing packet directory.
- `implementation-review` — packet content the implementer can address
  by amending the PR head: `Child Issue Contract is incomplete;
  sections missing: ...`.

The aggregate `blocker_classification` field summarises the wake:

- `ready` — `gaps` is empty and a queue item matched.
- `host-metadata-blocked` — at least one gap is `host-metadata`.
  Dominates `implementation-review-finding` when both kinds are
  present (the host loop must run reconcile first; the implementer
  cannot fix parent metadata regardless of any contract gap).
- `implementation-review-finding` — only implementation-review gaps
  are present.

`recommended_recovery_command` is set for `host-metadata-blocked` to the
deterministic host recovery: `intent-cli automation reconcile --lane
host-review --repo <r> --format json` (re-run with `--write` if a
high-confidence repair exists). It is absent for `ready` and for
`implementation-review-finding`.

The flat `gaps: string[]` field is preserved on the result for
backward compatibility; the new structured detail lives in
`classified_gaps: { description, classification }[]`.

### Host-loop / host-oneshot guide

Stage 1 of `guide prompt-matrix --mode host-loop` and `--mode
host-oneshot` now gates PR comments and `request-update` on the
classification:

> **Host-metadata blockers do NOT become PR repair comments (G287)**:
> when `review closeout-plan` returns `ready: false` with
> `blocker_classification: host-metadata-blocked` (e.g. `no queue item
> found with linked_pr matching #<n>`, missing `linked_issue`,
> missing/invalid queue-state, missing packet directory), do NOT post
> a PR comment and do NOT call `pr-transition --transition
> request-update`. The implementer cannot repair parent host metadata
> from the PR branch. Instead run the `recommended_recovery_command`
> (typically `intent-cli automation reconcile --lane host-review`
> followed by `--write` if a high-confidence repair exists) and retry
> the wake. If reconcile reports unsafe stops or no high-confidence
> repair, surface a structured operator stop.
>
> If review needs repair AND `blocker_classification:
> implementation-review-finding` (real code/contract gap the
> implementer can fix on the PR branch): leave an actionable PR
> comment, then `intent-cli automation pr-transition --transition
> request-update --repo <r> --pr <n> --write`.

## Boundaries

- Read-only on the closeout-plan side: this slice only adds
  classification fields.
- The flat `gaps: string[]` field is preserved for backward-compat;
  existing tests that assert on `gaps` still pass.
- Real code/contract review findings (e.g. missing required contract
  sections in the PR's published body) still surface as
  `implementation-review-finding` and may flow through the existing PR
  comment / `request-update` path.
- WIP cap, Hard Clarification, and the `automation reconcile --write`
  host-only invariant are unchanged.
- Child implementation loop behavior is unchanged.
- No raw `gh` label mutation.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~ReviewCloseoutPlan|FullyQualifiedName~GuidePromptMatrix"

git diff --check
```

Four new focused `ReviewCloseoutPlanCommandTests` cover:

- PR #670-shaped: queue exists but no item has `linked_pr` matching the
  selected PR → `host-metadata-blocked` with reconcile recovery
  command and `host-metadata`-classified gap.
- Real implementation finding: missing required contract sections →
  `implementation-review-finding`, no host-recovery command.
- Both kinds present (host metadata drift AND missing contract
  sections) → `host-metadata-blocked` wins; the implementer is not
  asked to fix what they cannot reach.
- Ready: no gaps, queue item matched → `blocker_classification: ready`,
  no recovery command.

Two new `GuidePromptMatrixCommandTests` confirm host-loop and
host-oneshot prompts mention both classifications, the
`blocker_classification` field, and the explicit `do NOT post a PR
comment` rule for host-metadata-blocked wakes.

Full suite: 2087 passed, 1 skipped.
