# Release Notes — intent-cli v0.16.0

> **Prepare-only / UNRELEASED.** This preparation changes version state and
> documentation only. It creates no GitHub Release, tag, package publish, or
> release workflow run; Release creation remains the operator's step.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.16.0`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.16.0.
See the [v0.15.0 notes](release-notes-v0.15.0.md) and the
[v0.14.0 notes](release-notes-v0.14.0.md) for the preceding scopes; those
notes are linked, not repeated here.

## Preview lane — read before the feature list

The G647 per-kind launch-recipe registry and G648 registration-loss
corroboration are `preview-through-1.x`: they are outside the
[1.0 compatibility promise](1.0-compatibility-promise.md), may change or be
withdrawn during the 1.x line, and are not 1.0 compatibility commitments.

## What's in v0.16.0

This minor release covers exactly two merged units: **G647 and G648**. The
list was derived by running `git log v0.15.0..main`; all three commits in that
range are accounted for as the post-release roll `e3c2e432`, G647's merge
`532b01b9`, and G648's merge `e3200aeb`. Both merge commits are verified to
resolve on `main`.

- G647 — PR #1398, merge commit `532b01b9` (verified on `main`): the recorded
  seat kind is the human's current wish, a requested kind switch takes one
  step, and recovery never changes kind unattended. The per-kind registry
  surfaces the measured target launch recipe or an explicit absent notice;
  `topology update-kind` therefore cannot silently guess a command or model.
- G648 — PR #1400, merge commit `e3200aeb` (verified on `main`): liveness,
  supervision, and delivery distinguish `registration-lost-process-present`
  from genuine `lost`, return `resend_permitted: true` for the corroborated
  state, and emit at most one finding per recorded pane per cycle. No kill,
  restart, or automatic re-registration is performed.

## Why this is a minor release

G647 adds the per-kind recipe/update-kind surface and the measured Codex
recipe registry that were absent at v0.15.0. G648 adds the distinct
registration-loss/process-presence state and its delivery/supervision
corroboration. These are new preview surfaces, not patch-only corrections to
an existing v0.15.0 contract.

## Operator principle

The recorded seat kind is the human operator's current wish. A human-requested
switch moves one step and records the target recipe; if the target is unknown,
the operator is asked rather than given an invented default. Recovery never
switches a kind unattended.

## What was measured, and what remains guidance

The Codex recipe is a measured observation, not a universal claim. On
**MyIntentHost** on **2026-08-07**, Codex **v0.144.1 / macOS** was observed with
this bounded invocation:

```text
herdr agent start <logical-role> --kind codex --pane <pane-id> -- --sandbox workspace-write --ask-for-approval never --add-dir <role-work-root> [--add-dir <host-routing-root>]
```

The observed envelope fact was asymmetric: a write outside the declared root
was rejected while a read outside it was not. That is measured evidence for
this host and date, not a promise that every platform or version behaves the
same way. An unmeasured target kind remains explicitly absent; its flags and
post-start answers are never inferred.

## G648 incident and fail-closed boundary

During the G648 incident, a healthy orchestrator reported `lost` six times
while a herdr registration flickered. The old-process stop stayed held
**fail-closed**: process corroboration prevented a kill or restart, and no
automatic re-registration was attempted. Genuine absence remains `lost` only
when both the registration and the recorded-pane process are absent. A
registration loss with a foreground process is the named
`registration-lost-process-present` state, is reported once per pane per cycle,
and keeps `resend_permitted: true` so the operator can re-register the pane.

## Release-readiness gate

Before creating the v0.16.0 Release, the operator must verify:

- `eng/version.json` records stable `0.15.0` and next `0.16.0`.
- PR #1398 resolves on `main` at merge `532b01b9` and PR #1400 resolves on
  `main` at merge `e3200aeb`; the three commits in `git log v0.15.0..main`
  remain fully accounted for above.
- The preview statement appears before the feature description, and the
  [1.0 compatibility promise](1.0-compatibility-promise.md) remains linked
  rather than restated.
- The bilingual release-notes guard and count guard pass, followed by the full
  Release suite and exact-head CI. Verify measured attribution for Codex and
  the G648 fail-closed incident boundary before treating the line as ready.
- The [v0.15.0 notes](release-notes-v0.15.0.md) and
  [v0.14.0 notes](release-notes-v0.14.0.md) remain linked preceding scopes;
  this note does not restate them.
- Prepare-only remains in force: do not create the GitHub Release, tag, or
  package publish until the operator explicitly approves it.

## Publishing v0.16.0

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does
not perform that step.
