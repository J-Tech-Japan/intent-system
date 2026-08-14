# ADR 0005: Delivery and authoring-only team modes

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-13
- Deciders: Operator, recorded by G691
- Related: ADR-014 (the parent intent's team-shape decision; unchanged), ADR 0001 (delivery collaboration model)

## Context

The delivery shape currently assumes four judgment-bearing threads and a
standing supervision process. That is the right shape when a team delivers
work through local seats, but it is not the smallest useful shape for an
operator who only shapes intents, authors packets, and publishes GitHub
issues. Session-layer transport (`agmsg` or `herdr-only`) is a separate
concern and must not be used as a third team shape.

## Decision

`team_mode` is a durable, command-produced value with two values:

| Mode | Contract |
| --- | --- |
| `delivery` | The existing delivery behavior, four judgment-bearing threads, and supervision contract. This is the default when no record exists. |
| `authoring-only` | An operator/design front door that shapes or interviews intent, authors packets, and publishes issues. It has zero delivery seats, no supervision process, no worker lifecycle, and no topology requirement. |

The record lives at `.intent-cli/team-mode.json`, is scoped by domain and
optional team, and retains an explicit transition trail. `team-mode set` is
dry-run by default and requires `--write` for mutation. A malformed or
command-impossible record fails closed. Team mode does not select or rewrite
the session-layer transport.

For `authoring-only`, bootstrap renders only front-door acceptance and
repository/claim/publish prerequisites. `guide next` offers only
shape/interview, packet authoring, publish, improve, inspect, and idle.
The measured bootstrap state is `authoring-only-complete` when the durable
`team_mode=authoring-only` record and front-door shape have been inspected;
repository/claim/publish commands remain explicit operator prerequisites, not
missing delivery facts. `notify supervise`, `notify supervise install`,
`notify adjudicate`, and delivery-topology surfaces return the named
`not-applicable-team-mode` outcome. `notify adjudicate` is not applicable
because an authoring-only team has no delivery seat or adjudication dialog to
adjudicate. The G691 gate leaves `notify report`, `notify escalate`,
`notify status`, and `notify dispose` usable as reporting and settlement
surfaces. No new publish, delegation, or handoff behavior is introduced here;
those pipeline decisions remain later-slice scope. Delivery output and
behavior are byte-identical when the mode is absent or explicitly recorded as
`delivery`.

## Consequences

- The smallest authoring team is first-class without inventing a transport or
  making a missing delivery topology look healthy.
- Existing delivery installations do not migrate automatically and retain
  their current behavior.
- Publish authority, confirmation, stalled-work ownership, and external handoff
  remain later slices; this ADR only defines the team shape and its routing.

## Traceability

This ADR is the child-repository successor note for **ADR-014** in the parent
intent record. ADR-014 remains unchanged; the parent record should link this
successor when its host-side write-back is performed.
