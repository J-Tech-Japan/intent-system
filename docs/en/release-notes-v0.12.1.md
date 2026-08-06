# Release Notes — intent-cli v0.12.1

> **Prepare-only / UNRELEASED.** These notes are ready for operator review.
> This PR creates no GitHub Release, tag, package, or version-state change.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.12.1`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.12.1.
See the [v0.12.0 notes](release-notes-v0.12.0.md) for the preceding release;
that content is linked, not repeated here.

## v0.12.1 patch scope

The unit list below was derived by running `git log v0.12.0..main`. The eight
commits in that range are fully accounted for: the G631 repair series, the G632
repair series, and the post-release version roll `e209d03`.

- G631 — PR #1368, merge commit `4c4ef22` (resolves on `main`): every redirected
  child-process stream is decoded as UTF-8; the helper is `ProcessOutputEncoding`,
  and a guard fails the suite when a spawn site omits the declaration.
- G632 — PR #1367, merge commit `77a57f2` (resolves on `main`): `worker
  issue-preflight` derives target classification from declared `Repository:` and
  `Target paths:`; prose mentions are advisory notes.

This is a patch rather than a minor release: neither unit adds a command or a
flag. G631 declares encoding at existing spawn sites and renames an internal
helper; G632 changes how an existing classification is derived. Both claims
are verifiable in the linked merge commits and the `git log` enumeration.

### Operator impact: Windows child-stream decoding (G631)

On a console whose code page is not UTF-8, non-ASCII child output could corrupt
JSON and make every transport operation fail together. The symptom was an
intermittent, environment-triggered total loop stall; ambient bytes such as a
pane title in an unrelated workspace could trigger it. The workaround announced
with v0.12.0—avoiding non-ASCII text in pane titles and paths—is no longer
needed. A source guard now fails the test suite when a future spawn site omits
the UTF-8 declaration.

### Author impact: declaration-based preflight (G632)

An issue that declares the child repository as its target remains actionable
regardless of what its prose mentions; those mentions appear as advisory notes.
A declared submodule target with a working directory outside that submodule
still blocks, and an unreadable target declaration fails closed rather than
guessing from prose.

## Release-readiness gate

Before creating the v0.12.1 Release, the operator must verify:

- `eng/version.json` remains stable `0.12.0` / next `0.12.1`.
- PRs #1368 and #1367 resolve on `main` at merge commits `4c4ef22` and
  `77a57f2`, and `git log v0.12.0..main` accounts for all eight commits.
- The bilingual release-notes guard and G634 count guard pass, followed by the
  full Release suite and exact-head CI.
- This remains prepare-only: do not create a Release, tag, or package publish
  until the operator explicitly approves it.

## Publishing v0.12.1

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does not
perform that step.
