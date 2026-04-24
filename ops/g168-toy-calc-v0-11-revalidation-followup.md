# G168 TOY-CALC-V0-11 Revalidation Follow-up

This note records the attempt to classify the post-`G166`
`TOY-CALC-V0-11` revalidation into Branch A (`no-actionable`) or
Branch B (`stable blocked persistence`) per issue #441, and narrows
the response to the exact deterministic blocker that prevents the
requested live-style rerun from completing inside this isolated
`intent-system` repo.

## Requested Branch Decision Shape

`G168` asks for a fresh post-`G166` `TOY-CALC-V0-11` live-style rerun
executed from the parent-host path:

```
cd submodules/toy-calc-sample
dotnet run --project ../intent-system/src/IntentSystem.Cli/IntentSystem.Cli.csproj -- run
```

Along with deterministic evidence
(`.intent-cli/queue-state.json`, tail of `.intent-cli/runs.jsonl`,
and run output), then a classification into:

- **Branch A** — no-actionable completion, or
- **Branch B** — stable blocked persistence requiring an immediate
  follow-up implementation issue with a ready-to-create packet.

## Deterministic Blocker

The requested parent-host rerun cannot complete inside this
`intent-system` repository. The same blocker recorded for `G167`
still holds on `origin/main` at `84fd296`:

- No `submodules/` directory at the repository root.
- No `.gitmodules` file at the repository root.
- No `submodules/toy-calc-sample` working tree to `cd` into.
- No `submodules/intent-system` working tree as the parent-host expects.
- No `.intent-cli/` directory at the repository root, so no
  `.intent-cli/queue-state.json` or `.intent-cli/runs.jsonl` can be
  produced by a rerun here.

The parent-host reproduction path requires a surrounding host layout
with both submodules checked out under a parent repo. This
`intent-system` repo is the standalone upstream component, not that
parent host. The verification command has no filesystem target inside
this repo.

As a result:

- `cd submodules/toy-calc-sample` fails before any `dotnet run`.
- No fresh `.intent-cli/queue-state.json` snapshot from a post-`G166`
  rerun can be captured here.
- No `.intent-cli/runs.jsonl` tail from a live rerun can be attached.
- Branch A vs Branch B cannot be determined from this repo.

## Scope Boundary

This PR is documentation-only inside the isolated `intent-system`
repo. It does not and cannot carry the parent-host live rerun
evidence, and it therefore does not classify the revalidation as
Branch A or Branch B. The classification must come from the
parent-host environment named in the source issue.

Per the already-landed `ops/g167-toy-calc-v0-11-post-g166-verification.md`
continuation decision (merged via PR #440), the in-repo xUnit
regression coverage that exercises the same product code path is
orthogonal regression-level confidence and is explicitly NOT a
substitute for the requested parent-host live rerun. This note
preserves that boundary.

## Follow-up Packet Placeholder

If, after a parent-host live rerun, the outcome turns out to be
Branch B (stable blocked persistence), the prepared follow-up packet
should contain, at minimum, the following inline fields so the next
implementation issue can be cut without clarification delay:

- execution unit: `TOY-CALC-V0-11`
- current predecessor context:
  - `G166` (merged as `c02d687`)
  - `G167` verification note
    (`ops/g167-toy-calc-v0-11-post-g166-verification.md`,
     merged as `84fd296` / PR #440)
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
- expected boundary fields in the next implementation issue:
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

Inside this isolated `intent-system` repo, `G168` cannot produce the
requested parent-host Branch A / Branch B decision from fresh live
evidence. The canonical in-repo continuation outcome is to stop
without cutting a new execution unit from this repo and defer the
classification to the parent-host environment that owns both the
`intent-system` and `toy-calc-sample` submodules.

This document intentionally does not upgrade the in-repo xUnit
coverage into a `G168` Branch A claim, and intentionally does not
fabricate a Branch B reproduction the parent-host rerun did not
produce. It records the blocker, preserves the `G167` boundary, and
defers the live-rerun-dependent decision to the environment that can
actually execute it.
