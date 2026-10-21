# Release notes — intent-cli 0.32.1

> **PREPARED — NOT PUBLISHED.** These are the stable 0.32.1 release-preparation
> notes. Merging this preparation PR does not create a tag, GitHub Release,
> NuGet package, npm package, or native archive.

The exact [GitHub release page for `v0.32.1`](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1)
is the intended destination after a separate publication. Until that release
is published, the public stable release remains `v0.32.0`. The existing public
[`v0.32.1-preview.1` release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1-preview.1)
and its preview notes remain separate and unchanged. This file replaces the
old `release-notes-v0.32.1.md` development-draft placeholder; it does not
replace or relabel the preview record.

## Measured product scope

This preparation starts from product main commit
`dba5724bc00f2207f601b5643cfb87595dc27795`. The release inventory below
contains 24 measured product units through G871. It is a unit inventory, not a
claim that the entire commit range from v0.32.0 to this baseline contains only
24 commits. G872 / PR #1906 is documentation preparation, not a 25th product
unit; G873 is release preparation and is not a feature unit.

The reviewed G872 source head
`8438aaaadab90215c1950869227ba007038cb8cd` had 10,708 passed, 0 failed, and
six `NotExecuted` test-result rows in its full11 run. The raw TRX counters
separately reported total 10,714, executed 10,708, passed 10,708, failed 0,
and `notExecuted` 0. This is source-test evidence for that reviewed source
head, not proof of a stable package or native artifact. The public
`0.32.1-preview.1` smoke is likewise preview evidence, not stable artifact
proof. Stable candidate and public assets require separate builds and checks.

These units improve solo-conductor onboarding; make evidence, approval, and
claim-release duties observable; add durable rulings and pinned-source
provenance; support local TRX and opt-in worker completion checks; select
safety-critical design-review criteria explicitly; and render role briefs
with explicit local lessons.

