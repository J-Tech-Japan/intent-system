# Aggregate

## Model

The closest aggregate-like seam in this repo is the execution-unit lifecycle that moves through intake, queue, run, review, fix, and closeout states.

## Main Elements

- execution units such as `G19`, `G20`, and later bug-driven units
- queue items and their state transitions
- run/review/fix artifacts that carry the same execution unit through each stage

## Repo-Specific Expectations

- execution-unit state should move through deterministic artifacts and logs
- state transitions should prefer existing command cores over duplicate workflow branches
- aggregate-like lifecycle changes should remain inspectable from `.intent-cli/` artifacts
