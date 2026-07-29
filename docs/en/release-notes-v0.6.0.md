# Release Notes — intent-cli v0.6.0

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.6.0` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it bumps version metadata and docs and adds
> **no** publish steps. See the
> [pre-merge release-readiness gate](#release-readiness-gate-g551) and
> [publishing v0.6.0](#publishing-v060).

## What's in v0.6.0

v0.6.0 is a **minor release** covering the eleven slices merged after `v0.5.0`:
**G539, G540, G541, G542, G543, G544, G545, G546, G548, G549, and G550**. It is
a minor bump rather than a patch for the same reasons v0.5.0 was: it ships **new
stalled-work detection kinds** (`backlog-ready-idle`, `blocked-label-drift`,
`repair-stalled`), a **new command surface** (`automation runs-audit`,
`queue priority-drift`, `automation issue-block`), **visible behavior changes**
to already-shipped commands, and the **repositioning of four-thread agmsg
orchestration as the primary documented model**. The package id remains
`JTechJapan.IntentSystem.Cli`; there are no package id, license, or
workflow-semantics changes.

> **G547 is deliberately absent from the slice list above.** The original
> release-prep unit for this cycle (G547) was canonically **retired** via
> `automation issue-retire` when the operator directed that G549/G550 join the
> release scope. Retirement is terminal for a unit id, so the continuation is a
> new unit — **G551**, this packet — rather than a republish of G547. G547
> shipped no code and no documentation; its packet remains as the audit trail.
> The unit range `G539–G550` therefore spans twelve unit ids of which **eleven
> are merged and shipped here**.

> **Four-thread agmsg orchestration is now the PRIMARY documented model** (see
> [G540/G541](#primary-model-repositioning-and-provisioning-g540g541-g549-g550)),
> superseding the preview/experimental framing carried by v0.5.0. Timer-loop
> mode remains fully supported as the simpler alternative. intent-cli and GitHub
> remain the authoritative source of truth; agmsg is only a message/progress/
> completion signal layer.

### Primary-model repositioning and provisioning (G540/G541, G549, G550)

This cycle moves four-thread orchestration from "preview" to the practiced,
documented default — and then supplies the two halves an operator actually
needs to run it: how a team comes into existence, and how it keeps moving.

- **Four-thread orchestration is the PRIMARY model** (G540) — `guide
  orchestrator-thread`, `guide model`, `guide onboarding`, `guide prompt-matrix`,
  and `guide help` all reframe orchestrator-message mode as primary and
  timer-loop mode as the fully supported, simpler ALTERNATIVE; every
  preview/experimental/opt-in qualifier on orchestration mode is removed. The
  role-boundary section gains the **design↔orchestrator double-check rule**,
  naming the four consulted decision categories (intent shaping, packet content
  and acceptance criteria, release scope, prioritization rulings) and the
  two-way non-bypass rule: the orchestrator never authors design content
  unilaterally, and design never bypasses the orchestrator for workflow
  transitions. Recorded in
  [ADR 0001](../adr/0001-four-thread-orchestration-primary-model.md).
- **The repositioning reaches every outward surface** (G541) — README quickstart
  leads with the four-thread orchestration path (the implementation-loop prompt
  is relabelled "timer-loop alternative"); the NuGet `Description` names the
  primary model and `PackageTags` gain `agmsg`/`orchestration`/`ai-agent`; the
  ja/en docs indexes put agent-message orchestration ahead of the timer-loop
  entries; and the remaining "optional"/"opt-in" framing is dropped from the
  orchestration page intro and its driver-mode table. Package id, tool command,
  and license are unchanged.
- **Terminal-workspace team provisioning** (G549) — `guide orchestrator-thread`
  gains a provisioning section a design thread can execute end to end with
  placeholders only, **starting from creating the dedicated role folders when
  they do not exist**: host-metadata-repo clones for the host-side roles
  (orchestrator, review) and a target-repo clone for the implementation role,
  with the never-share rule stated together with its reason — agmsg identity and
  the codex monitor bridge are `(project, type)`-scoped, so two roles in one
  folder resolve to the same identity and one silently stops receiving. It also
  pins the one-workspace/one-tab/one-pane-per-role topology, the shim-safe typed
  launch for codex (a workspace manager that exec's the canonical executable
  directly bypasses the shim and the bridge never arms), the actas form per CLI,
  and readiness split into three layers that must not be collapsed — delivery
  **configuration**, **live attachment** (agent-specific: the Claude Code
  Monitor markers vs the `Codex bridge: … alive (pid N)` marker), and
  **end-to-end**, where the ping/ack is the only end-to-end proof. herdr is
  named as the reference workspace manager with its surfaces listed and its
  internals linked out; any equivalent manager may substitute.
- **Design-thread workspace supervision and its authority boundary** (G550) —
  the other half: under authority the **operator grants**, the design thread
  drives the team's **session layer** (panes, processes, role holds, blocking
  dialogs). The **workflow layer** — labels, queue-state, publication,
  delegation, CI/review gating, closeout — is **not granted and does not move**:
  it stays with intent-cli, GitHub, and the orchestrator, so supervising a
  session never authorizes a workflow transition. The section documents session
  lifecycle (read the pane before concluding a session is dead; replace only
  through the graceful drop that honors one-holder exclusivity, with the drop
  confirmation operator-visible), **three supervision layers with cadences**
  (real-time message monitor; sub-minute blocking-UI pane scan — the failure
  mode that emits no message at all; tens-of-minutes state watchdog), and the
  **re-arm rule**: supervision schedulers are session-scoped and die silently
  with the design session, so each layer must survive a restart or be re-armed
  as the first act of the new session. Blocking dialogs are governed by the
  **verified-read rule** plus a closed four-item MAY list (each with its
  verification condition) and a closed four-category MUST-ESCALATE list in which
  credential, security, and permission waits are **never** answerable — with or
  without prior authorization. The boundary sentence: *unsticking a session is
  not deciding for it.*

### Stall-detection completion (G543, G544, G545, G546)

v0.5.0 shipped the first informational stalled-work kinds and deliberately left
them thresholdless "until field data exists". The field data arrived, and this
cycle closes the remaining blind spots.

- **`backlog-ready-idle`** (G544) — fires when WIP is empty for the domain, the
  **same canonical selector** `issue publish-flow` preflight uses reports a
  publishable (`issue-cut-ready`) candidate, and `runs.jsonl` has recorded no
  activity for at least `--backlog-idle-minutes` (default 45). No new heuristic:
  the publishability judgment is the existing selector, gates included. An
  execution unit that cannot be corroborated at all conservatively **blocks**
  the WIP-empty check rather than being excluded — a false "idle" report is the
  dangerous direction here. A missing or unparseable `runs.jsonl` fails closed
  into `excluded[]` rather than guessing an age. The G524 wake contract is
  extended so a wake must not end while a `backlog-ready-idle` item is
  actionable.
- **Canonical issue-level blocked representation + `claimed-but-silent`
  exemption** (G545) — `claimed-but-silent` now consults `queue-state.json` and
  never flags a unit whose queue item is `state=blocked`. A blocked unit whose
  GitHub labels have not been reconciled yet is instead reported under the new
  informational **`blocked-label-drift`** kind naming the reconcile command. The
  new canonical `automation issue-block --repo <r> --issue <n> --reason <text>
  [--write]` (and its `--clear` counterpart) applies the `intent-issue-blocked`
  label — the only supported way to make GitHub agree with a queue-state
  `blocked` transition, with no raw `gh` label edits. The label joins the
  canonical palette, so `label-palette-audit`/`-sync` provision and validate it.
  A no-weakening fixture proves a claimed, non-blocked, silent-past-threshold
  unit still fires `claimed-but-silent`.
- **`repair-stalled`** (G546) — a PR carrying `intent-pr-request-update`,
  `intent-pr-update-in-progress`, or `intent-pr-rereview-ready` with no
  observable activity for longer than `--repair-silent-minutes` (default 180) is
  promoted from informational to **actionable** (`stalled: true`). The
  recommendation is always a **status check to the responsible thread** —
  `implement` for the two repair labels, `review-dispatch` for
  `rereview-ready` — and never a transition or a reassignment, because silence
  alone never establishes that a repair succeeded, failed, or should be taken
  from its owner. Draft PRs get their own disjoint collection path, so a PR can
  never be reported twice and a draft mid-repair is no longer invisible. Field
  evidence: a repair claimed on 2026-07-23 went silent for **four days** after
  the implement session died while `stalled-work` reported `stalled=false`.
- **Priority enum reconciliation** (G543) — a single shared
  `QueuePriorityClassification` is now the source of truth for the documented
  `high|normal|low` enum **and** for how every value the candidate selector can
  encounter ranks, including legacy out-of-enum values such as the
  field-observed `medium`. The ranking rule is unchanged (unknown values rank
  like `normal`) — it is now shared, documented, and fixture-pinned rather than
  duplicated privately inside `intent next-slice`. New read-only `queue
  priority-drift` reports item counts per priority value, always listing
  `high`/`normal`/`low` for a stable shape, and flags out-of-enum values; it
  never mutates `queue-state.json` or `runs.jsonl`. A regression test locks in
  that `queue transition` never rewrites a pre-existing out-of-enum priority as
  a side effect.

### Durable-state integrity (G542, G548)

- **`automation runs-audit` + domain-scoped publish validation** (G542) — the
  new `automation runs-audit [--repo <r>] [--domain <d>] [--write]
  [--apply-inferred] --format json|markdown` reports every malformed
  `runs.jsonl` row in one pass: line, missing required fields, owning domain,
  and a within-record repair when one is losslessly derivable. `--write` applies
  only **within-record** repairs (`ts` from the record's own `timestamp`;
  `execution_unit` from `wip[0].eu` or `stage1.eu` for the two documented row
  shapes), appends one `runs-repair` audit event per repair, and preserves every
  unrelated byte. A field with no within-record source is always
  `non_derivable`; the separate, explicit `--apply-inferred` flag may apply a
  peer-convention suggestion instead, recorded as
  `derivation: inferred-peer-convention` — the two derivation classes are never
  mixed. Publish validation is now **domain-scoped**: `issue publish-flow`
  parses `runs.jsonl` line by line, and a malformed row belonging to a
  **different** domain becomes a warning naming `runs-audit` instead of a hard
  block, while a same-domain or domain-unresolvable row still fails closed
  exactly as before.
- **Queue-state no-item-loss invariant and stale-base re-application** (G548) —
  `queue-state.json` is one file shared by every domain on a multi-domain host,
  written concurrently from several checkouts. Every canonical writer previously
  deserialized the whole file, mutated in memory, and reserialized it, so a
  read-modify-write race did not merely conflict — it **silently erased**
  whatever the stale in-memory copy lacked. All **19** canonical mutations now
  write through one shared persistence layer providing three guarantees:
  **stale-base detection and re-application** (the base the caller read is
  compared against what is on disk at persist time through the same serializer
  round-trip, so formatting drift is never mistaken for a concurrent write; on
  mismatch the caller's derived item-level delta is re-applied to the fresh
  state and the re-application is reported back so it is never invisible); the
  **no-item-loss invariant** (any execution unit present on disk but missing
  from the outgoing state and not named as an expected removal aborts the write,
  naming the exact units and the canonical recovery path, leaving the file
  untouched); and **item-scoped re-application** (a re-applied mutation touches
  only the units its delta covers plus `updated_at`, with unrelated items
  carried through byte-identically in the fresh state's order). Legitimate
  removals — retire, the completed-item lifecycle — pass their units as expected
  removals, so the invariant targets **unrequested** loss only. Closes a
  2026-07-23 incident in which a cross-domain write dropped an item seeded an
  hour earlier, stayed invisible for four days, and then produced a circular
  recovery deadlock.

### Operational guidance (G539)

- **The design-thread watchdog is the RECOMMENDED default safety net** (G539) —
  a watchdog loop run from the design thread at a 30-minute-class interval,
  calling `automation heartbeat` and sending at most one canonical nudge when
  `stale=true`, silent otherwise. An **orchestrator-side long-interval
  automation** (the same heartbeat call run from the orchestrator's own thread
  at a 30–60-minute-class interval) is documented as the selectable
  ALTERNATIVE, with the loopless-vs-one-fewer-hop trade-off stated.
  `automation heartbeat` / `automation stalled-work` behavior is unchanged and
  stays scheduler-agnostic; the watchdog safety rules and the legacy 5-minute
  orchestrator fallback timer are preserved verbatim.

> #### Supersedes the v0.5.0 external-scheduler recommendation
>
> The v0.5.0-era guidance recommended an **external cron/launchd OS-scheduler
> heartbeat runner**. G539 **retires** that recommendation. Reason, recorded
> with field evidence: the credential store/keychain is unreachable from a cron
> context, so the runner failed **silently on every run for five continuous
> days** (2026-07-15..07-20), and a 105-minute stall went unrecovered even
> though `automation stalled-work` had correctly detected it — only a human ping
> surfaced it. An external scheduler also sits outside the agmsg model
> entirely. A watchdog that occasionally restarts but is **visible when broken**
> is a stronger guarantee than one that runs invisibly until someone happens to
> check its logs. Current guidance:
> [Agent-message orchestration — design-thread watchdog](12-agent-message-orchestration.md#design-thread-watchdog-recommended-safety-net).

## Workarounds retired by this release

Field consumers have been running documented workarounds that this release
removes the need for. After installing `v0.6.0`:

- **Title-convention workaround** — no longer needed. Execution-unit
  identification is corroborated against real packet/queue linkage rather than
  trusted from a title alone (shipped in v0.5.0 via G532; this release completes
  the surrounding detection coverage so the workaround has no remaining
  fallback role).
- **Duplicated top-level `domain:` fields** — no longer needed. Domain
  resolution and domain-scoped publish validation (G542) read the owning domain
  through the documented resolution order, so a duplicated compatibility field
  is not required to keep publish-flow validation from tripping on another
  domain's malformed row.
- **Queue-state hand-edit recovery** — retired. G548's no-item-loss invariant
  and stale-base re-application close the class of silent loss that made
  hand-editing necessary, and `automation runs-audit` (G542) plus the canonical
  queue transitions cover the repair paths that previously had none.
- **Manual repair-stall pings** — retired. `repair-stalled` (G546) promotes a
  silent repair claim to an actionable item with a status-check recommendation
  after `--repair-silent-minutes`, so a stalled repair no longer depends on a
  human noticing it. `blocked-label-drift` (G545) likewise replaces manually
  reconciling a blocked unit's GitHub labels with a named canonical command.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.0
```

