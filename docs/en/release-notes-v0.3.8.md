# Release Notes — intent-cli v0.3.8

> **Release checklist for maintainers:** see [Creating the v0.3.8 GitHub Release](#creating-the-v038-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g478) passes.**

## What's in v0.3.8

v0.3.8 is a loop-reliability release. It ships the two automation fixes
completed after `v0.3.7` that keep implementation/review loops moving without
manual operator intervention: child `pr-comment-preflight` no longer deadlocks
when a review comment cites a host metadata path as evidence, and host closeout
can recover deterministically when a queue item's `linked_pr` projection is
missing. No package id, license, or workflow-semantics changes. The package id
remains `JTechJapan.IntentSystem.Cli`.

### Packet evidence citations no longer deadlock child PR repair (G476)

- `intent-cli worker pr-comment-preflight` now classifies a review comment by
  its **requested edit target**, not by any incidental `.intent-cli/` or
  `intents/` mention. A G316-style request-update comment that cites a packet
  path such as `.intent-cli/issues/<unit>/packet.yaml` as evidence while asking
  to change implementation files is classified `repair-required` / actionable,
  so the child worker can claim and fix it instead of waiting forever on a host
  that has nothing to repair.
- A comment is only `host-artifact-repair-required` when **every** requested
  edit target is a host metadata path. Genuine host-artifact edit requests still
  route to the host repair agent (G353 preserved).
- Classification runs over the full comment body (not the truncated excerpt),
  and the result exposes `actionable_comments[].requested_edit_paths` and
  `actionable_comments[].host_evidence_paths` so the decision is explainable
  without reading host metadata. `worker next-action` consults the same
  classifier, so the two surfaces never disagree on child-claimability.

### Deterministic closeout recovery when `linked_pr` is missing (G477)

- `intent-cli closeout pr --pr <n>` now auto-recovers when it cannot match a
  queue item only because `linked_pr` was never projected into host durable
  state. When the merged PR's GitHub closing references identify exactly one
  queue item (by `linked_issue`), closeout completes without the operator having
  to know the `--issue <n>` fallback, and the write repairs the missing
  `linked_pr` projection.
- The result surfaces `recoverable_missing_linked_pr`, `inferred_issue`,
  `recovery_source` (`github-closing-reference`), and `recovery_action` so the
  recovery is auditable. Ambiguous evidence (closing references match more than
  one queue item) fails closed with a `linkage-ambiguous` error rather than
  guessing; only then is a manual `--issue <n>` rerun required.
- This is host-owned deterministic recovery, not an operator policy question;
  child `--github-only` loops still never write `linked_pr`.

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.7`,
> `nextVersion: 0.3.8`; G478 (this packet) is the release-readiness preparation.
> The post-v0.3.8 metadata advancement (`stableVersion → 0.3.8`,
> `nextVersion → 0.3.9`) is the operator's post-release step and is out of scope
> for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.8
```

Or download the self-contained binary from the
[v0.3.8 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.8).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.7

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.8
```

There are no breaking changes from v0.3.7.

## Release-readiness gate (G478)

Do not create the `v0.3.8` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G476, G477 (and G478 this prep). Confirm on the host/review side via the
      host queue-state / GitHub PR state — the child implementation loop must not
      read parent queue-state, so this is a host-owned precondition.
- [ ] `eng/version.json` `nextVersion` is `0.3.8` (the intended release version)
      and matches the tag to be created (`v0.3.8`). The release workflow derives
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

## Creating the v0.3.8 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g478) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.8 && git push origin v0.3.8`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.8`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.8`)
         then `intent-cli --version` reports `0.3.8`.
   - [ ] Binary artifact smoke check: download the platform archive, verify its
         `.sha256`, extract, and run `./intent-cli --version` → `0.3.8`.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.8` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)):
         `stableVersion → 0.3.8`, `nextVersion → 0.3.9`.
