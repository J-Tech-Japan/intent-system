# Runtime Artifact Family

## Model

The repo persists most workflow state as deterministic artifacts under `.intent-cli/`.

## Main Artifact Families

- `intake/`
  reconstructed and best-practice review inputs
- `issues/`
  implementation and review packets per execution unit
- `runs/` and `runtime-runs/`
  direct-run requests, provider raw-event logs, normalized results, and lifecycle traces
- `reviews/`
  review requests, review comments, and review-side handoff artifacts
- queue and supervision state such as `queue-state.json`, `runs.jsonl`, and supervision sessions

## Repo-Specific Expectations

- artifact paths should be deterministic and derivable from the execution unit or domain
- serialized shape matters as much as prose output because downstream commands re-read these files
- repo-local guidance should prefer extending an existing artifact family over inventing a parallel storage shape
