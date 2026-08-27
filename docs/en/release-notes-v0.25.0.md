# Release Notes — intent-cli v0.25.0

> **PREPARED / NOT PUBLISHED.** This is a prepare-only release-note set for
> the measured v0.25.0 line. No tag, GitHub Release, package publish, or
> post-release roll is performed by this unit.

No GitHub Release exists yet for v0.25.0; this file is preparation evidence only.
This prepare-only line is UNRELEASED: it has no tag, no publish, and no package release.
The matching install query is `JTechJapan.IntentSystem.Cli --version 0.25.0`.
No GitHub Release exists yet for v0.25.0; this file is preparation evidence only.

The previous 0.24.1 value was only a post-v0.24.0 placeholder. No v0.24.1
release was chosen or published. This preparation replaces that placeholder
with the measured next release line:

```json
{
  "stableVersion": "0.24.0",
  "nextVersion": "0.25.0"
}
```

The reason is auditable from the prepared functional head. A Release build of
5c4af5d88ddcfa47335bad4df56ad3e40dae9140 produced
intent-cli 0.24.1-5c4af5d-G741; the installed baseline produced
intent-cli 0.24.0-df472fe-G737. The prepared build adds two command options,
so the minor-version policy applies.

## Measured command-surface difference

The comparison used the installed CLI and the independently built exact
prepared head:

```bash
intent-cli --version
# intent-cli 0.24.0-df472fe-G737
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release --no-restore
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.24.1-5c4af5d-G741
```

The complete new surface supporting this minor bump is:

| command surface | installed 0.24.0 | prepared Release build |
| --- | --- | --- |
| session-layer topology record --model <text> | absent | present |
| session-layer topology record --reasoning-effort <text> | absent | present |
| notify supervise --delegation-execution-window-seconds <seconds> | absent | present; default 300 |

The built usage is:

```text
intent-cli session-layer topology record ... [--model <text>] [--reasoning-effort <text>]
intent-cli notify supervise ... [--delegation-execution-window-seconds <seconds>; default 300]
```

model and reasoning_effort are optional free-form operator declarations;
they are not an enumerated model list or a measurement. The supervision option
is a bounded execution window with the displayed default.

## What is in v0.25.0

The release inventory contains exactly three functional units. Each entry names
an outcome an operator can observe.

- G738 — PR #1609; merge commit f0a30f08de6281b34b6fd4a5e8732243ad176053.
  **Operator-observable outcome:** once a claim state has committed and pushed,
  teardown is best-effort and bounded. The committed claim cannot fail or hang
  during teardown, and Windows users no longer need to background the claim
  command to avoid waiting on cleanup.
- G739 — PR #1611; merge commit f0ea90fd3df65de3f1b95bd38f6f8c79b011d171.
  **Operator-observable outcome:** topology show and validate render the
  optional model and reasoning-effort declarations, including their absence.
  Who did this work is answered from the recorded topology; the values are
  operator declarations rather than measurements.
- G741 — PR #1614; merge commit 5c4af5d88ddcfa47335bad4df56ad3e40dae9140.
  **Operator-observable outcome:** supervision surfaces a delivered
  delegation that never observably starts as a finding only when delivery succeeded,
  the recipient is idle, the configured window elapsed, the
  canonical report is absent, the expected artifact is absent, and the durable
  target-entity transition is absent. Slow-but-started work is not a finding;
  the classifier observes and reports without prompting, restarting, or mutating
  a seat. Six motivating incidents informed this wording; no seat is named.

## First-parent range and release inventory

The exact prepared-head accounting was measured with:

```bash
git rev-list --first-parent --reverse v0.24.0..5c4af5d88ddcfa47335bad4df56ad3e40dae9140
git rev-list --first-parent --count v0.24.0..5c4af5d88ddcfa47335bad4df56ad3e40dae9140
# 4
```

The four first-parent commits are all accounted for below. The G740 row is a
classification row only: its version roll is not silently dropped and is not a
release unit.

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| f0a30f08de6281b34b6fd4a5e8732243ad176053 | G738 release unit; PR #1609 | included |
| f0ea90fd3df65de3f1b95bd38f6f8c79b011d171 | G739 release unit; PR #1611 | included |
| 8bcab9766412e3c946f3299274f969277135eb03 | G740 post-release version roll to the 0.24.1 placeholder; not a release unit | classified only |
| 5c4af5d88ddcfa47335bad4df56ad3e40dae9140 | G741 release unit; PR #1614 | included |

Therefore the release inventory is exactly G738, G739, and G741.

## Release-prep verification

The v0.24.0 shipped baseline remains represented by
intent-cli 0.24.0-df472fe-G737, and the tracked EN/JA
release-notes-v0.24.0.md files are untouched. The former v0.24.1 DRAFT stubs
are deleted in this preparation; the new EN/JA v0.25.0 notes are the only
unpublished line authored here.

Final focused documentation/version guards: 40 passed, 0 failed, 0 skipped (40 total).
Dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total).
Adjacent G739 topology + G741 supervision tests: 14 passed, 0 failed, 0 skipped (14 total).
Full Release suite: 5261 passed, 0 failed, 1 skipped (5262 total).
git diff --check: clean.
The installed G725 detector command `intent-cli automation stalled-work --domain intent-cli --repo J-Tech-Japan/intent-system --format json` ran from checkout commit 5c4af5d88ddcfa47335bad4df56ad3e40dae9140; origin/main was the same commit. It returned `stalled: true` with `version-roll-required` for released/expected stable 0.24.0 and expected next 0.24.1, so it was not a silent proof while this preparation is unmerged.
Host-duty request: orchestration must rerun that installed read-only detector after this PR is merged, from a synced main checkout at the final PR head, and record the silent result plus checkout commit. This child does not enter the host repository.
This unit changes only eng/version.json, release-note files, the EN/JA developer-reference readiness sections, and release-note/version tests.

## Prepare-only boundary

This preparation creates no tag, GitHub Release, package publish, workflow
change, credential action, post-release roll, or source runtime change. The
next post-release roll remains a separate operator-owned action after a real
release; no real release number is selected by the old 0.24.1 placeholder.
