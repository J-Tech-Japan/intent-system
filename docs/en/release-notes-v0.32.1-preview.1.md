# Release notes — intent-cli 0.32.1-preview.1

> **PREPARED — NOT PUBLISHED.** This is the documentation record for the
> intended `0.32.1-preview.1` prerelease candidate. Merging this preparation PR
> does not create a tag, GitHub Release, NuGet package, npm package, or binary.

These preview.1 notes are separate from the replaceable
`release-notes-v0.32.1.md` development-draft placeholder.

The exact [GitHub release page for `v0.32.1-preview.1`](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1-preview.1)
is the intended destination if and when the conductor publishes it. This link
is a destination, not evidence that the release exists or that any registry
asset is available. The existing stable release remains `v0.32.0`.

## Measured source scope

The source baseline is `6054eb9f46d8fe4e87e370a53bd527c116e3a05b`. The first-parent
range from `v0.32.0` to that commit contains 24 commits and the 24 shipped units
listed below. The G872 documentation preparation is not a 25th product unit.
The source full-suite result at this baseline was 10,701 passed, 0 failed, and
six `NotExecuted` result rows; the TRX raw `Counters.notExecuted` value was 0.
Those result rows and counters describe source tests, not installed-package
behavior or candidate artifact contents.

These changes improve solo-conductor onboarding, make evidence and claim/review
duties observable, add explicit durable rulings and pinned-source provenance,
support local TRX and opt-in worker completion checks, select safety-critical
design-review criteria explicitly, and render deterministic role briefs with
explicit local lessons.

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

## Compatibility and limits

- Existing stable `0.32.0` defaults remain in place. Verification, lessons, and
  worker evidence behavior stays unchanged unless its documented opt-in is
  selected. The worker proof gate requires explicit flags; it does not infer a
  default policy from team configuration.
- A local report's provenance is supplied by the caller, not authenticated.
  Source tests do not prove that a command ran, that a reviewer had authority,
  or that an acceptance criterion is semantically true.
- Lesson enforcement descriptions are advisory and `declared-not-verified`;
  they are not an active completion gate.
- Raw TRX evidence can include test names, output, and machine paths. A digest
  of the entire lesson registry exposes a stable identity for that registry.
  Review these disclosures before committing or publishing the inputs.
- A brief output failure can leave a partial or foreign residual. Inspect it
  separately; the renderer does not claim rollback or publication.
- G872 changes documentation only. It does not run release workflows, create
  tags, publish packages, or prove that candidate artifacts match eventual
  public assets. A workflow-dispatch candidate is a separate build from the
  published-release event, which rebuilds its assets. The previous
  `0.32.1-preview.483.1` artifact smoke is separate evidence and is not this
  `preview.1` candidate.

The remaining broader work includes shared-state semantic conflict recovery
(#1864), a public issue-template/privacy renderer (#1865), ruling-backed
hosted deferral (#1866), broader legacy/ruling acceptance reconciliation
(#1867), and remote public-PR composition/registry authoring (#1868). The
scoped solo-conductor approval, completion, debt, and claim duties in
G856–G861 do not make #1857 an authenticated history oracle. These items are
backlog boundaries, not claims that all related issues are complete or
prerequisites for this preview.

## Installation and publication

Use the [install guide](01-install.md) for the opt-in, version-pinned .NET,
npm, and self-contained asset commands after publication. It explains PATH
coexistence and how to return deliberately to stable `0.32.0`; the normal
stable installation instructions remain unchanged.

After this preparation merges, the conductor must build and review the exact
merged candidate through the existing nonpublishing `workflow_dispatch` path.
That review includes release reachability, the three native binaries, NuGet
and npm versions/checksums, isolated installs, and confirms the candidate has
no time-based preview expiry. Candidate artifact hashes are not proof of public artifact identity: the published
GitHub Release event rebuilds the assets. The conductor must separately verify
the public assets, source/version identity, npm dist-tags, and fresh installs
before declaring publication complete. This preparation PR does not claim
those steps have happened.
