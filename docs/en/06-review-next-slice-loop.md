# Review / next-slice loop setup

> **Ask intent-cli first:** `intent-cli guide start` →
> `intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>`.
> ← [docs index](index.md)

This is **host/review** work. It reviews PRs against the packet/intent contract,
requests updates, approves/merges, and cuts the next slice. It may operate on
host metadata, but always via `intent-cli`-supported transitions.

```bash
# Get the authoritative review/next-slice prompt
intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>

# PR-specific review guidance (checklist, packet refs, approval/request-update reqs)
intent-cli guide review --pr <n> --repo <owner>/<repo> --format json

# Label transitions (review-start, request-update, approve, …) — never by hand
intent-cli automation pr-transition --transition <name> --write --format json
```

## Ask-intent-cli prompt template

> Run the host review / next-slice loop for domain `<name>` / `<owner>/<repo>`.
> Use `intent-cli guide oneshot --kind host-review-next-slice` and
> `intent-cli guide review --pr <n>`. Apply every label transition via
> `intent-cli automation`, and tie approvals to packet/intent evidence, not just
> green tests.

## Metadata / label safety

- Review label transitions (`intent-pr-reviewing`, `intent-pr-request-update`,
  `intent-pr-approved`, …) are applied by `intent-cli automation`, never by hand.
- Passing tests is **necessary but not sufficient** — approval requires
  packet/intent conformance evidence (see `guide review`).
- Current-PR acceptance-criterion blockers get a durable PR comment before
  completing as request-update/clarification (see [recovery](07-recovery.md)).

## Next

[Recover when a loop looks wrong](07-recovery.md).
