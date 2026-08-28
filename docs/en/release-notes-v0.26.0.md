# Release Notes — intent-cli v0.26.0

> **PREPARED / NOT PUBLISHED.** This is a prepare-only release-note set for
> the measured v0.26.0 line. No tag, GitHub Release, package publish, or
> post-release roll is performed by this unit.

No GitHub Release exists yet for v0.26.0; this file is preparation evidence only.
This prepare-only line is UNRELEASED: it has no tag, no publish, and no package release.
The matching install query is `JTechJapan.IntentSystem.Cli --version 0.26.0`.

The former 0.25.1 value was only a post-v0.25.0 placeholder. No v0.25.1
release was chosen or published. This preparation replaces that placeholder
with the measured next release line:

```json
{
  "stableVersion": "0.25.0",
  "nextVersion": "0.26.0"
}
```

## Measured command-surface difference

This bump is based on an independent Release build of the exact prepared
head, not on an inference from `eng/version.json`:

```bash
intent-cli --version
# intent-cli 0.25.0-74a1c72-G741
dotnet build IntentSystem.sln --configuration Release --no-restore
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.25.1-a49ad93-G748
```

The installed CLI is `intent-cli 0.25.0-74a1c72-G741`. The Release build of
prepared head `a49ad93c36bd93d1ccc9317622d36fa01ea346b8` is
`intent-cli 0.25.1-a49ad93-G748`.
After the exact metadata-only version update to the stated policy, the same
Release build reports `intent-cli 0.26.0-a49ad93-G748`; this is the final
prepared identity, not the evidence used to infer the minor bump.

The measured new command surface is:

| command surface | installed 0.25.0 | prepared Release build |
| --- | --- | --- |
| `notify supervise archive --domain <d> --team <t> [--live-window-days <days>] [--dry-run\|--write] [--format markdown\|json]` | absent (`Unknown argument 'archive'.`) | present |

The built usage is:

```text
Usage: intent-cli notify supervise archive --domain <d> --team <t> [--live-window-days <days>] [--dry-run|--write] [--format markdown|json]
```

The `automation`, `claim`, and `worker` help surfaces are byte-identical
between the installed and prepared builds. The `notify` difference is only
the new `notify supervise archive` surface; no other addition was found.
The `state-doctor` and `closeout-drift-check` usages are byte-identical.
This one measured operator surface justifies the minor bump; the version file
was not used as the reason for choosing it.

## What is in v0.26.0

The release inventory contains exactly five functional units. Each entry names
an outcome an operator can observe. G743 and G747 finish and repair the
claim-transaction contract shipped in v0.25.0; they are recorded honestly as
that completion rather than as an invented new contract. G748 repairs the
G741 detector, which fired zero times across sixteen qualifying incidents
before this repair.

- G743 — PR #1620; merge commit `1ad68963b65a1fe4978d3a0e83d0812842a2de29`.
  **Operator-observable outcome:** a real pre-commit failure remains the
  primary result and cleanup evidence is separate; after claim state commits,
  a pushed claim is reported successful even if bounded cleanup warns. The
  committed transaction boundary from v0.25.0 is now directly usable by an
  operator.
- G744 — PR #1621; merge commit `0e97529c64294677b41e49cd87a40920c1dd3d4e`.
  **Operator-observable outcome:** a configurable recent live window keeps the
  small cycles file bounded while older records move to period-addressable
  archives. The existing history reader consumes archive and live records,
  and a live-safe move does not discard or duplicate a concurrent record.
- G746 — PR #1626; merge commit `d112dd957826864124d4b8f0d8c1940d4145e1fe`.
  **Operator-observable outcome:** duplicate execution-unit queue rows are
  reported by closeout and state-doctor instead of crashing. Only strictly
  more-informative duplicates are repaired; ambiguous entries stop safely with
  their competing information visible. **Consumer report #1622:** duplicate
  `execution_unit` rows made `closeout-drift-check` crash with a duplicate-key
  failure. Canonical commands could not recover that state, so the reporter
  had to hand-edit `.intent-cli/queue-state.json` to unblock. The new canonical
  finding/repair replaces that manual recovery.
- G747 — PR #1627; merge commit `7e7d16e4639f22530843b19f065b5a101cf1b0b4`.
  **Operator-observable outcome:** claim transactions preserve the actual
  pre-commit cause, target the remote default branch resolved from metadata,
  and keep JSON stdout parseable while cleanup warnings remain observable.
  This repairs the remaining v0.25.0 claim-transaction contract without
  changing its commit boundary, cleanup bound, retry count, or retry timing.
- G748 — PR #1629; merge commit `a49ad93c36bd93d1ccc9317622d36fa01ea346b8`.
  **Operator-observable outcome:** the G741 supervision finding recognizes the
  documented closed recipient-state set `{idle, done}` when delivery
  succeeded, the configured window elapsed, and report, artifact, and durable
  target transition are all absent. This repairs the G741 detector that fired
  zero times across sixteen qualifying incidents; blocked and unknown remain
  excluded so an impeded or unobservable seat is not misclassified.

## First-parent range and release inventory

The exact prepared-head accounting was measured with:

```bash
git rev-list --first-parent --reverse v0.25.0..a49ad93c36bd93d1ccc9317622d36fa01ea346b8
git rev-list --first-parent --count v0.25.0..a49ad93c36bd93d1ccc9317622d36fa01ea346b8
# 6
```

All six first-parent commits are accounted for below. The G745 row is a
classification row only: its post-v0.25.0 version roll is not silently
dropped and is not a release unit.

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `1ad68963b65a1fe4978d3a0e83d0812842a2de29` | G743 release unit; PR #1620 | included |
| `0e97529c64294677b41e49cd87a40920c1dd3d4e` | G744 release unit; PR #1621 | included |
| `b8f249e965cad2c3c2e19dda9dd99e726324485d` | G745 post-v0.25.0 version roll; not a release unit | classified only |
| `d112dd957826864124d4b8f0d8c1940d4145e1fe` | G746 release unit; PR #1626 | included |
| `7e7d16e4639f22530843b19f065b5a101cf1b0b4` | G747 release unit; PR #1627 | included |
| `a49ad93c36bd93d1ccc9317622d36fa01ea346b8` | G748 release unit; PR #1629 | included |

Therefore the release inventory is exactly G743, G744, G746, G747, and G748.
The G745 post-v0.25.0 roll is classified in the table and is not counted as a
release unit.

## Release-prep verification

The tracked EN and JA v0.25.0 shipped-note files are untouched. The former
v0.25.1 DRAFT stubs are deleted in this preparation; these v0.26.0 notes are
the only unpublished line authored here.

Targeted release-prep docs/version guards: 40 passed, 0 failed, 0 skipped (40 total).
Dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total).
Adjacent release/readiness suite: 59 passed, 0 failed, 0 skipped (59 total).
Full Release suite: 5305 passed, 0 failed, 1 skipped (5306 total).
git diff --check: clean. No tag, GitHub Release, package publish, or post-release roll is performed by this unit.

## Prepare-only boundary

This preparation changes only `eng/version.json`, release-note files, the EN/JA
developer-reference readiness sections, and release-note/version tests. It
does not change source runtime behavior, tags, Releases, publishing, or the
shipped v0.25.0 note files. The next post-release roll remains a separate
operator-owned action; no real release number is selected by this preparation.
