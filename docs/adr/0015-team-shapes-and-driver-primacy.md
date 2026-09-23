# ADR 0015: Team shapes are equal; ADR 0001's primacy is the orchestrator driver inside a multi-seat team

- Status: Accepted (preview-through-1.x)
- Date: 2026-09-22
- Deciders: Operator (2026-09-22 ruling), recorded by G852
- Related: ADR 0001, G829, G831, G833, G851, G852

## Context

G831 added the five-thread shape and G833 added `solo-conductor` to the
transport-neutral role model. G829 deprecated `agmsg`, but none of those
changes amended ADR 0001, which names four-thread `agmsg` orchestration as the
primary collaboration model. The old wording conflates team shape, driver
choice, and transport.

## Decision

The transport-neutral role model (design / orchestrator / implementation / review) is supported in three team shapes - four-thread, five-thread and `solo-conductor` - and no team shape is primary.

ADR 0001's PRIMARY designation is narrowed to the driver choice inside a multi-seat team: the orchestrator-message driver is primary and the timer-loop driver is the fully supported alternative.

ADR 0001's naming of `agmsg` as the transport is superseded by G829: no transport is primary, herdr-only is preferred, and agmsg + herdr is deprecated.

PRIMARY labels that denote that driver choice stay as they are, including the orchestrator-thread guide's 'PRIMARY four-thread orchestrator model (ADR-012 / spec-26)' and the 'Two driver modes' table in docs/12.

## Consequences

- G852 updates `SessionLayerMode.TransportPreferenceSentence`, the guide onboarding order-1 purpose, the guide onboarding order-2 purpose, and the guide commands catalog's session-layer purpose.
- G852 updates the session-layer presentation bullet in `docs/en/09-developer-reference.md` and `docs/ja/09-developer-reference.md`.
- The NuGet `<Description>` (G541, still `Primary model: four-thread agmsg orchestration`) is a follow-up.
