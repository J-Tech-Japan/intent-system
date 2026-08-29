# ADR 0012: External roles receive by cursor-based pull

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-29
- Deciders: Design, orchestration, implementation, and review; recorded by G757
- Related: G300, G578, G681, G756, G757 / #1645

## Context

An external-resident role has a recorded reader and can already receive a
durable notification event. It cannot, however, ask what arrived without
already knowing the task id. Polling a path derived by the role duplicates the
CLI's reader-resolution rules, while a pushed acknowledgement position would
add state and a second authority to the receive path.

## Decision

1. `notify collect` gains a role-scoped mode using `--role <role>` instead of
   `--task-id`. It reads the role's recorded external reader after resolving
   the effective path through `NotifyEventWriter.TryResolveReadPath`.
2. The caller may supply an opaque `--since <cursor>`. The result returns a
   `next_cursor`; the CLI stores no acknowledgement, watermark, or per-reader
   read position. Resumption therefore remains explicit at the call site and
   is deterministic without a server-side receive state.
3. `--wait` is an optional bounded receive operation and requires
   `--timeout-ms`. A timeout returns the explicit non-error `no-new-events`
   result. A missing reader is `no-events`, and a cursor that cannot identify
   an intact position is the explicit `cursor-unhonourable` refusal. Neither
   case silently resets or skips the caller's position.
4. The command is a short-lived synchronous read/poll. It creates no daemon,
   watcher, timer, process, or durable acknowledgement state. Pane-resident
   wake delivery, transports, supervise, and the existing task-id collection
   path remain unchanged.

## Consequences

- External roles have a canonical receive surface that can discover work
  through the durable event stream rather than an out-of-band wake channel.
- Caller-owned cursors make no-loss/no-duplicate resumption possible while
  keeping acknowledgement policy outside the CLI.
- A reader replacement, truncation, or malformed cursor is visible as an
  explicit refusal instead of causing replay or silent loss.
- The reader path remains compatible with scoped and legacy event locations
  because path selection is delegated to the existing shared resolver.

## Rejected alternatives

- Add a second path computation in `notify collect`: rejected because delivery
  and reading must share `TryResolveReadPath`.
- Persist a per-role acknowledgement file: rejected because it would create
  server-side receive state and make caller progress implicit.
- Reset an invalid cursor to the start or end: rejected because either choice
  duplicates already-consumed events or silently loses events.
- Run a background watcher or daemon: rejected because receive is an explicit
  bounded command and must leave no process or watcher after exit.
