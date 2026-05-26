# Review / next-slice loop setup

> ← [docs index](index.md)

This is **host/review** work. It reviews PRs against the packet/intent contract,
requests updates, approves/merges, and cuts the next slice. It may operate on
host metadata, but always via `intent-cli`-supported transitions.

## Design-thread prompt

Paste this into your AI agent (Claude, Codex, Copilot, etc.):

> Run the host review / next-slice loop for domain `<name>` / `<owner>/<repo>`.
> Get the full loop prompt via:
> `intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>`
> and follow it exactly. Apply every label transition via `intent-cli automation`
> — never hand-apply labels. Tie approvals to packet/intent evidence, not just
> green tests.

## What the agent will run (reference)

```bash
# Get the authoritative review/next-slice prompt
intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>

# PR-specific review guidance (checklist, packet refs, approval/request-update reqs)
intent-cli guide review --pr <n> --repo <owner>/<repo> --format json

# Label transitions (review-start, request-update, approve, …) — never by hand
intent-cli automation pr-transition --transition <name> --write --format json
```

## Metadata / label safety

- Review label transitions (`intent-pr-reviewing`, `intent-pr-request-update`,
  `intent-pr-approved`, …) are applied by `intent-cli automation`, never by hand.
- Passing tests is **necessary but not sufficient** — approval requires
  packet/intent conformance evidence (see `guide review`).
- Current-PR acceptance-criterion blockers get a durable PR comment before
  completing as request-update/clarification (see [recovery](07-recovery.md)).

## Next

[Recover when a loop looks wrong](07-recovery.md).
