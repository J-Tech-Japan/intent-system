# Runtime Artifacts And Testing

## Scope

This repository depends on deterministic artifact generation and stable test execution. Project-local guidance should optimize for reproducible files and suite-safe process behavior.

## Repo-Specific Guidance

- generated artifacts must have canonical, bounded schemas so downstream commands can consume them without hidden contracts
- append-only runtime traces such as `.intent-cli/runs.jsonl` and provider raw-event logs should be stable under repeated test runs
- command tests should prefer hermetic temp repositories and should not assume developer-machine state
- process-heavy tests should participate in the existing non-parallelized CLI test boundary when they share packaging or direct-run resources
- runtime helpers should tolerate directory creation and teardown races inside temp test fixtures instead of aborting the test host

## Review Prompts

- can the new artifact be re-read deterministically by the next stage?
- do tests prove the bounded contract instead of only checking happy-path text?
- does the change keep full `dotnet test IntentSystem.sln` reliable?
