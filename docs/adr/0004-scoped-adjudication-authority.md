# ADR-0004: Scoped adjudication authority with a hard risk floor

- Status: accepted for preview-through-1.x
- Date: 2026-08-13
- Scope: residual prompt adjudication (G690), preserving G683 and G689

## Context

G683 and G689 made residual prompt handling exact, policy-backed, audited, and
orchestration-owned. That boundary prevented generic relay and fuzzy answers,
but the phrase “design never answers” was broader than the authority model the
system needs to express. A future class may be mechanically answerable by a
different role without making an unscoped design thread an approval seat.

G689 also established a hard same-wake identity contract for
`owned-scratch-delete`. A new authority model must not weaken that contract or
turn a stale dialog observation into permission to send keys.

## Decision

Prompt classes and matched shell scopes declare `answerable_by` and risk tags.
The adjudication pipeline intersects class and scope capabilities, requires an
exact recipe class and validated policy, and refuses the design actor for the
closed risk-floor vocabulary:

- `destructive`
- `credential`
- `permission-change`
- `security`
- `product-decision`
- `unverifiable`

The canonical design-facing surface is `intent-cli notify adjudicate`. It
accepts a recorded pane, state-change sequence, and observed-text SHA-256;
reads the live dialog; and rechecks all three immediately before the bounded
`herdr agent send-keys`. A mismatch is a fail-closed
`stale-dialog-cas-refused` outcome with no keys sent. Direct relay, direct
`send-keys`, fuzzy classification, and unscoped forwarding are not authority
paths. Audits record the decision actor separately from the mechanical
executor, together with the scope/rule and CAS identity.

This slice does not make any shipped class or scope design-answerable. It
ships the capability resolution and canonical surface so a future class must
declare authority explicitly and inherit the hard floor. Existing G683/G689
classes remain orchestration-only. In particular, `owned-scratch-delete`
continues to require exact paths and the current wake/cycle identity.

## Consequences

The old absolute wording is replaced by a testable authority rule: design may
answer only when class, scope, capability, risk floor, audit, and live CAS all
permit it. Existing prompts retain their behavior, while stale pane changes
cannot authorize an answer. The command, audit fields, docs, and regression
tests form one preview contract; it remains outside the 1.0 compatibility
promise until a future major release.

G675 remains out of scope. Workflow ownership, GitHub labels, and intent-cli
metadata authority do not move to the design role.
