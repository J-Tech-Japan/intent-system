# ADR 0001: Four-thread agmsg orchestration is the primary collaboration model

- Status: Accepted
- Date: 2026-07-20
- Deciders: Operator (2026-07-20 decision), recorded by G540
- Related: G520–G539 (the orchestration-mode feature line this decision reflects), G540 (guide-surface repositioning that implements this decision), G541 (repo-facing docs; depends on this ADR)

## Context

`intent-cli` has supported two ways to drive the implementation/review pipeline for a while:

1. **Timer-loop mode** — implementation and review threads self-schedule on recurring timers and read `worker next-action` / host review-next-slice as their source of truth. No orchestrator thread is required.
2. **Orchestrator-message mode** — a fourth **orchestrator** thread coordinates with a **design** thread and paces loopless **implementation**/**review** receivers over `agmsg` instead of independent timers.

Orchestrator-message mode was originally introduced and documented as a preview/opt-in extra (ADR-012 / spec-26 in the host intent tree), with timer-loop mode as the unqualified default. In practice, that positioning stopped matching reality: the concentrated feature work between G520 and G539 (wake contract, stalled-work detection, heartbeat safety net, issue-retire, priority override, publish-flow reliability, and the design-thread watchdog repositioning) all targeted orchestrator-message mode. It is the model that is actually used, actively maintained, and hardened against field-observed failure modes. Guide surfaces (`guide model`, `guide onboarding`, `guide orchestrator-thread`, `guide prompt-matrix`, `guide workflow suggest`, `guide help`) still framed it as preview/opt-in, which meant every fresh agent read the guide, defaulted to timer-loop mode, and had to be manually corrected by an operator toward the model that was actually going to be supported going forward.

## Decision

**Four-thread agmsg orchestration — design / orchestrator / implementation / review coordinating over `agmsg` — is the PRIMARY documented collaboration model** across intent-cli's guide surfaces (G540). Its steady state is message-driven: implementation/review `agmsg` replies wake the orchestrator directly, so routine fast polling is not required; an explicit orchestrator timer remains supported only as a fallback/legacy option.

**Timer-loop mode is retained as the documented, fully supported ALTERNATIVE** for a domain/repo that does not run an orchestrator thread. It is not deprecated, not scheduled for removal, and not described as a lesser or legacy path — it is simply the simpler setup for a domain/repo that has not (yet, or ever) stood up an orchestrator thread. `guide prompt-matrix` / `guide prompt-template` remain its canonical setup surfaces.

Exactly one mode applies per domain/repo; the two must never run simultaneously for the same domain/repo (unchanged mixed-mode invariant). The implementation and review threads stay loopless receivers regardless of which mode drives them.

As part of this repositioning, a **design↔orchestrator double-check rule** is formalized in `guide orchestrator-thread`'s role-boundary section: neither thread decides design content alone. Four categories of decision — intent shaping and clarifications, packet content and acceptance criteria, release scope and version selection, and prioritization rulings — are always consulted between design and orchestrator before they take effect. The orchestrator never authors design content unilaterally (it escalates via a structured `packet-needed` message and waits); design never bypasses the orchestrator for workflow transitions (publish/delegate/review/closeout stay the orchestrator's canonical responsibility even when design authored the underlying packet). This formalizes practice that was already de facto in effect.

## Rationale

- **Observed practice.** Orchestrator-message mode is what operators actually run for active domains. Documenting a different mode as the default sends every new agent down a path that has to be corrected.
- **Maintenance concentration.** The sustained feature and hardening work (G520–G539) has gone into orchestrator-message mode's safety and reliability properties (wake contract, stalled-work detection, the heartbeat safety net and its G539 repositioning onto a design-thread watchdog, priority override, publish-flow idempotent recovery). Timer-loop mode has not received comparable investment recently — not because it is deprecated, but because it is already simple and stable. Documenting the better-maintained, more actively hardened path as primary is more honest to an agent deciding where to invest trust.

## Alternatives considered

- **Keep timer-loop mode as the documented default, with orchestration as an opt-in extra (status quo).** Rejected: contradicts observed practice and sends every fresh agent down the less-maintained path by default.
- **Deprecate or remove timer-loop mode.** Rejected: it remains a legitimate, simpler choice for a domain/repo that does not want to stand up a four-thread `agmsg` team, and forcing migration is out of scope and unjustified — there is no operational problem with timer-loop mode itself, only with its positioning relative to orchestration.
- **Present both modes as equally weighted alternatives with no primary/alternative framing.** Rejected: this is precisely the ambiguity that caused every fresh agent to default to timer-loop mode in practice (the first mode listed, and the one requiring no additional `agmsg` setup) even though orchestration is where support and hardening are concentrated. A clear primary/alternative framing removes that ambiguity.

## Consequences

- **G540** reframes `guide model`, `guide onboarding`, `guide orchestrator-thread`, `guide prompt-matrix`, and `guide help` accordingly, removing `preview`/`experimental`/`opt-in` qualifiers from orchestration mode and adding the design↔orchestrator double-check rule. `guide workflow suggest` routes generic multi-thread implementation/review goals to the orchestrator-setup recommendation first.
- **G541** (repo-facing docs — README, NuGet package description, docs index) depends on this ADR to update the repository-facing narrative consistently with this positioning.
- The mixed-mode invariant, the loopless-receiver rule, and the underlying command/label/transition contracts are unchanged by this decision — this is a documentation and positioning decision, not a behavior change.
- Design performs the corresponding host intent-tree writeback (`intents/intent-cli/intent-tree/means/08-agent-message-orchestration.md`) after G540/G541 land, per the packet's closeout instructions; that host-tree write is out of this child repository's boundary (G300/G330/G333) and is not part of this ADR.
