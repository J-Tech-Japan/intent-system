# Read Model

## Model

This repo's main read-model surface is the persisted artifact family under `.intent-cli/`.

## Main Elements

- intake artifacts such as reconstructed concept/interview and best-practice review outputs
- issue packet files under `.intent-cli/issues/<execution-unit>/`
- run and review outputs such as request/result artifacts, `runs.jsonl`, and review comments

## Repo-Specific Expectations

- read-side files should be canonical and re-readable by downstream commands
- adding a new file family is more expensive than extending an existing artifact contract
- test coverage should prove the persisted shape, not only the console summary
