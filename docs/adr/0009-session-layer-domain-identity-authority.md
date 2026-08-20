# ADR 0009: Durable session-layer records own domain identity

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-20
- Deciders: Operator and implementation seat, recorded by G724
- Related: G601 session-layer markers, G717 claim authority, G724 / #1570

## Context

The host carries more than one supported domain. The durable
`.intent-cli/session-layer-mode.json` record is already scoped by domain and,
when applicable, team. The generated `AGENTS.md` / `CLAUDE.md` marker is a
single working-tree display surface, so treating that block as the host's
domain identity makes the last marker writer appear to own every domain. That
causes silent rewrites and can make a worker for another domain fail closed.

## Decision

1. `.intent-cli/session-layer-mode.json` is authoritative for host
   session-layer domain identity and mode. A team-scoped entry is more
   specific than a domain-wide entry, exactly as the existing mode resolver
   defines. Durable execution-unit or packet domain metadata is authoritative
   for the execution unit itself. An explicit worker `--domain` is accepted
   only when it agrees with the durable queue/packet domain or, for a legacy
   domain-less queue row, with the authoritative session-layer record. It is
   a migration binding for that legacy shape, never an override of declared
   durable data.
2. The generated startup-file marker is display and verification evidence
   only. It carries the domain, team, mode, and record hash so an operator can
   see which durable record a block represents, but it never establishes
   identity, selects a worker domain, or overrides the durable record. Marker
   precedence is therefore below the durable record: a marker for domain A
   does not block or rewrite domain B.
3. Marker generation is domain-scoped and additive. It replaces only the
   requested domain/team block and preserves every other block byte-for-byte.
   When a recorded domain/team has no block, the canonical writer appends its
   block under `--write`; an operator must not hand-edit `CLAUDE.md` or
   `AGENTS.md`. A command that cannot prove it will preserve another domain's
   block refuses with an explanation before writing.
4. Worker completion resolves the execution-unit domain from the durable
   queue/packet record, with an explicit matching `--domain` available from
   the worker surface. It never reads the startup marker as an identity claim.
   If durable domain evidence is absent or contradictory, the worker remains
   fail-closed and emits the exact domain-scoped re-invocation needed to
   repair or select the durable record. This is the sanctioned recovery and
   preserves queue linkage; the reporter's accidental PR-linkage recovery is
   not a sanctioned substitute.

## Migration and compatibility

- A valid existing single-domain marker remains valid and is unchanged when
  its own domain is regenerated; no migration is required and the unchanged
  path remains byte-identical.
- A multi-domain host carrying only domain A's existing marker is not asked to
  edit the file. The next canonical `marker generate --domain B ... --write`
  appends B's generated block while retaining A. Existing A content is never
  silently replaced by B.
- Existing durable mode records are read in their current schema and remain
  the source of truth. No conversion of `.intent-cli/session-layer-mode.json`
  is required.

## Consequences

- Multiple domain/team markers can coexist in one startup file without a
  last-writer-wins claim.
- A domain-B worker can complete while domain A is the visible marker owner,
  because worker identity follows durable execution-unit evidence rather than
  the display file.
- Stale or missing display evidence is visible and repairable, but cannot
  silently change routing or ownership.
- A genuine durable-domain contradiction still stops the worker; the worker
  itself names the sanctioned explicit-domain recovery instead of inviting a
  manual label edit or an undocumented PR-linkage workaround.
