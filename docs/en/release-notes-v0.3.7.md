# Release Notes — intent-cli v0.3.7

> **Release checklist for maintainers:** see [Creating the v0.3.7 GitHub Release](#creating-the-v037-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g475) passes.**

## What's in v0.3.7

v0.3.7 is an automation-safety release. It collects the loop-prompt /
review-closeout / next-slice reliability fixes completed after `v0.3.6` that
make automated implementation/review loops behave correctly on non-default base
branches, stop a `issue-published` queue-state row from blocking review
closeout, keep the installed `intent-cli guide` output authoritative over stale
local rule docs, and prevent absorbed/superseded packets from being re-cut as
duplicate issues. No package id, license, or workflow-semantics changes. The
package id remains `JTechJapan.IntentSystem.Cli`.

### Non-default implementation base branches are first-class in loop prompts (G471)

- Loop-prompt prose and review guidance now treat a non-default implementation
  base branch as a first-class case instead of assuming `main`. The generated
  child/host loop prompts and the base-branch policy guidance carry the actual
  configured base so a child implementation agent picks the correct PR base
  without reading host metadata, and review guidance no longer mis-flags a
  correctly-based PR.

### `issue-published` queue rows no longer block review closeout (G472)

- Review-closeout parsing tolerates queue-state rows whose state is
  `issue-published`, so a published-but-not-yet-PR'd row no longer aborts the
  closeout read. The host review loop guidance is skill-free (closeout routes
  through installed `intent-cli` surfaces only) and treats a CI-pending result
  as a defer condition rather than a stop.

### Installed `intent-cli guide` is canonical over stale local rule docs (G473)

- When generating loop prompts, the installed `intent-cli guide` output wins
  over local `intents/rules/automations/*.md` rule docs. The hard rule is now
  explicit in the child/host loop and one-shot guidance: do not read the local
  rule docs even when an operator names them — the installed guide is the source
  of truth, so a stale checked-in rule file can no longer silently override the
  shipped guidance.

### Absorbed / superseded packet lifecycle retirement safety (G474)

- New machine-readable packet lifecycle retirement: a `lifecycle.yaml` sidecar
  (`lifecycle: ready|absorbed|retired|superseded` plus optional `absorbed_by` /
  `superseded_by` / `retired_reason` / `retired_at`) records that a packet
  directory is design history, not a next-slice candidate.
- New `intent-cli packet retire --execution-unit <id> (--absorbed-by <unit> |
  --superseded-by <unit> | --retired) --reason <text> [--write]` records the
  sidecar and appends a `packet-retired` run event. It is idempotent (re-running
  the same retirement reports `already-retired` without rewriting the sidecar or
  duplicating run events) and never deletes the packet files.
- `intent-cli intent next-slice` excludes machine-retired packets from
  issue-cut-ready selection in both scan passes; a packet carrying only a stale
  human marker (e.g. `STATUS: ABSORBED`) is excluded and surfaced with a
  `legacy-retirement-marker-needs-machine-metadata` warning and a repair note,
  so an absorbed packet is never blindly published. Host loop guidance tells
  agents to retire such packets through `intent-cli packet retire` rather than
  asking the operator whether to publish.

> Version metadata note: `eng/version.json` already records
> `stableVersion: 0.3.6`, `nextVersion: 0.3.7`; G475 (this packet) is the
> release-readiness preparation. The post-v0.3.7 metadata advancement
> (`stableVersion → 0.3.7`, `nextVersion → 0.3.8`) is the operator's
> post-release step and is out of scope for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.7
```

Or download the self-contained binary from the
[v0.3.7 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.7).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.6

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.7
```

There are no breaking changes from v0.3.6.

## Release-readiness gate (G475)

Do not create the `v0.3.7` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G471, G472, G473, G474 (and G475 this prep). Confirm on the host/review
      side via the host queue-state / GitHub PR state — the child implementation
      loop must not read parent queue-state, so this is a host-owned
      precondition.
- [ ] `eng/version.json` `nextVersion` is `0.3.7` (the intended release version)
      and matches the tag to be created (`v0.3.7`). The release workflow derives
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

## Creating the v0.3.7 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g475) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.7 && git push origin v0.3.7`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.7`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.7`)
         then `intent-cli --version` reports `0.3.7`.
   - [ ] Binary artifact smoke check: download the platform archive, verify its
         `.sha256`, extract, and run `./intent-cli --version` → `0.3.7`.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.7` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)):
         `stableVersion → 0.3.7`, `nextVersion → 0.3.8`.
