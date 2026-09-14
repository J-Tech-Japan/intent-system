# Release Notes — intent-cli v0.32.0

> **PREPARED / NOT PUBLISHED.** This prepare-only note set records the measured
> G795–G830 chain for the `v0.32.0-preview.3` prerelease. It does not create a tag
> or GitHub Release, publish a package, change a workflow or publish
> configuration, or change product source.

No stable GitHub Release exists for v0.32.0; these notes are preparation
evidence only. The matching stable install query, once v0.32.0 is cut, will be
`JTechJapan.IntentSystem.Cli --version 0.32.0`. The policy after this
preparation is unchanged:

```json
{
  "stableVersion": "0.32.0",
  "nextVersion": "0.32.1"
}
```

`0.32.1` is a replaceable development placeholder, not a decision about the
next real release. The EN and JA v0.32.1 files are planning scaffolds, not
changelogs. This prepare-only slice makes no tag, no GitHub Release, no workflow
change, and no product source change.

## Preview.3: what changed since preview.2

preview.2 (`v0.32.0-preview.2`, 2026-09-05) shipped with a deadlock: the claim
verifier required `--team` on a claims-enabled host while `worker claim` could
not accept it, so a team-owned unit could not be worker-claimed. preview.3
carries that fix (G815) and everything merged after it. Changes a preview.2 user
must act on:

- **Supervision is opt-in (G828).** `guide next` recommends `supervision-setup`,
  and `guide bootstrap` requires a supervision cycle, only for teams declared in
  `.intent-cli/config.toml` as `[supervision] opt_in_teams = ["<domain>/<team>"]`.
  Existing `bound.json`, install records, or cycles no longer opt a team in. A
  team that wants a standing supervisor adds that declaration. `guide next`
  `bootstrap.resume_recommended` now follows bootstrap completeness, so an
  opted-in team with a partial roster and a recorded cycle is told to resume.
- **`stalls.jsonl` is runtime-local (G827).** The CLI-managed
  `.intent-cli/supervision/.gitignore` now includes `**/stalls.jsonl`. A host
  that tracks it runs
  `intent-cli notify supervise repair-cycle-history --domain <d> --team <t> --write`,
  which removes it from the index and keeps the file. `notify supervise shrink`
  and `archive` no longer crash on files above about 1 GB.
- **agmsg + herdr is deprecated (G829).** It still works and remains the
  unrecorded default. Record herdr-only with
  `intent-cli session-layer set --domain <d> [--team <t>] --mode herdr-only --write`.
- **Review correction no longer deadlocks (G824).** `request-update` removes
  `intent-pr-approved`.
- **Published issue bodies can be updated canonically (G825)** with
  `intent-cli issue sync-body <unit> --repo <owner/repo>` (dry-run first, then
  `--write --expected-remote-sha256 <digest>`); the guarantee is
  read-compare-write-verified, not atomic.

## Independently measured minor justification

The named product base is `e78b27d1e99247380fa7518d67470304fb1d7e7b`. The
minor decision follows the v0.28.0 rule: **a command-route addition is a minor
bump; option-level additions do not count as command routes.** G796 adds
event-kind routing to a new role and G800 adds the research-delegation route;
those two command-surface route additions are the measured reason for this
minor. The alias table and config repair, guide rendering, and G801 npm
dist-tag behavior are listed as changes but explicitly **not counted** as
routes. G803's structured guide-field canonicalization is also listed but
explicitly **not counted** as an additional route.

Since preview.2 the compatibility ledger gained eight command routes:
`automation progress-supervision`, `guide progress-supervision`, `guide steward-thread`, `issue sync-body`, `notify ack`, `notify acknowledge`, `notify progress-supervision`, `session-layer seat`. By operator decision (2026-09-14) these stay in the unreleased
0.32.0 minor and ship as `v0.32.0-preview.3` rather than opening 0.33.0: v0.32.0
has no stable release yet, so the minor they would bump is still the one being
prepared. They are counted route additions within 0.32.0.

The route decision is independently observable in the merged history: G796 is
the six-kind event routing addition and G800 is the first-class research
delegation route; the eight later routes are listed above.

## Measured version identities

The named base was checked with a clean Release build:

```text
$ git rev-parse HEAD
e78b27d1e99247380fa7518d67470304fb1d7e7b
$ dotnet build IntentSystem.sln --configuration Release --no-restore; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.1-e78b27d-G829
```

