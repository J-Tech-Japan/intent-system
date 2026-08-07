# Release Notes — intent-cli v0.14.0

> **Prepare-only / UNRELEASED.** This preparation changes version state and
> documentation only. It creates no GitHub Release, tag, package publish, or
> release workflow run; Release creation remains the operator's step.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.14.0`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.14.0.
See the [v0.13.1 notes](release-notes-v0.13.1.md) and the
[v0.13.0 notes](release-notes-v0.13.0.md) for the preceding scopes; those
notes are linked, not repeated here.

## Preview lane — read before the feature list

The v0.14.0 measured recovery supervision surface is
`preview-through-1.x`: it is outside the [1.0 compatibility promise](1.0-compatibility-promise.md),
may change or be withdrawn during the 1.x line, and is not a 1.0 compatibility
commitment.

## What's in v0.14.0

This minor release covers exactly one merged unit: **G641**. The list was
derived by running `git log v0.13.1..main`; all five commits in that range are
accounted for as the post-release roll `e08a6a7`, G641's implementation
`3330ec0`, blocker repair `de0a142`, cadence/headroom repair `5cc8a2e`, and
the merge `7524c305`. G641 is PR #1388, merge commit `7524c305` (verified to
resolve on `main`).

- G641 — PR #1388, merge commit `7524c305`: one measured supervision pass over
  every known stall class, a declared detection bound that the loop checks
  itself against, and a per-stall record of when a condition became
  detectable, was surfaced, and was cleared.

## Why this is a minor release

The minor bump is verifiable rather than assumed. `notify supervise` gains
declared-bound and duration-record behaviour together with new options; this
is added surface on an existing command, not only a fix to existing behaviour.

## What the release addresses

On 2026-08-06 and 07 this team's loop stopped silently four times, with four
distinct causes: a recipient dying mid-task, checks finishing with nobody to
wake, a completion reaching a sleeping seat, and an escalation recorded
durably that woke no one. The measured worst gap was about two hours fifteen
minutes. G641 makes the detection interval a declared, self-checked property
and makes each stall's duration a number that can be read back.

## Honesty and boundaries

- An unknown start is recorded as unknown rather than being started at first
  observation; an unknown start never receives an invented duration.
- The supervisor reports its own absence since the last recorded cycle rather
  than presenting a missing cycle as healthy.
- The loop wakes the owning role through its recorded transport and records
  recovery evidence; it never takes an owed transition and hard-codes no agent
  kind.

Bounded recovery time is what makes supporting more agent kinds practical: a
wedged Copilot or other seat can cost minutes without silently becoming an
unbounded human investigation.

## Release-readiness gate

Before creating the v0.14.0 Release, the operator must verify:

- `eng/version.json` records stable `0.13.1` and next `0.14.0`.
- PR #1388 resolves on `main` at merge commit `7524c305`, and the five commits
  in `git log v0.13.1..main` remain fully accounted for as the three G641
  commits, its merge, and the post-release roll.
- The bilingual release-notes guard and count guard pass, followed by the full
  Release suite and exact-head CI.
- The [v0.13.1 notes](release-notes-v0.13.1.md) and
  [v0.13.0 notes](release-notes-v0.13.0.md) remain the linked preceding scopes;
  this note does not restate them.
- Prepare-only remains in force: do not create the GitHub Release, tag, or
  package publish until the operator explicitly approves it.

## Publishing v0.14.0

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does
not perform that step.
