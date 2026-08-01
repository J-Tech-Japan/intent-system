# Release Notes — intent-cli v0.7.1

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.7.1` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it authors the notes and adds **no** publish
> steps. See the [pre-merge release-readiness gate](#release-readiness-gate-g572)
> and [publishing v0.7.1](#publishing-v071).

## What's in v0.7.1

v0.7.1 covers exactly the five slices merged after `v0.7.0`: **G565**, **G566**,
**G567**, **G568**, and **G569**.

**Why patch, not minor.** The documented policy reserves a minor bump for a new
command surface, and this release **does add one**: G568 ships
`intent-cli automation queue-dependency-reconcile`. It is still a patch, and the
reason is what that command is rather than that it does not exist.

It is a **narrowly bounded repair utility that completes a bugfix**, not a
recurring workflow capability: it exists solely to correct queue items that the
same slice's fix stops producing, it participates in no workflow phase, nothing
invokes it automatically, and once historical items are reconciled it has no
further role. A repair path for data a bug already wrote is part of fixing that
bug — the minor reservation is for surfaces that add something the workflow can
now *do* on an ongoing basis, and this adds nothing to the workflow.

Everything else is a bugfix or a determinism fix, and no command, argument, or
flag was removed or renamed.

Three themes run through the batch.

### One parser for every packet acceptance surface (G565, G567)

A packet is now valid everywhere or rejected everywhere. Before this release the
toolchain disagreed with itself about what a `packet.yaml` even *is*, and the
disagreement was invisible until it bit.

**Projection stopped approximating YAML (G565).** A field report from the
sekiban-as-a-service design thread (2026-07-31, on v0.6.2) had
`intent-cli clarify open SKS-G837` reject an existing, otherwise-valid packet
with "Projection packet YAML contains invalid section header" — because the
title contained an em-dash and long punctuation. The reporter's diagnosis was
exact: the two parsers disagreed, and the packet surfaces were the ones reading
real YAML.

The defect was never that title. `ProjectionPacketSerializer` hand-rolled a line
reader, so **every** legal YAML construct it failed to anticipate became a
projection-only failure — one earlier slice had already patched block-sequence
indentation, another required-section rejection, and it would have continued one
construct at a time. Projection now reads `packet.yaml` through the same YAML
implementation `packet draft`, `clarify open` and the facet checks already use.
The projection *contract* is unchanged: the same required sections and fields,
the same validation order, the same messages. Only "what is valid YAML" moved,
and it moved to the answer the rest of the toolchain already gave. **The
reported failure is fixed permanently, as promised to the reporting team.**

**Queue-seed joined them (G567).** `automation queue-seed-from-packet`
classified and seeded from a regex scalar reader that never parsed the document,
so a packet the schema and projection surfaces both rejected could still
classify `queue-seed-ready` and put a malformed unit into the queue — where it
failed later at publish or preflight, far from its cause. It now validates
through the same whole-document parse, and malformed YAML **fails closed in
dry-run and `--write` alike**: the parse error is named, the exit is non-zero,
and no `queue-state.json` or `runs.jsonl` mutation is planned or applied.

### Dependencies are seeded faithfully (G568)

Queue seeding recorded a flow sequence (`dependencies: [G1, G2]`) as raw
bracket text and a **block** sequence (`- G1`) as nothing at all. That is not
cosmetic: dependency-aware selection reads the seeded list, so a dropped
dependency made a dependent unit look publish-ready while its root was still
open — exactly the failure the ordering rules exist to prevent, happening
silently at the surface furthest upstream.

Both styles now produce the same structured list, and ordering actually gates on
declared roots again. For items already seeded by the lossy path — and because
hand-editing `queue-state.json` is forbidden — there is a canonical repair:

```bash
intent-cli automation queue-dependency-reconcile                        # diagnose, read-only
intent-cli automation queue-dependency-reconcile --execution-unit G540  # diagnose one
intent-cli automation queue-dependency-reconcile --write                # repair
```

It **re-derives from the packet rather than merging** (a merge would make the
queue a second source of truth no packet could contradict), is idempotent,
touches only the `dependencies` field, preserves the queue's no-item-loss
invariants, and fails closed on an unknown unit or an unreadable packet — "I
cannot read the declaration" must never become "the declaration is empty", which
would repair a dropped dependency into a confirmed absence. It is never run
automatically.

### CI evidence you can trust (G566, G569)

Neither slice changes what the CLI does, and both protect the same thing: the
exact-head "CI green" evidence the review and merge gates treat as canonical.
G566 is test-code only; G569 also adjusts a production seam, with runtime
behaviour deliberately unchanged (below).

**A random red is worse than a red (G569).** One full-suite run failed, two
reruns passed, an isolated run passed — the signature of an interleaving race.
`IssuePrepareCommand.TimestampFactory` was a process-global mutable static
assigned by two test classes that shared no non-parallel collection, so one test
could read another's clock. The static is gone: the clock is a per-call
parameter, and the production path passes `DateTimeOffset.UtcNow` — exactly what
the static defaulted to. A parameter cannot be raced. A suite-wide audit for the
same pattern found eleven more classes assigning shared statics without a
non-parallel collection; all are fixed, and every remaining multi-class-mutated
static is dispositioned.

**The roll simulation could clobber its own fixture (G566).** The G560
roll-simulation helper rewrote the readiness heading first and applied a
plain-version substitution last, so whenever the live `stableVersion` equalled a
fixture's `nextVersion` the last pass rewrote the heading it had just written.
The 0.7.1 roll was the first to collide with it. The heading is now written into
a slot no substitution can reach, and the collision case is a fixture, so the
class of defect is closed rather than the instance.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.1
```

