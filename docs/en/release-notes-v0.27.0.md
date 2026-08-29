# Release Notes — intent-cli v0.27.0

> **PREPARED / NOT PUBLISHED.** This is a prepare-only release-note set for
> the measured v0.27.0 line. No tag, GitHub Release, package publish, or
> post-release roll is performed by this unit.

No GitHub Release exists yet for v0.27.0; this file is preparation evidence only.
This prepare-only line is UNRELEASED: it has no tag, no publish, and no package release.
The matching install query is `JTechJapan.IntentSystem.Cli --version 0.27.0`.

The former v0.26.1 value was only a post-v0.26.0 roll placeholder. No v0.26.1
release was chosen or published. This preparation replaces that placeholder
with the measured next release line:

```json
{
  "stableVersion": "0.26.0",
  "nextVersion": "0.27.0"
}
```

## Measured command-surface difference

This minor bump is based on an independent Release build of the exact prepared
functional head, not on an inference from `eng/version.json`:

```bash
intent-cli --version
# intent-cli 0.26.0-93f07f8-G749
dotnet build IntentSystem.sln --configuration Release --no-restore
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.26.0-bb97548-G751
```

The installed CLI is `intent-cli 0.26.0-93f07f8-G749`. The clean Release build
of prepared functional head
`bb9754859ac8055adbd504f294145b7494668c1a` is
`intent-cli 0.26.0-bb97548-G751`. The identity was generated from that
revision.

A programmatic sweep invoked help for all 32 command groups and every direct
subcommand. It counted 32 group descriptors plus 71 direct-help usage lines in
the installed CLI, and 32 plus 72 in the prepared Release build:

| command-surface measurement | installed 0.26.0 | prepared Release build |
| --- | ---: | ---: |
| total usages | 103 | 104 |
| `notify supervise repair-cycle-history` | absent (`invalid-notification: Unknown argument 'repair-cycle-history'.`) | present |

The prepared usage is:

```text
Usage: intent-cli notify supervise repair-cycle-history --domain <d> --team <t> [--dry-run|--write] [--format markdown|json]
```

There is exactly one addition, `notify supervise repair-cycle-history`, and no
removal. The `automation`, `claim`, and `worker` help surfaces are
byte-identical between the installed CLI and the prepared build; the
`state-doctor` and `closeout-drift-check` usages are also byte-identical.
This measured new operator surface, rather than the version file, is the
auditable reason for the minor bump.

## What is in v0.27.0

The release inventory contains exactly two functional units. Each entry names
an outcome an operator can observe.

- G750 — PR #1634; merge commit
  `b525191a24e361419b03f77e15e659110a22c395`.
  **Operator-observable outcome:** supervision cycle history is no longer
  carried in git, so a host whose shared state was blocked by a 100MB
  cycle-history file can push again. A host that already tracks the file has
  the supported `notify supervise repair-cycle-history` migration; it preserves
  the file and does not delete it.
- G751 — PR #1635; merge commit
  `bb9754859ac8055adbd504f294145b7494668c1a`.
  **Operator-observable outcome:** a successful event-mode wait with no
  observation no longer creates a durable cycle record, while genuine
  observations and interval safety-floor records remain durable. The running
  supervisor therefore settles at the declared one-record-per-interval rate
  instead of writing an event-wait record for every empty wait.

## First-parent range and release inventory

The exact prepared-head accounting was measured with:

```bash
git rev-list --first-parent --reverse v0.26.0..086344540d70a052555502971fa968aff6a252ac
git rev-list --first-parent --count v0.26.0..086344540d70a052555502971fa968aff6a252ac
# 3
```

All three first-parent commits are accounted for below. The G752 row is a
classification row only: its post-v0.26.0 version roll is not a release unit.

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `b525191a24e361419b03f77e15e659110a22c395` | G750 release unit; PR #1634 | included |
| `bb9754859ac8055adbd504f294145b7494668c1a` | G751 release unit; PR #1635 | included |
| `086344540d70a052555502971fa968aff6a252ac` | G752 post-v0.26.0 version roll to the 0.26.1 placeholder; not a release unit | classified only |

Therefore the release inventory is exactly G750 and G751. The G752 roll remains
in the classification table and is not silently dropped or counted as a unit.

## The honest three-unit supervision chain

The v0.26.0 G744 entry described a bound, not a reduction in write volume. It
bounded the live file, but it did not reduce how much the supervisor wrote. An
operator who upgraded to v0.26.0 expecting history growth to stop did not get
that outcome until G751 in this release. These are one problem across two
releases:

- G744 bounded live history.
- G750 removed runtime-local cycle history from git and provided a
  non-deleting migration for hosts that already tracked it.
- G751 reduced the write rate by making a successful no-observation event wait
  non-durable while preserving genuine observations and the interval floor.

The measured values are attributed measurements, not adjectives. G750 recorded
`cycles.jsonl` at **111.5MB**, at GitHub's **100MB** tracking limit. G751's
running-supervisor measurement recorded **3.6 records/second before** the
change and **12.00/hour after** it. The first number explains the git blockage;
the latter two describe the before/after durable-record rate.

## Prepare-only verification

Exact verification counts are:
- Focused release/doc/version guards: 14 passed, 0 failed, 0 skipped (14 total).
- Adjacent release/readiness guards: 51 passed, 0 failed, 0 skipped (51 total).
- Dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total).
- Full Release suite: 5332 passed, 0 failed, 1 skipped (5333 total).
`git diff --check` is clean. The tracked EN and JA v0.26.0 shipped-note files
remain byte-identical. No tag, GitHub Release, package publish, post-release
roll, or source runtime change belongs to this preparation.