That normal identity is the `nextVersion` placeholder and is **not** v0.32.0.
The same base with the explicit release property was measured separately:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.32.0; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.0-e78b27d-G829
```

Published versioning is the third identity and is derived by `release.yml`,
not by the local policy file:

```text
$ raw=v0.32.0; version="${raw#v}"; printf 'RAW=%s\nVERSION=%s\n' "$raw" "$version"
RAW=v0.32.0
VERSION=0.32.0
```

The release workflow supplies `-p:Version=<tag>` from `RAW`; `eng/version.json`
governs local builds and dry runs only. A preview release tag such as
`v0.32.0-preview.3` yields `VERSION=0.32.0-preview.3` the same way. This
prepare-only slice created no tag.

## Release inventory: exactly 26 shipped first-parent units

The shipped inventory is derived from the exact first-parent range. Git measured
thirty-three commits; the 26 shipped units below (G813 partial) each have one
operator-observable outcome, while the G802 and G804 release-prep commits and
five claim state commits are classified in the accounting table but are not
counted as shipped units:

- G795 — PR #1740 / issue #1737; merge commit `1b3c7229cfe8c8f8565034a7e2220a94ac14785b`.
  **Operator-observable outcome:** canonical Architect, Orchestrator, Builder, Reviewer, and Steward role values accept the four legacy aliases while unknown roles are refused.
- G798 — PR #1742 / issue #1741; merge commit `09b1f4edca51f3acbbe3e901356866996f4be29f`.
  **Operator-observable outcome:** recorded role configuration loads through the canonical normalizer while queue-state role fields remain read/display values without runtime semantics.
- G796 — PR #1743 / issue #1738; merge commit `67c8578090f1a53e8894aeff88abd6cd8b83ff15`.
  **Operator-observable outcome:** six event kinds route to Steward or Architect and an opaque ruling payload is relayed byte-identically with its digest and origin boundary.
- G800 — PR #1747 / issue #1745; merge commit `6e0bff220e2bf51308596c19ee258835ce509dd8`.
  **Operator-observable outcome:** Architect or Reviewer can delegate sourced research to Orchestrator or Steward; ruling-bearing research is refused at the judgement seat while direct research remains ungated.
- G797 — PR #1746 / issue #1739; merge commit `11457187ad0f9c2c269b80de84b0fd9ea278dfe5`.
  **Operator-observable outcome:** guides teach canonical roles, describe Steward, retain the retired-name glossary, and preserve all installed route names without vendor/runtime role coupling.
- G801 — PR #1749 / issue #1748; merge commit `2a833a976688b3139678e4954162a9c00d32d0f4`.
  **Operator-observable outcome:** npm publish calls derive `latest` for stable versions and a non-default prerelease dist-tag for preview, rc, beta, and alpha SemVer forms.
- G803 — PR #1753 / issue #1752; merge commit `16267f9d58af31669252186a16ce09ab0dd47ba4`.
  **Operator-observable outcome:** every structured guide field carrying a role identifier emits the canonical name, with a seven-entry inventory and one shared four-surface full-payload regression.
- G799 — PR #1756 / issue #1744; merge commit `d4645e2f02aea6a7969804a156df176cc2122a4a`.
  **Operator-observable outcome:** `notify adjudicate live-pair` returns the current live CAS pair to feed `notify adjudicate`, and CAS refusals name sequence, dialog-change, and wrong-projection causes distinctly.
- G805 — PR #1765 / issue #1757; merge commit `5ffcea48b0060569112efee06ad12baa5d8c9b59`.
  **Operator-observable outcome:** read-only `automation stalled-work` reports `review-verdict-ahead-of-label` when the newest APPROVED or CHANGES_REQUESTED review is ahead of the latest `intent-pr-*` label transition.
- G806 — PR #1766 / issue #1758; merge commit `8e1ded058aeb6738c9419ce584f8da0d9fa59888`.
  **Operator-observable outcome:** read-only `automation stalled-work` distinguishes a published issue with no intake from a delivered intake that has not reached a runtime queue transition, with owner and action evidence.
- G807 — PR #1767 / issue #1759; merge commit `ea5959f83f528675072b8f034f5c3fc30cbb07b8`.
  **Operator-observable outcome:** the new read-only `guide steward-thread` route renders the Steward's metadata-free operating contract in Markdown and JSON.
- G808 — PR #1768 / issue #1760; merge commit `80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36`.
  **Operator-observable outcome:** the new read-only `session-layer seat preflight` route probes `.git` writability, remote reachability, installed identity, host claim-path resolution, and runtime family before a seat starts, with remedy lines.
- G809 — PR #1769 / issue #1763; merge commit `3d604981865a04c2875fde06d76cf5b60d41fda0`.
  **Operator-observable outcome:** `notify delegate --to` delivers to exactly the requested recipient across event kinds instead of rerouting by event kind.
- G811 — PR #1770 / issue #1762; merge commit `828e19815e0bd8356298e5c49ae3c90bd8a1751d`.
  **Operator-observable outcome:** a Herdr-to-external Steward completion channel records the external consumption receipt and the identity-bound return acknowledgement through the new `notify acknowledge` route (alias `notify ack`), without waking a pane or model.
- G812 — PR #1772 / issue #1764; merge commit `cf40ac8f3211925339134f5fa8bb91bc722e549b`.
  **Operator-observable outcome:** the new `notify progress-supervision`, `automation progress-supervision`, and `guide progress-supervision` routes evaluate typed progress phases and deadlines with bounded recovery evidence; `--write` appends command-owned evidence only and never launches providers or panes.
- G810 — PR #1773 / issue #1761; merge commit `0f7cb50b0f7d42da120fe04bc4421807dcf5da16`.
  **Operator-observable outcome:** `notify supervise` emits the `g810-cost-aware-supervision/v1` contract with measured timing, source categories, and identity-bound recovery fields.
- G815 — PR #1776 / issue #1775; merge commit `7003e08b0152e91f89069a67b5ca299bef7cd6de`.
  **Operator-observable outcome:** `worker claim --team` carries the invoking team through claim ownership verification, so a team-owned execution unit can be worker-claimed on a claims-enabled host (preview.2 required `--team` but could not accept it).
- G813 — PR #1781 / issue #1774; merge commit `3d91a8004c97ab800f365d89189c12def8a39980`.
  **Operator-observable outcome:** worker and host-loop commands preserve an explicit team identity. **Partial:** the Orca Run mailbox binding (#1771) is not delivered and linkage issue #1774 stays open.
- G822 — PR #1786 / issue #1785; merge commit `9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5`.
  **Operator-observable outcome:** `automation host-loop-next-action` team-scoped identity refusals name the missing fact category and the command that supplies it.
- G823 — PR #1788 / issue #1778, #1787; merge commit `01944a5ed47139b47276ef2fcbc951183f8e442b`.
  **Operator-observable outcome:** `automation publish-lifecycle-repair` binds each unit to the repository in its `created_issue_url` before any evidence read and excludes foreign or unproven units instead of judging them on another repository's issue.
- G824 — PR #1791 / issue #1782, #1789; merge commit `20ef0a6caa184b91e4c3cd2378bd37d941e0de7a`.
  **Operator-observable outcome:** `automation pr-transition --transition request-update` removes `intent-pr-approved`, and reconcile stops with `conflicting-review-decision` instead of guessing which review decision came last.
- G825 — PR #1793 / issue #1777, #1790; merge commit `ca7272f4b88e00577e7c423d41a888f0f0defaf6`.
  **Operator-observable outcome:** the new `issue sync-body` route updates an already-published child issue body after validation and repository binding; every result states the guarantee `read-compare-write-verified; not atomic`.
- G826 — PR #1795 / issue #1792; merge commit `71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44`.
  **Operator-observable outcome:** `issue publish-flow` resolves the issue title from packet.yaml `issue_title` (YAML and JSON packet forms) instead of publishing scaffolded packets as `<id> (untitled)`.
- G827 — PR #1796 / issue #1794; merge commit `0762312ddccf14009d3252c5101d0b893ca7be6b`.
  **Operator-observable outcome:** `notify supervise shrink` and `archive` stream supervision files with memory bounded by the longest record (a 1.25 GB `cycles.jsonl` peaked at about 55 MB), and `stalls.jsonl` is runtime-local.
- G828 — PR #1799 / issue #1798; merge commit `227a981d42cee28924ba34cbde3f45d12717a202`.
  **Operator-observable outcome:** supervision is opt-in: only teams declared in `[supervision] opt_in_teams` get the `supervision-setup` recommendation or need a supervision cycle for bootstrap completeness.
- G829 — PR #1802 / issue #1801; merge commit `e78b27d1e99247380fa7518d67470304fb1d7e7b`.
  **Operator-observable outcome:** agmsg + herdr is described as deprecated and `session-layer show` adds `mode_deprecated` and `deprecation_notice`; the unrecorded default stays `agmsg`.

## First-parent accounting

```text
$ git rev-list --first-parent --reverse v0.31.0..e78b27d1e99247380fa7518d67470304fb1d7e7b
1b3c7229cfe8c8f8565034a7e2220a94ac14785b
09b1f4edca51f3acbbe3e901356866996f4be29f
67c8578090f1a53e8894aeff88abd6cd8b83ff15
6e0bff220e2bf51308596c19ee258835ce509dd8
11457187ad0f9c2c269b80de84b0fd9ea278dfe5
2a833a976688b3139678e4954162a9c00d32d0f4
b0f5354ba9a922e1676a2e654d866c2a08f60104
16267f9d58af31669252186a16ce09ab0dd47ba4
1f4f94155914c9f6097ec8f6bbad916f13e7817b
d4645e2f02aea6a7969804a156df176cc2122a4a
5ffcea48b0060569112efee06ad12baa5d8c9b59
8e1ded058aeb6738c9419ce584f8da0d9fa59888
ea5959f83f528675072b8f034f5c3fc30cbb07b8
80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36
3d604981865a04c2875fde06d76cf5b60d41fda0
828e19815e0bd8356298e5c49ae3c90bd8a1751d
cf40ac8f3211925339134f5fa8bb91bc722e549b
0f7cb50b0f7d42da120fe04bc4421807dcf5da16
ebb7f21745a24d985c3ffdc7b370195506c1338c
3262d7c543f3663a8ce83fb8a2e86018162d6962
540c6efdf63300d05fe79e8020574c1bc3f96ad7
f9a91a61edb3ed6291750a19e0247212fccad270
baef0f3534c0a7a0de7dac0e0f1b4a7946c7c0a9
7003e08b0152e91f89069a67b5ca299bef7cd6de
3d91a8004c97ab800f365d89189c12def8a39980
9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5
01944a5ed47139b47276ef2fcbc951183f8e442b
20ef0a6caa184b91e4c3cd2378bd37d941e0de7a
ca7272f4b88e00577e7c423d41a888f0f0defaf6
71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44
0762312ddccf14009d3252c5101d0b893ca7be6b
227a981d42cee28924ba34cbde3f45d12717a202
e78b27d1e99247380fa7518d67470304fb1d7e7b
$ git rev-list --first-parent --count v0.31.0..e78b27d1e99247380fa7518d67470304fb1d7e7b
33
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `1b3c7229cfe8c8f8565034a7e2220a94ac14785b` | G795 / PR #1740 / issue #1737 | included |
| `09b1f4edca51f3acbbe3e901356866996f4be29f` | G798 / PR #1742 / issue #1741 | included |
| `67c8578090f1a53e8894aeff88abd6cd8b83ff15` | G796 / PR #1743 / issue #1738 | included |
| `6e0bff220e2bf51308596c19ee258835ce509dd8` | G800 / PR #1747 / issue #1745 | included |
| `11457187ad0f9c2c269b80de84b0fd9ea278dfe5` | G797 / PR #1746 / issue #1739 | included |
| `2a833a976688b3139678e4954162a9c00d32d0f4` | G801 / PR #1749 / issue #1748 | included |
| `b0f5354ba9a922e1676a2e654d866c2a08f60104` | G802 / PR #1751 / issue #1750 | prior release prep |
| `16267f9d58af31669252186a16ce09ab0dd47ba4` | G803 / PR #1753 / issue #1752 | included |
| `1f4f94155914c9f6097ec8f6bbad916f13e7817b` | G804 / PR #1755 | prior release prep |
| `d4645e2f02aea6a7969804a156df176cc2122a4a` | G799 / PR #1756 / issue #1744 | included |
| `5ffcea48b0060569112efee06ad12baa5d8c9b59` | G805 / PR #1765 / issue #1757 | included |
| `8e1ded058aeb6738c9419ce584f8da0d9fa59888` | G806 / PR #1766 / issue #1758 | included |
| `ea5959f83f528675072b8f034f5c3fc30cbb07b8` | G807 / PR #1767 / issue #1759 | included |
| `80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36` | G808 / PR #1768 / issue #1760 | included |
| `3d604981865a04c2875fde06d76cf5b60d41fda0` | G809 / PR #1769 / issue #1763 | included |
| `828e19815e0bd8356298e5c49ae3c90bd8a1751d` | G811 / PR #1770 / issue #1762 | included |
| `cf40ac8f3211925339134f5fa8bb91bc722e549b` | G812 / PR #1772 / issue #1764 | included |
| `0f7cb50b0f7d42da120fe04bc4421807dcf5da16` | G810 / PR #1773 / issue #1761 | included |
| `ebb7f21745a24d985c3ffdc7b370195506c1338c` | `claim: acquire execution-unit:G813` | claim state commit, not a unit |
| `3262d7c543f3663a8ce83fb8a2e86018162d6962` | `claim: acquire execution-unit:G815` | claim state commit, not a unit |
| `540c6efdf63300d05fe79e8020574c1bc3f96ad7` | `claim: take over execution-unit:G815` | claim state commit, not a unit |
| `f9a91a61edb3ed6291750a19e0247212fccad270` | `claim: take over execution-unit:G815` | claim state commit, not a unit |
| `baef0f3534c0a7a0de7dac0e0f1b4a7946c7c0a9` | `claim: take over execution-unit:G815` | claim state commit, not a unit |
| `7003e08b0152e91f89069a67b5ca299bef7cd6de` | G815 / PR #1776 / issue #1775 | included |
| `3d91a8004c97ab800f365d89189c12def8a39980` | G813 / PR #1781 / linkage issue #1774 (not closed) | partial unit, included |
| `9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5` | G822 / PR #1786 / issue #1785 | included |
| `01944a5ed47139b47276ef2fcbc951183f8e442b` | G823 / PR #1788 / issue #1778, #1787 | included |
| `20ef0a6caa184b91e4c3cd2378bd37d941e0de7a` | G824 / PR #1791 / issue #1782, #1789 | included |
| `ca7272f4b88e00577e7c423d41a888f0f0defaf6` | G825 / PR #1793 / issue #1777, #1790 | included |
| `71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44` | G826 / PR #1795 / issue #1792 | included |
| `0762312ddccf14009d3252c5101d0b893ca7be6b` | G827 / PR #1796 / issue #1794 | included |
| `227a981d42cee28924ba34cbde3f45d12717a202` | G828 / PR #1799 / issue #1798 | included |
| `e78b27d1e99247380fa7518d67470304fb1d7e7b` | G829 / PR #1802 / issue #1801 | included |

