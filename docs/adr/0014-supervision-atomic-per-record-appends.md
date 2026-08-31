# ADR 0014: supervision history appends are atomic per record

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-30
- Deciders: Design, orchestration, implementation, and review; recorded by G768
- Related: G734, G744, G751, G767, G768 / #1674

## Context

Supervision history is append-only JSONL, and one team can have a cooperating
supervisor plus another writer that does not take the team's directory lock.
A `File.AppendAllText` open/write sequence allowed those writers to race so
records could be lost or a line could be malformed. The existing directory
lock coordinates cooperating writers and recovery, but it cannot protect a
writer that does not participate in that lock.

## Decision

1. The regular supervision event append path serializes the complete JSONL
   record as UTF-8 without a BOM and writes it with one OS append primitive.
   Unix uses `O_APPEND`; Windows opens with `FILE_APPEND_DATA`. The complete
   record is submitted in one native write, and no seek-to-end emulation is
   used.
2. The existing per-team `.supervision.lock` remains in place. It continues
   to coordinate recovery, evidence-definition maintenance, and cooperating
   writers; the OS append primitive additionally protects the record boundary
   for an uncooperative writer.
3. The shared append path is used by cycle, stall, and prompt-audit records.
   The serial bytes, JSONL field order, event semantics, and read behavior are
   unchanged. Existing shrink/audit and repair semantics remain separate.
4. This is an integrity guarantee, not a lifecycle policy: intent-cli does not
   kill, stop, rank, elect, or lease supervisor processes, and it does not
   repair pre-existing corruption or arbitrary non-append file rewrites.

## Consequences

- One locked writer and one writer outside the directory lock retain every
  complete record when both use the OS append contract.
- A serial fixture can compare raw bytes with the parent implementation, so
  the concurrency repair does not silently change the persisted format.
- Existing damaged files and writers that replace or truncate files remain
  outside this append-boundary decision.

## Rejected alternatives

- Rely only on the directory lock: rejected because the measured losing writer
  did not acquire it.
- Coordinate writers through a second process, watcher, election, or lease:
  rejected because that would cross the no-lifecycle boundary.
- Change JSONL framing or add a repair/compaction pass: rejected because this
  unit changes the append primitive only and preserves existing history behavior.