Or download the self-contained binary from the
[v0.7.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.7.1).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.7.0

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.7.1
```

This release is **corrective**. No command, argument, or flag was removed or
renamed, and no packet schema changed.

**Corrective behavior changes — things that used to pass silently now fail
closed.** These are the only changes that can alter an existing run's outcome,
and in every case the new outcome is the one the other surfaces already gave:

- **A malformed `packet.yaml` no longer seeds the queue.** `queue-seed-from-packet`
  previously classified some unparseable packets as `queue-seed-ready`; it now
  reports the parse error and exits non-zero without mutating anything. If a
  seed that used to "succeed" now stops, the packet was already broken and the
  failure simply moved to where it can be read.
- **Projection accepts more, and refuses differently.** Packets that were
  rejected only by projection (em-dashes, quoted colons, comments at column 0,
  flow sequences, folded scalars) now parse. Genuinely malformed YAML is still
  refused — now reported as a parse failure rather than as a guess about section
  headers.
- **Block-style `dependencies` now reach the queue.** A unit whose dependencies
  were silently dropped may now be correctly withheld until its root completes.
  That is the intended gate doing its job; use
  `automation queue-dependency-reconcile` to bring already-seeded items in line.

**Internal changes with no observable difference for CLI users.** G566 is
test-code only. G569 is an internal and test-determinism **seam** change: it
touches production source (`IssuePrepareCommand`, and a doc comment in
`TaskingPublishReviewedBridgeCommand`) to replace a process-global clock with a
per-call one, and runtime behaviour is intentionally byte-identical because the
production path passes exactly what the removed static defaulted to. G568's
parser plumbing is byte-compatible for packets that already seeded correctly.

## Release-readiness gate (G572)

These items must hold **before the GitHub Release for `v0.7.1` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G565 (PR #1236), G566 (PR #1234), G567 (PR #1238), G568 (PR #1240), and
      G569 (PR #1242), plus the G572 release-prep (PR #1244). Confirm on
      the host/review side via the host queue-state / GitHub PR state — the
      child implementation loop must not read parent queue-state, so this is a
      host-owned precondition.
- [ ] **No DRAFT stub remains in either `release-notes-v0.7.1.md`.** An unfilled
      stub means release-prep has not run; this packet replaces both.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before
      publishing).
- [ ] `eng/version.json` shows `stableVersion` `0.7.0` and `nextVersion` `0.7.1`
      (the intended release version) — **unchanged by this packet**.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.
- [ ] **Post-merge build + pack evidence** on the merge commit is recorded in
      the PR (mirroring the G528/G538/G551/G554/G558/G562 readiness gate).

## Publishing v0.7.1

This packet does **not** publish the release and adds **no** publish steps. The
merge of these notes does **not** create a GitHub Release or tag on its own.

**This is a silent release**: no external promotion or announcement accompanies
it. That affects promotion only — the notes above are authored to the same
standard as any other release, and the publishing mechanics below are unchanged.

1. After this packet is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.7.1` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.1`)
      then `intent-cli --version` reports `0.7.1`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.7.1`.
- [ ] **Parser-unification smoke** (G565/G567): a packet whose `issue_title`
      contains an em-dash and a quoted `": "` is accepted by
      `intent-cli clarify open`, and `intent-cli automation
      queue-seed-from-packet` refuses a deliberately malformed packet with a
      named parse error and a non-zero exit.
- [ ] **Dependency-fidelity smoke** (G568): `intent-cli automation
      queue-dependency-reconcile --help` prints its usage.
- [ ] **ROLL `eng/version.json` NOW**, per the G554 rule as amended by G557 and
      completed by G560: `stableVersion → 0.7.1`, `nextVersion → 0.7.2`, in the
      **same commit as new DRAFT `release-notes-v0.7.2.md` stubs (EN/JA)**, with
      the **"Next release readiness" sections updated to the new line in both
      language mirrors**, and the roll counts as complete only after
      **child-main CI is confirmed green**. See
      [version flow](09-developer-reference.md#version-flow).
