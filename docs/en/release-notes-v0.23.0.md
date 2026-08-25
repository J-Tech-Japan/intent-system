# Release Notes — intent-cli v0.23.0

> **RELEASED FROM TAG `v0.23.0` — NPM PUBLICATION GAP.** The GitHub Release,
> NuGet package, and self-contained binaries shipped from this tag. The npm
> leg never reached the registry; `0.23.0` must not be treated as available
> from npm.

Install verification for the line being cut: `JTechJapan.IntentSystem.Cli --version 0.23.0`.
The eventual release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.23.0.

## Why this line is v0.23.0

The post-release roll after v0.22.0 set `nextVersion` to `0.22.1` before this
line existed. Release-prep retargets that placeholder to `0.23.0` because the
line has two new command surfaces and because G715 changes the validation
verdict for a broad population of real records:

- `intent-cli notify supervise reconcile|uninstall` is a new explicit
  current-GUI-session lifecycle surface for managed supervision artifacts.
- `intent-cli guide workflow task supervision-setup` is a new guide workflow
  task that renders the session-scoped supervision setup contract.
- On the measured host, 190 of the 191 units carrying a `linkage-recovered`
  event become valid under the merged G715 head. That population-wide
  compatibility change is not patch-shaped.

The bump follows the established policy: a line carrying new command surfaces
or broad behaviour changes uses a minor version. This is release preparation
only; it creates no tag or GitHub Release, publishes no package, handles no
credential, and does not perform the post-release roll.

## Preview lane — read before the feature description

G710 through G720 remain preview-through-1.x surfaces. They are outside the
[1.0 compatibility promise](1.0-compatibility-promise.md) until that promise
is explicitly updated; the minor version does not by itself add a stability
guarantee.

## Eleven merged feature units

The v0.22.0 tag `c06dc49e89446bf3b723612dd72004d628914734` through the prepared
head `be13f7c0b9b306dad99d692903cee8837b31f0e8` contains exactly twenty-six commits.
The post-release roll `c48a5635` is one of those commits but is **not a release
execution unit**. The release inventory below covers exactly eleven merged feature units, G710 through G720. The first-parent accounting was checked with:

```bash
git rev-list --count v0.22.0..HEAD
git log --first-parent v0.22.0..origin/main
git log --first-parent v0.22.0..main
```

The count command returned `26` for the named prepared head.

- G710 — PR #1537; repaired the released v0.22.0 verification evidence,
  policy-derived version checks, and bilingual release documentation rather
  than adding a product feature; merge commit `335bb686ba966368abbdadac149bc27d9aea7c6b`.
- G711 — PR #1539; added npm trusted publishing through GitHub Actions OIDC,
  with pinned Node/npm versions, package-specific trusted-publisher guidance,
  and provenance; merge commit `a4b4ecb7ac904b077f3cd75f2c738aa5c163ebc6`.
- G712 — PR #1541; added session-scoped supervision reconciliation and the `notify supervise reconcile|uninstall` lifecycle boundary; merge commit `037c4acc8a02401d4bdb58b0011e05d2026dafdb`. PR #1542; restored the reachable `guide workflow task supervision-setup` route and its documentation; merge commit `130c99f828a6574b822203072d03554cda6a1182`.
- G713 — PR #1545; added herdr pane-label topology guidance and validation for
  the recorded session-layer workspace; merge commit `553f963439b2e3a700c2acc5800679b78d86b325`.
- G714 — PR #1548; **correction, not a feature**: repaired earlier herdr-only
  host-state routing guidance and made the host-state boundary truthful; merge commit `c21c2c7e2e976914eed5231148cc1f1f6cf3c5e3`.
- G715 — PR #1554; added backward-compatible reading for the legacy publish,
  queue, and runs shapes, including the shipped `linkage-recovered` closeout
  event; merge commit `e25d770caacbcdafa2aa9bebea72e895dc22fcbb`.
- G716 — PR #1553; **correction, not a feature**: the earlier `.git` claim was
  retracted/corrected after the live E-versus-F probe. The corrected rule says
  the exact `<repo>/.git` root enables the measured metadata write, while this
  recipe retains non-sandboxed host-state routing because that is the
  least-privilege choice and `git worktree add` remains unmeasured; merge commit `4b0a1a31b075746927d0d73c6f9b370c531e9845`.
- G717 — PR #1559; on a claims-enabled host, a stale `intent-issue-in-progress`
  label no longer makes a worker appear to own an issue: an unheld execution-unit
  claim lets preflight proceed, while a held claim still stops the worker and
  identifies its owner; merge commit `68c11039f4335df7799c65c814c39873132f4c68`.
