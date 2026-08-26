# G734 supervision-state shrink verification

This is the emitted verification record for the G734 implementation. The
fixtures are test-owned roots; no host `.intent-cli` state was read or changed.
The data-file byte totals below intentionally exclude the fixed-size
`evidence-definitions.json` manifest and the append-only `shrink-audit.jsonl`
metadata, and the command reports those paths separately.

## Density gate

The issue reports 10,063 records at an average of 4,970 bytes/record. The
focused 10,063-record fixture was sized to exercise that baseline and then
passed through the real `notify supervise shrink --write --format json` path.
The test emitted:

```text
G734 density: before_bytes=50804891; after_bytes=48631283; records=10063; before_average=5048.68; after_average=4832.68; baseline=4970; invariant={
    "literal_bytes_removed_from_records": 2435246,
    "reference_bytes_added_to_records": 322016,
    "net_record_bytes_saved": 2173608,
    "other_record_bytes_saved": 0,
    "definition_manifest": "<test-root>/.intent-cli/supervision/intent-cli/intent-cli-dev/evidence-definitions.json",
    "resolution": "Read evidence-definitions.json and resolve the evidence_ref 'recorded-herdr-seat-registration'."
  }
```

The measured average fell from 5,048.68 to 4,832.68 bytes/record: 137.32
bytes/record below the reported 4,970 baseline. The 2,173,608-byte record
saving is fully attributable to replacing the repeated invariant definition:
2,435,246 literal bytes were removed, 322,016 reference bytes were added,
and the measured non-invariant saving was 0 bytes.

## Live supervisor and existing-file recovery

The external-process regression created 10,063 existing legacy stall records,
started a separate `dotnet ... notify supervise` process at one-second
interval, waited for its first cycle, and invoked the real shrink command while
that process was still alive. The emitted result included:

```json
{
  "command_mode": "write",
  "applied": true,
  "live_safe": true,
  "supervisor_state": "running",
  "supervisor_writer": {
    "pid": 34462,
    "process_start_time": "2026-08-26T02:34:05.700644+00:00",
    "host": "Mac"
  },
  "before_bytes": 11045665,
  "after_bytes": 8872057,
  "before_record_count": 20127,
  "after_record_count": 20127,
  "before_average_bytes_per_record": 548.7983802851891,
  "after_average_bytes_per_record": 440.80374621155664,
  "files": {
    "stalls.jsonl": {
      "before_bytes": 11044884,
      "after_bytes": 8871276,
      "before_record_count": 20126,
      "after_record_count": 20126
    },
    "cycles.jsonl": {
      "before_bytes": 781,
      "after_bytes": 781,
      "before_record_count": 1,
      "after_record_count": 1
    }
  }
}
```

The same test emitted `G734 live supervisor shrink: state=running;
cycles=1->2`. The second cycle was appended after the atomic replacement,
proving the running supervisor remained able to write the next cycle.

The corresponding durable audit record reported:

```json
{
  "records_archived": 0,
  "records_discarded": 0,
  "records_compacted": 20127,
  "records_rotated": 0,
  "files": {
    "stalls.jsonl": {
      "action": "atomically compacted; every stall event retained"
    },
    "cycles.jsonl": {
      "action": "atomically rewritten; every cycle and prompt-audit event retained"
    }
  },
  "evidence_reference": "evidence-definitions.json#recorded-herdr-seat-registration",
  "audit_summary": "No records were archived, discarded, or rotated. Existing stalls and cycles were retained in place; invariant registration prose was moved once to the readable definition manifest and records now reference it."
}
```

The manifest is human-readable and resolves the reference without an opaque
code:

```json
{
  "schema": "intent-cli.supervision-evidence/v1",
  "definitions": {
    "recorded-herdr-seat-registration": "a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane"
  }
}
```

`.intent-cli/runs/*.provider.jsonl` was not inspected or changed; it remains
outside G734.
