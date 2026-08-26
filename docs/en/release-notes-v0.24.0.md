# Release Notes — intent-cli v0.24.0

> **PREPARED / NOT PUBLISHED.** This is a prepare-only release-note set for
> the line after v0.23.2. It records what is present on `main`; no v0.24.0 tag,
> GitHub Release, package publish, or post-release roll has happened.

Install verification for the line being prepared will use
`JTechJapan.IntentSystem.Cli --version 0.24.0` after the prepared line lands.

## Why this line is v0.24.0

The post-v0.23.2 roll left `nextVersion` at the patch placeholder `0.23.3`
before this line had any feature content. Release-prep retargets
`eng/version.json` to `0.24.0` because the Release build of the line exposes
two command surfaces that installed 0.23.2 does not have; no GitHub Release
exists yet for v0.24.0:

- `notify supervise shrink` — installed 0.23.2 returns `Unknown argument 'shrink'`.
- `session-layer topology record-host-state` — installed 0.23.2 returns
  `Unknown session-layer topology subcommand`.

The bump policy reserves a minor version for new command surfaces. This is the
settled measured decision, not a re-derived version guess.

## What is in v0.24.0

The exact inventory below contains six release units from the first-parent
range after v0.23.2. Each entry is written from the merged commit and names an
outcome an operator can observe.

- G731 — PR #1589; merge commit `d168fac3cbef482879aa9521f6478e7d3a8dc6d1`.
  **Operator-visible outcome:** a sender-local report is recovered by the
  observed delivery result: a successful external append is accepted even
  when report and routing roots differ, while an actual write failure remains
  undelivered and exposes the named `notify collect` delivery-level recovery
  path. The implementation seat does not retry a host write or widen its root.
- G732 — PR #1591; merge commit `37068fa076ccf9eed5f1f87f92075756f4b5abf7`.
  **Operator-visible outcome:** the v0.23.0 notes now say which artifacts
  shipped and preserve the npm publication gap: GitHub Release, NuGet, and
  self-contained binaries are available, but the npm leg never reached the
  registry.
- G733 — PR #1595; merge commit `0bb78b85df6467a1ebadb5c9d35e4a5ffb4c9072`.
  **Operator-visible outcome:** an implementation seat can take a GitHub issue
  to a pushed PR without a host round trip. The child owns its child-repository
  Git and PR path, while an exact host-state duty is sent through the canonical
  message channel rather than being silently delegated or locally improvised.
- G734 — PR #1598; merge commit `4aea6b5ef24cf86d8ef6cc2aba88b5ecf02d4e65`.
  **Operator-visible outcome:** a running supervisor can safely shrink existing
  supervision state, preserve readable evidence and audit accounting, and
  append the next cycle after the atomic replacement. `cycles.jsonl` shares the
  safety boundary; provider-run logs remain out of scope.
- G735 — PR #1599; merge commit `2d77c557e7e7871fac70d17906c18b0c4416f185`.
  **Operator-visible outcome:** roles sharing one old workspace/pane travel
  together when that old pane maps to one new pane. Distinct old panes
  converging on one new pane remain an explicit ambiguity and are refused, and
  the topology record refusal names the sanctioned whole-team move path.
- G736 — PR #1600; merge commit `a7d10026a9a4dd2693f464a5c5e34ce134b2c661`.
  **Operator-visible outcome:** before the first publish attempt, topology
  validation reports missing host-state capacity for a legacy or all-sandboxed
  team. A recorded host-state role and envelope makes the route discoverable,
  but declaring a role does not create a capable participant; the team still
  needs an actually capable host-state seat.

## First-parent range accounting

The measurement command was:

```bash
git log --first-parent v0.23.2..main
git rev-list --first-parent --count v0.23.2..main  # seven commits
```

The seven first-parent commits are accounted for below. G730 is deliberately
not a release unit: it is the post-release version roll that guessed the next
patch before this line existed.

| first-parent commit | classification | PR |
| --- | --- | --- |
| `3debf8ee2f571612f969e18ac46898de1057457f` | G730 post-release version roll; not a release unit | #1584 |
| `d168fac3cbef482879aa9521f6478e7d3a8dc6d1` | G731 release unit | #1589 |
| `37068fa076ccf9eed5f1f87f92075756f4b5abf7` | G732 release unit | #1591 |
| `0bb78b85df6467a1ebadb5c9d35e4a5ffb4c9072` | G733 release unit | #1595 |
| `4aea6b5ef24cf86d8ef6cc2aba88b5ecf02d4e65` | G734 release unit | #1598 |
| `2d77c557e7e7871fac70d17906c18b0c4416f185` | G735 release unit | #1599 |
| `a7d10026a9a4dd2693f464a5c5e34ce134b2c661` | G736 release unit | #1600 |

## Prepared functional head and identity evidence

G737 is outside its own prepared functional head. The six functional units
were measured at exact prepared functional head
`a7d10026a9a4dd2693f464a5c5e34ce134b2c661`, before this release-prep
documentation/version unit changed the policy. The Release build from that
revision produced:

```bash
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.23.3-a7d1002-G734
```

- **Release identity evidence source revision:**
  `a7d10026a9a4dd2693f464a5c5e34ce134b2c661`
- **Display identity from that Release build:**
  `intent-cli 0.23.3-a7d1002-G734`
- **Why the identity says 0.23.3:** the pre-G737 functional head still carries
  the rolled placeholder. This release-prep unit retargets the policy to
  0.24.0; the eventual v0.24.0 tag belongs after this documentation commit
  lands, not at the earlier functional head.

## Release-prep verification

The final verification commands and their measured counts are recorded here:

```text
Targeted release-prep guards: 164 passed, 0 failed, 0 skipped, total 164.
Full Release suite: 5232 passed, 0 failed, 1 skipped, total 5233.
```

The targeted guards cover the v0.24.0 inventory, version-source policy,
package metadata, and both developer-reference mirrors. The full Release suite
is the Release configuration of the CLI test project. `git diff --check` is
also required.

## Prepare-only boundary

This PR changes only `eng/version.json`, the EN/JA v0.24.0 release notes, the
EN/JA developer-reference readiness section, and release-note/version tests.
It deletes the superseded v0.23.3 draft stubs. It does not change source
runtime behavior, v0.23.x shipped note files, tags, GitHub Releases, packages,
credentials, workflows, or the post-release version roll. No tag, no GitHub
Release, no publish, and no post-release roll are part of this evidence.
