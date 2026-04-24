# G167 TOY-CALC-V0-11 Post-G166 Verification

This note records the attempt to perform `G167` post-`G166` verification
for `TOY-CALC-V0-11` and narrows it to the exact deterministic blocker
that prevents the requested live-style rerun from completing inside
this isolated `intent-system` repo.

## Requested Verification Shape

`G167` asks for a fresh post-`G166` `TOY-CALC-V0-11` live-style rerun
executed from the parent-host path:

```
cd submodules/toy-calc-sample
dotnet run --project ../intent-system/src/IntentSystem.Cli/IntentSystem.Cli.csproj -- run
```

Along with the resulting queue and run-log evidence
(`.intent-cli/queue-state.json` and associated run-log entries) and a
continuation decision derived from that evidence.

## Deterministic Blocker

The requested parent-host rerun cannot complete inside this
`intent-system` repository.

The exact blocker, observed on branch
`claude/issue-439-g167-verify-toy-calc-v0-11` at `6d5aec3`:

- There is no `submodules/` directory at the repository root.
- There is no `.gitmodules` file at the repository root.
- No `submodules/toy-calc-sample` working tree exists to `cd` into.
- No `submodules/intent-system` working tree exists as the parent-host
  expects.
- No `.intent-cli/` directory exists at the repository root, so no
  `.intent-cli/queue-state.json` or run-log can be produced by a rerun
  here.

The parent-host reproduction path requires a surrounding host layout
with both submodules checked out under a parent repo. This
`intent-system` repo is the standalone upstream component, not that
parent host. The command shape therefore has no filesystem target to
operate against in this repo.

As a result:

- The `cd submodules/toy-calc-sample` step would fail before any
  `dotnet run` is invoked.
- No fresh `.intent-cli/queue-state.json` snapshot from a post-`G166`
  rerun can be captured inside this repo.
- No run-log entries from a live rerun can be attached from this repo.

The rerun is not blocked by a runtime defect. It is blocked by the
repository scope.

## Scope Boundary

This PR is documentation-only inside the isolated `intent-system` repo.
It does not and cannot carry the live parent-host rerun evidence.

A fresh post-`G166` `TOY-CALC-V0-11` live-style rerun with the
requested queue/run evidence must be executed from the parent-host
that owns both the `intent-system` and `toy-calc-sample` submodules.
That is the only environment where the reproduction shape named in
the source issue has a valid filesystem target.

## Orthogonal Context (Not the Requested G167 Evidence)

For operator context only, and explicitly not as a substitute for the
requested live rerun, `G166` (`c02d687`,
`Fix implement dead-session contract-gap stop reason`) landed the
deterministic replay of the blocked + dead + terminal `contract-gap`
shape as in-repo xUnit coverage in
`tests/IntentSystem.Cli.Tests/RunCommandTests.cs`.

That coverage currently passes on `origin/main` at `c02d687`. It
exercises the same product code path
(`src/IntentSystem.Cli/Commands/RunCommand.cs` root-finalization
branch) that the parent-host reproduction drives, but it does so via
xUnit, not via the live CLI invocation named in the issue.

This in-repo coverage is orthogonal regression-level confidence. It is
not the parent-host live rerun evidence requested by `G167` and must
not be read as a substitute for it.

## Continuation Decision

Because the requested live-style verification cannot complete inside
this repo, the continuation decision inside this repo is narrow:

- In this isolated `intent-system` repo, `G167` cannot produce the
  requested parent-host rerun evidence. The canonical in-repo
  continuation outcome is to stop without cutting a new execution unit
  from this repo.
- Any fresh live-style post-`G166` verification for `TOY-CALC-V0-11`
  must be run from the parent-host layout that owns both submodules,
  and its queue/run evidence must be attached there.

This document intentionally does not upgrade the in-repo xUnit
coverage into a `G167` verification claim. It records the blocker and
defers the live-rerun evidence to the environment that can actually
produce it.