- G718 — PR #1558; prepared the bilingual v0.23.0 release notes and
  release-readiness evidence for this line without creating a tag or GitHub
  Release, publishing a package, handling credentials, or performing the
  post-release roll; merge commit `e7cbba0ce2d143edd19e1c60804073e41ac9401d`.
- G719 — PR #1562; a sender-local implementation seat can write its report and
  hand it to orchestration when the host routing root is not writable, while an
  external reader reports a delegation-level routing fault; merge commit `bc13c9436b98cc48aa02c4eb85cfbb99e9fab598`.
- G720 — PR #1563; `issue validate-body` rejects a Target section without the
  authored `- Target paths: <path>` line with a distinct diagnostic before issue
  creation, while historical published bodies remain accepted by the legacy
  consumer; merge commit `be13f7c0b9b306dad99d692903cee8837b31f0e8`.

The first-parent merge accounting for the prepared line is:

| first-parent commit | inventory |
| --- | --- |
| `c48a5635` | post-release roll after v0.22.0; not a release execution unit |
| `335bb686ba966368abbdadac149bc27d9aea7c6b` | G710 / PR #1537 |
| `a4b4ecb7ac904b077f3cd75f2c738aa5c163ebc6` | G711 / PR #1539 |
| `037c4acc8a02401d4bdb58b0011e05d2026dafdb` | G712 / PR #1541 |
| `130c99f828a6574b822203072d03554cda6a1182` | G712 repair / PR #1542 |
| `553f963439b2e3a700c2acc5800679b78d86b325` | G713 / PR #1545 |
| `c21c2c7e2e976914eed5231148cc1f1f6cf3c5e3` | G714 / PR #1548 |
| `4b0a1a31b075746927d0d73c6f9b370c531e9845` | G716 / PR #1553 |
| `e25d770caacbcdafa2aa9bebea72e895dc22fcbb` | G715 / PR #1554 |
| `e7cbba0ce2d143edd19e1c60804073e41ac9401d` | G718 / PR #1558 |
| `ea17d02d9489c60ee96e0d088693814b1daad945` | G717 claim handoff; not a release execution unit |
| `68c11039f4335df7799c65c814c39873132f4c68` | G717 / PR #1559 |
| `7f0233080366c86dd449aa7873b339037a7f8f39` | G719 claim handoff; not a release execution unit |
| `bc13c9436b98cc48aa02c4eb85cfbb99e9fab598` | G719 / PR #1562 |
| `be13f7c0b9b306dad99d692903cee8837b31f0e8` | G720 / PR #1563; prepared head |

## Release-readiness evidence

- `eng/version.json` is the single policy source: `stableVersion` is `0.22.0`
  and `nextVersion` is `0.23.0`.
- **Release identity evidence source revision:**
  `be13f7c0b9b306dad99d692903cee8837b31f0e8`, the exact prepared head named
  above. The Release build below was run from that revision before this
  documentation-only PR changed the checkout.
- The Release build command was:

```bash
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
```

It reported exactly `intent-cli 0.23.0-be13f7c-G718`.
- The two new command surfaces were independently probed from that build:

```bash
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll notify supervise reconcile --help
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll guide workflow task supervision-setup --format json
```

The first exited 0 with the reconcile/uninstall usage; the second exited 0
with `metadata_free: true` and `read_only: true`.
- The focused release-note documentation and version-source policy guards were
  run with:

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj --configuration Release --no-build --no-restore -p:IsTestProject=true --filter 'FullyQualifiedName~ReleaseNotesV0230DocsTests|FullyQualifiedName~VersionSourcePolicyGuardTests'
```

They passed with **12 passed, 0 skipped, 0 failed, 12 total**.
- The full Release sweep over all eleven test projects used the Release/no-restore
  settings and recorded **5,474 passed, 1 skipped, 2 failed, 5,477 total**.
  The only failures were the pre-existing `PackagedInvocationSmokeTests`; their
  child `dotnet pack` could not update the shared NuGet vulnerability cache
  (`NU1900`, permission denied), not a failure in these documentation/test edits.
- Both `docs/en/release-notes-v0.22.1.md` and
  `docs/ja/release-notes-v0.22.1.md` are superseded and deleted; these 0.23.0
  notes are the prepared bilingual replacement.

## Prepare-only boundary

This change set changes the prepared bilingual release documentation and its
documentation tests only. It does
not create a tag or GitHub Release, publish a package, handle credentials, or
perform the post-release roll. The post-release roll remains a later action:
after publication, set `stableVersion` to the released version, choose the
next patch for `nextVersion`, add the next DRAFT stubs, refresh both readiness
sections, and verify green main CI.
