# Release Notes — intent-cli v0.16.1

> **Prepare-only / UNRELEASED.** These notes replace the post-release
> placeholder with the operator-review content for this patch. This preparation creates
> no GitHub Release, tag, package publish, or version-state change; Release
> creation remains the operator's step.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.16.1`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.16.1.
See the [v0.16.0 notes](release-notes-v0.16.0.md) for the preceding release;
that content is linked, not repeated here.

## v0.16.1 patch scope

This patch release covers exactly one merged unit: **G650**. The list was
derived by running `git log v0.16.0..main`; the two commits in that range are
fully accounted for as the post-release roll `428eea70` and G650's merge
`53ee440e`. The G650 merge commit is verified to resolve on `main`.

- G650 — PR #1405, merge commit `53ee440e` (verified to resolve on `main`):
  the team-scoped `guide orchestrator-thread` renders again because its
  undeclared Setup-intake fragment is declared, and a guard renders every guide
  under every session-layer mode with and without `--team`, failing on any
  undeclared fragment.

## What v0.16.0 broke

On v0.16.0, `guide orchestrator-thread --domain <d> --team <t>` exited 1 with
an undeclared-fragment error while the same invocation without `--team`
rendered successfully. The failing shape is a herdr-only team's normal
invocation, so the setup sentences v0.16.0 announced (asking which CLI and
model each seat runs) were unreachable for the readers they were written for.

## What this patch preserves

- The fragment-typing rule that produced the error is correct and was not
  weakened: G650 declares the fragment rather than bypassing the rule.
- **Source presence is not reachability.** A guide is verified by rendering it
  on the shipped build, not merely by finding its source fragment.
- The guard renders every guide × every session-layer mode × with and without
  `--team`, and fails closed on any undeclared fragment.

This is a patch rather than a new command surface: **no command and no flag are
added**. The fix restores the guide that v0.16.0 could not render for the
configuration it was written for.

## Release-readiness gate

Before creating the v0.16.1 Release, the operator must verify:

- `eng/version.json` remains stable `0.16.0` / next `0.16.1`.
- The two commits in `git log v0.16.0..main` remain fully accounted for above,
  and PR #1405 resolves on `main` at merge `53ee440e`.
- The bilingual release-notes guard and count guard pass, followed by the full
  Release suite and exact-head CI. EN and JA remain in parity under the G613
  terminology policy.
- The [v0.16.0 notes](release-notes-v0.16.0.md) remain linked as the preceding
  scope; this patch note does not restate them.
- Prepare-only remains in force: do not create the GitHub Release, tag, or
  package publish until the operator explicitly approves it.

## Publishing v0.16.1

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does not
perform that step.
