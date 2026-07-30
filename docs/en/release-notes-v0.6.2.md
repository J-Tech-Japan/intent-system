# Release Notes — intent-cli v0.6.2

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.6.2` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it authors the notes and adds **no** publish
> steps. See the [pre-merge release-readiness gate](#release-readiness-gate-g558)
> and [publishing v0.6.2](#publishing-v062).

## What's in v0.6.2

v0.6.2 is a **patch release** covering exactly the three slices merged after
`v0.6.1`: **G555**, **G556**, and **G557**. It is a patch rather than a minor
because nothing here changes a CLI surface: G555 and G556 are guide amendments,
and G557 is a test/release-flow hotfix. No new commands, no removed or renamed
arguments. The package id remains `JTechJapan.IntentSystem.Cli`; there are no
package id, license, or workflow-semantics changes.

Two of the three close field incidents from the same day, and the third closes
the failure that those incidents' own fix produced.

### Cross-project isolation on a shared machine (G555)

The provisioning and supervision guidance described how to build and keep **one**
team; nothing said that other teams are running on the same machine. Operator
incident (2026-07-29): with several project teams live at once, one project's
design thread damaged another project's resources and the operator had to
intervene by hand. A near-miss of the same class was avoided earlier that week
only by ad-hoc discipline — verifying each pid's cwd before killing anything —
discipline that lived in one session transcript rather than in the guide.

`guide orchestrator-thread` gains a cross-project isolation section. It narrows
the **objects** a supervising thread may act on to its own team's; it does not
change what it may **do**, so the supervision authority boundary is unchanged.

- **Attribution before mutation** — before injecting keys into a pane, killing a
  process, closing or restructuring a workspace, or removing/rewriting a state
  file, ownership is established from four keys: **workspace label**, **pane
  cwd**, **process cwd** (read per pid — a pid list filtered by process *name*
  attributes nothing), and **agmsg `(team, role)` file naming**. Attribution is a
  positive result, not the absence of counter-evidence, and **unverifiable
  attribution is read-only**: look, report, escalate — never mutate on a guess.
- **One workspace per team** (labelled with the team name, never reused or
  borrowed) and **team-exclusive role folders**, with the folder-scoping reason
  stated: agmsg identity and the codex bridge are folder-scoped, so an agent
  started in another team's folder takes over *their* identity and delivery.
- **A shared-substrate ownership table** naming the sharing unit and ownership
  rule for the workspace-manager server (per workspace, never the server), the
  agmsg run directory (per `(team, role)` file, never a wholesale clear), codex
  app-servers (per folder, cwd-verified), and the host repo (per domain path).
- **Non-destructive recovery** — preserve and set aside another project's damaged
  artifacts (a broken artifact is still its owner's evidence), rebuild your own
  fresh. **Recovery defaults to recreate, not cleanup.**

### Verified liveness — a startup report is not readiness (G556)

Field incident (2026-07-29): two codex agents sent startup-complete reports and
died **seconds** later when their shared remote app-server was lost to a
websocket transport reset, dropping both TUIs to shell prompts. The supervising
design thread went on *"waiting for startup reports"* while every agent was
already dead. The provisioning flow ended at the readiness ping — nothing
required re-verification **after** the report — and the supervision pane scan
named blocking dialogs but not a pane showing a shell prompt where an agent
should be.

- **Verified liveness.** A role is provisioned when its startup report has
  arrived **and**, after a **settle delay**, all three still pass: the pane still
  hosts the agent TUI (read the pane — it is ground truth; a message is a claim
  about the past), an agmsg ping-pong round trip succeeds **now**, and for codex
  the bridge is armed with a stable app-server attachment. The settle delay is
  load-bearing: the failure happens seconds *after* the report, so an instant
  re-check merely re-observes the moment the report already described.
- **Early death is a normal mode**, with its signature named — the TUI exits to a
  shell prompt, typically leaving a resume hint, after a **transport reset**
  drops the app-server connection. That pane looks like an ordinary terminal,
  which is why a dialog-only scan misses it. On a failed check the thread
  **re-checks and recovers rather than waiting for another report**: a dead agent
  sends nothing, so waiting is waiting forever.
- **`agent-absent`** joins the supervision pane-scan list as an equal stuck state
  beside blocking dialogs, and routes by state rather than through dialog
  handling — there is no dialog to answer. Recovery is a **shim-based relaunch**
  (typed into the pane's interactive shell, recreating the app-server when that
  is what died) followed by the **complete verified-liveness sequence** again.
  The permission mode is set with the **launch flag** rather than switched
  afterwards: synthetic key injection cannot be relied on for mode switching,
  because modifier chords such as shift+tab are not delivered faithfully
  (observed across multiple teams).
- **Shared app-server death mode** — killing an app-server takes down **every
  attached TUI at once**, including other teams' agents that had nothing to do
  with the kill. Prevention points at G555's attribution rules: this is the
  second-order cost of an attribution violation, where the victim is everything
  attached to the process rather than the process itself.

### Release-flow hardening (G557)

The first live execution of the v0.6.1 post-release version roll turned child
main red on four checks: three tests pinned the `stableVersion` / `nextVersion`
pair **by value**, and the G475 guard requires release notes to exist for
whatever `nextVersion` names. An unrelated PR inherited the red main and was
frozen until this landed.

- **Version-agnostic assertions.** A literal version pair is the wrong assertion
  for a field a *required recurring step* is supposed to change — pinning it
  guarantees that a correct roll breaks the test. The three assertions now derive
  from `eng/version.json` through one shared source and assert the property that
  holds across every roll: the policy parses, and the release-to-be-cut is
  strictly ahead of the published stable. A **roll-simulation fixture** (next
  patch, minor rollover, major rollover) proves they stay green, so the
  regression is expressible rather than merely fixed.
- **The draft-stub mechanism.** The roll now creates clearly-marked **DRAFT**
  `release-notes-v<nextVersion>.md` stubs in the same commit. They satisfy the
  G475 guard with **its semantics unchanged** — existence is still required; it
  is simply now possible to satisfy at roll time — and they block a Release by
  their own contract until release-prep fills them in. *(These notes are that
  release-prep for v0.6.2.)*
- **The roll rule completed.** The release closeout checklist now includes stub
  creation with the version bump **and** a final step requiring the roller to
  verify child main CI green after pushing: a red main blocks every unrelated PR
  that inherits it, so the roll is complete only when CI is.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.2
```