The first-parent range contains exactly these thirty-three commits and nothing
else; the table is not a changelog of second-parent commits. G802 and G804 are
prior release preparation for preview.1 and preview.2. The five claim state
commits were written to the child default branch by claim transactions; they
carry no product change and are not units. G813 is included as a partial unit:
its linkage issue #1774 remains open.

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
- G825's `issue sync-body` states `read-compare-write-verified; not atomic`;
  it does not claim compare-and-swap, which GitHub issue bodies do not offer.
- G828 and G829 change guidance and descriptions only: supervision commands
  keep working for any team, and `agmsg` keeps working and remains the
  unrecorded default.
- No tag, GitHub Release, package publish, workflow or publish-configuration
  change, consumer follow-up, or product-source change belongs to this
  prepare-only slice.

## Prepare-only verification

`ReleaseNotesV0320G802Tests` compares the EN/JA unit/PR/issue/merge tuples,
asserts the four alias statements in both mirrors, checks the three measured
identities and exact thirty-three-commit accounting, and deliberately fails on
a one-field mirror mutation and on a stale measurement from the preview.2 base
`16267f9d58af31669252186a16ce09ab0dd47ba4`. `ReleasePackageMetadataTests` continues to guard
the policy shape and demanded next-version placeholder. The diff is limited to
the EN/JA v0.32.0 notes and tests; `eng/version.json` and the v0.32.1
placeholders are untouched. It contains no tag, GitHub Release, package
publish, workflow/publish-config, or product source change.
