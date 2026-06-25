# Release Notes — intent-cli v0.3.9

> **Release checklist for maintainers:** see [Creating the v0.3.9 GitHub Release](#creating-the-v039-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g483) passes.**

## What's in v0.3.9

v0.3.9 is a loop-stability release. It ships the four reliability fixes
completed after `v0.3.8` that keep Claude/Codex implementation and review loops
moving without stalling on interactive prompts, role confusion, duplicate-issue
races, or publish-blocking packet gaps. No package id, license, or
workflow-semantics changes. The package id remains `JTechJapan.IntentSystem.Cli`.

### Fail closed instead of Asking UI during loop wakes (G479)

- Every recurring loop prompt (`intent-cli guide prompt-matrix`) now carries one
  shared policy: during an automation loop wake, agents do **not** stop on
  interactive Asking UI for operational ambiguity (duplicate publish, queue /
  linkage mismatch, role confusion, CI pending, WIP-cap, draft PR, stale lease).
- Asking is reserved for narrow safety gates only — security-sensitive approval,
  external credentials / login, destructive local operations, irreversible
  public release / publish, or an explicit operator-requested policy decision.
- Recoverable ambiguity converges to intent-cli safe repair or a normal wait;
  non-recoverable ambiguity ends with `STOP: <classification>` and exactly one
  next operator action. Unsafe options such as continuing two concurrent host
  loops are never offered — the safe invariant is one active wake per host
  repo + domain.

### Host-orchestrator and semantic-reviewer roles are explicit (G480)

- The host review / next-slice prompt now makes three responsibilities distinct:
  **host-orchestrator** (preflight, diagnostics, safe repair, merge approved
  PRs, closeout, next-slice publish, metadata reconciliation),
  **semantic-reviewer** (inspect diff, map to packet / intent, approve /
  request-update — permitted only when the running agent is the packet
  `review_role`, default `Codex`, or is explicitly assigned), and
  **child-implementer**.
- An agent neither over-reviews nor concludes "the host never reviews." An
  already-approved PR stays mergeable by the orchestrator even when a different
  agent performed the review; role mismatch is a wait / `STOP: review-role-mismatch`,
  never Asking UI. The Claude host-orchestrator and Codex semantic-reviewer
  prompt variants read correctly for each role.

### Duplicate host publish is detected and canonicalized fail-closed (G481)

- `intent-cli automation state-doctor` now classifies duplicate execution-unit
  issues and concurrent host publish: `concurrent-host-publish-detected`,
  `canonical-issue-mismatch`, and `pr-closes-noncanonical-issue` (the
  last classified separately from ordinary missing-`linked_pr` recovery).
- Canonical selection prefers durable evidence (queue-state `linked_issue`, then
  packet `publish.yaml`) over live GitHub recency. A safe repair (closing the
  non-canonical duplicate) is offered only as `duplicate-execution-unit-issue-detected`
  when the canonical issue is unique and the duplicate carries no active PR.
- Ambiguous or thrashing races fail closed without picking a winner by recency,
  reopening/closing issues arbitrarily, or auto-editing a PR body mid-race.

### Packet creation emits complete publish-ready contracts (G482)

- The packet scaffold (`intent-cli packet draft`) and the publish-body validator
  now share one required-section source of truth, so they can never drift apart.
- A freshly scaffolded `github-body.md` carries the complete contract shape by
  default, including `Standalone Child Issue Contract`, and the packet-draft
  guide tells agents to dry-run publish validation (`issue validate-body`,
  `packet draft --dry-run`, `intent next-slice --dry-run`) before declaring a
  packet ready for GitHub issue creation. Publish validation stays fail-closed
  for incomplete bodies — the repeated missing-section publish blocks stop
  recurring.

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.8`,
> `nextVersion: 0.3.9`; G483 (this packet) is the release-readiness preparation.
> The post-v0.3.9 metadata advancement (`stableVersion → 0.3.9`,
> `nextVersion → 0.3.10`) is the operator's post-release step and is out of scope
> for this packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.9
```

Or download the self-contained binary from the
[v0.3.9 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.9).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.8

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.9
```

There are no breaking changes from v0.3.8.

## Release-readiness gate (G483)

Do not create the `v0.3.9` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G479, G480, G481, G482 (and G483 this prep). Confirm on the host/review
      side via the host queue-state / GitHub PR state — the child implementation
      loop must not read parent queue-state, so this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before tagging).
- [ ] `eng/version.json` `nextVersion` is `0.3.9` (the intended release version)
      and matches the tag to be created (`v0.3.9`). The release workflow derives
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

## Creating the v0.3.9 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g483) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.9 && git push origin v0.3.9`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.9`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.9`)
         then `intent-cli --version` reports `0.3.9`.
   - [ ] Binary artifact smoke check: download the platform archive, verify its
         `.sha256`, extract, and run `./intent-cli --version` → `0.3.9`.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.9` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)):
         `stableVersion → 0.3.9`, `nextVersion → 0.3.10`.
