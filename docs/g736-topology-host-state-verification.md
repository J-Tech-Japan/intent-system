# G736 topology host-state verification

This is the durable, test-owned verification transcript for issue #1597. The
examples use a synthetic `g736-domain` and never read or write the host
repository. The focused regression
`SessionLayerTopologyG736Tests` emits the same record, validation, and guide
surfaces from a temporary test-owned routing root.

## Explicit declaration

Command:

```text
intent-cli session-layer topology record-host-state \
  --domain g736-domain --team g736-declared --role design \
  --envelope non-sandboxed-host-repository-write --write --format json
```

Emitted result:

```json
{
  "team": "g736-declared",
  "role": "design",
  "envelope": "non-sandboxed-host-repository-write",
  "mode": "write",
  "record_path": ".intent-cli/topology/g736-domain/g736-declared.json",
  "applied": true,
  "changed": true,
  "already_recorded": false,
  "conflict": false
}
```

The resulting topology contains the declaration alongside the recorded roles:

```json
{
  "domain": "g736-domain",
  "team": "g736-declared",
  "workspace_id": "wG736",
  "roles": {
    "design": { "resident": "external", "reader": ".intent-cli/events/g736-declared.jsonl" },
    "implementation": { "resident": "herdr", "workspace_id": "wG736", "pane_id": "wG736:p2" },
    "orchestration": { "resident": "herdr", "workspace_id": "wG736", "pane_id": "wG736:p3" },
    "review": { "resident": "herdr", "workspace_id": "wG736", "pane_id": "wG736:p4" }
  },
  "host_state": {
    "role": "design",
    "envelope": "non-sandboxed-host-repository-write"
  }
}
```

Declared validation emits no finding:

```json
{
  "valid": true,
  "findings": [],
  "host_state": {
    "role": "design",
    "envelope": "non-sandboxed-host-repository-write"
  }
}
```

## Missing declaration, including the all-sandboxed case

For a legacy four-role record with no `host_state`, and for the constructed
all-sandboxed variant (`design`, `implementation`, `orchestration`, and
`review` all resident in herdr with `kind: codex`), validation remains
successful for compatibility but emits the capacity finding before any
publish attempt:

```json
{
  "valid": true,
  "findings": [
    {
      "role": "<host-state>",
      "field": "role",
      "cause": "host-state-role-missing",
      "is_informational": true,
      "message": "This team cannot perform required host-state workflow work ... Record an actually capable participant and an explicit host-state role plus envelope; a declaration alone does not supply a non-sandboxed participant ..."
    }
  ]
}
```

The topology bytes are unchanged and no migration is performed. `resident`,
`kind`, external placement, shared workspace/pane, and co-location alone never
create authority. The finding is an early capability disclosure, not a
publish-flow refusal or a claim that recording a string supplies a
non-sandboxed participant.

## Rendered orchestrator discovery

With the explicit declaration, `guide orchestrator-thread` emits:

```json
{
  "host_state_discovery": {
    "status": "declared",
    "source": ".intent-cli/topology/g736-domain/g736-declared.json",
    "role": "design",
    "envelope": "non-sandboxed-host-repository-write",
    "route": "Topology discovery selected host-state role 'design' with envelope 'non-sandboxed-host-repository-write' ... A declared design host-state role is legitimate; the prohibition is only on undeclared or ad-hoc requests. The declaration records the route but does not supply a non-sandboxed participant or create host capability."
  }
}
```

Without the declaration, the same surface reports
`status: missing-declaration`, names `cause=host-state-role-missing`, keeps
the legacy record usable, and points to
`session-layer topology record-host-state`. The English and Japanese
orchestration guides carry the same qualified rule: a topology-declared
design host-state role is legitimate; only undeclared or ad-hoc routine
requests are prohibited.

The focused regression also asserts the missing finding in the shared
session-layer preflight, proving discovery happens before publish rather than
only at the first publish attempt.

## Focused test result

Final focused command:

```text
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --configuration Release --no-restore \
  --filter FullyQualifiedName~SessionLayerTopologyG736
```

Result: `Passed: 7, Failed: 0, Skipped: 0, Total: 7`.

The final full CLI suite used the same Release/no-restore configuration and
finished with `Passed: 5219, Failed: 0, Skipped: 1, Total: 5220`.
