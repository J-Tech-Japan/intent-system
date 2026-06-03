# Release Notes — intent-cli v0.3.5

> **Release checklist for maintainers:** see [Creating the v0.3.5 GitHub Release](#creating-the-v035-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g452) passes.**

## What's in v0.3.5

v0.3.5 is a structural-reliability release. It collects the host-loop / next-slice
/ review reliability packets completed after `v0.3.4` that reduce repeated
host-loop stalls, stale host-metadata drift, contradictory next-slice decisions,
long prompt choreography, and repeated review-policy questions. No package id,
license, or workflow-semantics changes. The package id remains
`JTechJapan.IntentSystem.Cli`.

### Unified state doctor + fail-closed safe repair (G448)

- New `intent-cli automation state-doctor` — a unified, OSS-safe diagnostic over
  host-metadata drift across queue-state, publish artifacts, and GitHub PRs
  (open + merged). Read-only by default; reports deterministic drift categories
  (missing linked_pr, missing linked_issue from a publish artifact,
  merged-PR-not-completed) with evidence, and classifies ambiguous cases
  (duplicate issue evidence, multiple closing PRs) as fail-closed unsafe
  findings.
- `--write` applies ONLY high-confidence, forward-only queue-state repairs and
  appends an append-only `runs.jsonl` event per repair; it never clears,
  rewrites, or downgrades existing host data and never migrates old hosts.
  `--workdir` consistently drives the host context for all reads/writes.

### Unified next-slice readiness engine (G449)

- New shared `NextSliceReadinessEvaluator` (and `IsPublishable` adapter) is the
  single engine for the "is this candidate ready to cut an issue?" decision.
  `intent next-slice`, `automation host-loop-next-action`, `automation
  host-review-diagnostics`, next-slice classify, packet-draft validation, and
  `issue publish-flow` now route their contract-completeness / publishability
  verdict through it, so a candidate one surface rejects is never reported
  `issue-cut-ready` by another. Fail-closed precedence: true-idle →
  contract-incomplete → clarification-required → duplicate-existing →
  issue-cut-ready. An existing open GitHub issue/PR routes to reconcile/recovery
  instead of a duplicate publish.

### One-safe-wake host-loop command (G450)

- New `intent-cli automation host-loop-wake` collapses the host loop's ordered
  preflight / sync / review / closeout / publish / diagnostics choreography into
  one structured verdict (`true-idle` / `review` / `publish` / `blocker`),
  gating on the installed-CLI surface and reusing the existing
  host-loop-next-action decision. It enforces the at-most-one-PR-review and
  at-most-one-issue-publish invariant.
- Read-only by default. `--write` runs the safe, no-judgement lanes through
  existing surfaces — the deterministic host-metadata repair and the next-slice
  publish chain (`packet draft` → `issue publish-flow --write` → `automation
  issue-publish --write`) — fail-closed at every step. Review approval /
  request-update transitions remain expert-judgement-gated and surface a
  `pending_command` rather than auto-approving.

### Domain review standing-policy registry (G451)

- New optional `.intent-cli/review-policy.json` standing-policy registry for
  recurring review decisions (draft handling, device/operator/hardware-gated
  evidence, external artifact intake, test-evidence sufficiency, follow-up
  tracking). `guide review` and host-loop guidance (`guide prompt-matrix`)
  consume it and surface `review_policy_source`, so agents stop re-asking the
  same standing-policy question. Absent/invalid files fail closed to safe
  built-in defaults (no migration; existing hosts behave as before). The
  built-in default preserves the installed draft-aware flow — draft state alone
  is not a review stop, and approval/merge while a draft is forbidden.

> Version metadata note: `eng/version.json` already records
> `stableVersion: 0.3.4`, `nextVersion: 0.3.5`; G452 (this packet) is the
> release-readiness preparation. Post-v0.3.5 metadata advancement (0.3.6) is out
> of scope here.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.5
```

Or download the self-contained binary from the
[v0.3.5 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.5).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.4

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.5
```

There are no breaking changes from v0.3.4.

## Release-readiness gate (G452)

Do not create the `v0.3.5` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G448, G449, G450, G451 (and G452 this prep). Confirm on the host/review
      side via the host queue-state / GitHub PR state — the child implementation
      loop must not read parent queue-state, so this is a host-owned
      precondition.
- [ ] `eng/version.json` `nextVersion` is `0.3.5` (the intended release version)
      and matches the tag to be created (`v0.3.5`). The release workflow derives
      the package version from the tag; `-p:Version=` overrides the static
      `<Version>` in `src/IntentSystem.Cli/IntentSystem.Cli.csproj`.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.

## Creating the v0.3.5 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g452) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.5 && git push origin v0.3.5`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.5`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` then
         `intent-cli --version` reports `0.3.5`.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.5` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)).
