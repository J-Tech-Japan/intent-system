# CLI Command Surface

## Model

The primary model in this repo is a deterministic command surface rooted in `intent-cli`.

## Main Seams

- top-level command routing in `IntentSystem.Cli`
- thin composition commands that orchestrate existing command cores
- command results and renderers that persist bounded outputs for downstream stages

## Repo-Specific Expectations

- command names and persisted artifact names should stay aligned with parent contracts
- new commands should prefer existing shared helpers over duplicating lifecycle logic
- docs-only or input-baseline issues should not silently widen into runtime behavior changes
