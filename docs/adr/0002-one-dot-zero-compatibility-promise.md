# ADR 0002: The 1.0 line makes a machine-surface compatibility promise

- Status: Accepted
- Date: 2026-08-04
- Deciders: Operator, design, and orchestrator; recorded by G612
- Related: [1.0 compatibility promise](../en/1.0-compatibility-promise.md), G599, G604

## Context

`intent-cli` exposes commands, structured JSON, exit codes, and durable
records to automation, but pre-1.0 compatibility practice was implicit. A 1.0
number without a clear compatibility boundary would leave callers unable to
tell an API from presentation text or decide when an old surface can retire.

## Decision

Starting at 1.0, command and flag names; machine-consumed JSON field semantics
and documented cause values; exit codes; and durable-record schemas and state
transitions are covered compatibility surfaces. Prose/layout and unstructured
diagnostics are explicitly outside the promise.

The repository keeps a reviewable compatibility ledger of the v0.11.1
machine-consumed baseline. Every registered command/subcommand has a
disposition, and a test prevents a later registered command from shipping
without a ledger row. Covered changes require a documented replacement, a
structured warning, and an alias through 1.x; removal is for the next major.

## Consequences

- The `operator-attention` name is listed as `deprecate-with-alias`; its
  mechanical rename remains a separate follow-up and is not performed here.
- Legacy compatibility reads, runtime-state shapes, packet-schema variants,
  and field aliases are inventoried, not removed. A pre-1.0 retirement needs
  named migration evidence; otherwise it remains through 1.x.
- The pre-1.0 road is G611 link guard, G612 promise/ledger, aliased attention
  rename, separately eligible retirements, evidence-gated herdr-only
  graduation, and 1.0 release preparation.

## Alternatives considered

- **Promise only commands.** Rejected: machine JSON, exits, and durable state
  are consumed by automation and need the same clear boundary.
- **Freeze prose and layout.** Rejected: this would make ordinary guide and
  diagnostic improvements unnecessarily breaking while not protecting the
  machine contract.
- **Remove known legacy paths now.** Rejected: inventory is not migration
  evidence; removal is a separately eligible change.
