# G734 supervision-state shrink verification

This artifact records the G734 implementation proof from test-owned temporary
roots. The implementation never reads or changes host `.intent-cli` state.
The density totals below cover `stalls.jsonl` and `cycles.jsonl`; the readable
evidence manifest, transaction journal, and append-only audit are reported
separately because they are control and audit state rather than compacted
records.

## Density gate and reproducible calculation

The issue baseline is 10,063 records at 4,970 bytes/record. The focused
fixture uses exactly 10,063 legacy stall records, a fixed `2026-01-01T00:00:00Z`
timestamp epoch, and a 4,100-character payload. It invokes the real command:

```text
intent-cli notify supervise shrink --domain intent-cli --team intent-cli-dev --write --format json
```

The emitted density line was:

```text
G734 density: before_bytes=50735552; after_bytes=48561944; records=10063; before_average=5041.79; after_average=4825.79; baseline=4970
```

The corresponding command metrics were:

```json
{
  "before_bytes": 50735552,
  "after_bytes": 48561944,
  "before_record_count": 10063,
  "after_record_count": 10063,
  "before_average_bytes_per_record": 5041.79,
  "after_average_bytes_per_record": 4825.79,
  "literal_bytes_removed_from_records": 2435246,
  "reference_bytes_added_to_records": 322016,
  "net_record_bytes_saved": 2173608,
  "other_record_bytes_saved": 0,
  "definition_manifest": "<test-root>/.intent-cli/supervision/intent-cli/intent-cli-dev/evidence-definitions.json",
  "resolution": "Read evidence-definitions.json and resolve the evidence_ref 'recorded-herdr-seat-registration'."
}
```

The calculation is reproducible: `50,735,552 - 48,561,944 = 2,173,608`
bytes saved while retaining all 10,063 records, and the average falls from
5,041.79 to 4,825.79 bytes/record, 144.21 below the 4,970 issue baseline.
The command attributes 2,173,608 bytes to the invariant-record rewrite,
reports 2,435,246 literal bytes removed and 322,016 reference bytes added,
and reports 0 bytes from other record changes.

## Live supervisor and existing-file recovery

The live regression seeded 10,063 existing legacy records in a test-owned
root, started a separate `dotnet ... notify supervise --interval 1 --write`
process, waited for its first cycle, and ran shrink while that process was
still alive. The emitted result reported:

```json
{
  "supervisor_state": "running",
  "before_bytes": 10976411,
  "after_bytes": 8802803,
  "before_record_count": 20127,
  "after_record_count": 20127,
  "stalls_before_bytes": 10975592,
  "stalls_after_bytes": 8801984,
  "stalls_before_records": 20126,
  "stalls_after_records": 20126,
  "cycles_before_bytes": 819,
  "cycles_after_bytes": 819,
  "cycles_before_records": 1,
  "cycles_after_records": 1,
  "invariant_bytes_saved_in_records": 2173608,
  "other_bytes_saved": 0
}
```

The same test emitted `G734 live supervisor shrink: state=running;
cycles=1->2`. The second cycle appended after the atomic replacement, so the
proof covers a genuinely running external supervisor and the next-cycle
write, not only a stopped-process demonstration. The unchanged record count
and the seeded existing file prove the sanctioned path shrinks state that
already exists rather than only changing future files.

The audit names both files and the outcome:

```json
{
  "outcome": "completed",
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
  "evidence_reference": "evidence-definitions.json#recorded-herdr-seat-registration"
}
```

No records were archived, discarded, or rotated. `.intent-cli/runs/*.provider.jsonl`
was not inspected or changed; it remains outside G734.

## Readable evidence and fail-closed validation

The manifest is human-readable and resolves the reference rather than using
an opaque code:

```json
{
  "schema": "intent-cli.supervision-evidence/v1",
  "definitions": {
    "recorded-herdr-seat-registration": "a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane"
  }
}
```

Before planning any replacement, shrink holds the same directory lock and
resolves every retained `evidence_ref`, validates the manifest schema and
definitions, and only then stages or writes anything. Constructed tests cover
all three failure boundaries:

- an unknown reference;
- the canonical reference with no definition manifest; and
- a malformed/unsupported definition manifest.

Each returns exit code 1 with the named `shrink-validation-failed` error. In
each case `stalls.jsonl`, `cycles.jsonl`, the definition manifest, and
`shrink-audit.jsonl` remain byte-identical; no audit line is appended and no
transaction journal is created.

## Recoverable replacement transaction

Write mode stages complete manifest, stalls, and cycles replacements under a
transaction-specific directory. It durably writes
`shrink-transaction.json` with before/after SHA-256 hashes before replacing a
target. After each replacement and before the final audit append, the focused
tests inject a failure and then restart the canonical shrink command. Recovery
verifies the hashes, completes only missing staged replacements, appends a
`recovered-completed` audit outcome, and removes the journal. All four fault
points produced exit `1` on the injected run, exit `0` on restart, 3 retained
stall records, 1 retained cycle, readable evidence, and a removed journal:

| injected point | restart result | durable outcomes |
| --- | --- | --- |
| after manifest replacement | readable, exit 0 | `recovered-completed`, then `completed` |
| after stalls replacement | readable, exit 0 | `recovered-completed`, then `completed` |
| after cycles replacement | readable, exit 0 | `recovered-completed`, then `completed` |
| before final audit append | readable, exit 0 | `recovered-completed`, then `completed` |

A fifth constructed test mutates a target after the simulated crash. Recovery
does not overwrite the unexpected hash; it appends a durable `aborted` audit
outcome, removes the journal, and leaves the externally appended valid cycle
and the already-readable stall state intact. Thus completed, recovered, and
aborted outcomes are all accounted for without silent record loss.

The focused G734 class finished with 15 passed tests, including the density
measurement, live-supervisor cycle transition, three no-write validation
counterexamples, four replacement/audit fault points, and the aborted-target
proof.
