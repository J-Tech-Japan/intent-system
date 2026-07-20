# Release Notes — intent-cli v0.5.0

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.5.0` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it bumps version metadata and docs and adds
> **no** publish steps. See the
> [pre-merge release-readiness gate](#release-readiness-gate-g538) and
> [publishing v0.5.0](#publishing-v050).

## What's in v0.5.0

v0.5.0 is a **minor release** covering nine slices (G529–G537) completed
after `v0.4.0`. It is a minor bump rather than a patch because it ships
**two new commands** (`intent facet-check`, `queue reprioritize`), a **new
intent-tree schema surface** (`facets`), **new stalled-work kinds**, and a
**new transition target** (`retired`) — all more than routine maintenance.
The package id remains `JTechJapan.IntentSystem.Cli`; there are no package
id, license, or workflow-semantics changes.

> **Orchestrator mode is still preview/experimental.** It is opt-in, still
> being hardened, and not the default workflow. intent-cli and GitHub remain
> the authoritative source of truth; agmsg is only a message/progress/
> completion signal layer.

### Semantic facets (G529–G531)

Originating from operator issue #1159, this three-slice initiative gives
intent-tree nodes a closed, four-value vocabulary for the kind of semantic
weight they carry, then makes that classification consumable by both
tooling and reviewers.

- **`facets:` frontmatter** (G529) — a node may declare zero or more of
  `vocabulary` (event/command vocabulary: what counts as a fact),
  `invariant` (invariants and consistency boundaries), `decider` (decider
  judgments: what a command decides), or `acceptance-property` (what must
  not break). Facets are optional and additive — a tree with none of them
  annotated is unaffected.
- **Facet-aware context supply** (G530) — `intent-cli context collect` gains
  a `## Facet context` section, rendered AHEAD of the unclassified
  queue-state/clarification/automation-bindings context (the semantic core,
  not an afterthought), grouped in the canonical order `vocabulary →
  invariant → decider → acceptance-property`. `--facets` restricts which
  groups appear; `--scope` narrows to nodes overlapping given paths, checked
  symmetrically in both directions. `intent-cli packet draft` generates the
  same section inside scaffolded `review-context.md`, scoped to the
  packet's own `intent_references`, kept current on every rerun via a
  fail-closed marker-pair protocol that never touches hand-written prose
  around the generated block.
- **`intent facet-check`** (G531) — a read-only lexical scaffold that points
  a change proposal AT the facet nodes G530 makes reachable: it checks a
  proposal's candidate command/event terms against existing
  `vocabulary`/`invariant` nodes so reviewers see naming collisions and
  coverage gaps early. Explicitly **not** a semantic verifier and **never**
  a gate — matching is lexical, false negatives are expected, and the
  command always exits `0` regardless of findings.

### stalled-work correctness (G532–G533)

Field data against a downstream adopter (2026-07-15, 2026-07-18) showed
`automation stalled-work` misclassifying candidates when its execution-unit
identification logic itself was wrong.

- **Execution-unit and domain identification** (G532) — the candidate
  execution unit is now the LEADING ID token of the issue/PR title with a
  mandatory right boundary, trusted only when a real
  `.intent-cli/issues/<token>/packet.yaml` corroborates it; otherwise the
  candidate is matched against every packet's own declared
  `source_execution_unit`. Domain confirmation applies the same `--domain` >
  packet-declared > fail-loud order as every other execution-unit-resolving
  surface, but only for a candidate whose execution unit is itself
  corroborated by real packet/queue linkage — an uncorroborated candidate is
  excluded (`domain-underivable`) rather than silently guessed.
- **Informational stalled-work kinds** (G533) — three new kinds
  (`repair-pending`, `rereview-pending`, `claimed-but-silent`), each
  `is_informational: true` with descriptive (never transition-command)
  `recommended_action`. Fixes the exact field incident where a PR mid-repair
  was misreported as `pr-created-not-reviewing` with a wrong `review-start`
  recommendation. `claimed-but-silent` fails closed into `excluded[]` on any
  unusable activity-timestamp data rather than guessing from `createdAt`.

