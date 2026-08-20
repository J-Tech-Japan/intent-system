# ADR 0007: Execution-unit claims are authoritative over lifecycle labels

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-19
- Deciders: Operator, recorded by G717
- Related: G679 drafting claims, G680 ownership verification, G717 / #1556

## Context

The execution-unit claim registry and GitHub lifecycle labels describe the
same work from different surfaces. The registry is written by the claim
transaction, while labels are an operator-visible workflow shadow. Treating
both as independent ownership facts can deadlock a handoff: a drafting claim
may be released while `intent-issue-in-progress` remains, or a second process
may relabel an issue after a repair.

## Decision

1. The active `execution-unit:<unit>` claim in the canonical claims registry
   is authoritative for ownership. Lifecycle labels are derived shadow state
   and never override an unheld or held claim.
2. `issue publish-flow` and `automation issue-publish` keep the drafting claim
   held through the publish boundary. The attributed drafter explicitly runs
   `claim release` after the final boundary; the implementation worker then
   acquires the same execution-unit scope. The commands do not guess an actor
   or silently release someone else's claim.
3. Worker preflight reports a stale in-progress label when the canonical claim
   is unheld and proceeds on the claim. An active claim remains an in-progress
   stop even when its label is absent. Unavailable or invalid claim evidence is
   a fail-closed preflight result.
4. Publish lifecycle repair accepts one issue or one execution unit as an
   explicit scope. A scoped write enumerates and changes only that unit; the
   existing repo-wide mode remains available when intentionally requested.
5. `automation issue-release` removes only `intent-target`. It does not release
   an in-progress label or mutate the claims registry.

## Consequences

- A stale label cannot permanently block a worker after the claim is released.
- A stale or unauthorized relabel is not prevented by this ADR; the next
  preflight reports the disagreement again. Retries and backoff are not a
  concurrency policy.
- The explicit attributed release preserves claim auditability and makes the
  design-to-implementation handoff visible in rendered guidance.
- Operators can repair one domain's lifecycle artifact without writing across
  unrelated drift.

## Out of scope

This ADR does not remove G679 ownership checks, allow claim takeover, change
claim transaction attribution, or define cross-domain repair semantics.