Or download the self-contained binary from the
[v0.6.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.6.0).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.5.0

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.6.0
```

This release is **compatibility-conscious and non-breaking in intent**, but a
few slices change the behavior of already-shipped commands, and consumers
relying on the previous behavior should read these carefully:

- **Additive, no action needed**: the new commands (`automation runs-audit`,
  `queue priority-drift`, `automation issue-block`) are opt-in surfaces, and the
  new stalled-work kinds (`backlog-ready-idle`, `blocked-label-drift`,
  `repair-stalled`) add finer-grained classification to an existing read-only
  surface.
- **Corrective — existing command behavior changes**:
  - **G545** — `automation stalled-work` no longer reports `claimed-but-silent`
    for a unit whose queue item is `state=blocked`. A caller that treated every
    `claimed-but-silent` item as needing a nudge will see fewer items, and a
    blocked-but-unreconciled unit now appears as `blocked-label-drift` instead.
  - **G546** — a PR carrying a repair/rereview label past
    `--repair-silent-minutes` is now **actionable** (`stalled: true`) rather
    than informational. A caller that filtered on `stalled == true` will now see
    these items.
  - **G542** — `issue publish-flow` no longer hard-blocks on a malformed
    `runs.jsonl` row belonging to a **different** domain; it warns and names
    `runs-audit`. A same-domain or domain-unresolvable row still fails closed.
  - **G548** — any canonical queue-state write may now **abort** with a
    no-item-loss error naming the units it would have dropped, where it
    previously succeeded and silently lost them. A write whose base is stale is
    re-applied against the fresh state and **reports** the re-application. This
    is the intended correction, but a caller that assumed queue-state writes
    always succeed must handle the abort.
  - **G543** — priority ordering is unchanged in behavior, but it is now shared
    and documented; `queue priority-drift` will surface legacy out-of-enum
    values (e.g. `medium`) that were previously invisible.
- **Documentation framing change**: orchestrator mode is no longer described as
  preview/experimental anywhere (G540/G541). Timer-loop mode is unchanged and
  remains fully supported.

No package id, license, or CLI argument/flag shape changes; every corrective
change above brings behavior in line with the documented intent of its own
command.

## Release-readiness gate (G551)

These items must hold **before the GitHub Release for `v0.6.0` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G539, G540, G541, G542, G543, G544, G545, G546, G548, G549, G550 (and this
      G551 release-notes prep). Confirm on the host/review side via the host
      queue-state / GitHub PR state — the child implementation loop must not read
      parent queue-state, so this is a host-owned precondition.
- [ ] **G547 is confirmed terminally retired** and contributes nothing to this
      release; its successor is this G551 packet.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before
      publishing).
- [ ] `eng/version.json` shows `stableVersion` `0.5.0` and `nextVersion` `0.6.0`
      (the intended release version). `release.yml` builds the package version
      from the published Release/tag; `src/IntentSystem.Cli/IntentSystem.Cli.csproj`
      derives its local default from the same policy.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] Release notes / README describe four-thread orchestration as the
      **PRIMARY** model with timer-loop mode as the fully supported alternative —
      **no** preview/experimental framing remains (G540/G541).
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.
- [ ] **Post-merge build + pack evidence** on the merge commit is recorded in
      the PR (mirroring the G528/G538 readiness gate).

## Publishing v0.6.0

This packet does **not** publish the release and adds **no** publish steps. The
version-bump merge does **not** create a GitHub Release or tag on its own.

1. After this version bump is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.6.0` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.0`)
      then `intent-cli --version` reports `0.6.0`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.6.0`.
