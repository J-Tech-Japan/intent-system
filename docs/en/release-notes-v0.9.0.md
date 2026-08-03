# Release Notes — intent-cli v0.9.0

> **Release model:** this change is **prepare-only**. It does not create a
> GitHub Release or tag, publish a package, perform the post-release version
> roll, or change product behavior. After this preparation is merged and the
> readiness gate below is satisfied, the operator must explicitly approve
> Release creation. Publishing that GitHub Release triggers
> `.github/workflows/release.yml` (`on: release: published`) to build and
> publish the NuGet package and platform artifacts.

## What's in v0.9.0

v0.9.0 covers exactly two merged slices after `v0.8.1`: **G591** and **G592**.
Their PRs and merge commits are:

- **G591** — [PR #1286](https://github.com/J-Tech-Japan/intent-system/pull/1286),
  “G591: make host-local CLI refresh fail closed,”
  merge [`d73072668c7bda24b124ca46d65c805ed561b740`](https://github.com/J-Tech-Japan/intent-system/commit/d73072668c7bda24b124ca46d65c805ed561b740).
- **G592** — [PR #1288](https://github.com/J-Tech-Japan/intent-system/pull/1288),
  “G592: Add canonical session-layer topology commands,”
  merge [`a18ecefc9dd6aae9c551f424edfdb00752cc9d91`](https://github.com/J-Tech-Japan/intent-system/commit/a18ecefc9dd6aae9c551f424edfdb00752cc9d91).

Both merge commits were verified on `main` during release preparation. Together
they make **herdr-only stop requiring hand-authored state** and close one class
of operational risk: durable state intent-cli depends on is now either owned by
an intent-cli writer or protected by an atomic, fail-closed replacement path.

**Why minor, not patch.** The documented version policy reserves a **MINOR**
bump for a new command surface or a broad behavior change. G592 adds the new
`session-layer topology` command group, so the in-development `0.8.2` patch
line is retargeted to `0.9.0`. No `v0.8.2` release will be cut; its superseded
EN/JA draft stubs are removed by this same preparation.

### Host-local CLI refresh preserves the working install (G591)

The host-local refresh script now resolves the generated package by the CLI
project's package id and derives the local candidate version from
`eng/version.json`. It builds and verifies the package and temporary wrapper in
an isolated candidate location, including the wrapper version and required
automation-summary capabilities, before the final atomic promotion.

If any candidate check fails, refresh reports the failed check and remedy,
leaves the previously installed wrapper and package byte-for-byte intact, and
removes candidate and temporary artifacts. A failed refresh can no longer
destroy a working host-local CLI install.

### Delivery topology has a canonical writer and validator (G592)

The team delivery mapping at
`<host-repo>/.intent-cli/role-pane-mapping.json` now has a canonical command
surface:

- `intent-cli session-layer topology validate --team <team> --format json`
  reads the recorded topology and returns a machine-readable `valid` answer
  plus every finding in one invocation. Findings name the role and field for
  missing or unsupported residence, missing pane ids, unsafe external readers,
  workspace mismatches, and unreadable or absent topology.
- `intent-cli session-layer topology record --team <team> ... --write`
  records an operator-supplied herdr role (`workspace_id`, `pane_id`, `cwd`,
  and optional kind) or external role (routing-root-relative reader and
  optional frontend). An exact match is an idempotent no-op; an existing
  conflicting role is refused without silent repair.
- `intent-cli session-layer topology show --team <team> --format json`
  reports every recorded residence and resolved pane or reader without
  sending. It uses the same delivery-target resolution function as `notify`,
  so the two surfaces cannot disagree about where delivery would go.

The commands never query herdr, guess ids, provision resources, auto-repair a
conflict, or add a delivery fallback. `automation doctor` also reports invalid
topology health when the mapping exists or a herdr-only scope requires it, and
notify topology refusals point to `topology validate` / `record` as the remedy.
Fail-closed delivery semantics are unchanged.

## v0.8.1 shipped silently

`v0.8.1` was published as a **silent release** by operator decision and is
announced together with v0.9.0. Readers who did not see a separate v0.8.1
announcement should consult the
[v0.8.1 release notes](release-notes-v0.8.1.md) for its five wake-reliability
slices. Those five slices are linked rather than restated here; they are not
part of the two-slice v0.9.0 scope above.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.9.0
```

After the operator has approved and published it, self-contained binaries are
available from the
[v0.9.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.9.0).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.8.1

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.0
```

No existing command, argument, flag, topology schema, topology location, or
session-layer mode was removed or renamed. The new `session-layer topology`
group is additive. Existing notify routing remains fail-closed and gains no
fallback. The host-local refresh safety change affects only how a candidate CLI
install is verified and promoted; successful refresh behavior remains the same.

## Release-readiness gate (G593)

These items must hold **before the GitHub Release for `v0.9.0` is created or
published**. This gate fails closed.

- [ ] The two shipped slices above are merged to `main` at exactly PR #1286 /
      `d73072668c7bda24b124ca46d65c805ed561b740` and PR #1288 /
      `a18ecefc9dd6aae9c551f424edfdb00752cc9d91`; no additional shipped slice is
      included in these notes.
- [ ] Both EN/JA `release-notes-v0.9.0.md` files contain real, parity-matched
      notes with no DRAFT-stub banner, and both superseded
      `release-notes-v0.8.2.md` files are absent.
- [ ] `eng/version.json` records `stableVersion` `0.8.1` and `nextVersion`
      `0.9.0` on the same exact preparation head.
- [ ] The MINOR rationale names the new `session-layer topology` command group,
      and the notes link the silent v0.8.1 release instead of restating it.
- [ ] The G475 next-version notes/package guard, release-note/version-policy
      guards, and full Release suite are green with no test flips on the exact
      G593 preparation head.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      repository/project URLs point at this repository, the license is
      `Apache-2.0`, and README/docs links resolve.
- [ ] Exact-head main CI (`Build and test (source contract)`) is green and the
      preparation PR records its verification evidence.
- [ ] The operator has explicitly approved creating the `v0.9.0` Release.

## Publishing v0.9.0

Merging this preparation does **not** create a GitHub Release or tag, publish a
package, or perform the post-release roll. After merge, the readiness evidence
must be checked and the operator must explicitly approve Release creation.

Only then may a maintainer/operator (or explicitly authorized external release
automation) create and publish the `v0.9.0` GitHub Release. Publishing it
triggers `release.yml`, which publishes the NuGet package and platform
archives.

Post-release verification includes:

- [ ] NuGet and GitHub asset links resolve and downloaded checksums match.
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.0`
      followed by `intent-cli --version` reports `0.9.0`.
- [ ] A platform archive passes checksum verification and its binary reports
      `0.9.0`.
- [ ] After publication, perform the separately authorized immediate roll to
      `stableVersion → 0.9.0` / `nextVersion → 0.9.1`, with new EN/JA DRAFT
      stubs, refreshed readiness sections, and green post-roll child-main CI.
      That roll is not part of G593.
