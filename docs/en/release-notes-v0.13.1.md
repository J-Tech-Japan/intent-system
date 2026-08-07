# Release Notes — intent-cli v0.13.1

> **Prepare-only / UNRELEASED.** These notes replace the post-release stub
> with the v0.13.1 preparation. This PR creates no GitHub Release, tag,
> package publish, or release workflow run; Release creation remains the
> operator's step.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.13.1`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.13.1.
See the [v0.13.0 notes](release-notes-v0.13.0.md) for the preceding release;
that content is linked, not repeated here.

## What's in v0.13.1

This patch release covers exactly one merged unit: **G640**. The list was
derived by running `git log v0.13.0..main`; all four commits in that range are
accounted for as G640's two implementation commits `a69430f` and `3d9b793`,
its merge commit `b206075`, and the post-release version roll `9d1705d`.

- G640 — PR #1384, merge commit `b206075` (resolves on `main`): a report whose
  task id matches no open pending delegation is delivered with an advisory
  instead of being refused.

## What v0.13.0 broke

In v0.13.0, a report whose task id did not match an open pending delegation was
refused with `delivered: false` and `cause: unknown-task-id`. The failure looked
like silence to the person waiting for the report rather than a visible error.
A role that answers escalations rather than delegations—on this host, the
design thread—never holds a pending record, so its reporting channel was closed
entirely. The same refusal also lost unsolicited reports, corrections, and
out-of-band answers from any role: precisely the messages carrying news the
recipient did not know to request.

## What is preserved

- An unmatched report is delivered with an advisory, but it creates or resolves
  no pending record.
- The refusal for two conflicting identifiers describing the same work still
  fires.
- A matching task id still resolves its pending record exactly as before.

This is a patch rather than a new command surface: **no command and no flag are
added**. The change narrows one existing report-path refusal while keeping the
state-mutation protections intact.

## Upgrade advice

Anyone running v0.13.0 should upgrade to v0.13.1. Until upgrading, send blocked
reports through the existing `intent-cli notify escalate` path so they reach
the design boundary instead of relying on the refused report path.

## Release-readiness gate

Before creating the v0.13.1 Release, the operator must verify:

- `eng/version.json` remains stable `0.13.0` / next `0.13.1`.
- PR #1384 resolves on `main` at merge commit `b206075`, and the four commits
  in `git log v0.13.0..main` remain fully accounted for.
- The bilingual release-notes guard and count guard pass, followed by the full
  Release suite and exact-head CI.
- Prepare-only remains in force: do not create the GitHub Release, tag, or
  package publish until the operator explicitly approves it.

## Publishing v0.13.1

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does not
perform that step.
