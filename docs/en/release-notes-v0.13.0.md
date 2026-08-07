# Release Notes — intent-cli v0.13.0

> **Prepare-only / UNRELEASED.** This preparation changes version state and
> documentation only. It creates no GitHub Release, tag, package publish, or
> release workflow run; Release creation remains the operator's step.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.13.0`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.13.0.
See the [v0.12.1 notes](release-notes-v0.12.1.md) and the
[v0.12.0 notes](release-notes-v0.12.0.md) for the preceding scopes; those
notes are linked, not repeated here.

## Preview lane — read before the feature list

Every surface in v0.13.0 is `preview-through-1.x`: it is outside the
[1.0 compatibility promise](1.0-compatibility-promise.md), may change or be
withdrawn during the 1.x line, and is not a 1.0 compatibility commitment.
This is the first release after the freeze where that preview lane applies to
shipped surfaces.

## What's in v0.13.0

This minor release covers exactly five units: G636, G629, G630, G637, and
G638. The list was derived by running `git log v0.12.1..main`; the sixteen
commits in that range are all accounted for. The post-release roll is
`ca24b94`. The G636 sequence is `75e7a3a`, `8b4d0c1`, `d7cc4a5`, `844e3e7`,
`c58aa9b`, and merge `861f1978`; G629 is `140b520` and merge `98e8805`; G630
is `8ee090d` and merge `fa39c857`; G637 is `aa86ba8` and merge `26c2b465`;
G638 is `2bd744a`, `1d0f81e`, and merge `b45f675`.

- G636 — PR #1372, merge commit `861f1978` (resolves on `main`): the launch recipe records a post-start interaction, including the Copilot permission dialog whose default is the unbounded answer.
- G629 — PR #1374, merge commit `98e8805` (resolves on `main`): a dispatch becomes durable state, with a pending record and `notify status` returning live, settled, or lost.
- G630 — PR #1376, merge commit `fa39c857` (resolves on `main`): `notify supervise` stays silent while seats are healthy, verifies recovery and loss, and makes re-dispatch opt-in.
- G637 — PR #1378, merge commit `26c2b465` (resolves on `main`): the workspace layout convention and `guide workspace-layout` make the recorded topology reproducible.
- G638 — PR #1380, merge commit `b45f675` (resolves on `main`): `automation ci-wait`, the `ci-all-green-not-transitioned` stall class, and a recipient warning make exact-head waits durable.

## Why this is a minor release

The minor bump is verifiable rather than assumed. `notify status`, `notify
supervise`, `automation ci-wait`, and `guide workspace-layout` were absent at
v0.12.1 and first ship in this line. Every one of those surfaces remains in
the preview lane described above.

## What the new surfaces deliberately do not do

- `notify status` reads durable delegation state and never acts on a process.
- `notify supervise` stays silent on healthy seats and keeps re-dispatch off by
  default; it does not invent a recovery action from silence alone.
- `automation ci-wait` records an exact-head wait and starts no polling or
  background timer.
- `guide workspace-layout` prints the commands and conventions needed to
  reproduce the layout and executes none of them.

## Operational purpose and operator disclosure

On 2026-08-06 this team's loop stopped silently three times, with different
causes: a recipient process died mid-task, a CI run finished with nobody to
wake, and a completion report reached a sleeping seat. The five units make
those states visible, and the durable delegation, supervision, CI-wait, and
report paths make the relevant recovery states explicit; two of the observed
stalls are now recoverable through the recorded workflow.

G636 carries an authority disclosure that must not be inferred away: a seat
launched with a correct command line can still hold authority the operator
never granted, because the agent's startup dialog defaults to enabling all
permissions. The launch recipe now records the answer that preserves the
declared envelope.

## Release-readiness gate

Before creating the v0.13.0 Release, the operator must verify:

- `eng/version.json` records stable `0.12.1` and next `0.13.0`.
- PRs #1372, #1374, #1376, #1378, and #1380 resolve on `main` at merge
  commits `861f1978`, `98e8805`, `fa39c857`, `26c2b465`, and `b45f675`; the
  sixteen commits in `git log v0.12.1..main` remain fully accounted for.
- The bilingual release-notes guard and G634 count guard pass, followed by
  the full Release suite and exact-head CI.
- Prepare-only remains in force: do not create the GitHub Release, tag, or
  package publish until the operator explicitly approves it.

## Publishing v0.13.0

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does
not perform that step.
