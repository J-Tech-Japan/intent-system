# Release Notes — intent-cli v0.3.11

> **Release checklist for maintainers:** see [Creating the v0.3.11 GitHub Release](#creating-the-v0311-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g497) passes.**

## What's in v0.3.11

v0.3.11 introduces the **agmsg orchestrator mode (preview/experimental)** and
the review-side automated-comment triage policy that were designed through the
G487–G496 intent packets. No package id, license, or workflow-semantics changes,
and the existing timer-loop mode is fully supported and unchanged. The package id
remains `JTechJapan.IntentSystem.Cli`.

> **Orchestrator mode is preview/experimental.** It is opt-in, still being
> hardened, and not the default workflow. agmsg is the first local
> message-bus example, not a permanent architecture boundary. intent-cli and
> GitHub remain the authoritative source of truth for queue, issue, PR, label,
> review, and closeout state; agmsg is only a message/progress/completion signal
> layer.

### Agent-message (agmsg) orchestrator mode — preview (G487–G496)

An optional fourth **orchestrator** thread can coordinate the **implementation**
and **review** threads over a local message bus (agmsg) instead of independent
recurring timers. Generate the paste-ready prompts and the operating contract
with:

```bash
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --format markdown
```

The guide surface now covers:

- **Roles and folders** — orchestrator / implementation / review, each running
  from its own folder, clone, or worktree (G487, G494).
- **Single-domain vs multi-domain routing** — a host repo can hold several
  domains, and one repo may serve more than one domain; visibility is not
  authorization (G489).
- **Scheduled wake cadence** — the orchestrator is the single scheduled driver
  (Codex automation 5m or Claude `/loop 5m`); implementation/review are loopless
  receivers. **Do not also run implement/review recurring timers for the same
  route in orchestrator mode** (G490).
- **Bounded next-slice publication** — the orchestrator may publish one ready
  `issue-cut-ready` issue per wake through canonical intent-cli surfaces, then
  verify before delegating (G491).
- **CI wait state** — pending CI is an active wait-and-recheck state, not a
  blocker (G492).
- **Automated reviewer comment triage** — `intent-cli guide review` classifies
  automated reviewer comments (e.g. Copilot) instead of forwarding every comment
  to implementation (G493).
- **Dependency-aware planning** — unmet dependencies are routed to the earliest
  unmet dependency rather than pausing for the operator (G495).
- **Stale-thread health check** — a safe no-reply liveness check that asks
  before acting and never auto-clears a permission prompt, cancels work, or
  duplicates a task (G496).
- **Design-thread setup** — a concrete setup checklist (paths, team, delivery,
  role prompts, first read-only wake, ping test, cleanup) reachable from
  `guide workflow suggest` (G494).

Safe-repair vs escalation, next-slice publish, and delegation boundaries are all
documented in [Agent-message orchestration](12-agent-message-orchestration.md).

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.10`,
> `nextVersion: 0.3.11`; G497 (this packet) is the release-readiness preparation.
> The post-v0.3.11 metadata advancement (`stableVersion → 0.3.11`,
> `nextVersion → 0.3.12`) is the operator's post-release step and is out of scope
> for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.11
```

Or download the self-contained binary from the
[v0.3.11 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.11).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.10

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.11
```

There are no breaking changes from v0.3.10. Orchestrator mode is opt-in; existing
timer-loop setups are unaffected.

## Release-readiness gate (G497)

Do not create the `v0.3.11` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G487–G496 (and G497 this prep). Confirm on the host/review side via the
      host queue-state / GitHub PR state — the child implementation loop must not
      read parent queue-state, so this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before tagging).
- [ ] `eng/version.json` `nextVersion` is `0.3.11` (the intended release version)
      and matches the tag to be created (`v0.3.11`). The release workflow derives
      the package version from the tag; `-p:Version=` overrides the policy-derived
      default in `src/IntentSystem.Cli/IntentSystem.Cli.csproj`.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] Release notes / README state that **orchestrator mode is
      preview/experimental** and opt-in, and that timer-loop mode is unchanged.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.

## Creating the v0.3.11 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g497) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.11 && git push origin v0.3.11`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.11`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.11`)
         then `intent-cli --version` reports `0.3.11`.
   - [ ] Binary artifact smoke check: download the platform archive, verify its
         `.sha256`, extract, and run `./intent-cli --version` → `0.3.11`.
   - [ ] **Orchestrator-mode preview smoke**: `intent-cli guide orchestrator-thread
         --domain <d> --target-repo <repo> --agent <agent> --format markdown`
         renders the role prompts, setup checklist, and safety boundaries; the
         README and docs label the mode preview/experimental.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.11` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)):
         `stableVersion → 0.3.11`, `nextVersion → 0.3.12`.
