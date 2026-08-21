# Release Notes — intent-cli v0.23.2

> **PREPARED / NOT PUBLISHED.** This is a prepare-only release-note set for
> the line opened after v0.23.1. It is substantive release documentation, but
> it is not evidence that a tag, GitHub Release, or package has been published.

Install verification for the prepared functional line: `JTechJapan.IntentSystem.Cli --version` reported
`intent-cli 0.23.2-2caa6d4-G728` from the Release build named below.

## What is in v0.23.2

The published v0.23.1 tag is `d49984dae761d589b2568f8eb1677ce3ff2facbc7`.
The exact inventory below contains the six execution units that are
unreleased since that tag. Each entry names its merge commit and the result an
operator can observe.

- G723 — PR #1571; merge commit `0252948e631194087a2cdacc7605f6023d8d0213`.
  **Operator-visible fix:** a topology recorded with coordinating role
  `orchestrator` reaches heartbeat through role-alias resolution, while a
  genuinely missing seat produces an actionable finding.
- G724 — PR #1572; merge commit `771d5e9d147997cf184e5c8db6be2407cee4b6cf`.
  **Operator-visible fix:** two session-layer domains can coexist, domain-B
  worker completion does not silently rewrite domain A, and legacy recovery
  names recorded domains instead of guessing the host default.
- G725 — PR #1576; merge commit `6820fef35dad12c07ef936278bf40e4a2071772e`.
  The stalled-work check **detects and reports a skipped post-release version
  roll**. It is silent when there is no release or the roll is correct; it
  does not perform or repair a roll.
- G726 — PR #1577; merge commit `728989c6ef5bc7166718f0b7222a22c95d1c2e2e`.
  The release path **gates and refuses an unreachable tag** by checking the
  exact commit against the repository default branch before publication; it
  does not rewrite history or repair the unreachable commit.
- G727 — PR #1578; merge commit `5d2d1ce51530c035944194e6cb762246fc589b13`.
  Stalled-work **reports checkout freshness/provenance**: stale checkouts are
  actionable, current checkouts stay silent, and offline evidence is unknown;
  the report never fetches, pulls, resets, or synchronizes a checkout.
- G728 — PR #1580; merge commit `2caa6d42f1578d57c5667db1d475024d1afbc9f9`.
  The post-release policy roll records stable `0.23.1` and next `0.23.2` and
  opens this line's release-note preparation. It does not tag, publish, or
  perform another post-release roll.

The merge commit `eb65cbc100e9a2bea9f3c7d912315233d0a6720c` is deliberately
not an inventory item: its content shipped in the published v0.23.1 tag
`d49984dae761d589b2568f8eb1677ce3ff2facbc7`. A later merge position is not
evidence that shipped content is new to this line.

## Accounting for the six-unit inventory

| merge commit | unit | PR |
| --- | --- | --- |
| `0252948e631194087a2cdacc7605f6023d8d0213` | G723 | #1571 |
| `771d5e9d147997cf184e5c8db6be2407cee4b6cf` | G724 | #1572 |
| `6820fef35dad12c07ef936278bf40e4a2071772e` | G725 | #1576 |
| `728989c6ef5bc7166718f0b7222a22c95d1c2e2e` | G726 | #1577 |
| `5d2d1ce51530c035944194e6cb762246fc589b13` | G727 | #1578 |
| `2caa6d42f1578d57c5667db1d475024d1afbc9f9` | G728 | #1580 |

## Prepared functional head and identity evidence

The functional content was independently built and measured at the exact
prepared functional head
`2caa6d42f1578d57c5667db1d475024d1afbc9f9`, before this G729 documentation
unit changed the checkout. The Release build and version query were:

```bash
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
```

- **Release identity evidence source revision:**
  `2caa6d42f1578d57c5667db1d475024d1afbc9f9`
- **Display identity from that build:** `intent-cli 0.23.2-2caa6d4-G728`
- **Installed comparison:** `intent-cli 0.23.1-d49984d-G721`

The installed 0.23.1 command surface and the prepared functional head have
the following direct `--help` counts. Every group is unchanged; this confirms
that the prepared line adds no command surface. The accepted version roll is
already 0.23.2; this comparison is verification of that settled line, not a
new version decision.

| command group | installed 0.23.1 | prepared functional head | result |
| --- | ---: | ---: | --- |
| `automation` | 39 | 39 | unchanged |
| `notify` | 9 | 9 | unchanged |
| `session-layer` | 6 | 6 | unchanged |
| `guide` | 35 | 35 | unchanged |
| `worker` | 8 | 8 | unchanged |
| `issue` | 9 | 9 | unchanged |
| `review` | 3 | 3 | unchanged |
| `closeout` | 1 | 1 | unchanged |
| `claim` | 4 | 4 | unchanged |
| `metadata` | 2 | 2 | unchanged |

This G729 release-prep unit itself is outside the prepared functional head:
its correction exists only in the documentation merge commit that carries
these notes. The eventual tag will land on the documentation merge commit,
not on the earlier functional head.

## Prepare-only boundary

This unit changes only the two v0.23.2 release-note mirrors and their
release-note documentation tests. It does not edit `eng/version.json`, source,
workflows, shipped v0.23.0/v0.23.1 notes, tags, GitHub Releases, packages, or
the post-release roll. No tag, no GitHub Release, and no publish action is
part of this evidence.
