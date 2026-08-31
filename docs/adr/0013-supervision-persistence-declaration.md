# ADR 0013: supervision persistence is declared by the operator

- Status: Accepted for the preview-through-1.x supervision surface
- Date: 2026-08-30
- Scope: `notify supervise install`, reconciliation, and read-only liveness

## Decision

The operator must explicitly declare a supervision scheduler artifact's
cross-login persistence intent at install time with
`--persistence persistent`. intent-cli records that choice in the emitted
artifact metadata. It does not infer persistence from a path, from a loaded
process, or from a previous cycle.

`notify supervise reconcile --write` keeps artifacts carrying the declaration
and reports them as declared persistent. An undeclared login-persistent
artifact remains legacy and is removable. This distinction prevents a normal
session-only artifact from silently acquiring a new lifecycle contract while
giving an operator-declared artifact a recoverable, auditable disposition.

Install remains authoring-only: it does not execute `launchctl`, `systemctl`,
Task Scheduler, or any other lifecycle command. Registration stays an explicit
operator action using the command printed by the artifact. The liveness surface
therefore reports durable installation evidence rather than claiming that the
CLI registered or loaded a scheduler job.

`notify supervise liveness --domain <domain> --team <team> --format json` is a
separate read-only observer. It reads the persisted last completed cycle,
declared bound, elapsed age, and artifact/installation evidence without
starting or depending on the supervisor process. This gives a design or
orchestration thread an independent answer when the supervisor is absent.

## Consequences

- Omitting `--persistence` preserves the pre-G765 artifact bytes and the
  session-only cleanup behavior.
- Reconciliation can explain and test keep-versus-remove decisions instead of
  treating every scheduler artifact as one indistinguishable class.
- A persistent declaration is intent and evidence, not automatic OS
  registration or an entitlement to execute lifecycle operations.
- A stale or missing cycle is observable without staffing supervision with a
  language-model seat or adding a second supervisor.

## Rejected alternatives

- Inferring persistence from `~/Library/LaunchAgents` or another path would
  make legacy operator workarounds indistinguishable from explicit intent and
  would preserve the accidental lifecycle contract G712 rejected.
- Having liveness invoke or restart the supervisor would make the detector
  depend on the thing it detects and would violate the no-execution boundary.
- Changing cycle format, interval/bound semantics, archive, shrink, or repair
  behavior would broaden this persistence/liveness slice.
