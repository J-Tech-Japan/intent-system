# Release Notes — intent-cli v0.17.0

> **Prepare-only / UNRELEASED.** This preparation changes version state,
> release notes, and readiness documentation only. It creates no GitHub
> Release, tag, package publish, or release workflow run; Release creation
> remains the operator's step.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.17.0`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.17.0.
See the [v0.16.1 notes](release-notes-v0.16.1.md) and the
[v0.16.0 notes](release-notes-v0.16.0.md) for the preceding scopes; those
notes are linked, not repeated here.

## Preview lane — read before the feature description

The surfaces described below are `preview-through-1.x`: they are outside the
[1.0 compatibility promise](1.0-compatibility-promise.md), may change or be
withdrawn during the 1.x line, and are not 1.0 compatibility commitments.

## Operating contract for supervised teams

Three teams ran Codex as the design seat and failed in the same week, not on
model capability but on an unwritten operating contract. The three field
reports and the 45-unit remote-herdr report are the measured basis for this
release. G654 writes that contract; the surrounding units give the watcher
durable activity and delivery evidence, an OS-owned lifetime, an escalation
ladder, and a recency signal for coherence work.

The organizing formula is: **each team has four judgment-bearing threads —
design, orchestration, implementation, and review — plus one supervision
process**. The supervision process is watcher infrastructure. It observes,
records, and wakes the authorized thread; it does not own judgment or recovery.

## What's in v0.17.0

This minor release covers exactly eleven merged units, in contract order. The
list was derived by running `git log v0.16.1..main`; all twenty commits in that
range are accounted for below, and every named merge resolves on `main`.

- G656 — PR #1410, merge commit `853b48ab` (verified on `main`): JSON guide
  fragments are explicitly declared, and the rendering guard covers every
  reachable missing-count headline in JSON and Markdown.
- G652 — PR #1412, merge commit `542133f7` (verified on `main`): `notify status`
  and supervision use durable activity sequence/time evidence to distinguish
  `working` from `live-idle`; a bound below interval is warned, not rewritten.
- G653 — PR #1414, merge commit `83c5feea` (verified on `main`): reports persist
  to a generation-aware outbox before transport. An undelivered report is
  recovered by `notify collect`, never by re-delegating the task.
- G655 — PR #1416, merge commit `c06e16d3` (verified on `main`): orchestration
  prepares workspace prerequisites before delegation and documents the
  prepare-and-resume path for permission failures without making intent-cli run git.
- G654 — PR #1418, merge commit `eae66f05` (verified on `main`): the
  agent-kind-neutral design-thread guide defines four-outcome wakes, provenance
  vocabulary, transaction-scoped approval, merge-authority comparison, and
  monitoring separation.
- G657 — PR #1420, merge commit `7ab3e297` (verified on `main`): the escalation
  ladder gains the owner-role subject fallback, settled-red CI findings, and a
  declared-label green fallback while preserving single-rung wakes.
- G658 — PR #1422, merge commit `39d7cf42` (verified on `main`):
  `notify supervise install` emits per-team launchd, Task Scheduler, or systemd
  artifacts and their exact management commands. Install emits and never registers.
- G659 — PR #1424, merge commit `5331ec11` (verified on `main`): opt-in event
  mode holds recorded `herdr agent wait` calls in the one supervisor process,
  re-arms failed waits, and wakes within seconds. Event mode keeps the interval floor.
- G660 — PR #1426, merge commit `bdc5b5b1` (verified on `main`): one
  residency-resolved delivery judgment is shared by status, escalate, and
  supervise, so durable external-reader append and pane wake retain distinct bases.
- G661 — PR #1428, merge commit `b06dac5d` (verified on `main`): five field
  frictions are corrected across writeback commit clarity, retire reactivation,
  placeholder exclusion, reachability scaffolding, and multi-checkout host defaults.
- G662 — PR #1430, merge commit `f2e53c03` (verified on `main`): improve runs
  become durable records, `guide next` uses realignment recency, and facet-check
  states honestly that `no_facet_data: true` means the lexical check did not run.

## Twenty-commit derivation

The range contains the post-release roll, three squash merges, and eight
direct-commit/merge pairs. This enumeration accounts for every commit returned
by `git log v0.16.1..main`:

| account | commits |
|---|---|
| post-release roll | `f3165a5c` |
| G656 | `853b48ab` |
| G652 | `542133f7` |
| G653 | `83c5feea` |
| G655 | `f6f2b6f0`, `c06e16d3` |
| G654 | `d1ec27d8`, `eae66f05` |
| G657 | `970eb671`, `7ab3e297` |
| G658 | `f9b5ff96`, `39d7cf42` |
| G659 | `30931bd2`, `5331ec11` |
| G660 | `99f5f2b2`, `bdc5b5b1` |
| G661 | `28a68cd0`, `b06dac5d` |
| G662 | `234a7058`, `f2e53c03` |

## Deliberate boundaries

- `notify supervise install` emits an artifact and exact commands; it never
  registers, unregisters, starts, or stops the OS scheduler.
- Event mode adds seconds-scale evidence to the same supervisor process; the
  independent interval cycle remains the safety floor.
- Realignment-window recency recommends a paste-ready improve action; it never
  schedules, runs, or grades realignment work.
- The escalation ladder may wake design when owner-role is itself the subject;
  design receives one escalation-class wake and gains no recovery authority.

## Why this is a minor release

`supervise install`, event mode, `packet retire --reactivate`, the design-thread
guide surface, and the durable improve-run record were all absent at v0.16.1.
They are new preview surfaces rather than patch-only corrections to an existing
v0.16.1 contract.

## Release-readiness gate

Before creating the v0.17.0 Release, the operator must verify:

- `eng/version.json` records stable `0.16.1` and next `0.17.0`.
- Every one of the eleven PR merges above resolves on `main`, and all twenty
  commits in `git log v0.16.1..main` remain accounted for by the table.
- The preview statement precedes the feature description and links the
  [1.0 compatibility promise](1.0-compatibility-promise.md).
- The bilingual release-notes guard and count guard pass, followed by the full
  Release suite, `git diff --check`, and exact-head CI.
- The [v0.16.1 notes](release-notes-v0.16.1.md) and
  [v0.16.0 notes](release-notes-v0.16.0.md) remain links to preceding scopes;
  this note does not restate them.
- Prepare-only remains in force: do not create a GitHub Release, tag, or package
  publish until the operator explicitly performs that separate step.

## Publishing v0.17.0

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does not
perform that step.