### Queue robustness (G534)

Three related field findings against real, hand-authored packets and queue
state, fixed together:

- **`queue enqueue` accepts both YAML list-item conventions** — the
  4-space-plus-`"- "` renderer convention and the more common 2-space
  convention are both recognized by content, not column count.
- **`queue transition --to retired` backfills a queue-state entry** — a
  new guarded, idempotent, terminal transition with its own entry point
  (`QueueManager.Retire`), consistent with `automation issue-retire`'s
  refusal semantics: legal from any state except `Completed`; verifies the
  linked PR's real GitHub state (refusing when it is confirmed merged or
  closed) before mutating; idempotent when already `Retired`; and terminal —
  a retired item can never transition to any other state again.
- **The publish selector (`intent next-slice`) combines queue-state and
  packet-lifecycle evidence explicitly** — either signal alone recording
  retirement excludes the unit; a contradiction between the two now
  surfaces as an actionable `lifecycle-metadata-diagnostic` warning instead
  of resolving silently in either direction.

### Label supersession (G535)

Field finding #5 (SKS-G824 / PR #1760): `automation pr-transition
--transition request-update` added its repair labels but left a
pre-existing `intent-pr-rereview-ready` in place — `worker claim` correctly
refuses a rereview-ready PR, producing a deadlock between two canonical
rules with no installed command able to proceed. `request-update` now
clears `intent-pr-rereview-ready` (and its legacy string form) in the
**same** write that adds `intent-pr-request-update` — a repair request
always supersedes pending rereview-readiness.

### Publish reliability (G536)

`issue publish-flow`'s idempotent rerun now independently verifies and
restores all three durable artifacts — the GitHub issue, the queue-state
entry, and the `runs.jsonl` event — instead of trusting one signal (e.g. the
GitHub issue existing) to imply the other two are also intact. A partial
prior failure that left, say, the queue-state entry missing while the issue
was created is now detected and repaired on rerun rather than silently
treated as fully done; genuinely unrecoverable inconsistencies fail loud
rather than being papered over.

### Priority override (G537)

The new `queue reprioritize <execution-unit> --priority <high|normal|low>
--reason <text> [--write]` command sets priority on a queued, unpublished
execution unit under a durable, concurrency-safe audit protocol:

- `intent next-slice` now selects eligible publish candidates
  **priority-class-first** (high > normal > low), with a stable tiebreak by
  authoring order within a class — dependency/WIP/clarification/lifecycle
  gates always dominate priority; a high-priority but gate-blocked candidate
  is never selected over an eligible lower-priority one.
- Each successful `--write` is identified by a durable, monotonically
  incrementing `priority_revision` counter (not wall-clock time, not a
  content fingerprint — both were tried and broken by clock non-monotonicity
  and genuine state revisits respectively) recorded on the queue item, so a
  retry can always distinguish "my own prior attempt, safe to skip
  re-appending" from "a genuinely new mutation" from "a conflicting claim,"
  and fails closed on the latter two.
- `--write` acquires a non-blocking, OS-level exclusive lock (`FileShare.None`
  on a sibling `queue-state.reprioritize.lock` file) before the authoritative
  read and holds it across the entire write critical section — a concurrent
  invocation that cannot acquire the lock fails closed immediately rather
  than racing. Dry-run never mutates and never takes the lock.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.5.0
```

