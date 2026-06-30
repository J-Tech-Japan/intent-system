# Release Notes — intent-cli v0.3.14

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.3.14` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it bumps version metadata and docs and adds
> **no** publish steps. See the
> [pre-merge release-readiness gate](#release-readiness-gate-g512) and
> [publishing v0.3.14](#publishing-v0314).

## What's in v0.3.14

v0.3.14 is a **patch release** that ships the orchestrator-mode guidance work
completed after `v0.3.13` (G508–G511). No package id, license, or
workflow-semantics changes, and the existing timer-loop mode is fully supported
and unchanged. The package id remains `JTechJapan.IntentSystem.Cli`.

> **Orchestrator mode is still preview/experimental.** It is opt-in, still being
> hardened, and not the default workflow. intent-cli and GitHub remain the
> authoritative source of truth; agmsg is only a message/progress/completion
> signal layer.

### Concrete agmsg startup steps in the setup guide (G508)

- The orchestrator-thread guide (`intent-cli guide orchestrator-thread`) now
  produces **concrete agmsg startup steps** — a preflight and copy-paste
  registration/delivery commands plus first role prompts — so an operator can
  bring up the orchestrator, implementation, and review receivers without
  guessing the sequence.

### Design-thread handoff and monitor recovery (G509)

- The guide documents the **design-thread handoff** and a **monitor recovery**
  checklist for orchestrator mode: what to do when a receiver's monitor did not
  start, a message is not visible, or a receiver started after a message was
  sent (read with `inbox.sh`, re-confirm registration/delivery, resend after an
  ack).

### Setup intake form and traffic-controller playbook (G510)

- The guide renders a **setup intake form** (with `missing-inputs` /
  `setup-ready` / `blocked` outcomes) and a **design traffic-controller
  playbook**, so a design thread that asks for orchestrator mode is walked
  through the required inputs and the routing/escalation rules.

### Monitor tool vs agmsg delivery-mode (G511)

- The guide and the new `orchestrator-message-mode` docs distinguish Claude
  Code's generic **`Monitor` tool** (the real inbox-stream delivery mechanism,
  attached by agmsg via `watch.sh` from the SessionStart directive) from agmsg's
  `delivery.sh status` `mode=monitor` configuration (which is **not** proof a
  Monitor is attached and streaming). They add a **live-attachment
  success-marker** list (`ToolSearch select:Monitor` resolves Monitor;
  `Monitor(agmsg inbox stream)`; footer `1 monitor`; `Monitor event`), a
  **failure-marker** list (fallback to Bash/background `watch.sh`; footer
  `1 shell`; Azure Monitor / MCP monitor confusion), and a **project-trust
  repair runbook** (exact-cwd `~/.claude.json` `hasTrustDialogAccepted=false`
  suppresses Monitor → repair Claude project trust and restart, then re-verify).

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.13`,
> `nextVersion: 0.3.14`; G512 (this packet) is the release-prep metadata bump.
> The post-v0.3.14 metadata advancement (`stableVersion → 0.3.14`,
> `nextVersion → 0.3.15`) is the operator's post-release step and is out of scope
> for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.14
```

Or download the self-contained binary from the
[v0.3.14 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.14).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.13

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.14
```

There are no breaking changes from v0.3.13. Orchestrator mode remains opt-in;
existing timer-loop setups are unaffected.

## Release-readiness gate (G512)

These items must hold **before the GitHub Release for `v0.3.14` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G508, G509, G510, G511 (and G512 this prep). Confirm on the host/review
      side via the host queue-state / GitHub PR state — the child implementation
      loop must not read parent queue-state, so this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before publishing).
- [ ] `eng/version.json` `nextVersion` is `0.3.14` (the intended release
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

## Publishing v0.3.14

This packet does **not** publish the release and adds **no** publish steps. The
version-bump merge does **not** create a GitHub Release or tag on its own.

1. After this version bump is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.3.14` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.14`)
      then `intent-cli --version` reports `0.3.14`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.3.14`.
- [ ] **Orchestrator guide smoke** (G508–G511): `intent-cli guide
      orchestrator-thread --domain <d> --target-repo <repo> --agent <agent>
      --format markdown` renders the concrete agmsg startup steps, the
      design-thread handoff / monitor recovery, the setup intake form and
      traffic-controller playbook, and the **Monitor tool vs delivery-mode**
      section with the success/failure markers and trust-repair runbook.
- [ ] Local preview/dry-run version metadata uses the next development line
      after `0.3.14` (bump `eng/version.json` per the post-release step in
      [Version flow](09-developer-reference.md#version-flow)):
      `stableVersion → 0.3.14`, `nextVersion → 0.3.15`.
