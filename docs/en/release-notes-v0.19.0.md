# Release Notes — intent-cli v0.19.0

> **prepare-only / UNRELEASED.** This change prepares version state, release
> notes, and readiness documentation only. It does not create a GitHub Release
> or tag, publish a package, run the release workflow, or perform a post-release
> roll.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.19.0`.
After the separate operator release action, the Release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.19.0.
The preceding shipped scope remains in the
[v0.18.0 notes](release-notes-v0.18.0.md); it is linked rather than restated.

## Preview lane — read before the feature description

The G666–G676 surfaces are `preview-through-1.x`, outside the
[1.0 compatibility promise](1.0-compatibility-promise.md). They may change or
be withdrawn during 1.x and are not part of the 1.0 compatibility commitment.

## The feedback loop closed at day scale

v0.19.0 contains exactly eleven merged units, G666 through G676. The list was
derived by running `git log v0.18.0..main --first-parent`; every commit in that
range is accounted for below as the post-release roll or one of the eleven
merge commits. Every PR below is MERGED and every full merge commit resolves on
`main`.

- G666 — PR #1440, merge commit `1b7f8b718d9c22cfe67707ee9ca23a9a9e6f0b7b` (verified on `main`): approval layers eliminate first by recipe, adjudicate recorded policy, and keep design from relaying.
- G667 — PR #1444, merge commit `2c253a01ea3b7d3836ad044eb5e9ffac38d46f77` (verified on `main`): packet draft resolves the effective base branch through the shared judgment.
- G668 — PR #1446, merge commit `e9d125ea45a163636323a7a0420476b7267cf94e` (verified on `main`): named branch lanes provide a registry, explicit membership, and an immutable routing snapshot.
- G669 — PR #1448, merge commit `e1924405e6d0fcdfdccf8665abc7263dc9a0ee96` (verified on `main`): lane propose/confirm decision records distinguish the two routing stall classes.
- G670 — PR #1450, merge commit `8a85262cd1e42f73d9ba1f438f783e394f8a3828` (verified on `main`): placeholder scaffolds leave the issue-cut-ready pool after the gate's judgment.
- G671 — PR #1452, merge commit `c4f2d66af72c278d0de1d38b0c2c4ea508b1be5f` (verified on `main`): pending-delegation dispositions end the expectation while retaining the carriage.
- G672 — PR #1454, merge commit `cc60fc7ae94ddba7746caf2acdef53ecb29becaf` (verified on `main`): the role contract is first in guide next, and event mode is explained at both offer and duty.
- G673 — PR #1456, merge commit `e6762a5151dc8f489dd5ba108a63adca4ee8c0a6` (verified on `main`): GitHub API quota exhaustion is a named degraded state and doctor reports quota per resource.
- G674 — PR #1458, merge commit `44c4a27befe458399777743ed5c8e16c0d5f3fe1` (verified on `main`): verified GitHub reads use REST with field equivalence, while unverified reads remain GraphQL-bound.
- G675 — PR #1460, merge commit `1c7cace56fdf29a834ee2de61df768e3b083a796` (verified on `main`): scheduler artifacts carry their environment and transport start failure is degraded rather than lost.
- G676 — PR #1462, merge commit `85a4d451d9a91daaf936e3997cf36f67b73766f1` (verified on `main`): duplicate supervisors are detected by writer identity, never elected.

### Full first-parent range accounting

The first-parent range has twelve commits. The eleven merge rows above account
for the feature units; the remaining commit is the post-release context and is
not a release execution unit.

| account | full commit | treatment |
| --- | --- | --- |
| post-release roll to 0.18.1 | `478dd57b5de609e47dbe678c82f714fd0e463dd8` | accounted for; context, not a unit |
| G666 merge | `1b7f8b718d9c22cfe67707ee9ca23a9a9e6f0b7b` | merged unit above |
| G667 merge | `2c253a01ea3b7d3836ad044eb5e9ffac38d46f77` | merged unit above |
| G668 merge | `e9d125ea45a163636323a7a0420476b7267cf94e` | merged unit above |
| G669 merge | `e1924405e6d0fcdfdccf8665abc7263dc9a0ee96` | merged unit above |
| G670 merge | `8a85262cd1e42f73d9ba1f438f783e394f8a3828` | merged unit above |
| G671 merge | `c4f2d66af72c278d0de1d38b0c2c4ea508b1be5f` | merged unit above |
| G672 merge | `cc60fc7ae94ddba7746caf2acdef53ecb29becaf` | merged unit above |
| G673 merge | `e6762a5151dc8f489dd5ba108a63adca4ee8c0a6` | merged unit above |
| G674 merge | `44c4a27befe458399777743ed5c8e16c0d5f3fe1` | merged unit above |
| G675 merge | `1c7cace56fdf29a834ee2de61df768e3b083a796` | merged unit above |
| G676 merge | `85a4d451d9a91daaf936e3997cf36f67b73766f1` | merged unit above |

### Four attributed origins

These units are one feedback loop closing at day scale, but their measured
facts remain separately attributed under G625:

- The operator's branch-lane request produced G667–G669. Those units are the
  operator-request origin, not a measurement from the remote-herdr team.
- Operator-filed feedback issue [#1441](https://github.com/J-Tech-Japan/intent-system/issues/1441)
  produced G670–G672. It was reported by the remote-herdr design thread (Claude)
  for the remote-herdr domain, with Tomohisa Takaoka as operator, over
  2026-08-04–2026-08-11: 48 packets and 21 tier-2 E2E rounds. These measurements
  belong to that reporting team and period.
- Operator-filed feedback issue [#1442](https://github.com/J-Tech-Japan/intent-system/issues/1442)
  produced G673–G674. The remote-herdr four-thread team measured the outage at
  2026-08-12T02:05–02:08Z: GraphQL remaining 0 after 5,046 requests while REST
  still had 4,948 remaining. That report's team and timestamp stay attached to
  those facts; they are not this repository's measurements.
- Same-day incidents on this host produced G675–G676. G675 measured the
  scheduler exit-127 loop and then ten false losses on 2026-08-12; G676 measured
  four concurrent supervisors for the Sekiban workers team in workspace `w2H`
  on this machine on 2026-08-12. The host incidents and the sibling-team
  observation remain distinguishable, including their teams and date.

The minor bump is checkable: the branch-lane registry and routing snapshot, the
pending-delegation disposition record, the named quota-degraded state and
doctor quota report, and duplicate-supervisor detection were absent at
v0.18.0. This is additive preview capability, not a patch-only correction.

## Deliberate boundaries

- This preparation changes version policy, release notes, readiness
  documentation, and release guards only. It makes no code or runtime behavior
  change.
- Earlier release notes are linked rather than restated. The table above is
  bounded exactly to G666–G676; it does not silently include an earlier unit.
- Prepare-only remains in force: this PR creates no GitHub Release or tag,
  publishes no package, and does not run release automation.

## Release-readiness gate

Before the separate operator release action:

- `eng/version.json` records stable `0.18.0` and next `0.19.0`.
- The eleven PRs and full merge commits above resolve on `main`, and every
  commit in `git log v0.18.0..main --first-parent` is accounted for by the
  range table.
- The preview statement precedes the feature description and links the 1.0
  compatibility promise.
- The EN/JA notes remain in parity under the G613 terminology policy; the
  release-notes count guard, version/readiness guards, full suite, and
  `git diff --check` are green.
- Prepare-only remains in force. Release creation, tagging, and package
  publication are separate operator actions.

## Publishing v0.19.0

Release creation remains a distinct operator action after this preparation is
merged and its readiness evidence is green. Only then may an authorized
maintainer create and publish the GitHub Release for `v0.19.0`; publishing it
triggers `release.yml` (`on: release: published`) to build and publish the
NuGet package and platform artifacts. After that separate release, roll
`eng/version.json` to stable `0.19.0` / next `0.19.1`, add the next DRAFT note
stubs in the same commit, refresh both readiness mirrors, and verify child-main
CI before calling the post-release roll complete.
