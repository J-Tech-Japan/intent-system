# G169 TOY-CALC-V0-11 Parent-Host Revalidation

This note records the response to issue #443 (`G169`) inside this
isolated `intent-system` repo. `G169` asks for the deferred
`TOY-CALC-V0-11` revalidation to be finalized as Branch A
(`no-actionable / complete`) or Branch B (`blocked persists`),
explicitly in the parent-host environment.

## What G169 Asks For

From the source issue:

- execute a fresh parent-host rerun via:
  ```
  cd submodules/toy-calc-sample
  dotnet run \
    --project ../intent-system/src/IntentSystem.Cli/IntentSystem.Cli.csproj \
    -- run
  ```
- capture terminal-boundary evidence from parent-host
  `.intent-cli/queue-state.json` and the tail of
  `.intent-cli/runs.jsonl`
- decide Branch A or Branch B
- if Branch B, leave a ready-made follow-up packet (the `G170`
  candidate) with required context

The issue body itself acknowledges the scope:

> `#441` could not finalize because this standalone repo lacks the
> required submodule layout.

## Why the Parent-Host Rerun Still Cannot Run Here

The same deterministic filesystem blocker recorded in `G167` (PR
#440, merged as `84fd296`) and `G168` (PR #442, merged as `7425cb5`)
still holds on `origin/main` at `7425cb5`:

- No `submodules/` directory at the repository root.
- No `.gitmodules` file at the repository root.
- No `submodules/toy-calc-sample` working tree to `cd` into.
- No `submodules/intent-system` working tree to `--project` at.
- No `.intent-cli/` directory at the repository root, so no
  `.intent-cli/queue-state.json` or `.intent-cli/runs.jsonl` can be
  produced from a rerun here.

The parent-host reproduction path requires a surrounding host
layout that owns both the `intent-system` and `toy-calc-sample`
submodules. This repository is the standalone upstream component,
not that parent host. `G169`'s verification command has no
filesystem target inside this repo, so:

- `cd submodules/toy-calc-sample` fails before any `dotnet run`.
- No fresh `.intent-cli/queue-state.json` snapshot from a post-`G166`
  rerun can be captured here.
- No `.intent-cli/runs.jsonl` tail from a live rerun can be attached.
- Branch A vs Branch B cannot be determined from this repo.
- A concrete Branch B follow-up packet (`G170` candidate) cannot be
  populated with real parent-host queue/run evidence from this repo.

`G169`'s Accepted Baseline correctly notes this constraint; this PR
just records that the same constraint still applies at `7425cb5`.

## Scope Boundary

This PR is documentation-only inside the isolated `intent-system`
repo. It does not and cannot carry the parent-host live rerun
evidence, and it therefore does not finalize Branch A or Branch B
for `G169`. The classification must come from the parent-host
environment named in the source issue.

Per the already-landed continuation decision in
`ops/g167-toy-calc-v0-11-post-g166-verification.md` (PR #440) and
`ops/g168-toy-calc-v0-11-revalidation-followup.md` (PR #442),
in-repo xUnit regression coverage that exercises the same product
code path is orthogonal regression-level confidence and is
explicitly NOT a substitute for the parent-host live rerun
evidence. This note preserves that boundary.

## Follow-up Packet Placeholder (unchanged from G168)

If, after a parent-host live rerun, the outcome turns out to be
Branch B, the prepared `G170` follow-up packet should contain, at
minimum, the following inline fields:

- execution unit: `TOY-CALC-V0-11`
- predecessor context:
  - `G166` (merged as `c02d687`)
  - `G167` verification note (PR #440, merged as `84fd296`)
  - `G168` revalidation follow-up (PR #442, merged as `7425cb5`)
  - `G169` parent-host revalidation (this note)
- exact reproduction command (from parent host):
  ```
  cd submodules/toy-calc-sample
  dotnet run \
    --project ../intent-system/src/IntentSystem.Cli/IntentSystem.Cli.csproj \
    -- run
  ```
- attached evidence (from parent host at reproduction time):
  - `.intent-cli/queue-state.json` snapshot showing the stopping
    boundary
  - tail of `.intent-cli/runs.jsonl` showing the final-shape events
  - run output showing the deterministic final boundary and the
    action count behavior
- expected boundary fields to name in the next implementation issue:
  - the queue item state at stop
  - the specific `BlockedBy` reason, if any
  - which worker entry (implement / supervise / run) produced the
    terminal boundary
  - which worktree under `.intent-cli/worktrees/` is authoritative
- target repo/path/part for the next slice:
  - repo: `J-Tech-Japan/intent-system`
  - paths likely in scope:
    - `src/IntentSystem.Cli/Commands/RunCommand.cs`
    - `tests/IntentSystem.Cli.Tests/RunCommandTests.cs`
  - part: whichever finalization branch the attached evidence proves
    is now the stable blocker

This placeholder packet cannot itself be attached as concrete
evidence from inside this repo. It becomes a real handoff only when
the parent-host rerun above produces Branch B evidence. Until then
no new implementation execution unit should be cut from this repo.

## Continuation Decision

Inside this isolated `intent-system` repo, `G169` cannot produce the
requested parent-host Branch A / Branch B decision from fresh live
evidence. The canonical in-repo continuation outcome is to stop
without cutting a new execution unit from this repo and defer the
classification to the parent-host environment that owns both the
`intent-system` and `toy-calc-sample` submodules.

This document intentionally does not upgrade the in-repo xUnit
coverage into a `G169` Branch A claim, and intentionally does not
fabricate a Branch B reproduction the parent-host rerun did not
produce. It records the blocker, preserves the `G167` / `G168`
boundary, and defers the live-rerun-dependent decision to the
environment that can actually execute it.
