# Release Notes — intent-cli v0.8.0

> **Release model:** this change is **prepare-only**. It does not create a
> GitHub Release or tag and does not publish a package. After this preparation
> is merged and the readiness gate below is satisfied, the operator must
> explicitly approve Release creation. Publishing that GitHub Release triggers
> `.github/workflows/release.yml` (`on: release: published`) to build and
> publish the NuGet package and platform artifacts.

## What's in v0.8.0

v0.8.0 covers exactly twelve shipped slices merged after `v0.7.1`: **G570**,
**G571**, **G573**, **G574**, **G575**, **G577**, **G578**, **G579**,
**G580**, **G581**, **G582**, and **G583**. G576 prepared the first edition of
these notes and G584 refreshes them; neither maintenance slice is counted as a
shipped product slice.

**Why minor.** G570 adds `session-layer` and G578 adds `notify` as new top-level
command groups. Together with the persisted dual-mode session layer and the
transport-neutral role workflow, these are broad, ongoing user capabilities
rather than bounded repairs, so the documented policy calls for `0.8.0`, not
`0.7.2`.

### A persisted, reversible session layer (G570)

Each domain/team can now record which session transport it uses. The default is
`agmsg`; setting the same mode again is idempotent; real changes append a
transition trail; and teams can switch in either direction:

```bash
intent-cli session-layer show --domain <domain> --team <team>
intent-cli session-layer set --domain <domain> --team <team> --mode agmsg --write
intent-cli session-layer set --domain <domain> --team <team> --mode herdr-only --write
```

The recorded mode routes `intent-cli guide orchestrator-thread`, so one team is
never handed operating instructions for both transports at once. **Both agmsg
and herdr-only remain first-class session-layer modes: they are selectable and
reversible, and agmsg is not deprecated.** agmsg remains the default and PRIMARY
transport; herdr-only remains PREVIEW. **PREVIEW scopes only to the herdr-only
session transport, never to the four-thread collaboration model.** The design /
orchestrator / implementation / review roles and their authority boundaries
remain the same in both modes.

### The herdr-only operating contract (G571, G575)

The herdr-only preview now has a complete, user-facing operating procedure:

- Provision a team workspace and role-specific panes, record a durable
  logical-role→pane mapping, and declare READY only after verified liveness:
  expected cwd/repository and agent kind, same-pane process detection, and a
  bounded ping/ack all have to agree.
- Dispatch structured tasks whose terminal marker points to an inspectable
  artifact. Completion is **artifact-gated**: state or marker text alone is
  never success; the named file, commit, PR, or report must exist and pass its
  task-specific verification.
- Bound every `agent wait` and `pane wait-output`, persist progress at timeout,
  and re-enter on a later wake rather than holding one turn indefinitely.
- Keep design-relevant completion, blocked, question, and escalation records at
  the mode-independent `<host-repo>/.intent-cli/events/<team>.jsonl` boundary.
  It is not an inter-agent bus. Its three frontend recipes are:
  1. a Claude app watcher tails complete unseen lines and retains its offset;
  2. Codex CLI in herdr is prompted through its pane and only reads the file
     when acting as a design-boundary reader; and
  3. **Codex Desktop uses a timer poll and durable byte-offset watermark.**
     This Desktop recipe is a new capability and was never supported by agmsg.

G575 hardened three procedure defects found while exercising that contract:

- a marker literal embedded in dispatched task text could be matched from its
  own pane echo, so dispatch now separates a fresh nonce from the prefix and a
  marker match can never be sufficient by itself;
- `agent wait` can report `idle` while an approval or question is visible, so
  every wait return requires pane inspection and the established supervision
  MAY/escalate boundary before re-entry; and
- the documented `workspace_created` fields and `agent wait` idle shape now
  match installed herdr 0.7.5.

### Safe shipped-skill updates (G573)

The embedded `intent-cli` dispatcher skill now carries a shipped-version
lineage. A copy that exactly matches a previously shipped version is classified
as `stale-shipped` and updates to the current embedded version without
`--force`. A locally edited copy remains `locally-modified`, and installation
fails closed rather than overwriting it. Guide surfaces emit one bounded
"Skill update available" nudge when a known installed copy is stale.

The supported customization boundary is explicit: **editing the installed
official skill is unsupported**. Keep local workflow knowledge outside that
managed artifact; `--force` remains an explicit operator choice, not an
automatic answer to detected edits.

### Parked work is no longer advertised as publishable (G574)

Stalled-work detection now distinguishes a converged blocked unit from a true
idle candidate. When the queue entry's `state` is `blocked` and its `blocked_by`
list is non-empty, the unit is informational `blocked-parked`, uses heartbeat
transport, and carries no publish recommendation; `linked_issue` does not
participate in this classification and may be null. G574 introduced the
`state-drift` classification for half-converged representations, and if a unit
is unblocked its age restarts at the unblock transition.

### Production-trial findings fixed in the same release (G577–G583)

The evidence for herdr-only is now a real production trial. The intent-cli team
used herdr-only for the actual work that published, implemented, reviewed,
merged, and closed out G577 through G583, with **zero agmsg processes for the
team**. The trial surfaced concrete defects and quality gaps; every one was
fixed on this same release line:

- **G577 — one `--workdir` rule.** Ten automation commands had private,
  inconsistent relative-path handling. They now share one resolver: an omitted
  or blank value uses the repository root, a relative value resolves from that
  root rather than the caller cwd, and an absolute path is preserved.
- **G578 — transport-neutral role workflow.** `intent-cli notify delegate`,
  `report`, and `escalate` now validate logical roles, resolve the recorded
  session mode internally, embed the canonical report command in delegated
  work, and keep design-boundary escalation on the existing events channel.
