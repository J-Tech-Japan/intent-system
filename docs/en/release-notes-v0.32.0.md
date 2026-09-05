# Release Notes — intent-cli v0.32.0

> **PREPARED / NOT PUBLISHED.** This prepare-only note set records the measured
> G795–G804 chain. It does not create a tag or GitHub Release, publish a package,
> change a workflow or publish configuration, or change product source.

No GitHub Release exists for v0.32.0; these notes are preparation evidence only.
The matching install query is `JTechJapan.IntentSystem.Cli --version 0.32.0`.
The policy after this preparation is:

```json
{
  "stableVersion": "0.32.0",
  "nextVersion": "0.32.1"
}
```

`0.32.1` is a replaceable development placeholder, not a decision about the
next real release. The EN and JA v0.32.1 files are planning scaffolds, not
changelogs. This prepare-only slice makes no tag, no workflow change, and no
product source change.

## Independently measured minor justification

The named product base is `16267f9d58af31669252186a16ce09ab0dd47ba4`. The
minor decision follows the v0.28.0 rule: **a command-route addition is a minor
bump; option-level additions do not count as command routes.** G796 adds
event-kind routing to a new role and G800 adds the research-delegation route;
those two command-surface route additions are the measured reason for this
minor. The alias table and config repair, guide rendering, and G801 npm
dist-tag behavior are listed as changes but explicitly **not counted** as
routes. G803's structured guide-field canonicalization is also listed but
explicitly **not counted** as an additional route.

The route decision is independently observable in the merged history: G796 is
the six-kind event routing addition and G800 is the first-class research
delegation route. No other listed change is counted as a command route.

## Measured version identities

The named base was checked with a clean Release build after the policy roll:

```text
$ git rev-parse HEAD
16267f9d58af31669252186a16ce09ab0dd47ba4
$ dotnet build IntentSystem.sln --configuration Release --no-restore; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.1-16267f9-G803
```

That normal identity is the `nextVersion` placeholder and is **not** v0.32.0.
The same base with the explicit release property was measured separately:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.32.0; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.0-16267f9-G803
```

Published versioning is the third identity and is derived by `release.yml`,
not by the local policy file:

```text
$ raw=v0.32.0; version="${raw#v}"; printf 'RAW=%s\nVERSION=%s\n' "$raw" "$version"
RAW=v0.32.0
VERSION=0.32.0
```

The release workflow supplies `-p:Version=<tag>` from `RAW`; `eng/version.json`
governs local builds and dry runs only. This prepare-only slice created no tag.

## Release inventory: exactly seven shipped first-parent units

The shipped inventory is derived from the exact first-parent range. Git measured
eight commits; the seven shipped units below each have one operator-observable
outcome, while the separate G802 prep commit is classified in the accounting
table but is not counted as a shipped unit:

- G795 — PR #1740 / issue #1737; merge commit `1b3c7229cfe8c8f8565034a7e2220a94ac14785b`.
  **Operator-observable outcome:** canonical Architect, Orchestrator, Builder,
  Reviewer, and Steward role values accept the four legacy aliases while
  unknown roles are refused.
- G798 — PR #1742 / issue #1741; merge commit `09b1f4edca51f3acbbe3e901356866996f4be29f`.
  **Operator-observable outcome:** recorded role configuration loads through
  the canonical normalizer while queue-state role fields remain read/display
  values without runtime semantics.
- G796 — PR #1743 / issue #1738; merge commit `67c8578090f1a53e8894aeff88abd6cd8b83ff15`.
  **Operator-observable outcome:** six event kinds route to Steward or
  Architect and an opaque ruling payload is relayed byte-identically with its
  digest and origin boundary.
- G800 — PR #1747 / issue #1745; merge commit `6e0bff220e2bf51308596c19ee258835ce509dd8`.
  **Operator-observable outcome:** Architect or Reviewer can delegate sourced
  research to Orchestrator or Steward; ruling-bearing research is refused at
  the judgement seat while direct research remains ungated.
- G797 — PR #1746 / issue #1739; merge commit `11457187ad0f9c2c269b80de84b0fd9ea278dfe5`.
  **Operator-observable outcome:** guides teach canonical roles, describe
  Steward, retain the retired-name glossary, and preserve all installed route
  names without vendor/runtime role coupling.
- G801 — PR #1749 / issue #1748; merge commit `2a833a976688b3139678e4954162a9c00d32d0f4`.
  **Operator-observable outcome:** npm publish calls derive `latest` for stable
  versions and a non-default prerelease dist-tag for preview, rc, beta, and
  alpha SemVer forms.
- G803 — PR #1753 / issue #1752; merge commit `16267f9d58af31669252186a16ce09ab0dd47ba4`.
  **Operator-observable outcome:** every structured guide field carrying a role
  identifier emits the canonical name, with a seven-entry inventory and one
  shared four-surface full-payload regression.

## First-parent accounting

```text
$ git rev-list --first-parent --reverse v0.31.0..16267f9d58af31669252186a16ce09ab0dd47ba4
1b3c7229cfe8c8f8565034a7e2220a94ac14785b
09b1f4edca51f3acbbe3e901356866996f4be29f
67c8578090f1a53e8894aeff88abd6cd8b83ff15
6e0bff220e2bf51308596c19ee258835ce509dd8
11457187ad0f9c2c269b80de84b0fd9ea278dfe5
2a833a976688b3139678e4954162a9c00d32d0f4
b0f5354ba9a922e1676a2e654d866c2a08f60104
16267f9d58af31669252186a16ce09ab0dd47ba4
$ git rev-list --first-parent --count v0.31.0..16267f9d58af31669252186a16ce09ab0dd47ba4
8
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `1b3c7229cfe8c8f8565034a7e2220a94ac14785b` | G795 / PR #1740 / issue #1737 | included |
| `09b1f4edca51f3acbbe3e901356866996f4be29f` | G798 / PR #1742 / issue #1741 | included |
| `67c8578090f1a53e8894aeff88abd6cd8b83ff15` | G796 / PR #1743 / issue #1738 | included |
| `6e0bff220e2bf51308596c19ee258835ce509dd8` | G800 / PR #1747 / issue #1745 | included |
| `11457187ad0f9c2c269b80de84b0fd9ea278dfe5` | G797 / PR #1746 / issue #1739 | included |
| `2a833a976688b3139678e4954162a9c00d32d0f4` | G801 / PR #1749 / issue #1748 | included |
| `b0f5354ba9a922e1676a2e654d866c2a08f60104` | G802 / PR #1751 / issue #1750 | this release's own prep |
| `16267f9d58af31669252186a16ce09ab0dd47ba4` | G803 / PR #1753 / issue #1752 | included |

