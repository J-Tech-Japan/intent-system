# API

## Model

The public API surface of this repo is the `intent-cli` command tree plus its packaged `.NET tool` invocation shape.

## Main Elements

- top-level command routing in `IntentSystem.Cli`
- subcommand families for intake, run, review, bug, and generate-from-current flows
- packaged invocation via `intent-cli`, `dotnet tool exec`, and `dnx`

## Repo-Specific Expectations

- outward command names and persisted artifact names should stay aligned with parent contracts
- new surfaces should be thin wrappers around existing command cores when possible
- docs/input baseline work should not quietly mutate runtime command behavior
