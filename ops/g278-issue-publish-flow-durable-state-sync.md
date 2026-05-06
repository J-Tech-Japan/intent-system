# G278 Fix issue publish-flow durable state synchronization

## Why this slice

Publishing G277 created GitHub issue #657 but left
`.intent-cli/queue-state.json` `linked_issue` and
`.intent-cli/issues/G277/publish.yaml` null/drafted, so host-side manual
repair was needed. `intent-cli issue publish-flow --write` must atomically
reflect GitHub issue creation in parent durable artifacts before reporting
success, so queue-state, publish.yaml, and runs.jsonl cannot drift from the
created issue.

## What changed

- `intent-cli issue publish-flow --write` now updates three durable
  artifacts in the same wake after the GitHub issue is created:
  - `queue-state.json` — the matching execution-unit's
    `linked_issue` is filled with `{ repo, number, url }` and
    `updated_at` advances.
  - `.intent-cli/issues/<execution-unit>/publish.yaml` — `publish_status`
    advances to `issue-created` with the GitHub issue number/URL.
  - `.intent-cli/runs.jsonl` — an `issue-created` event is appended
    (one per successful create).
- Re-running `--write` after a successful create is **idempotent**:
  `publish.yaml`'s `issue-created` marker short-circuits both the
  `gh issue create` call and the durable-state writes. The result reports
  `created: false`, `idempotent: true`, `durable_state_synced: true`.
- If `gh issue create` fails, no durable artifact is mutated.
- If a durable write fails after the GitHub issue is created, the result
  reports `created: false`, `durable_state_synced: false`, returns exit
  code `1`, and points the operator at
  `intent-cli automation reconcile` for repair. The command never claims
  `created: true` while local artifacts remain unmodified.
- Dry-run mode (no `--write`) remains read-only.

## Boundaries

- The command never applies `intent-target`. Use
  `intent-cli automation issue-publish --write` at the explicit publish
  boundary.
- The command does not mutate the GitHub issue body, labels, or any
  field other than the local durable artifacts described above.
- Workflow label transitions remain CLI-owned; no raw `gh` label
  fallback is introduced by this slice.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~IssuePublishFlowCommandTests"

git diff --check
```

Focused tests cover: create-success patches all three artifacts and
appends exactly one `issue-created` event; idempotent rerun does not
call `gh` and does not append a duplicate event; create-failure leaves
queue-state/publish.yaml/runs unchanged; dry-run never writes; the
output never claims `created: true` without a synced durable state.
