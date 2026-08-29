TASK G578-demo
role: implementation
objective: Implement the notification contract
inputs:
  - issue #1259
expected-artifacts:
  - draft PR URL
reporting-contract:
  task-id: G578-demo
  expected-artifact: draft PR URL
  canonical-report-command: intent-cli notify report --domain intent-cli --team intent-cli-dev --from implementation --to orchestration --task-id G578-demo --status <completed|blocked|question> --artifact <artifact> --summary <one-line-summary> --routing-root '<workspace-root>' --report-root . --write --format json
  required-final-step: Run canonical-report-command after all other work; never hand-write a transport invocation.
result-prefix: ORCH_RESULT
result-nonce: demo-nonce
completion-marker: When the artifact is ready, concatenate result-prefix, one space, result-nonce, one space, status, one space, and artifact; use completed, blocked, or question. Do not precompose the marker in this task block.