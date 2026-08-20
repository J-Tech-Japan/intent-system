# ADR 0008: Sender-local report reconciliation

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-20
- Deciders: Orchestration and implementation seats, recorded by G719
- Related: G300 seat boundaries, G330 host authority, G719 / #1560 / #1562

## Context

An implementation checkout is a separate seat. It may have one bounded
`--add-dir <role-work-root>` but must not receive a write grant to the host
routing root. The canonical report therefore carries
`--routing-root <host>` as read/transport context and `--report-root .` as the
sender-local durable outbox.

Before G719, a successful sender-local report could reach orchestration while
leaving the host pending delegation and continuation chain open. The child
could not safely repair that state: a host write would cross the seat boundary,
and a retry could append duplicate lifecycle evidence. External-reader delivery
has the related case where the reader is under the host root: that is a
delegation-level routing fault, while the local report must remain available.

## Decision

The child writes only its local report outbox and uses the transport-neutral
`notify report` command. Orchestration consumes a delivered local entry through
the explicit host-owned command:

```text
intent-cli notify reconcile --domain <d> --team <t> --task-id <id> \
  --routing-root <host> --report-root <role-work-root> --write --format json
```

The reconciliation surface:

1. Reads the sender-local outbox entry for the task's result-nonce and accepts
   only the `delivered` generation.
2. Appends one host pending `report` snapshot keyed by task id and result
   nonce. An identical existing snapshot returns `already_converged`; a
   conflicting snapshot fails closed.
3. Appends one `report-received` continuation link keyed by the completion
   signal. An identical link returns `already_converged`; a conflict fails
   closed.

The two append-only stores are intentionally reconciled in a retry-safe order,
not as a new cross-seat transaction. If one host append succeeds and the next
fails, the next orchestration attempt sees the first as already converged and
completes only the missing part. The child never writes host pending state,
continuation state, queue, runs, packet, or a hand-written transport call.

## G300/G330 tradeoff

G300's seat boundary makes the role-work root the only implementation write
surface. G330 makes the host routing root and its durable workflow state an
orchestration authority, rather than an implicit child capability. This costs
one explicit orchestration reconciliation wake after a sender-local report,
but it preserves least privilege, makes authority visible in emitted JSON, and
allows a genuine sandbox-denied seat to complete its report without retrying a
host write. The local outbox remains the handoff if delivery or reader routing
fails.

## Rejected alternatives

- Granting the child `--add-dir <host-routing-root>`: violates G300/G330 and
  makes a child responsible for host-owned lifecycle mutation.
- Having the child append pending or continuation records directly: creates a
  split authority and cannot make the pending-plus-chain pair exactly once
  across a seat boundary.
- Using `notify collect` as reconciliation: collection is delivery recovery for
  an undelivered entry, while a delivered sender-local report needs host-state
  closure without re-sending work.
- Hand-writing herdr/agmsg or reader transport from the seat: bypasses the
  canonical route and can turn a host-reader failure into an untracked write.
- Retrying, re-registering, restarting, or killing the seat automatically:
  changes an executability diagnostic into an ownership mutation. Active
  registration remains an ownership stop; operator action is explicit.

## Evidence

`NotifyG719Tests` launches the exact generated `notify report` command from a
separate Unix seat with host paths mode-denied. It proves local outbox
persistence, denied queue/runs/packet writes, unchanged host state, one-time
host reconciliation, idempotent replay, and the external-reader
`report-routing-root-write-required` outcome with the local handoff retained.
