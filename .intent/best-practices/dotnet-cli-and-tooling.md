# .NET CLI And Tooling

## Scope

This repository is a `.NET 10` CLI-first codebase. Project-local guidance should prefer bounded `.NET` and shell flows over introducing a second toolchain.

## Repo-Specific Guidance

- prefer `dnx` or `dotnet tool exec` as the primary packaged invocation path
- keep command implementations thin and deterministic; compose existing command cores before adding new orchestration logic
- do not introduce Node or TypeScript tooling unless an issue explicitly requires it
- keep runtime and packaging behavior aligned with the `intent-cli` tool package surface
- treat `.intent-cli/` as the canonical runtime artifact root for generated requests, results, queue state, reviews, and intake artifacts

## Review Prompts

- does the change preserve the packaged `.NET tool` invocation path?
- does the command stay within an existing intent boundary instead of widening into unrelated workflow logic?
- does the implementation keep repo-local guidance in docs/config rather than hard-coding machine-local assumptions?
