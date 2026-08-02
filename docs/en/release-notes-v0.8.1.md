# Release Notes — intent-cli v0.8.1

> **Release model:** this change is **prepare-only**. It does not create a
> GitHub Release or tag, publish a package, roll the version policy, or change
> product behavior. After this preparation is merged and the readiness gate
> below is satisfied, the operator must explicitly approve Release creation.
> Publishing that GitHub Release triggers `.github/workflows/release.yml`
> (`on: release: published`) to build and publish the NuGet package and
> platform artifacts.

## What's in v0.8.1

v0.8.1 covers exactly five correctness slices merged after `v0.8.0`: **G585**,
**G586**, **G587**, **G588**, and **G589**. Their PRs and merge commits are:

- **G585** — [PR #1274](https://github.com/J-Tech-Japan/intent-system/pull/1274),
  “G585: disclose omitted session-layer team,”
  merge [`9e87d0973f53bd56e03af0622c54dd4d505eedbd`](https://github.com/J-Tech-Japan/intent-system/commit/9e87d0973f53bd56e03af0622c54dd4d505eedbd).
- **G586** — [PR #1276](https://github.com/J-Tech-Japan/intent-system/pull/1276),
  “G586: restore herdr pane-first topology,”
  merge [`0e93973c95cd992dbceda1c8416818a20ab1c319`](https://github.com/J-Tech-Japan/intent-system/commit/0e93973c95cd992dbceda1c8416818a20ab1c319).
- **G587** — [PR #1278](https://github.com/J-Tech-Japan/intent-system/pull/1278),
  “G587: make packet readiness answer in one shot,”
  merge [`bdadd711f6e6ed9960a2542f82369c886e3bb163`](https://github.com/J-Tech-Japan/intent-system/commit/bdadd711f6e6ed9960a2542f82369c886e3bb163).
- **G588** — [PR #1280](https://github.com/J-Tech-Japan/intent-system/pull/1280),
  “Fix notify routing for recorded external roles,”
  merge [`e4f18f50c31ddb327f598d6fe5017a7674e1685b`](https://github.com/J-Tech-Japan/intent-system/commit/e4f18f50c31ddb327f598d6fe5017a7674e1685b).
- **G589** — [PR #1282](https://github.com/J-Tech-Japan/intent-system/pull/1282),
  “G589: make CI waits observable without timers,”
  merge [`d6a5110a6d9d2c6fd4575b6b8c28b46ca6820a23`](https://github.com/J-Tech-Japan/intent-system/commit/d6a5110a6d9d2c6fd4575b6b8c28b46ca6820a23).

All five merge commits were verified on `main` during release preparation.
Together, the five fixes make wake reliability under herdr-only the release
theme: correct mode and topology guidance, fail-closed packet readiness,
delivery to every recorded role, and an observable end to CI waits.

**Why patch, not minor.** The documented policy reserves MINOR for a new
command surface or a broad behavior change. None of these five slices adds a
command surface, and every slice is a correctness fix to an existing surface.
The release therefore advances from `0.8.0` to `0.8.1` as a PATCH.

### Correct session-mode resolution and visible herdr topology (G585, G586)

- **G585** fixes the omitted-team routing defect. When a caller omits `--team`,
  the session-layer resolver no longer silently answers from the wrong scope;
  guidance discloses the team-scoped records and gives corrective commands.
- **G586** restores the literal herdr-only topology: one workspace per team,
  one team-named tab, and one role-cwd pane per role. Pane split is the default;
  tab creation is an explicitly justified exception. Explicit-id and
  fail-closed mutation rules remain in force.

### Packet readiness now fails closed in one actionable answer (G587)

Packet readiness no longer reports green when only `packet.yaml` exists. The
packet-draft and queue-seed surfaces consistently report every missing
canonical file, missing contract section, and other refusal reason together,
so an author can repair the whole packet without repeated one-error cycles.

### Canonical notify reaches every recorded role (G588)

`intent-cli notify delegate` and `notify report` now route to team-recorded
external residents through their recorded reader. Sender and report-to roles
must exist in the team topology, while only the recipient must be deliverable.
herdr resolution stays scoped to the caller team's workspace, dry-run and
write share the same resolution verdict, and unresolved routes still fail
closed without prompting a foreign pane.

### CI wait completion is observable without a timer (G589)

`automation stalled-work` now distinguishes a legitimate pending exact-head CI
wait from terminal all-green and terminal failed outcomes. CI-aware findings
carry the PR head SHA, pass/fail/skip/pending breakdown, and a stable dedupe key;
pending alone remains non-escalating and every path stays read-only. The
orchestrator guide names the re-check producer: the configured timer in
timer-loop, or an explicitly armed exact-head CI-completion watch in
herdr-only. The end of the wait is a legitimate wake signal, never proof of
success.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.8.1
```

After the operator has approved and published it, self-contained binaries are
available from the
[v0.8.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.8.1).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.8.0

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.1
```

No command, argument, flag, or documented session-layer mode was removed or
renamed. All five slices correct existing behavior. Two **additive,
non-breaking output-shape changes** deserve explicit attention:

1. For an `external` recipient, `notify delegate` and `notify report` can now
   append the team event and return `event_appended=true`; previously those
   routes always reported `false`. The existing event and command-result
   schemas are unchanged.
2. `automation stalled-work` can now emit the finding kinds `ci-pending`,
   `ci-all-green-not-transitioned`, and `ci-failed-not-transitioned`. Consumers
   that switch exhaustively on `kind` must accept these additive values. The
   finding schema is unchanged.

## Release-readiness gate (G590)

These items must hold **before the GitHub Release for `v0.8.1` is created or
published**. This gate fails closed.

- [ ] The five shipped slices above are merged to `main` at exactly the listed
      PRs and merge commits; there are no additional shipped slices in these
      notes.
- [ ] Both EN/JA `release-notes-v0.8.1.md` files contain real, parity-matched
      notes with no DRAFT-stub banner, and both disclose the two additive
      output-shape changes.
- [ ] `eng/version.json` remains unchanged at `stableVersion` `0.8.0` and
      `nextVersion` `0.8.1`.
- [ ] The G475 next-version notes/package guard, release-note/version-policy
      guards, and full Release suite are green with no current-state guard
      flips on the exact G590 preparation head.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      repository/project URLs point at this repository, the license is
      `Apache-2.0`, and README/docs links resolve.
- [ ] Main CI (`Build and test (source contract)`) and preview-pack are green,
      and post-merge build + pack evidence is recorded on the preparation
      merge commit.
- [ ] The operator has explicitly approved creating the `v0.8.1` Release.

## Publishing v0.8.1

Merging this preparation does **not** create a GitHub Release or tag, publish a
package, roll to `0.8.2`, remove a stub for a future version, or announce a
release. After merge, the readiness evidence must be checked and the operator
must explicitly approve Release creation.

Only then may a maintainer/operator (or explicitly authorized external release
automation) create and publish the `v0.8.1` GitHub Release. Publishing it
triggers `release.yml`, which publishes the NuGet package and platform
archives.

Post-release verification includes:

- [ ] NuGet and GitHub asset links resolve and downloaded checksums match.
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.1`
      followed by `intent-cli --version` reports `0.8.1`.
- [ ] A platform archive passes checksum verification and its binary reports
      `0.8.1`.
- [ ] After publication, perform the separately authorized immediate roll to
      `stableVersion → 0.8.1` / `nextVersion → 0.8.2`, with new EN/JA DRAFT
      stubs, refreshed readiness sections, and green post-roll child-main CI.
      That roll is not part of G590.
