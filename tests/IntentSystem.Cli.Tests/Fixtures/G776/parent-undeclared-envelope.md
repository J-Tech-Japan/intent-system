TASK G776-task
role: implementation
objective: Implement wake contract
inputs:
  - issue #1689
expected-artifacts:
  - ready PR
reporting-contract:
  task-id: G776-task
  expected-artifact: ready PR
  canonical-report-command: intent-cli notify report --domain intent-cli --team g776-team --from implementation --to orchestration --task-id G776-task --status <completed|blocked|question> --artifact <artifact> --summary <one-line-summary> --routing-root '<workspace-root>' --report-root <role-work-root> --write --format json
  required-final-step: Run canonical-report-command after all other work; never hand-write a transport invocation.
result-prefix: ORCH_RESULT
result-nonce: g776-nonce
completion-marker: When the artifact is ready, concatenate result-prefix, one space, result-nonce, one space, status, one space, and artifact; use completed, blocked, or question. Do not precompose the marker in this task block.
