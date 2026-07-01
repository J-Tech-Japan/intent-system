# Release Notes — intent-cli v0.3.15

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.3.15` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it bumps version metadata and docs and adds
> **no** publish steps. See the
> [pre-merge release-readiness gate](#release-readiness-gate-g519) and
> [publishing v0.3.15](#publishing-v0315).

## What's in v0.3.15

v0.3.15 is a **patch release** that ships two orchestrator/agmsg operational
fixes completed after `v0.3.14`: a Claude Code project-settings diagnosis for a
missing agmsg `Monitor` tool (G517) and a shift of orchestrator-mode timers to
a message-driven steady state with an optional design-side watchdog (G518). No
package id, license, or workflow-semantics changes, and the existing
timer-loop mode is fully supported and unchanged. The package id remains
`JTechJapan.IntentSystem.Cli`.

> **Orchestrator mode is still preview/experimental.** It is opt-in, still being
> hardened, and not the default workflow. intent-cli and GitHub remain the
> authoritative source of truth; agmsg is only a message/progress/completion
> signal layer.

### Claude project-settings diagnosis for a missing agmsg Monitor (G517)

- Dogfooding surfaced a failure mode where `ToolSearch select:Monitor` finds
  **no** Claude Code `Monitor` tool at all — a different, earlier failure than
  the "`1 shell` vs `1 monitor`" delivery-mode confusion covered by G511/G516.
  When the tool itself is absent, the orchestrator-thread guide now treats it
  as a **Claude Code tool-surface problem first**, before debugging agmsg
  delivery, regardless of what `delivery.sh status` reports.
- The guide adds a **known-good comparison checklist** (diff `.claude/
  settings.json`, `.claude/settings.local.json`, `~/.claude.json` project
  trust/onboarding flags, enabled/disabled MCP server lists, and project-level
  `env` settings against a folder where `1 monitor` already works), names
  **suspect project-level `env` overrides** observed in dogfooding
  (`CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`, `CLAUDE_CODE_ENABLE_TELEMETRY`,
  `DISABLE_ERROR_REPORTING`, `DISABLE_TELEMETRY`), and documents **safe
  remediation** (an operator action: close sessions, remove/isolate the
  suspect `env` overrides while preserving the agmsg SessionStart hooks,
  reopen, then re-verify the Monitor success markers). intent-cli never edits
  `.claude/settings.json` or `~/.claude.json` itself.

### Orchestrator-mode timers shift to a design-side watchdog (G518)

- The orchestrator-thread guide now frames the **normal steady state as
  message-driven**: implementation/review receivers already send
  accepted/progress/completed/blocked replies to the orchestrator, and those
  replies wake the orchestrator path, so a fast recurring orchestrator loop is
  no longer required by default. An explicit orchestrator timer (Codex
  automation every 5 minutes, or Claude same-thread `/loop 5m`) remains
  **supported**, but only as an opt-in **fallback/legacy polling** option.
- The recommended safety net for the message-driven steady state is a new
  **optional, low-frequency design-side watchdog**: it checks the design/HITL
  (human-in-the-loop) inbox and orchestrator staleness, sends **at most one**
  canonical repair/status request, and stops or archives itself once both the
  backlog and the human-decision queues are drained. The watchdog's safety
  rules explicitly **prohibit** duplicate delegation, clearing a permission
  prompt, cancelling/resetting in-flight work, force-closing an issue/PR, and
  speculative durable-state surgery — it only sends a message and reads
  read-only facts.

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.14`,
> `nextVersion: 0.3.15`; G519 (this packet) is the release-prep metadata bump.
> The post-v0.3.15 metadata advancement (`stableVersion → 0.3.15`,
> `nextVersion → 0.3.16`) is the operator's post-release step and is out of
> scope for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.15
```

Or download the self-contained binary from the
[v0.3.15 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.15).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.14

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.15
```

There are no breaking changes from v0.3.14. Orchestrator mode remains opt-in;
existing timer-loop setups are unaffected, and an explicit orchestrator timer
remains available as a fallback/legacy polling option.

## Release-readiness gate (G519)

These items must hold **before the GitHub Release for `v0.3.15` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G517, G518 (and this G519 release-notes prep). Confirm on the host/review
      side via the host queue-state / GitHub PR state — the child implementation
      loop must not read parent queue-state, so this is a host-owned
      precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before publishing).
- [ ] `eng/version.json` `nextVersion` is `0.3.15` (the intended release
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

## Publishing v0.3.15

This packet does **not** publish the release and adds **no** publish steps. The
version-bump merge does **not** create a GitHub Release or tag on its own.

1. After this version bump is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.3.15` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.15`)
      then `intent-cli --version` reports `0.3.15`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.3.15`.
- [ ] **Missing-Monitor diagnosis smoke** (G517): `intent-cli guide
      orchestrator-thread --domain <d> --target-repo <repo> --agent <agent>
      --format markdown` renders the **Missing-Monitor project-settings
      diagnosis** subsection (known-good comparison checklist, suspect `env`
      overrides, safe remediation) under **Monitor tool vs delivery-mode**.
- [ ] **Design-side watchdog smoke** (G518): the same guide output renders the
      **Scheduled orchestrator cadence** section framed as message-driven
      steady state (orchestrator timer as fallback/legacy only) and the new
      **Design-side watchdog (optional safety net)** section (frequency,
      HITL/staleness checks, one repair/status request, stop condition, and
      the safety rules prohibiting duplicate delegation, permission-prompt
      clearing, cancellation, force-close, and durable-state surgery).
- [ ] Local preview/dry-run version metadata uses the next development line
      after `0.3.15` (bump `eng/version.json` per the post-release step in
      [Version flow](09-developer-reference.md#version-flow)):
      `stableVersion → 0.3.15`, `nextVersion → 0.3.16`.
