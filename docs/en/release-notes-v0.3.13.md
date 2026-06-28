# Release Notes — intent-cli v0.3.13

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.3.13` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it bumps version metadata and docs and adds
> **no** publish steps. See the
> [pre-merge release-readiness gate](#release-readiness-gate-g506) and
> [publishing v0.3.13](#publishing-v0313).

## What's in v0.3.13

v0.3.13 is a **patch release** that ships the design-thread agmsg receiver
guidance completed after `v0.3.12` (G505). No package id, license, or
workflow-semantics changes, and the existing timer-loop mode is fully supported
and unchanged. The package id remains `JTechJapan.IntentSystem.Cli`.

> **Orchestrator mode is still preview/experimental.** It is opt-in, still being
> hardened, and not the default workflow. intent-cli and GitHub remain the
> authoritative source of truth; agmsg is only a message/progress/completion
> signal layer.

### Design-thread agmsg receiver guidance (G505)

- The orchestrator-thread guide (`intent-cli guide orchestrator-thread`) now
  documents an optional **fourth logical role** — a **design / human receiver** —
  so human-needed escalations can be delivered over agmsg. The four roles are:
  orchestrator, implementation receiver, review receiver, and the optional
  design/human receiver.
- **Routine progress stays internal** to orchestrator / implementation / review;
  **only human-needed decisions** are routed to the design thread (clarification,
  product ambiguity, permission/credentials, destructive action, repeated
  no-progress, unresolved canonical state, release/publish, explicit policy).
- The design receiver is **optional** for routine operation but **recommended**
  for reliable escalation delivery, and it is **loopless** — the human can read
  on demand. The guide provides paste-ready registration/addressing text, a
  minimal manual inbox trigger prompt for the design thread, and a note that
  messages sent before the design monitor started may need a manual `inbox.sh`
  check.
- Implementation and review receivers remain loopless; agmsg stays a signal
  layer only.

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.12`,
> `nextVersion: 0.3.13`; G506 (this packet) is the release-readiness preparation.
> The post-v0.3.13 metadata advancement (`stableVersion → 0.3.13`,
> `nextVersion → 0.3.14`) is the operator's post-release step and is out of scope
> for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.13
```

Or download the self-contained binary from the
[v0.3.13 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.13).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.12

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.13
```

There are no breaking changes from v0.3.12. Orchestrator mode remains opt-in;
existing timer-loop setups are unaffected.

## Release-readiness gate (G506)

These items must hold **before the GitHub Release for `v0.3.13` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G505 (and G506 this prep, plus G507 this correction). Confirm on the
      host/review side via the host queue-state / GitHub PR state — the child
      implementation loop must not read parent queue-state, so this is a
      host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before publishing).
- [ ] `eng/version.json` `nextVersion` is `0.3.13` (the intended release
      version). `release.yml` builds the package version from the published
      Release/tag; `src/IntentSystem.Cli/IntentSystem.Cli.csproj` derives its
      local default from the same policy.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] Release notes / README keep **orchestrator mode described as
      preview/experimental** and opt-in, with timer-loop mode unchanged.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.

## Publishing v0.3.13

This packet does **not** publish the release and adds **no** publish steps. The
version-bump merge does **not** create a GitHub Release or tag on its own.

1. After this version bump is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.3.13` (tagging the release commit). This is a
   post-merge host/operator/external action.
2. Publishing that GitHub Release fires `.github/workflows/release.yml`
   (`on: release: published`), which builds and publishes the NuGet package and
   the per-platform binary archives (with `.sha256` checksums) and attaches them
   to the triggering Release.

Post-release verification (after the GitHub Release is published and
`release.yml` has run):

- [ ] NuGet.org package page links all resolve correctly.
- [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
      accessible.
- [ ] `.sha256` checksums match the downloaded artifacts.
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.13`)
      then `intent-cli --version` reports `0.3.13`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.3.13`.
- [ ] **Orchestrator guide smoke** (G505): `intent-cli guide
      orchestrator-thread --domain <d> --target-repo <repo> --agent <agent>
      --format markdown` renders the four logical roles and the optional
      design/human receiver section.
- [ ] **Design receiver inbox smoke** (G505): the rendered guide includes the
      minimal manual inbox trigger prompt and the pre-start `inbox.sh` note for
      the design thread.
- [ ] Local preview/dry-run version metadata uses the next development line
      after `0.3.13` (bump `eng/version.json` per the post-release step in
      [Version flow](09-developer-reference.md#version-flow)):
      `stableVersion → 0.3.13`, `nextVersion → 0.3.14`.
