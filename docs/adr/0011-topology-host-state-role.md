# ADR 0011: Record and discover the topology host-state role

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-26
- Deciders: Operator, design, orchestration, implementation, and review; recorded by G736
- Related: G300, G655, G716, G733, G736; [EN orchestration guide](../en/12-agent-message-orchestration.md); [JA orchestration guide](../ja/12-agent-message-orchestration.md)

## Context

The orchestration guide routes write-bearing publication, host Git, and
host-state linkage to a non-sandboxed host-state role, while the topology had
no field that could name that role or its envelope. An all-sandboxed team could
therefore validate as healthy and discover the missing capacity only at its
first publish attempt. The existing least-privilege refusal to widen a Codex
seat remains correct, and G733's seat/host duty division remains unchanged.

## Decision

1. A topology may contain an explicit `host_state` object with `role` and
   `envelope` strings. It is recorded through the canonical
   `session-layer topology record-host-state` command and is consumed by
   `topology validate`, `topology show`, and the orchestrator guide.
2. Authority comes only from that recorded declaration. `resident`, `kind`,
   external placement, a shared pane/workspace, and co-location are not
   authority signals and never substitute for the declaration.
3. A topology without the declaration remains valid and is not migrated. Its
   validation emits an informational `host-state-role-missing` finding before
   publish, naming the host-state workflow work the team cannot perform.
4. The finding is honest: a declaration records the intended route but does
   not provision a non-sandboxed participant. The team needs an actually
   capable participant plus the explicit declaration.
5. A design role explicitly declared as host-state is legitimate. The
   prohibition on asking design to perform routine workflow transitions applies
   only to undeclared or ad-hoc requests; the orchestrator still owns the
   canonical routing and must not let design bypass it.

## Consequences

- The orchestrator can discover a named role and envelope instead of routing
  to an unrecordable abstract role.
- Legacy and all-sandboxed topologies remain readable and usable, while their
  missing host-state capacity is visible before publish.
- The declaration does not weaken the Codex sandbox boundary or make design
  mandatory/automatic. A team with no capable participant remains unable to
  perform the required host-state workflow until one is supplied.
- English and Japanese guides can state one consistent contract: declared
  design host-state is allowed; undeclared/ad-hoc requests are not.

## Rejected alternatives

- Infer authority from `resident`, `kind`, external placement, or co-location:
  rejected because those attributes describe topology/delivery, not host
  write capability.
- Widen the Codex sandbox or make host repository state a child dependency:
  rejected by G300/G733 and the measured seat boundary.
- Make design automatically host-state or require design for every team:
  rejected; the operator records whichever actually capable role and envelope
  the team supplies.
- Refuse or migrate every legacy topology without a declaration: rejected;
  the compatibility contract is to remain valid while reporting the missing
  capacity before publish.
