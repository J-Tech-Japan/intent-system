# ADR 0010: Child implementation owns child Git; host duties use the message channel

- Status: Accepted (preview-through-1.x)
- Date: 2026-08-25
- Deciders: Operator, design, orchestration, and implementation; recorded by G733
- Related: G300, G330, G333, G679, G719, G733; [EN implementation loop](../en/05-implementation-loop.md); [JA implementation loop](../ja/05-implementation-loop.md)

## Context

The implementation seat and the intent host are separate authorities. The
implementation seat must be able to complete an assigned child repository
change, but execution-unit claim acquisition and durable host workflow state
belong to the host role. On a co-located machine, the child may accidentally be
able to read host files or use host credentials, which makes a boundary failure
look like a missing permission rather than a missing capability.

That accidental access is not portable. A remote-herdr deployment can put the
implementation seat on another VM with no shared filesystem, host credentials,
or GitHub API access for the host repository. A child workflow that reaches for
those surfaces therefore passes today's local tests and fails at the intended
role separation.

## Decision

1. The implementation seat owns the child-repository path end to end: issue
   contract, child Git fetch and branch, source edits, tests, commit, push,
   ready-for-review PR with `Closes #<issue>`, and GitHub-only worker
   completion.
2. The host role owns `.intent-cli/` queue-state, claims, runs, packets,
   metadata branches, host Git, host credentials, and host-repository API
   operations. Execution-unit claim acquisition remains a host duty.
3. The host role acquires and verifies ownership only through the existing
   compare-and-swap surfaces:

   ```text
   intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json
   intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json
   ```

   The seat accepts evidence only when the acquire result has
   `status=acquired`, `push_succeeded=true`, matching scope/actor/team and a
   pushed commit, and verification has `passed=true` / `status=owned`. A label,
   local record, local commit, or preflight result is not ownership.
4. A child that needs host work sends one canonical message-channel request
   through `intent-cli notify report`, naming the exact host commands and
   returning the host JSON evidence. The child never hand-writes agmsg/herdr
   transport and never performs the host operation locally:

   ```text
   intent-cli notify report --domain <domain> --team <team> --from implementation --to orchestration --task-id <task-id> --status question --artifact <child-artifact> --summary 'HOST DUTY REQUEST: run intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json; then intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json; return the JSON evidence, pushed commit, and owned verdict.' --routing-root <host-routing-root> --report-root . --write --format json
   ```
5. The child must fail closed at the boundary. It does not read or mutate host
   metadata, acquire/release/take over a claim, use host credentials or the
   host-repository GitHub API, widen its sandbox, or improvise a clone. A
   host-aware refusal such as inability to refresh `FETCH_HEAD` is reported as
   the host duty, not repaired by the child.

## Consequences

- Child Git and PR delivery are no longer silently delegated to design or
  orchestration merely because the child cannot reach host state.
- Host ownership remains auditable and race-safe: **Only successful remote push is acquisition.**
  The successful push is still the claim fact, and compare-and-swap semantics
  are unchanged.
- The message channel carries a bounded host request and evidence; it does not
  turn the child into a host writer or make a local report a claim.
- Verification can prove both halves independently: child emitted
  issue-to-PR output and the still-refused host-boundary probe.
- Co-location remains convenient but is deliberately not a capability. The
  contract continues to work when host filesystem, credentials, and
  host-repository API are absent from the seat.

## Rejected alternatives

- **Widen the child sandbox to the host root:** rejected; it violates G300/G330
  and makes the accidental co-located capability contractual.
- **Let the child acquire the execution-unit claim:** rejected; claim Git and
  compare-and-swap state belong to the host role, and a local claim is not
  visible ownership.
- **Use host-repository GitHub API access from the child:** rejected; shared
  credentials are not portable and make remote-herdr fail at role separation.
- **Route routine child Git/PR work to design or orchestration:** rejected; the
  implementation seat owns its child repository and reports only the host duty
  it cannot perform.
- **Treat a lifecycle label or preflight as claim evidence:** rejected; the
  existing G679 claim transaction and `claim verify` evidence remain required.
