# Release Notes — intent-cli v0.20.0

> **prepare-only / UNRELEASED.** This PR prepares version state, release
> notes, readiness documentation, and guards only. It does not create a GitHub
> Release or tag, publish a package, run release automation, or perform a
> post-release roll.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.20.0`.
After a separate operator approval and release action, the Release will be
published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.20.0.
The preceding shipped scope remains in the
[v0.19.0 notes](release-notes-v0.19.0.md); it is linked rather than restated.

## Preview lane — read before the feature description

The G678–G686 surfaces are `preview-through-1.x`, outside the
[1.0 compatibility promise](1.0-compatibility-promise.md). They may change or
be withdrawn during 1.x and are not part of the 1.0 compatibility commitment.

## The day-scale feedback loop

v0.20.0 contains exactly nine merged feature units, G678 through G686. This
scope was derived by running `git log v0.19.0..main --first-parent`; every
commit in that range is accounted for below as the post-release roll or one of
the nine merge commits. Every PR below is MERGED, and every full merge commit
resolves on `main`.

- G678 — [PR #1468](https://github.com/J-Tech-Japan/intent-system/pull/1468), merge commit `3671ba062cd1a4e4b54d634e7160da381fdd3ceb` (verified on `main`): per-lane `operator-merge` landing authority is visible and patient, and no `intent-cli` path merges that lane.
- G679 — [PR #1471](https://github.com/J-Tech-Japan/intent-system/pull/1471), merge commit `42789d6d8b1e4ac0d7133a277decd6ebcddeaf6b` (verified on `main`): git push-CAS claims provide multi-user work ownership for the shared claim verification surfaces.
- G680 — [PR #1473](https://github.com/J-Tech-Japan/intent-system/pull/1473), merge commit `46836e83098c6dd1192beeffe7daf6a32c529d89` (verified on `main`): packet draft, queue seed, publish flow, worker next-action, and next-slice share claim verification, with claim-before-scaffold numbering.
- G681 — [PR #1475](https://github.com/J-Tech-Japan/intent-system/pull/1475), merge commit `7540932f61ee34cb2941405d13964b5aa90affb1` (verified on `main`): event streams are scoped by domain and team while the legacy team-file fallback remains readable.
- G682 — [PR #1477](https://github.com/J-Tech-Japan/intent-system/pull/1477), merge commit `bbcc360255ecc01fefbf30f4ea06687b763208e6` (verified on `main`): pre-approval and pre-escalation records loudly report inapplicability when no prompt-class producer exists and fail closed until coverage is real.
- G683 — [PR #1479](https://github.com/J-Tech-Japan/intent-system/pull/1479), merge commit `358d8b83b3ea53ae62a5f8323a9b2a26db34235e` (verified on `main`): literal prompt classes come from kind recipes, matched answers are bounded and audited, and unknown or unmatched prompts remain escalate-only.
- G684 — [PR #1481](https://github.com/J-Tech-Japan/intent-system/pull/1481), merge commit `23a90d36ec9907541b1b3aa6aec789cf3ea00df7` (verified on `main`): security-envelope recipe drift is detect-only, with observed and recorded shapes named while model and reasoning effort remain human-selected wish fields.
- G685 — [PR #1483](https://github.com/J-Tech-Japan/intent-system/pull/1483), merge commit `5e6bf6b6f1ffa3e882c8445960881ed85cc415d7` (verified on `main`): grammar-only model and effort resolution uses host-local positive or negative evidence and a live same-kind argv fallback, never a shipped model list.
- G686 — [PR #1485](https://github.com/J-Tech-Japan/intent-system/pull/1485), merge commit `e759bc04eeb4e4a56ac5334401b130fd749cb084` (verified on `main`): typed host-recorded envelope profiles have explicit precedence and invalid profile shapes fail closed before registry fallback.

### Full first-parent range accounting

The first-parent range has ten commits. The nine merge rows above account for
the feature units; the remaining commit is the post-release context and is not
a release execution unit.

| account | full commit | treatment |
| --- | --- | --- |
| post-release roll to 0.19.1 | `32fefec52ae353dbbe10b827020047c57ddfa279` | accounted for; context, not a unit |
| G678 merge | `3671ba062cd1a4e4b54d634e7160da381fdd3ceb` | merged unit above |
| G679 merge | `42789d6d8b1e4ac0d7133a277decd6ebcddeaf6b` | merged unit above |
| G680 merge | `46836e83098c6dd1192beeffe7daf6a32c529d89` | merged unit above |
| G681 merge | `7540932f61ee34cb2941405d13964b5aa90affb1` | merged unit above |
| G682 merge | `bbcc360255ecc01fefbf30f4ea06687b763208e6` | merged unit above |
| G683 merge | `358d8b83b3ea53ae62a5f8323a9b2a26db34235e` | merged unit above |
| G684 merge | `23a90d36ec9907541b1b3aa6aec789cf3ea00df7` | merged unit above |
| G685 merge | `5e6bf6b6f1ffa3e882c8445960881ed85cc415d7` | merged unit above |
| G686 merge | `e759bc04eeb4e4a56ac5334401b130fd749cb084` | merged unit above |

### Four attributed origins

These units form one day-scale feedback loop, but measured facts remain
separately attributed under G625:

- The operator's landing-authority and multi-user requests produced G678–G681.
  These are operator-request origins, not measurements borrowed from another
  team.
- The operator-filed [#1469 audit](https://github.com/J-Tech-Japan/intent-system/issues/1469)
  produced the configured-looking-but-inert policy and envelope observations
  carried by G682–G684. Its audit attribution remains separate from this
  host's later corroboration.
- The neighboring-domain `--model sol` incident on 2026-08-12 produced the
  G685 model-resolution evidence: a `btx-mvc` launch returned account-shaped
  HTTP 400, and live same-kind argv supplied recovery evidence. That incident
  belongs to the neighboring domain, not this team's measurement.
- This team's own first-cycle drift finding on 2026-08-12 produced G686. It is
  this host's envelope-profile observation and is not relabeled as the #1469
  audit or the neighboring-domain incident.

The minor bump is checkable: operator-controlled landing and work ownership,
domain-scoped event streams, prompt-policy applicability and bounded audit,
detect-only envelope drift, host-local model resolution, and typed envelope
profiles were absent from the v0.19.0 line. This is additive preview
capability, not a patch-only correction.

## Deliberate boundaries

- This preparation changes version policy, release notes, readiness
  documentation, and release guards only. It makes no code or runtime behavior
  change.
- Earlier release notes are linked rather than restated. The feature list is
  bounded exactly to G678–G686; it does not silently include G687 or an earlier
  unit.
- Prepare-only remains in force: this PR creates no GitHub Release or tag,
  publishes no package, and does not run release automation.

## Release-readiness gate

Before the separate operator release action:

- `eng/version.json` records stable `0.19.0` and next `0.20.0`.
- The nine PRs and full merge commits above resolve on `main`, and every commit
  in `git log v0.19.0..main --first-parent` is accounted for by the range table.
- The preview statement precedes the feature description and links the 1.0
  compatibility promise.
- The EN/JA notes remain in parity under the G613 terminology policy; the
  release-notes count guard, version/readiness guards, full suite, and
  `git diff --check` are green.
- Prepare-only remains in force. Release creation, tagging, and package
  publication are separate operator actions.

## Publishing v0.20.0

Release creation remains a distinct operator action after this preparation is
merged and its readiness evidence is green. Only then may an authorized
maintainer create and publish the GitHub Release for `v0.20.0`; publishing it
triggers `release.yml` (`on: release: published`) to build and publish the
NuGet package and platform artifacts. After that separate release, roll
`eng/version.json` to stable `0.20.0` / next `0.20.1`, add the next DRAFT note
stubs in the same commit, refresh both readiness mirrors, and verify child-main
CI before calling the post-release roll complete.
