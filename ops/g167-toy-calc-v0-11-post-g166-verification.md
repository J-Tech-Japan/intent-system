# G167 TOY-CALC-V0-11 Post-G166 Verification

This note records the re-verification of `TOY-CALC-V0-11` after `G166`
(`Fix implement dead-session contract-gap stop reason`, commit `c02d687`)
landed on `main`.

## Scope

- Confirm `G166` deterministically closes the blocked + dead implement
  session path for `TOY-CALC-V0-11` when terminal contract-gap evidence
  exists.
- Confirm the replay of this reproduction no longer collapses to
  `non-retryable-failure` with `Actions executed: 0`.
- Decide continuation: cut a next execution unit, or record
  `no-actionable-item`.

This note is documentation-only. It does not change runtime code or
`intent-cli` contracts.

## Baseline

- Branch baseline: `origin/main` at `c02d687` (`G166` merged).
- Product change under test: `src/IntentSystem.Cli/Commands/RunCommand.cs`
  root-finalization branch for a current dead implement session with
  terminal `contract-gap` evidence.
- Regression coverage added in the same commit in
  `tests/IntentSystem.Cli.Tests/RunCommandTests.cs`.

## Verification Approach

The command shape referenced by the source issue
(`cd submodules/toy-calc-sample; dotnet run --project
../intent-system/src/IntentSystem.Cli/IntentSystem.Cli.csproj -- run`)
is a parent-host reproduction path. It requires both the
`submodules/intent-system` and `submodules/toy-calc-sample` submodules
under the parent host repo.

Inside this isolated `intent-system` repo the exact parent-host
reproduction is not directly executable. `G166` already landed the
deterministic replay of that reproduction as in-repo xUnit regression
coverage, so the canonical in-repo verification is to re-run that
coverage on the current merged baseline.

## Evidence

Two targeted runs on the current `origin/main` baseline were executed:

1. The specific `G166` acceptance test alone:
   `dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj
   --filter
   "FullyQualifiedName~ExecuteCore_GivenBlockedImplementSessionWithBackendEvidenceAndContractGap_StopsWithDeterministicContractGap"`
   — `Passed: 1, Failed: 0`.

2. The broader related cluster covering `TOY-CALC-V0-11`, dead implement
   sessions, and contract-gap handling:
   `dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj
   --filter
   "FullyQualifiedName~TOY_CALC_V0_11|FullyQualifiedName~BlockedImplementSessionWithBackendEvidence|FullyQualifiedName~DeadImplement|FullyQualifiedName~ContractGap"`
   — `Passed: 51, Failed: 0`.

3. Full CLI test suite:
   `dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj`
   — `Passed: 899, Failed: 0`.

The `G166` acceptance test asserts, among other things, that for the
replay of the blocked + dead + terminal `contract-gap` scenario:

- `result.StopReason` is `deterministic-contract-gap`, not
  `non-retryable-failure`.
- `result.Actions` is empty (no spurious retry action stream).
- No `retry-attempted` or `retry-exhausted` run-log events are emitted.
- Queue item remains `blocked` with preserved `BlockedBy` reason.
- The worktree under `.intent-cli/worktrees/TOY-CALC-V0-11` is retained.

These match the `G167` acceptance criterion of a stable continuation
shape that avoids opaque `non-retryable-failure` with
`Actions executed: 0` for this path.

## Continuation Decision

- `G166` is accepted, merged, and its regression coverage passes on the
  current `main` baseline.
- The blocked + dead + terminal `contract-gap` path for
  `TOY-CALC-V0-11` is deterministically finalized and preserved.
- No further runtime change is required in `intent-cli` for this
  specific path today.

Per `ops/post-closeout-next-slice-continuation.md`, the canonical
continuation outcome here is **`no-actionable-item`**:

- No new implementation execution unit is cut for this path in this
  slice.
- Automation should stop cleanly rather than fabricate a speculative
  follow-up.

If a fresh parent-host reproduction ever re-surfaces the original
symptom (`non-retryable-failure` with `Actions executed: 0` for
`TOY-CALC-V0-11` under the same shape), a new execution unit should be
cut with the exact reproduction and terminal boundary attached.
