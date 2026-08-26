# G734 supervision-state shrink verification

This document is the committed verification contract for G734. The evidence
record is emitted by the focused tests in test-owned temporary roots and then
assembled by `eng/emit-g734-verification.sh`. It never reads or changes host
`.intent-cli` state.

## One source of truth for exact-head evidence

The CI source-contract job sets `G734_VERIFICATION_ROOT` and the two focused
tests write these inputs:

| input | producer | contents |
| --- | --- | --- |
| `density.json` | `DensityReport_MeasuresTenThousandRecordsAgainstTheIssueBaseline` | deterministic 10,063-record before/after totals, averages, baseline, and measured invariant-text split |
| `live.json` | `ShrinkWrite_ReportsRunningExternalSupervisorAndNextCycleAppends` | the emitted shrink JSON, running-supervisor identity/state, cycle counts before/after, next-cycle append proof, and timestamp-dependent fields |

The CI step then validates those inputs and the TRX files and writes one
machine-readable artifact, `g734-verification.json`, with this schema:

```text
intent-system.g734-supervision-verification/v1
  source.repository
  source.head_sha
  source.ci_run_id / source.ci_run_url
  tests.source_contract.{passed, skipped, failed}
  tests.focused_g734
  density.{before_bytes, after_bytes, record_count,
           before_average_bytes_per_record, after_average_bytes_per_record,
           baseline_bytes_per_record, invariant_text}
  live.{supervisor_state_at_shrink, cycle_count_before, cycle_count_after,
        next_cycle_appended, timestamp_dependent_fields, shrink}
  integrity.{invariant_saving, audit_outcome, records_archived,
              records_discarded, records_rotated}
```

The script refuses to emit the final artifact unless the density saving
reconciles, the shrink observed `supervisor_state=running`, the next cycle
appended, the retained live record count is unchanged, the focused class has
15 passing tests, and the complete TRX set has no failed test. It also writes
`g734-verification.json.sha256` beside the report.

The exact-head rule is explicit: consume the artifact only when
`source.head_sha` equals the commit under review. The run URL and the test
counts come from that same artifact; they are not copied from a different
run. The PR summary is a human-readable rendering of the same fields.

## Density gate and reproducible calculation

The issue baseline is 10,063 records at 4,970 bytes/record. The density test
uses a fixed `2026-01-01T00:00:00Z` epoch and a 4,100-character payload, so
its byte totals are deterministic. The artifact must report:

```text
before_bytes=50,735,552
after_bytes=48,561,944
records=10,063
before_average=5,041.79 bytes/record
after_average=4,825.79 bytes/record
baseline=4,970 bytes/record
```

The calculation is `50,735,552 - 48,561,944 = 2,173,608` bytes saved while
retaining all 10,063 records. The invariant split is measured by the command
and carried in `density.invariant_text`:

```text
literal_bytes_removed_from_records = 2,435,246
reference_bytes_added_to_records   =   322,016
net_record_bytes_saved              = 2,173,608
other_record_bytes_saved            =         0
```

The direct byte delta and the command's `net_record_bytes_saved` are both
2,173,608, and the resulting average is 144.21 bytes/record below the 4,970
issue baseline. The literal/reference counters are diagnostic occurrence
counters: `2,435,246 - 322,016 = 2,113,230` is not used as the byte-saving
claim because the serialized field and prefix changes needed to replace the
invariant payload are included in the direct record delta. `other_record_bytes_saved=0`
means the command attributes the measured changed-record saving to the
invariant rewrite rather than to a second transformation.

## Live supervisor and existing-file recovery

The live regression seeds 10,063 existing legacy records in a test-owned root,
starts a separate `dotnet ... notify supervise --interval 1 --write` process,
waits for its first cycle, and invokes the sanctioned command while that
process is still alive:

```text
intent-cli notify supervise shrink --domain intent-cli --team intent-cli-dev --write --format json
```

The exact values for the live files are deliberately not hardcoded in this
document. The supervisor writes wall-clock timestamps, so `cycles.jsonl` byte
length and the total live before/after byte counts can legitimately change by
one or more bytes between otherwise identical CI runs. A copied transcript
would become stale again. Instead, the exact-head emitted artifact is the
numeric source for these fields:

| proof | artifact field |
| --- | --- |
| supervisor was genuinely running | `live.supervisor_state_at_shrink` and `live.shrink.supervisor_writer` |
| shrink before/after totals | `live.shrink.before_bytes`, `live.shrink.after_bytes` |
| retained records | `live.shrink.before_record_count`, `live.shrink.after_record_count` |
| `stalls.jsonl` totals | `live.shrink.files.stalls.*` |
| `cycles.jsonl` totals | `live.shrink.files.cycles.*` |
| next cycle after replacement | `live.cycle_count_before`, `live.cycle_count_after`, `live.next_cycle_appended` |
| timestamp explanation | `live.timestamp_dependent_fields` |

The artifact therefore preserves the actual emitted live transcript without
pretending that timestamp-dependent fixture bytes are a fixed constant. It
also proves that the existing file is shrunk in place: the seeded records are
present before the live shrink, the retained record count is unchanged, and a
later cycle appends after the atomic replacement. It is not a rotation-only or
stopped-supervisor demonstration.

The audit record in `live.shrink.audit.record` names both files and the
outcome. The accepted proof expects zero archived, discarded, and rotated
records, with all retained records compacted in place. `.intent-cli/runs/*.provider.jsonl`
is outside this unit and is neither read nor changed.

## Readable evidence and fail-closed validation

The evidence manifest remains human-readable and resolvable:

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
an unknown reference, the canonical reference with no definition manifest,
and a malformed/unsupported definition manifest. Each returns exit code 1
with `shrink-validation-failed`; `stalls.jsonl`, `cycles.jsonl`, the
definition manifest, and `shrink-audit.jsonl` stay byte-identical, with no
audit line and no transaction journal.

## Recoverable replacement transaction

Write mode stages complete manifest, stalls, and cycles replacements under a
transaction-specific directory. It durably writes `shrink-transaction.json`
with before/after SHA-256 hashes before replacing a target. Fault injection
after each replacement and immediately before final audit append is followed
by a fresh canonical invocation. Recovery verifies the hashes, completes only
missing staged replacements, appends `recovered-completed`, and removes the
journal. The constructed external-target conflict records `aborted` without
overwriting the unexpected target. The matrix is:

| injected point | restart result | durable outcome |
| --- | --- | --- |
| after manifest replacement | readable, exit 0 | `recovered-completed`, then `completed` |
| after stalls replacement | readable, exit 0 | `recovered-completed`, then `completed` |
| after cycles replacement | readable, exit 0 | `recovered-completed`, then `completed` |
| before final audit append | readable, exit 0 | `recovered-completed`, then `completed` |
| unexpected external target | readable, no overwrite | `aborted` |

The focused G734 class covers the density measurement, live-supervisor cycle
transition, three no-write validation counterexamples, four replacement/audit
fault points, and the aborted-target proof. The exact test count and complete
source-contract count belong to `g734-verification.json`, so this document,
the PR summary, and the emitted CI artifact cannot silently acquire different
run IDs, live byte totals, averages, savings, transcripts, or test counts.
