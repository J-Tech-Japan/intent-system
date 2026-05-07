# G288 Operator-approved queue warming beyond WIP cap

## Why this slice

A host wake reported `wip-cap-blocked` while issue #677 remained open
with `intent-target`. The product owner explicitly wanted to keep the
child implementation queue warm with the next prepared packet. The CLI
had no auditable, single-shot way to publish under those circumstances —
only suppressing the WIP cap globally or hand-editing labels, both of
which violate the existing automation contract.

## What changed

### `automation host-review-diagnostics`

A new `--allow-wip-cap-override` flag opts the wake into operator-
approved queue warming. Default behavior is unchanged.

With the flag:

- If a complete candidate is provided AND no review/clarification/
  unsafe-metadata blocker is present, the WIP-cap branch routes to
  `issue-publish-ready` (instead of `wip-cap-blocked`).
- `warnings` gains `wip-cap-overridden` so the audit trail is explicit.
- `details` gains both a `wip-cap-blocked` entry listing the in-flight
  intent-target items that were bypassed at override time AND the
  `issue-publish-ready` entry naming the candidate execution unit.
- `recommended_next_command` is the standard publish chain (`packet
  draft` → `issue publish-flow --write` → `automation issue-publish
  --write`), so at most one prepared issue lands per invocation.

Without the flag:

- Default WIP-cap behavior is unchanged. An open `intent-target` issue
  or PR still produces `wip-cap-blocked` and no override warning.

The override never bypasses other gates:

- Hard Clarification still wins (`clarification-required`).
- Contract completeness is enforced upstream by `intent next-slice
  --dry-run`; the override only fires when the host has already
  determined a candidate is publish-ready.
- Unsafe metadata (reconcile `unsafe_stops`) still wins.
- Without a candidate, `--allow-wip-cap-override` is a no-op and the
  wake remains `wip-cap-blocked`.

### Host-loop / host-oneshot guide

The host-loop hard rules now name the override as the only legitimate
path past the WIP cap, and emphasise that it requires an explicit
operator ask:

> **Operator-approved queue warming (G288)**: only when the operator
> explicitly asks to keep the child queue warm beyond the cap, pass
> `--allow-wip-cap-override` to `automation host-review-diagnostics`.
> With that flag and a complete candidate, the diagnostic returns
> `issue-publish-ready` with `wip-cap-overridden` in `warnings`. The
> override publishes at most one prepared next-slice issue per wake;
> clarification gates and contract completeness are still hard blockers,
> and the override never lands without an operator ask.

## Boundaries

- Read-only: this slice only adds a flag to a read-only diagnostic.
  No new mutating command surface.
- WIP cap remains the default; unattended automation is unchanged.
- Override does not weaken Hard Clarification, contract completeness,
  unsafe-metadata, or stale-host-cli stops.
- At most one prepared issue per invocation (no multi-publish loop).
- No raw `gh` label mutation.
- No change to child implementation loop behavior.
- Reconcile `--write` and pr-transition rules are unchanged.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationHostReviewDiagnostics|FullyQualifiedName~GuidePromptMatrix"

git diff --check
```

Four new focused `AutomationHostReviewDiagnosticsCommandTests` cover:

- WIP > 0, candidate present, NO override → `wip-cap-blocked`,
  `wip-cap-overridden` not in warnings (default regression).
- WIP > 0, candidate present, `--allow-wip-cap-override` →
  `issue-publish-ready` with `wip-cap-overridden` warning and the
  publish chain in `recommended_next_command`.
- WIP > 0, NO candidate, `--allow-wip-cap-override` →
  `wip-cap-blocked` (the override needs a candidate to fire).
- WIP > 0, candidate present, `--clarification-required` AND
  `--allow-wip-cap-override` → `clarification-required` (override
  doesn't bypass clarification).

One new `GuidePromptMatrixCommandTests` confirms the host-loop prompt
mentions `--allow-wip-cap-override`, `wip-cap-overridden`, and the
"Operator-approved queue warming (G288)" wording.

Full suite: 2092 passed, 1 skipped.
