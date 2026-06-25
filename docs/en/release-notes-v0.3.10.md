# Release Notes — intent-cli v0.3.10

> **Release checklist for maintainers:** see [Creating the v0.3.10 GitHub Release](#creating-the-v0310-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g486) passes.**

## What's in v0.3.10

v0.3.10 is a dogfooding-stability patch release. It ships the two fixes
completed after `v0.3.9` that unblock real Estivo users running the
public/NuGet-installed intent-cli outside the original developer environment:
Japanese Windows `gh` JSON decoding, and same-repo metadata-branch publish-flow
reliability. No package id, license, or workflow-semantics changes. The package
id remains `JTechJapan.IntentSystem.Cli`.

### Windows Japanese `gh` JSON decoding (G484)

- Every `gh` subprocess now decodes stdout/stderr as **UTF-8 regardless of the
  ambient Windows console code page** (cp932/932). Japanese issue/PR titles and
  bodies stay valid JSON, so `worker next-action --github-only --format json`,
  `worker issue-preflight`, `worker pr-comment-preflight`, and the host/review
  preflight paths no longer break with invalid-JSON parse errors on a Japanese
  Windows console.
- You do **not** need to run `chcp 65001` or set `$OutputEncoding` /
  `[Console]::OutputEncoding` manually. `gh` error output is decoded the same
  way so diagnostics stay readable. macOS/Linux behavior is unchanged (those
  consoles are already UTF-8).

### Same-repo metadata publish-flow reliability (G485)

- `automation queue-seed-from-packet` now resolves the domain's
  `execution_unit_regex` through the **same shared resolver** `automation
  summary` and the host loop use, instead of a duplicate parser that could
  disagree. A valid same-repo packet (code branch `main`, metadata branch
  `main-metadata`) now seeds and publishes through the regular
  `queue-seed-from-packet` → `issue publish-flow` → `automation issue-publish`
  path instead of being rejected as `missing-domain-binding-regex`.
- The refusal diagnostic now names the exact bindings source consulted and
  points at `automation summary --domain <d>` (the same source), so a missing
  bindings file vs an empty regex field is actionable without manual queue-state
  edits.
- The supported same-repo `[project]` config keys (`same_repo_topology`,
  `metadata_source_branch`, `metadata_write_branch`) and the seed → publish path
  are now documented in the developer reference.

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.9`,
> `nextVersion: 0.3.10`; G486 (this packet) is the release-readiness preparation.
> The post-v0.3.10 metadata advancement (`stableVersion → 0.3.10`,
> `nextVersion → 0.3.11`) is the operator's post-release step and is out of scope
> for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.10
```

Or download the self-contained binary from the
[v0.3.10 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.10).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.9

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.10
```

There are no breaking changes from v0.3.9.

## Release-readiness gate (G486)

Do not create the `v0.3.10` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G484, G485 (and G486 this prep). Confirm on the host/review side via the
      host queue-state / GitHub PR state — the child implementation loop must not
      read parent queue-state, so this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before tagging).
- [ ] `eng/version.json` `nextVersion` is `0.3.10` (the intended release version)
      and matches the tag to be created (`v0.3.10`). The release workflow derives
      the package version from the tag; `-p:Version=` overrides the policy-derived
      default in `src/IntentSystem.Cli/IntentSystem.Cli.csproj`.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.

## Creating the v0.3.10 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g486) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.10 && git push origin v0.3.10`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.10`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.10`)
         then `intent-cli --version` reports `0.3.10`.
   - [ ] Binary artifact smoke check: download the platform archive, verify its
         `.sha256`, extract, and run `./intent-cli --version` → `0.3.10`.
   - [ ] **G484 Windows Japanese smoke**: on a Japanese Windows console (cp932),
         `intent-cli worker next-action --repo <repo> --github-only --format json`
         parses a Japanese-titled issue without a JSON error.
   - [ ] **G485 same-repo smoke**: with `[project] same_repo_topology = true` +
         `metadata_source_branch`/`metadata_write_branch` set, a valid packet
         passes `automation queue-seed-from-packet` then `issue publish-flow`.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.10` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)):
         `stableVersion → 0.3.10`, `nextVersion → 0.3.11`.
