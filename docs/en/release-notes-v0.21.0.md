# Release Notes — intent-cli v0.21.0

> **prepare-only / UNRELEASED.** This preparation changes version state,
> release notes, readiness documentation, and release guards only. It creates
> no GitHub Release or tag, publishes no package, runs no release automation,
> and performs no post-release roll. It contains no code or runtime behaviour
> change.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.21.0`.
After a separate operator approval and release action, the Release will be
published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.21.0.
The preceding shipped scope remains in the
[v0.20.0 notes](release-notes-v0.20.0.md); earlier release notes are linked
rather than restated.

## Preview lane — read before the feature description

The G689–G692 surfaces are `preview-through-1.x`, outside the
[1.0 compatibility promise](1.0-compatibility-promise.md). They may change or
be withdrawn during 1.x and are not part of the 1.0 compatibility commitment.

## The four-unit feedback loop

v0.21.0 contains exactly four merged feature units, G689 through G692. This
scope was derived by running `git log v0.20.0..main --first-parent`; every
commit in that range is accounted for below as the post-release roll or one of
the four merge commits. Every PR below is MERGED, and every full merge commit
resolves on `main`.

- G689 — [PR #1492](https://github.com/J-Tech-Japan/intent-system/pull/1492), merge commit `b80d358913be6375741fe95ef93113159b2e0087` (verified on `main`): shell approval has a two-layer prompt-class and scope model; classes recognize dialogs, while scoped policies name what may be answered, so bare-class wholesale approval is structurally impossible.
- G690 — [PR #1494](https://github.com/J-Tech-Japan/intent-system/pull/1494), merge commit `bf9ca28b670362c24d439c847e477dfd55598440` (verified on `main`): design adjudication is scoped by `answerable_by` under a non-overridable hard risk floor, uses live-dialog CAS, and keeps decision-actor and executor audit roles distinct.
- G691 — [PR #1496](https://github.com/J-Tech-Japan/intent-system/pull/1496), merge commit `d305987bc6580e2bd137a17e1764e77bc6b219aa` (verified on `main`): `team_mode` records delivery versus authoring-only; issue-authoring teams bootstrap with the front door alone, while supervise returns a named not-applicable verdict and delivery remains byte-identical.
- G692 — [PR #1498](https://github.com/J-Tech-Japan/intent-system/pull/1498), merge commit `05b0aa575fb3fb160a6f0035de6c5aaab0aa8bd9` (verified on `main`): authoring-only publish records a design front-door audit and operator acceptance, keeps publish gates, confirms a distinct operator lane, shares the mode-capability matrix, and records a published external handoff without delegating to a named worker.

### Full first-parent range accounting

The first-parent range has five commits. The four merge rows above account for
the feature units; the remaining post-release context is accounted for and is
not a release execution unit.

| account | full commit | treatment |
| --- | --- | --- |
| post-release roll to 0.20.1 | `a73fea1c54fb544645074cf0edf038158f539332` | accounted for; context, not a unit |
| G689 merge | `b80d358913be6375741fe95ef93113159b2e0087` | merged unit above |
| G690 merge | `bf9ca28b670362c24d439c847e477dfd55598440` | merged unit above |
| G691 merge | `d305987bc6580e2bd137a17e1764e77bc6b219aa` | merged unit above |
| G692 merge | `05b0aa575fb3fb160a6f0035de6c5aaab0aa8bd9` | merged unit above |

### Two distinguishable origins and the minor rationale

The two origins remain separate under G625. The operator-filed [#1489 audit](https://github.com/J-Tech-Japan/intent-system/issues/1489) found that the vocabulary was stricter than the work; G689–G690 answered that audit end to end. The operator's authoring-only team use-case request is a separate origin answered by G691–G692. Measured facts from the audit and shipped surfaces from the use-case request stay separately attributed; neither origin is relabeled as the other's measurement.

The hard risk floor is non-overridable: the `rm`-containing compound command
from #1489 remains the `rm-containing compound` that is design-unanswerable by
design. The minor rationale is
checkable: the shell prompt-class scope registry with `prompt-class list/describe`,
the canonical `adjudicate` surface with `answerable_by` and its hard risk floor,
the recorded `team_mode`, and the mode-capability matrix were absent at
v0.20.0. These are additive preview surfaces, not a patch-only correction.

## Deliberate boundaries

- This preparation changes version policy, release notes, readiness
  documentation, and release guards only. It makes no code or runtime
  behaviour change.
- The feature list is bounded exactly to G689–G692. It does not silently
  include G693 or restate earlier release notes.
- Prepare-only remains in force: this child creates no GitHub Release or tag,
  publishes no package, and performs no post-roll. Release creation is the
  operator's separate action after readiness is green.

## Release-readiness gate

Before the separate operator release action:

- `eng/version.json` records stable `0.20.0` and next `0.21.0`.
- The four PRs and full merge commits above resolve on `main`, and every commit
  in `git log v0.20.0..main --first-parent` is accounted for by the range table.
- The preview statement precedes the feature description and links the 1.0
  compatibility promise.
- EN/JA notes remain in parity under the G613 terminology policy; the
  release-notes guard, bilingual count guard, version/readiness guards, full
  Release suite, and `git diff --check` are green.
- Prepare-only remains in force. Release creation, tagging, package
  publication, and post-release rolling are separate operator actions.

## Publishing v0.21.0

After this preparation is merged and readiness evidence is green, the operator
must explicitly approve Release creation. Only then may an authorized
maintainer create and publish the GitHub Release for `v0.21.0`; its downstream
release automation is outside this child PR. Any post-release version roll is
a separate operator action and is not performed here.
