# Release Notes — intent-cli v0.9.1

> **Release model:** this change is **prepare-only**. It does not create a
> GitHub Release or tag, publish a package, perform the post-release version
> roll, announce the release, or change product behavior. After this
> preparation is merged and the readiness gate below is satisfied, the
> operator must explicitly approve Release creation. Publishing that GitHub
> Release triggers `.github/workflows/release.yml` (`on: release: published`)
> to build and publish the NuGet package and platform artifacts.

## What's in v0.9.1

v0.9.1 contains exactly one merged correctness slice after `v0.9.0`: **G594**.

- **G594** — [PR #1292](https://github.com/J-Tech-Japan/intent-system/pull/1292),
  “G594: make session-layer readiness record-first,”
  merge [`022e7ec2acbc354aeb6a054f675165ba0e2a9238`](https://github.com/J-Tech-Japan/intent-system/commit/022e7ec2acbc354aeb6a054f675165ba0e2a9238).

That merge commit was verified as an ancestor of `main` during release
preparation. No other shipped slice is included in these notes.

**Why PATCH, not MINOR.** Inspection of the merged implementation confirmed
that G594 adds no new user-facing command surface: its shared record-first
preflight is consumed through the existing `automation doctor`, guide, and
`notify` surfaces. The doctor check is opt-in for a named scope because its
paired `--domain` and `--team` arguments are optional. Existing unscoped doctor
invocations and CI jobs retain their status behavior.

The release-preparation measurement used the same host root containing
`config.toml` and `host-binding.toml` but no session-layer mode record. The
source-built merged CLI reported exactly:

```text
intent-cli automation doctor --domain intent-cli --team sekiban-workers --format json
→ exit 1; status: session-layer-not-ready; preflight verdict: configuration-incomplete

intent-cli automation doctor --format json
→ exit 0; status: ok; preflight verdict: unjudged
```

The scoped check therefore closes a false-green readiness gap without flipping
existing unscoped callers. This measured compatibility is the PATCH rationale;
it is not inferred from the issue label or copied from planning text.

### Readiness is record-first and delivery identity is operational (G594)

One machine-readable session-layer preflight now supplies the answer consumed
by `automation doctor`, the orchestrator guide's READY definition, and
`notify`. A named team with no recorded mode is configuration-incomplete rather
than vacuously green, topology is correlated with the recorded mode, and
contradictions remain diagnostic-only without inference or repair.

For herdr-only delivery, a logical role now resolves to its recorded workspace
plus pane. It no longer has to equal the herdr agent name, which is globally
unique on a machine. This unblocks multiple teams on one machine: each team can
use the canonical logical roles while its herdr agents keep distinct global
names. Credit goes to the **sekiban design thread**, whose production report
surfaced the collision and prompted the independent verification folded into
G594.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.9.1
```

After the operator has approved and published it, self-contained binaries are
available from the
[v0.9.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.9.1).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.9.0

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.1
```

No command, argument, flag, session-layer mode, topology schema, or record
writer was removed or renamed. Two non-breaking-for-existing-callers changes
on the PREVIEW herdr-only path deserve explicit attention:

1. **`delivered: true` is stricter.** For a settled herdr recipient, notify now
   requires a bounded observed unattended working transition followed by a
   fresh settled acknowledgement. A prompt left in a composer is not reported
   delivered. An already-working recipient remains deliverable and reports its
   working transition as `unobservable`.
2. **`automation doctor` gains the scoped preflight.** Supplying the optional
   `--domain <d> --team <t>` pair judges that named team record-first; omitting
   the pair leaves an anonymous record-less root unjudged and preserves the
   existing `ok` status shown above.

The **herdr-only session transport remains PREVIEW**. The agmsg transport and
its delivery behavior are unchanged, and **agmsg remains PRIMARY**. PREVIEW
qualifies only the transport, never the four-thread model.

## Release-readiness gate (G595)

These items must hold **before the GitHub Release for `v0.9.1` is created or
published**. This gate fails closed.

- [ ] The only shipped slice in these notes is G594, merged through PR #1292 at
      `022e7ec2acbc354aeb6a054f675165ba0e2a9238`, and that commit is an ancestor
      of `main`.
- [ ] Both EN/JA `release-notes-v0.9.1.md` files contain parity-matched real
      notes with no DRAFT-stub banner and disclose both non-breaking changes.
- [ ] `eng/version.json` is byte-unchanged at `stableVersion` `0.9.0` and
      `nextVersion` `0.9.1`.
- [ ] The record-less-host measurement still returns
      `session-layer-not-ready` for the scoped doctor and `ok` for the unscoped
      doctor on the same host.
- [ ] The G475 next-version notes/package guard, release/version guards, full
      Release suite, build, pack, and diff check are green on the exact G595
      preparation head.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      repository/project URLs point at this repository, the license is
      `Apache-2.0`, and README/docs links resolve.
- [ ] Exact-head GitHub CI (`Build and test (source contract)`) is green and the
      preparation PR records its verification evidence.
- [ ] The operator has explicitly approved creating the `v0.9.1` Release.

## Publishing v0.9.1

Merging this preparation does **not** create a GitHub Release or tag, publish a
package, announce the release, roll to `0.9.2`, create new DRAFT stubs, or make
any product behavior change. After merge, the readiness evidence must be
checked and the operator must explicitly approve Release creation.

Only then may a maintainer/operator (or explicitly authorized external release
automation) create and publish the `v0.9.1` GitHub Release. Publishing it
triggers `release.yml`, which publishes the NuGet package and platform
archives.

Post-release verification includes:

- [ ] NuGet and GitHub asset links resolve and downloaded checksums match.
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.1`
      followed by `intent-cli --version` reports `0.9.1`.
- [ ] A platform archive passes checksum verification and its binary reports
      `0.9.1`.
- [ ] Only after publication, perform the separately authorized immediate
      version roll and create the next EN/JA DRAFT stubs. That post-release
      work is not part of G595.