| Unit | Source PR | User-visible scope |
| --- | --- | --- |
| G851 | [PR #1848](https://github.com/J-Tech-Japan/intent-system/pull/1848) | Routes a single-seat operator to `solo-conductor` before multi-seat setup. |
| G852 | [PR #1850](https://github.com/J-Tech-Japan/intent-system/pull/1850) | Clarifies the four-thread and solo-conductor choices without ranking one transport as primary. |
| G844 | [PR #1851](https://github.com/J-Tech-Japan/intent-system/pull/1851) | Reports actual issue-publish refusal causes and details. |
| G843 | [PR #1852](https://github.com/J-Tech-Japan/intent-system/pull/1852) | Recognizes authored acceptance-criterion labels in the evidence gate. |
| G814 | [PR #1854](https://github.com/J-Tech-Japan/intent-system/pull/1854) | Resolves models from scoped, stable target evidence. |
| G853 | [PR #1856](https://github.com/J-Tech-Japan/intent-system/pull/1856) | Completes guidance for an agent-executed Orca mailbox lifecycle. |
| G854 | [PR #1860](https://github.com/J-Tech-Japan/intent-system/pull/1860) | Rejects misplaced guide reachability declarations. |
| G855 | [PR #1863](https://github.com/J-Tech-Japan/intent-system/pull/1863) | Adds read-only unit status. |
| G856 | [PR #1871](https://github.com/J-Tech-Japan/intent-system/pull/1871) | Requires posted exact-head solo-conductor approval. |
| G857 | [PR #1874](https://github.com/J-Tech-Japan/intent-system/pull/1874) | Requires published solo completion evidence before claim release. |
| G858 | [PR #1877](https://github.com/J-Tech-Japan/intent-system/pull/1877) | Adds a solo-debt adoption window. |
| G859 | [PR #1880](https://github.com/J-Tech-Japan/intent-system/pull/1880) | Isolates unrelated source reports in unit-status inventory. |
| G860 | [PR #1882](https://github.com/J-Tech-Japan/intent-system/pull/1882) | Audits explicit missing-receipt override at claim release. |
| G861 | [PR #1884](https://github.com/J-Tech-Japan/intent-system/pull/1884) | Audits the declared-team approval-posting exception. |
| G862 | [PR #1886](https://github.com/J-Tech-Japan/intent-system/pull/1886) | Adds immutable, scoped ruling record/read support. |
| G863 | [PR #1888](https://github.com/J-Tech-Japan/intent-system/pull/1888) | Validates pinned ruling sources before packet publication. |
| G864 | [PR #1890](https://github.com/J-Tech-Japan/intent-system/pull/1890) | Observes pinned ruling sources in unit status. |
| G865 | [PR #1892](https://github.com/J-Tech-Japan/intent-system/pull/1892) | Validates packet verification contracts. |
| G866 | [PR #1894](https://github.com/J-Tech-Japan/intent-system/pull/1894) | Adds packet-bound local TRX evidence. |
| G867 | [PR #1896](https://github.com/J-Tech-Japan/intent-system/pull/1896) | Adds read-only verification-evidence coverage. |
| G868 | [PR #1898](https://github.com/J-Tech-Japan/intent-system/pull/1898) | Requires explicit current local evidence for opted-in worker completion. |
| G869 | [PR #1900](https://github.com/J-Tech-Japan/intent-system/pull/1900) | Selects safety-critical design-review criteria explicitly. |
| G870 | [PR #1902](https://github.com/J-Tech-Japan/intent-system/pull/1902) | Renders deterministic role-specific briefs. |
| G871 | [PR #1904](https://github.com/J-Tech-Japan/intent-system/pull/1904) | Adds explicit local lessons to role briefs. |

## Compatibility and operator impact

- No destructive schema or data migration was identified. New verification
  behavior remains opt-in; lessons remain advisory and
  `declared-not-verified`; caller-supplied provenance is not authenticated.
- The stricter solo-conductor checks enforce documented duties. Use the normal
  exact-head review and eligible closeout path with applicable receipts by
  default. G856 requires
  an independent review posted for the exact head. G857 requires eligible
  builder claim-release closeout and the applicable role receipts: when
  knowledge write-back is declared required, one attributed architect and one
  attributed orchestrator knowledge-writeback receipt; when a guide route is
  declared, an architect guide-reachability receipt.
- G860 and G861 exceptions are bounded. `claim release --override-loop-evidence`
  requires an explicit nonblank reason and applies only to the listed missing
  receipts after closeout. `automation pr-transition --override-loop-evidence
  --override-id <uuid>` requires the flag, reason, and override id and applies
  only to missing canonical posting after the local and other approval checks
  pass. A reason alone does not waive duties. Neither exception is a general
  bypass or an authenticated history claim.
- G868 verification is explicit opt-in: callers must supply
  `--require-verification` and the verification unit, domain, team, head,
  packet digest, and evidence references. Missing identity or evidence is not
  inferred from the checkout or a default.
- Raw TRX evidence can include test names, output, and machine paths. A digest
  of the whole lesson registry discloses a stable registry identity. Review
  these disclosures before committing or publishing the inputs.
- A brief output failure can leave a partial or foreign residual. Inspect it
  separately; the renderer does not promise rollback or publication.

The broader backlog remains open where the work was not delivered: shared-state
semantic conflict recovery (#1864), a public issue-template/privacy renderer
(#1865), ruling-backed hosted deferral (#1866), broader legacy/ruling
acceptance reconciliation (#1867), and remote public-PR composition/registry
authoring (#1868). These are boundaries, not proof of completion, and are not
prerequisites for this stable release.

## Installation and publication

The [install guide](01-install.md) contains version-pinned .NET, npm, and
self-contained asset instructions for use after actual publication. Until
then, keep using stable `0.32.0` or explicitly choose the separately published
preview. Existing installations are not silently overwritten.

After this preparation merges, the conductor must build and review the exact
stable candidate through the existing nonpublishing `workflow_dispatch` path.
The check covers release reachability, the three native binaries, NuGet and
npm versions/checksums, and isolated installs. Candidate hashes do not identify
public assets: the published GitHub Release event rebuilds those assets. The
conductor must verify the rebuilt public assets, source/version identity,
registry tags, and fresh installs before declaring publication complete.
This preparation PR does not claim that any of those steps have happened.

Stable publication will move npm `latest` to `0.32.1`; the existing
`0.32.1-preview.1` version and `preview` tag remain separate. GitHub's stable
latest release changes only after the actual stable release is published.
