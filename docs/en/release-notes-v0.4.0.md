# Release Notes — intent-cli v0.4.0

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.4.0` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it bumps version metadata and docs and adds
> **no** publish steps. See the
> [pre-merge release-readiness gate](#release-readiness-gate-g528) and
> [publishing v0.4.0](#publishing-v040).

## What's in v0.4.0

v0.4.0 is a **minor release** covering eight slices (G520–G527) completed
after `v0.3.15`. It is a minor bump rather than a patch because it ships
**three new automation commands** and a **visible fail-loud behavior change**
in domain resolution — both are more than routine maintenance. The package id
remains `JTechJapan.IntentSystem.Cli`; there are no package id, license, or
workflow-semantics changes.

> **Orchestrator mode is still preview/experimental.** It is opt-in, still
> being hardened, and not the default workflow. intent-cli and GitHub remain
> the authoritative source of truth; agmsg is only a message/progress/
> completion signal layer.

### New automation commands: stalled-work, heartbeat, issue-retire

These three commands exist because an orchestrator-mode pilot
(sekiban-as-a-service-orch, 2026-06-28..07-14) lost significant time to
silent pipeline stalls with no built-in way to detect or recover from them.

- **`automation stalled-work`** (G523) — a read-only inventory of pending
  pipeline transitions with ages, across three categories: `published-not-
  delegated` (an OPEN issue carries `intent-target` but has no claim and no
  PR yet), `pr-created-not-reviewing` (the closing PR is missing the
  `review-start` transition), and `merged-not-closed-out` (a MERGED PR's
  linked queue item is not yet `Completed`). Each item reports its execution
  unit, age, and the exact canonical next command to run. No GitHub
  mutation, no queue-state/runs.jsonl write.
- **`automation heartbeat`** (G526) — wraps `stalled-work` and, when
  anything is stale, emits a ready-to-send reconcile message body naming
  every stale item and its recommended action; silent JSON (`stale: false`,
  no `message_body`) when healthy. Never sends a message or launches an
  agent itself — the orchestrator-thread guide now documents this as the
  **recommended safety net**: a session-independent external scheduler
  (cron/launchd, 60-minute class) with a fail-closed copy-paste wrapper,
  ahead of the design-side watchdog and the in-session fallback timer (both
  of which showed measured weaknesses in the field trial — see the guide's
  "External heartbeat" section for the full comparison).
- **`automation issue-retire`** (G525) — the canonical, atomic transition
  for a published `intent-target` issue that can never be started as
  authored (e.g. a slice must be decomposed). Closes the issue as "not
  planned", removes workflow labels, and marks the queue-state entry
  `retired` — creating the entry if none existed, so `metadata validate`
  never reports a missing queue entry for a legitimately retired unit.
  Fails closed on an open linked PR, an active claim, or already-`Completed`
  work; a partial-write retry converges safely via a durable provenance
  marker rather than dead-ending.

### Fail-loud domain resolution — migration note

**(G522, tightened further across G523/G525)** Execution-unit-resolving
surfaces — `automation queue-seed-from-packet`, `review closeout-plan`,
`automation publish-recovery`, `automation stalled-work`, and `automation
issue-retire` — now resolve the domain in a strict order:

1. an explicit `--domain` wins (it is an error if it contradicts the domain
   declared by the resolved packet.yaml's `domain:` field);
2. otherwise the packet-declared `domain:` field is used;
3. otherwise the surface **fails loud**, naming candidate domains (scanned
   from `intents/*/`) and the exact `--domain` re-invocation.

**This is a visible behavior change.** Previously some of these surfaces
silently fell back to the host's config-default domain binding when neither
signal was available — on a multi-domain host, this could misattribute an
execution unit to the wrong domain. **If you have scripts or automation that
relied on that silent fallback**, they will now fail loud instead of silently
succeeding against the wrong domain. Adapt by either passing `--domain`
explicitly, or ensuring the relevant packet.yaml declares its `domain:`
field.

### Orchestrator wake contract (G524)

Field data showed ~60 hours of "publish-then-sleep" stalls and an 88-minute
silent-completion gap. The orchestrator-thread guide now requires, every
wake:

- **publish and delegate in the same wake** — no more deferring a delegation
  to "the next wake" (nothing else would reliably wake the orchestrator to
  send it);
- the message cap is reframed as **"at most one delegation per receiver per
  wake"**, not a blanket one-message rule — publish + its delegation + repair
  messages + an escalation + receiver-report handling are all permitted
  within one wake;
- a new **end-of-wake `automation stalled-work` check** with a never-defer,
  escalate-instead-of-defer rule;
- the receiver's completion-or-blocked report is now a **REQUIRED FINAL
  STEP** of every delegation, stated with its exact expected JSON shape;
- **dispatch roster verification** (`team.sh`) before every send, closing a
  field-observed message loss to a dead `review` vs `reviewer` address.

### Managed review worktrees + design-alignment checks (G520)

Review worktrees are now enforced under the managed root
(`.intent-cli/worktrees/review-<unit>`) — never a raw `/tmp/...` path — and a
stale/dirty/unregistered worktree becomes a structured blocker reply, never
an operator `rm -rf` approval prompt. A review `completed` reply is now
**incomplete** unless it shows evidence that design alignment (packet,
review-context, intent tree, ADR/decision notes) was actually checked.

### Codex monitor (beta) guidance (G521)

Adds a Codex monitor setup preflight and three troubleshooting entries for
the agmsg Codex bridge: a silent launcher (multiple identities), a static
TUI (stale app-server threads, with the full recovery sequence), and doubled
responses (a doubled bridge).

### Packet-yaml parser fix (G527)

`PreparedPacketYamlScalarParser`'s quote-balance check is now
delimiter-aware instead of counting every quote character in a value — a
double-quoted scalar's balance depends only on double quotes (an apostrophe
inside is ordinary content), and a single-quoted scalar's balance depends
only on single quotes (honoring the YAML `''` escape). This fixes the exact
field incident where `automation queue-seed-from-packet` twice refused a
valid packet whose double-quoted values merely contained an apostrophe.
Genuinely unterminated or ambiguous quoting still fails closed.

> Version metadata note: `eng/version.json` records `stableVersion: 0.3.15`,
> `nextVersion: 0.4.0`; G528 (this packet) is the release-prep metadata bump.
> The post-v0.4.0 metadata advancement (`stableVersion → 0.4.0`, `nextVersion
> → 0.4.1`) is the operator's post-release step and is out of scope for this
> packet.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.4.0
```

Or download the self-contained binary from the
[v0.4.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.4.0).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.15

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.4.0
```

**Read the fail-loud domain resolution migration note above before upgrading
any automation that scripts these commands.** Everything else is additive:
the three new commands are opt-in surfaces, and the orchestrator/review-guide
changes only affect operators who have opted into orchestrator mode. Existing
timer-loop setups are unaffected.

## Release-readiness gate (G528)

These items must hold **before the GitHub Release for `v0.4.0` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G520, G521, G522, G523, G524, G525, G526, G527 (and this G528
      release-notes prep). Confirm on the host/review side via the host
      queue-state / GitHub PR state — the child implementation loop must not
      read parent queue-state, so this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before
      publishing).
- [ ] `eng/version.json` `nextVersion` is `0.4.0` (the intended release
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

## Publishing v0.4.0

This packet does **not** publish the release and adds **no** publish steps. The
version-bump merge does **not** create a GitHub Release or tag on its own.

1. After this version bump is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.4.0` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.4.0`)
      then `intent-cli --version` reports `0.4.0`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.4.0`.
- [ ] **New commands smoke**: `intent-cli automation stalled-work --domain <d>
      --repo <r> --format json`, `intent-cli automation heartbeat --domain <d>
      --repo <r> --format json`, and `intent-cli automation issue-retire --help`
      all run against a real repo without crashing.
- [ ] **Fail-loud domain migration smoke** (G522): running one of the affected
      surfaces without `--domain` against a packet with no declared `domain:`
      fails loud naming candidate domains, rather than silently resolving the
      host's config-default domain.
- [ ] **External heartbeat guide smoke** (G526): `intent-cli guide
      orchestrator-thread --domain <d> --target-repo <repo> --agent <agent>
      --format markdown` renders the **External heartbeat (recommended safety
      net)** section (frequency, command, copy-paste wrapper, at-most-one-
      message rule) ahead of the **Design-side watchdog (alternative safety
      net)** section.
- [ ] **Wake contract guide smoke** (G524): the same guide output renders the
      end-of-wake `automation stalled-work` check, the "at most one delegation
      per receiver per wake" framing, and the dispatch roster verification
      step.
- [ ] Local preview/dry-run version metadata uses the next development line
      after `0.4.0` (bump `eng/version.json` per the post-release step in
      [Version flow](09-developer-reference.md#version-flow)):
      `stableVersion → 0.4.0`, `nextVersion → 0.4.1`.