- **G579 — atomic canonical state writes.** Canonical queue-state persistence
  now writes and flushes a uniquely named temporary sibling, publishes it with
  one overwrite move, and removes the temporary file after success or
  interruption, so a reader never observes a truncated target.
- **G580 — shared-static race guard.** A discovery-based test reflects over
  every settable CLI static seam, discovers assigning test classes from IL, and
  fails with the seam and class names unless the classes are provably
  serialized. The five accepted split-collection cases are explicit and bound
  fail-closed to the supported xUnit v2 runner semantics.
- **G581 — observation without worker cooperation.** herdr
  `pane.agent_status_changed` became the normative second wake source. It uses
  the recorded logical-role mapping, working-to-settled transitions, a settle
  delay, and per-role dedupe; the event is only a reason to inspect, never proof
  of success.
- **G582 — five measured field findings.** Both modes now render the session
  switch checklist; agmsg-to-herdr teardown removes the outgoing project hook
  and delivery mode that could block the next launch at the hook-trust screen;
  every herdr mutation resolves a non-empty explicit pane/workspace id and
  fails closed instead of mutating another team's focused pane; every
  `events.jsonl` reader keeps a restart-durable identity/offset/line watermark
  and fails closed on rotation, truncation, backwards progress, or replacement;
  and stalled-work now reports `approved-not-merged` for an approved open PR
  that has exceeded its threshold.
- **G583 — warning-free build floor.** The authored 56-test-warning inventory
  plus five nullable warnings on then-current `main` were fixed at source.
  Solution-wide .NET 10 analyzers and warnings-as-errors now make the next
  warning fail the build; the deliberate CS8603 negative proof demonstrated the
  floor before the scratch edit was removed.

### Three wake sources, complementary failure modes (G578, G581, G582)

herdr-only uses three wake sources; none is treated as an outcome by itself:

1. **herdr state-change subscription** — `pane.agent_status_changed` needs no
   worker cooperation, but carries no task outcome. After its settle/dedupe
   gate, orchestration still checks pane state, a fresh completion marker, the
   artifact, and canonical intent-cli/GitHub facts, including approval or
   question pauses.
2. **canonical notify report** — `intent-cli notify report` is the most
   informative source because it carries task id, status, summary, and artifact,
   but it depends on the worker reaching and executing the report step. Its
   claims are still verified against the artifact and canonical state.
3. **periodic stalled-work check** — this is the last net rather than a
   real-time completion signal. It derives overdue work from canonical state and
   recommends recovery, including merge and closeout for
   `approved-not-merged`.

The coverage standard is that **a stall must remain detectable even if every
single wake source fails**: no source may be the sole detector. G582 added
`approved-not-merged` precisely so an approved open PR cannot become invisible
after its report or state-change wake is missed.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.8.0
```

Or download a self-contained binary from the
[v0.8.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.8.0)
after the operator has approved and published it. Verify the `.sha256` sidecar.

## Upgrade from v0.7.1

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.0
```

The default remains agmsg. Existing teams do not enter herdr-only unless the
operator records that mode through `session-layer set`; mode changes are
reversible and must follow the guide's drain/provision/READY checklist.

## Release-readiness gate (G576, refreshed by G584)

These items must hold **before the GitHub Release for `v0.8.0` is created or
published**. This gate fails closed.

- [ ] The twelve shipped slices are merged to `main`: G570 (PR #1246), G571
      (PR #1248), G573 (PR #1250), G574 (PR #1252), G575 (PR #1254), G577
      (PR #1258), G578 (PR #1260), G579 (PR #1262), G580 (PR #1264), G581
      (PR #1266), G582 (PR #1268), and G583 (PR #1270). G576 (PR #1256) is
      the initial release preparation and this G584 refresh is notes
      maintenance; neither is a shipped slice.
- [ ] These EN/JA notes cover exactly that twelve-slice lineage, and neither
      superseded `release-notes-v0.7.2.md` copy remains.
- [ ] `eng/version.json` shows `stableVersion` `0.7.1` and `nextVersion` `0.8.0`.
- [ ] The G475 next-version release-note/package guards pass with **zero
      current-state guard flips**, and the full test suite is green on the exact
      G584 notes-maintenance head.
- [ ] The recorded pre-release evidence is the intent-cli team's real
      herdr-only production trial for G577–G583, including publish through
      closeout with zero agmsg processes for the team, and every surfaced
      finding is tied above to the slice that fixed it.
- [ ] All three wake sources and their failure modes remain documented, and
      `approved-not-merged` preserves the coverage standard when report or
      state-change delivery is missed.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      repository/project URLs point at this repository, the license is
      `Apache-2.0`, and README/docs links resolve.
- [ ] Main CI (`Build and test (source contract)`) and preview-pack are green,
      and post-merge build + pack evidence is recorded on the merge commit.
- [ ] The operator has explicitly approved creating the `v0.8.0` Release.

## Publishing v0.8.0

Merging this notes maintenance does **not** create a Release, tag, package
publish, version roll, or announcement. After merge, readiness evidence must be
checked and the operator must explicitly approve Release creation.

Only then may a maintainer/operator (or explicitly authorized external release
automation) create and publish the `v0.8.0` GitHub Release. Publishing it
triggers `release.yml`, which publishes the NuGet package and platform archives.

Post-release verification includes:

- [ ] NuGet and GitHub asset links resolve and downloaded checksums match.
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.0`
      followed by `intent-cli --version` reports `0.8.0`.
- [ ] A platform archive passes checksum verification and its binary reports
      `0.8.0`.
- [ ] After publication, perform the separately authorized immediate roll to
      `stableVersion → 0.8.0` / `nextVersion → 0.8.1`, with new EN/JA DRAFT
      stubs, refreshed readiness sections, and green post-roll child-main CI.
      That roll is not part of G576 or G584.