- [ ] **New commands smoke**: `intent-cli automation runs-audit --domain <d>
      --format json`, `intent-cli queue priority-drift --format json`, and
      `intent-cli automation issue-block --help` all run against a real repo
      without crashing.
- [ ] **Stall-detection smoke** (G544/G545/G546): `intent-cli automation
      stalled-work --domain <d> --repo <r> --format json` reports the new
      `backlog-ready-idle` / `blocked-label-drift` / `repair-stalled` kinds where
      applicable, with `repair-stalled` carrying `stalled: true`.
- [ ] **Queue-state integrity smoke** (G548): a canonical queue mutation against
      a fixture whose on-disk state contains an extra unit aborts with the
      no-item-loss error naming that unit, and leaves the file untouched.
- [ ] **Provisioning/supervision guidance smoke** (G549/G550): `intent-cli guide
      orchestrator-thread --format markdown` renders both the
      `Terminal-workspace provisioning` and `Design-thread workspace
      supervision` sections.
- [ ] Notify the operator to publish the `v0.6.0` GitHub Release, then notify
      sekiban-as-a-service-orch to drop the documented workarounds listed under
      [Workarounds retired by this release](#workarounds-retired-by-this-release)
      after installing `v0.6.0`.
- [ ] Local preview/dry-run version metadata uses the next development line
      after `0.6.0` (bump `eng/version.json` per the post-release step in
      [Version flow](09-developer-reference.md#version-flow)):
      `stableVersion → 0.6.0`, `nextVersion → 0.6.1`. This bump is deferred to
      the **next** release-prep packet, not this one.