The first-parent range contains exactly these eight merge commits and nothing
else; the table is not a changelog of second-parent commits. G802's row is
explicitly this release's own preparation unit, not omitted accounting.

## Alias promise and compatibility boundary

The role-renaming release is safe for existing hosts: the four legacy names
`design`, `orchestration`, `implementation`, and `review` still work as aliases
for Architect, Orchestrator, Builder, and Reviewer. Existing roles
configuration keeps loading, existing queue-state keeps reading and
displaying, and no installed guide route changed name. These are compatibility
promises, not new route claims; the release only teaches canonical role values
in new guidance.

## Truthfulness and prepare-only boundaries

- G795's five canonical roles and four aliases are normalized once; unknown
  role values are refused rather than silently persisted.
- G798's roles configuration remains loadable and queue-state `worker_role` /
  `review_role` values remain read/display fields, not runtime behavior.
- G796's ruling payload remains opaque: bytes, digest, and origin are retained;
  only the specified relay envelope may be added.
- G800's research delegation requires a source-bearing finding and refuses a
  ruling-bearing report while naming the judgement seat that must rule. Direct
  Architect and Reviewer research is successful and is not a gate. Visibility
  counts are measurements without grading, and no size threshold, model name,
  or runtime condition is used.
- No tag, GitHub Release, package publish, workflow or publish-configuration
  change, consumer follow-up, or product-source change belongs to this
  prepare-only slice.

## Prepare-only verification

`ReleaseNotesV0320G802Tests` compares the EN/JA unit/PR/issue/merge tuples,
asserts the four alias statements in both mirrors, checks the three measured
identities and exact eight-commit accounting, and deliberately fails on a
one-field mirror mutation. `ReleasePackageMetadataTests` continues to guard
the policy shape and demanded next-version placeholder. The PR pastes actual
parent absence/failure output for each new test, criterion-named release-policy
output, `git diff --check`, focused Release counts, full CLI Release counts,
and exact-head CI. The diff is limited to the EN/JA v0.32.0 notes and tests;
`eng/version.json` and the v0.32.1 placeholders are untouched. It contains no
tag, GitHub Release, package publish, workflow/publish-config, or product
source change.