Or download the self-contained binary from the
[v0.6.2 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.6.2).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.6.1

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.6.2
```

This release is **additive on the guide surfaces and corrective in the release
flow**. There are no new commands and no argument/flag changes.

- **Additive — guidance only, no action needed.** G555 and G556 extend
  `intent-cli guide orchestrator-thread` output (and the mirrored ja/en
  orchestration docs) with new sections and fields. Nothing that consumed the
  guide before behaves differently; there is simply more of it. If you supervise
  a shared machine or provision teams, **read the two new sections** — they
  encode incidents that cost other teams outages.
- **Corrective — release-flow only.** G557 changes how the repository's own
  release tooling is asserted and how the post-release roll is performed. It
  affects maintainers cutting a release, not consumers of the CLI:
  - the version-policy assertions no longer pin a literal version pair, so a
    correct roll no longer breaks them;
  - the post-release roll now creates DRAFT notes stubs and requires a green-CI
    check before it counts as complete.

No package id, license, or CLI argument/flag shape changes.

## Release-readiness gate (G558)

These items must hold **before the GitHub Release for `v0.6.2` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G555 (PR #1214), G556 (PR #1218), and G557 (PR #1216), plus this G558
      release-prep. Confirm on the host/review side via the host queue-state /
      GitHub PR state — the child implementation loop must not read parent
      queue-state, so this is a host-owned precondition.
- [ ] **These notes are no longer a draft.** The G557 stub contract blocks a
      Release while the file still carries its DRAFT banner; this file replacing
      it is what lifts that block.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before
      publishing).
- [ ] `eng/version.json` shows `stableVersion` `0.6.1` and `nextVersion` `0.6.2`
      (the intended release version), **unchanged by this packet**.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.
- [ ] **Post-merge build + pack evidence** on the merge commit is recorded in
      the PR (mirroring the G528/G538/G551/G554 readiness gate).

## Publishing v0.6.2

This packet does **not** publish the release and adds **no** publish steps. The
merge of these notes does **not** create a GitHub Release or tag on its own.

1. After this packet is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.6.2` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.2`)
      then `intent-cli --version` reports `0.6.2`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.6.2`.
- [ ] **Guide smoke** (G555/G556): `intent-cli guide orchestrator-thread
      --format markdown` renders both the `Cross-project isolation on a shared
      machine` section and the `Verified liveness` subsection.
- [ ] **ROLL `eng/version.json` NOW**, per the G554 rule as amended by G557:
      `stableVersion → 0.6.2`, `nextVersion → 0.6.3`, **in the same commit as
      new DRAFT `release-notes-v0.6.3.md` stubs (EN/JA)**, then **verify child
      main CI is green** before calling the roll complete. See
      [Version flow](09-developer-reference.md#version-flow).
- [ ] Notify the operator and downstream consumers that publication **and**
      verification of `v0.6.2` are complete. (The publish request itself belongs
      to the pre-release phase above; by this point the Release is already
      published.)
