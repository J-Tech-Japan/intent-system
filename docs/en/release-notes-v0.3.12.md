# Release Notes — intent-cli v0.3.12

> **Release checklist for maintainers:** see [Creating the v0.3.12 GitHub Release](#creating-the-v0312-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g504) passes.**

## What's in v0.3.12

v0.3.12 is a **patch release** that ships two orchestrator-mode preview fixes
completed after `v0.3.11`: agmsg receiver startup ordering (G502) and approved-PR
label cleanup (G503). No package id, license, or workflow-semantics changes, and
the existing timer-loop mode is fully supported and unchanged. The package id
remains `JTechJapan.IntentSystem.Cli`.

> **Orchestrator mode is still preview/experimental.** It is opt-in, still being
> hardened, and not the default workflow. intent-cli and GitHub remain the
> authoritative source of truth; agmsg is only a message/progress/completion
> signal layer.

### agmsg receiver startup ordering (G502)

- The orchestrator setup guidance (`intent-cli guide orchestrator-thread`) now
  requires a strict startup order and a **ping/ack handshake before any real
  delegation**: join roles → set delivery mode → launch/restart the receiver CLI
  sessions → wait for the monitor/bridge to attach → ping after the session is
  active → require an ack (or confirm with `inbox.sh`) → only then delegate.
- It warns that messages sent before a receiver is ready may be stored in agmsg
  history but **not visibly delivered** to a freshly launched/restarted session —
  an unacked send is receiver-not-ready, not a successful delegation — and
  provides a copy-paste recovery message for the operator to send when receivers
  were launched after the initial messages.

### Approved PR label cleanup (G503)

- The `approved` PR transition now removes a stale `intent-pr-rereview-ready`
  (and the other in-flight review labels `intent-pr-request-update` /
  `intent-pr-update-in-progress`) in addition to `intent-pr-reviewing`, so an
  approved PR never visibly carries both `intent-pr-approved` and a
  "waiting for re-review" label. The transition stays idempotent when those
  labels are absent.
- `intent-cli automation reconcile` detects a PR that already carries both
  `intent-pr-approved` and a stale in-flight review label and repairs it as a
  high-confidence, intent-cli-owned label cleanup (never a raw `gh label` edit).

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.11`,
> `nextVersion: 0.3.12`; G504 (this packet) is the release-readiness preparation.
> The post-v0.3.12 metadata advancement (`stableVersion → 0.3.12`,
> `nextVersion → 0.3.13`) is the operator's post-release step and is out of scope
> for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.12
```

Or download the self-contained binary from the
[v0.3.12 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.12).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.11

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.12
```

There are no breaking changes from v0.3.11. Orchestrator mode remains opt-in;
existing timer-loop setups are unaffected.

## Release-readiness gate (G504)

Do not create the `v0.3.12` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G502, G503 (and G504 this prep). Confirm on the host/review side via the
      host queue-state / GitHub PR state — the child implementation loop must not
      read parent queue-state, so this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before tagging).
- [ ] `eng/version.json` `nextVersion` is `0.3.12` (the intended release version)
      and matches the tag to be created (`v0.3.12`). The release workflow derives
      the package version from the tag; `-p:Version=` overrides the policy-derived
      default in `src/IntentSystem.Cli/IntentSystem.Cli.csproj`.
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

## Creating the v0.3.12 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g504) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.12 && git push origin v0.3.12`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.12`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.12`)
         then `intent-cli --version` reports `0.3.12`.
   - [ ] Binary artifact smoke check: download the platform archive, verify its
         `.sha256`, extract, and run `./intent-cli --version` → `0.3.12`.
   - [ ] **Orchestrator setup guide smoke** (G502): `intent-cli guide
         orchestrator-thread --domain <d> --target-repo <repo> --agent <agent>
         --format markdown` renders the numbered startup order, the ping/ack
         requirement, and the copy-paste recovery message.
   - [ ] **Approved-label transition smoke** (G503): `intent-cli automation
         pr-transition --transition approved --pr <n> --repo <repo> --format json`
         plans removing `intent-pr-rereview-ready` alongside `intent-pr-approved`.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.12` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)):
         `stableVersion → 0.3.12`, `nextVersion → 0.3.13`.
