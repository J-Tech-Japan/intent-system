# Release Notes — intent-cli v0.8.0

> **Release model:** this change is **prepare-only**. It does not create a
> GitHub Release or tag and does not publish a package. After this preparation
> is merged and the readiness gate below is satisfied, the operator must
> explicitly approve Release creation. Publishing that GitHub Release triggers
> `.github/workflows/release.yml` (`on: release: published`) to build and
> publish the NuGet package and platform artifacts.

## What's in v0.8.0

v0.8.0 covers exactly five slices merged after `v0.7.1`: **G570**, **G571**,
**G573**, **G574**, and **G575**.

**Why minor.** G570 adds `session-layer` as a new top-level command group, and
the persisted dual-mode session layer is a broad behavior addition across
orchestrator guidance. That is an ongoing user capability rather than a bounded
repair, so the documented policy calls for `0.8.0`, not `0.7.2`.

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
never handed operating instructions for both transports at once. The
positioning is deliberate: **agmsg is PRIMARY and herdr-only is PREVIEW. PREVIEW
is scoped only to the session transport, never to the four-thread model.** The
design / orchestrator / implementation / review roles and their authority
boundaries remain the same in both modes.

### The herdr-only operating model, proven before release (G571, G575)

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

Before release, the design team ran the full concept spike against merged main
on 2026-08-02: **11/11 checks passed, with zero agmsg processes for the spike
team**. The spike also found three procedure defects, all corrected in G575:

- a marker literal embedded in dispatched task text could be matched from its
  own pane echo, so dispatch now separates a fresh nonce from the prefix and a
  marker match can never be sufficient by itself;
- `agent wait` can report `idle` while an approval or question is visible, so
  every wait return requires pane inspection and the established supervision
  MAY/escalate boundary before re-entry; and
- the documented `workspace_created` fields and `agent wait` idle shape now
  match installed herdr 0.7.5.

No additional dogfood is claimed by this release-preparation change.

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
idle candidate. When both the queue item and linked issue are blocked, the unit
is informational `blocked-parked`, uses heartbeat transport, and carries no
publish recommendation. A half-converged blocked state remains `state-drift`,
and if a unit is unblocked its age restarts at the unblock transition.

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

## Release-readiness gate (G576)

These items must hold **before the GitHub Release for `v0.8.0` is created or
published**. This gate fails closed.

- [ ] The five release-bound slices are merged to `main`: G570 (PR #1246), G571
      (PR #1248), G573 (PR #1250), G574 (PR #1252), and G575 (PR #1254), plus
      this G576 release-preparation PR.
- [ ] These EN/JA notes cover exactly that five-slice lineage, and neither
      superseded `release-notes-v0.7.2.md` copy remains.
- [ ] `eng/version.json` shows `stableVersion` `0.7.1` and `nextVersion` `0.8.0`.
- [ ] The G475 next-version release-note/package guards pass with **zero
      current-state guard flips**, and the full test suite is green on the exact
      release-preparation head.
- [ ] The recorded pre-release evidence is still the verified 11/11 spike with
      zero agmsg processes for its team; do not replace it with an unverified
      marker-only or state-only claim.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      repository/project URLs point at this repository, the license is
      `Apache-2.0`, and README/docs links resolve.
- [ ] Main CI (`Build and test (source contract)`) and preview-pack are green,
      and post-merge build + pack evidence is recorded on the merge commit.
- [ ] The operator has explicitly approved creating the `v0.8.0` Release.

## Publishing v0.8.0

Merging this preparation does **not** create a Release, tag, package publish,
version roll, or announcement. After merge, readiness evidence must be checked
and the operator must explicitly approve Release creation.

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
      That roll is not part of G576.