Or download the self-contained binary from the
[v0.5.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.5.0).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.4.0

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.5.0
```

This release is **compatibility-conscious and non-breaking in intent**, but
not every change is purely additive — a few slices correct the behavior of
already-shipped commands, and consumers relying on the corrected behavior
should read these carefully:

- **Additive, no action needed**: the two new commands
  (`intent facet-check`, `queue reprioritize`) are opt-in surfaces, `facets:`
  frontmatter is an optional annotation, and G533's new stalled-work kinds
  only add finer-grained classification to an existing read-only surface.
- **Corrective — existing command behavior changes**:
  - **G535** — `automation pr-transition --transition request-update` now
    also clears a stale `intent-pr-rereview-ready` label in the same write.
    A caller that previously expected `request-update` to leave that label
    untouched (e.g. re-applying it manually afterward) will see it gone.
  - **G536** — `issue publish-flow`'s idempotent rerun now independently
    verifies and restores all three durable artifacts instead of trusting
    one signal for all three; a rerun that previously silently no-opped on
    a partially-failed prior run may now perform additional writes (or fail
    loud) where it previously did neither.
  - **G532/G534** — `automation stalled-work`'s execution-unit/domain
    identification is stricter (an uncorroborated candidate is now
    excluded rather than possibly misattributed), and `intent next-slice`'s
    retirement-evidence combination is stricter (a lifecycle/queue-state
    contradiction that previously resolved silently in one direction now
    excludes the candidate and raises a diagnostic instead). A candidate
    that previously matched loosely may now be excluded or flagged where it
    previously was not.

No package id, license, or CLI argument/flag shape changes; every corrective
change above is a bug fix to bring behavior in line with the documented
intent of its own command, not a new command surface or a removed one.

**Consumers running the sekiban-as-a-service-orch field-workaround set**
(SKS-G8xx: title-convention workaround, duplicated top-level `domain:`
fields, queue-state hand-edit recovery) can retire all three once this
release is installed — G532's execution-unit identification no longer needs
the title-convention workaround, G534's `queue transition --to retired`
replaces the hand-edit recovery path, and the duplicated top-level `domain:`
field compatibility alias (already supported since earlier releases) covers
the remaining case cleanly going forward.

## Release-readiness gate (G538)

These items must hold **before the GitHub Release for `v0.5.0` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G529, G530, G531, G532, G533, G534, G535, G536, G537 (and this G538
      release-notes prep). Confirm on the host/review side via the host
      queue-state / GitHub PR state — the child implementation loop must not
      read parent queue-state, so this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before
      publishing).
- [ ] `eng/version.json` `nextVersion` is `0.5.0` (the intended release
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
- [ ] **Post-merge build + pack evidence** on the merge commit is recorded in
      the PR (mirroring the G528 readiness gate).

## Publishing v0.5.0

This packet does **not** publish the release and adds **no** publish steps. The
version-bump merge does **not** create a GitHub Release or tag on its own.

1. After this version bump is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.5.0` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.5.0`)
      then `intent-cli --version` reports `0.5.0`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.5.0`.
- [ ] **New commands smoke**: `intent-cli intent facet-check --domain <d>
      --terms Example --format json` and `intent-cli queue reprioritize
      --help` both run against a real repo without crashing.
- [ ] **Facet context smoke** (G530): `intent-cli context collect --domain <d>
      --format json` renders a `## Facet context` section (or its
      `facet_context_note` graceful-degradation note on a tree with no
      annotated nodes).
- [ ] **stalled-work classification smoke** (G532/G533): `intent-cli
      automation stalled-work --domain <d> --repo <r> --format json` reports
      the new `repair-pending` / `rereview-pending` / `claimed-but-silent`
      kinds where applicable, each carrying `is_informational: true`.
- [ ] **Queue retirement smoke** (G534): `intent-cli queue transition
      <execution-unit> retired --help` and `intent-cli queue enqueue --help`
      both run without crashing against a real repo.
- [ ] **Priority override smoke** (G537): `intent-cli queue reprioritize
      --help` and `intent-cli intent next-slice --help` both run without
      crashing; priority-class-first ordering documented in
      [09-developer-reference.md](09-developer-reference.md#canonical-publish-order-override--queue-priority-g537).
- [ ] Notify the operator to publish the `v0.5.0` GitHub Release, then notify
      sekiban-as-a-service-orch to drop the three documented workarounds
      after installing `v0.5.0`.
- [ ] Local preview/dry-run version metadata uses the next development line
      after `0.5.0` (bump `eng/version.json` per the post-release step in
      [Version flow](09-developer-reference.md#version-flow)):
      `stableVersion → 0.5.0`, `nextVersion → 0.5.1`. This bump is deferred to
      the **next** release-prep packet, not this one.
