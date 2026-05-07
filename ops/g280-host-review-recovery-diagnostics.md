# G280 Host review-next recovery diagnostics

## Why this slice

Recent host loop failures became hard to interpret. PRs could retain
`intent-pr-reviewing`, the preflight could time out or abort, and operators
could not distinguish a correct **idle** wake from **stale metadata** or an
**in-progress recovery need**. Without a structured classifier, "no-actionable"
masked several distinct problems.

## What changed

- New read-only command
  `intent-cli automation host-review-diagnostics --repo <owner/repo> [--workdir <path>] [--candidate <id>] [--clarification-required] [--format text|json]`
  classifies why the host review/next-slice loop did not advance:
  - `true-idle` — genuine idle wake
  - `stale-host-cli` — installed CLI surface missing
  - `stuck-reviewing` — `intent-pr-reviewing` without exit-transition label
  - `missing-target-on-pr` — PR closes a published intent-target issue but
    lacks `intent-target`
  - `request-update-rereview-conflict` — both `intent-pr-request-update` and
    `intent-pr-rereview-ready` on the same PR (structured clarification
    emitted with background / question / options)
  - `wip-cap-blocked` — open intent-target issue or PR is in flight
  - `clarification-required` — operator-supplied clarification flag
  - `review-pr-actionable` — preflight should have picked it up
  - `candidate-ready` — next-slice candidate supplied
- The command reuses the existing GitHub candidate lister and the installed-CLI
  surface probe; it never mutates labels, queue state, runs.jsonl, packet
  files, or any GitHub state.
- Each non-idle classification surfaces a `recommended_next_command` (e.g.,
  `intent-cli automation pr-transition --transition request-update --write`
  or `intent-cli automation reconcile --lane host-review --write`) or a
  structured clarification. The existing reconcile / pr-transition surfaces
  remain the only mutators; this command only classifies and recommends.
- Both host-loop and host-oneshot prompt-matrix entries gain a Stage 4
  recovery-diagnostics step. Before the host loop reports a no-actionable /
  idle wake, the agent runs the diagnostic and reports the returned
  `classification` + `summary` instead of saying "no-actionable".

## Boundaries

- Read-only by design. Never mutates labels or parent files.
- Host-only by intent: child loops never invoke it.
- Diagnostic does not replace the existing `host-review-preflight`
  selector or the `automation reconcile` mutator; it sits between them as
  a stop-gap before declaring idle.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationHostReviewDiagnostics"

git diff --check
```

Focused tests cover: true-idle, stuck-reviewing (with recommended pr-transition
hint), missing-target-on-pr (with recommended reconcile hint), conflict (with
structured clarification + 2 options), wip-cap-blocked, clarification-required,
stale-host-cli (lister never called), review-pr-actionable, candidate-ready,
read-only invariant (file snapshot before/after equal), command router
registration, and host-loop guide mention of Stage 4 + the new command.
