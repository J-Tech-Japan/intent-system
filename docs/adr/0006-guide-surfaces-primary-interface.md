# ADR 0006: Guide surfaces are the primary interface

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-14
- Deciders: Operator, recorded by G701
- Related: G645 guide-reachability records, G696 structured guide registries, G701

## Context

Humans and AI agents operate intent-cli through installed guide surfaces. A
feature that exists in code but cannot be found and executed from the declared
route is not usable by either executor. G645 already records reachability for
each unit, but the decision that makes those records meaningful was not yet a
repository ADR.

The herdr standard layout is a current measured example: one team tab, with
orchestration on the left and implementation above review in the right
column, and role labels on every pane. The guide must expose the exact
operator commands without taking ownership of herdr state.

## Decision

The following clauses are normative and checkable:

1. **Guide surfaces are the primary interface for humans and AI agents.** A
   human or agent executor must be able to discover the supported capability
   from the installed guide route that names it.
2. **A missing, wrong, or stale guide route means the capability is operationally unshipped.** Code, a passing unit test, or an undocumented
   manual workaround does not change that classification.
3. **Guide-route execution is acceptance substance equal to functional tests.** Acceptance is incomplete until the built CLI executes the declared
   route from the applicable metadata-free or recorded context and its
   production output contains the claimed surface.
4. **G645 per-unit reachability records enforce this decision.** Each unit's
   declared role, guide surface, and target surface must have the corresponding
   reachability record; the record mechanics remain unchanged and do not judge
   guide quality on the host's behalf.

The standard herdr layout is rendered by the versioned structured registry
`herdr-standard-layout/v1` in `guide orchestrator-thread`. The registry is
enumerable and includes the one-tab/three-pane arrangement, labels, exact
creation commands, and the measured move/rename repair commands. Its named
`layout-and-labels` setup check reports visible incompleteness but is not a
READY hard block. The guide is read-only and never executes herdr.

Setup and design-thread guides render the same `dialog-answering/v1` rule:
self-provisioned gates are answered by the provisioner; an action already
approved by the human in conversation may be answered mechanically by design
through the session layer only after an exact dialog/action match, with the
human as decision actor and the conversation approval as grounds; all
unapproved, unknown-origin, uncertain, or mismatching dialogs escalate through
design to the human with grounds. A per-action approval never generalizes to a
class. G690 is distinct: its hard risk floor bounds what design may decide
alone, not the execution of a human decision already recorded in conversation.

## Consequences

- Guide output is reviewed and tested as a shipped interface alongside code.
- The layout registry can evolve by version without making herdr state an
  intent-cli responsibility.
- A setup operator receives an actionable, exact plan while the CLI retains
  observation-only and no-terminal-mutation boundaries.
- EN/JA documentation must carry the same decision, route, layout, and dialog
  authority semantics under the G613 terminology policy.

## Out of scope

This ADR does not enforce herdr layout programmatically, execute creation or
repair commands, change G645 record mechanics, or add a new supervision seat.
