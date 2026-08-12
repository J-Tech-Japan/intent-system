# ADR 0003: Git push-CAS is the multi-user work-ownership primitive

- Status: Accepted
- Date: 2026-08-12
- Deciders: Operator, design, and orchestrator; recorded by G679
- Related: G604, G628, G629, G630, G671, G679, G680; [EN claim lifecycle](../en/05-implementation-loop.md#preview-git-backed-cross-clone-scope-claims-g679); [JA claim lifecycle](../ja/05-implementation-loop.md#preview-git-backed-cross-clone-scope-claim-g679)

## Context

Two humans may each run a complete team against one intent domain. Existing
per-team topology, messaging, pending-delegation, and supervision files isolate
delivery, but they do not decide which team owns one execution unit or one
release preparation. GitHub label claims are read-then-add operations, and
release preparation previously had no shared transaction scope.

The operator chose an optimistic, serverless mechanism: write that work is
starting to Git and proceed only when that write reaches the shared remote
without a conflicting same-scope claim. Full cross-clone consistency for queue
state and every workflow surface is not required.

The repository's broad `.intent-cli/**/*.jsonl merge=union` policy is correct
for append-only logs but unsafe for ownership exclusion. If it applied to
claims, an add/add conflict could retain both claims and silently turn the lock
into a log.

## Decision

Git push compare-and-swap semantics are the multi-user work-ownership
primitive for these two preview scopes:

- `execution-unit:<EU>`
- `release-prep:<owner/repo>:<version>`

One immutable active file under `.intent-cli/claims/` carries scope, actor,
team, claim time, and base commit. Acquisition is fast-forward-only pull,
create, commit, and a plain push. **Only successful remote push is acquisition.**
A local file, local commit, preflight read, GitHub label, or queue projection is
not an ownership fact. Claim paths never force-push.

A rejected push is inspected after fetch. A same-scope active record means
`held` and names the recorded actor and team. If no same-scope record exists,
the rejection is an unrelated remote advance and the change may be reapplied
on a fresh fast-forwarded base with a bounded retry. These outcomes remain
distinct.

Release is authorized by the complete recorded holder identity: both actor and
team must match. Takeover is a separate explicit attributed operation that
names the displaced holder. Both transitions record actor, team, timestamp,
and reason in history. Neither happens automatically.

Claims must retain conflicts. Fresh hosts place
`.intent-cli/claims/** -merge` after broad union rules; existing hosts receive
the exact lines as guidance and are never auto-migrated.

Age is evidence only. `claim-stale` reports actor, team, scope, age, and last
evidence, but time never expires, releases, reassigns, or takes over ownership.

## Consequences

- Multi-user ownership needs no allocator, daemon, lease server, or database;
  the shared Git remote is the sole arbiter.
- Busy repositories do not turn every non-fast-forward into a false `held`;
  unrelated advances receive one bounded fresh-base reapplication path.
- Exclusion depends on preserving the Git conflict. Union merging or force
  pushing the claims subtree would invalidate the primitive.
- Holder identity is `(actor, team)`, not actor alone; teams may reuse actor
  names without gaining release authority over one another's claims.
- Operators retain judgment over stale claims and takeover. There is no lease,
  heartbeat, automatic expiry, automatic release, or automatic takeover.
- G679 provides the primitive and role-guide routes. G680 separately owns
  command-level consumer enforcement in packet draft, publish, worker,
  release-prep, and next-slice surfaces.
- Queue-state/runs cross-clone semantics, additional scope kinds, and
  remote-herdr single-orchestration machinery remain unchanged.

## Alternatives considered

- **Central lock/lease service.** Rejected: adds a server and operational
  authority the operator explicitly did not request.
- **GitHub labels as ownership.** Rejected: label mutation has a read-then-add
  race and no conditional compare-and-swap fact.
- **Local lockfile or local commit as acquisition.** Rejected: neither is
  visible to another clone; only the remote can arbitrate.
- **Treat every rejected push as held.** Rejected: unrelated repository traffic
  would create false ownership conflicts.
- **Union-merge claim records.** Rejected: retaining both sides destroys mutual
  exclusion exactly when two claimants race.
- **Time-based expiry or automatic takeover.** Rejected: duration is not an
  ownership judgment and cannot safely identify an abandoned claim.
