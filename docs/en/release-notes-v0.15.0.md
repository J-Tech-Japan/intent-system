# Release Notes — intent-cli v0.15.0

> **Prepare-only / UNRELEASED.** This preparation changes version state and
> documentation only. It creates no GitHub Release, tag, package publish, or
> release workflow run; Release creation remains the operator's step.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.15.0`.
When approved, the release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.15.0.
See the [v0.14.0 notes](release-notes-v0.14.0.md) and the
[v0.13.1 notes](release-notes-v0.13.1.md) for the preceding scopes; those
notes are linked, not repeated here.

## Preview lane — read before the feature list

G644 supervision setup discoverability and G645 guide reachability are
`preview-through-1.x`: they are outside the [1.0 compatibility
promise](1.0-compatibility-promise.md), may change or be withdrawn during the
1.x line, and are not 1.0 compatibility commitments.

## What's in v0.15.0

This minor release covers exactly two merged units: **G644 and G645**. The
list was derived by running `git log v0.14.0..main`; all six commits in that
range are accounted for as the post-release roll `96ab9947`, G644's
implementation `605f377a`, G644's repair `9aa377a1`, G644's merge `0031bfb1`,
G645's implementation `d6b64785`, and G645's merge `5ee849a8`. Both merge
commits are verified to resolve on `main`.

- G644 — PR #1392, merge commit `0031bfb1` (verified on `main`): the guides a
  role reads now name the supervision setup, and a team with no recorded
  supervision cycle is told to set it up.
- G645 — PR #1394, merge commit `5ee849a8` (verified on `main`): each packet
  declares which guide routes which role to every role-facing surface it adds,
  and an unsatisfied declaration is reported as
  `guide-reachability-pending` debt.

## Why this is a minor release

The minor bump is verifiable rather than assumed. G645 adds the packet
guide-reachability declaration and the `guide-reachability-pending` stall
class; neither existed at v0.14.0. This is a new packet and closeout surface,
not a patch-only correction to an existing contract.

## What these units make testable

intent-cli exists so that keeping the whole intent in view while adding one
feature is a mechanism inside the process, rather than something people must
remember to do. Guidance lives in the guide, not for a human to read as a
reference after the fact: the intended path is that a thread handed a keyword
converses with the guide, understands the surface, and acts. Writing something
in a reference is not completion. G644 routes the supervision setup through
the guides a role reads; G645 makes every future slice declare its guide route
and reports an omitted recording as debt.

## What was measured, and what remains guidance

The measurement that forced this work is concrete: immediately after the
supervision loop shipped, the installed v0.14.0 build's
`review-next-slice-loop`, `implementation-loop`, `init-host`, and `guide next`
guides mentioned `supervise` **zero times**. That is an observed discoverability
gap, not a claim about whether a deployment process was running.

G644's deployment facts are guidance: `guide next` can recommend
`supervision-setup` when no cycle is recorded, and the host-init/design-side
guides explain the deployment step, while `next` remains read-only and never
starts or manages a background process. The notes therefore do not present a
recorded cycle or a running deployment as measured merely because the guidance
exists; the operator must verify those facts at readiness.

## Boundaries and deliberate non-behaviour

- G644 surfaces a missing recorded cycle and points to setup; it does not start
  or supervise a background process.
- G645 never infers a route from a filename, keyword, or guide wording, and it
  never judges whether a guide is good.
- `guide-reachability-pending` is closeout debt, not a merge or closeout gate;
  an explicit no-role-facing-surface declaration is silent.

## Release-readiness gate

Before creating the v0.15.0 Release, the operator must verify:

- `eng/version.json` records stable `0.14.0` and next `0.15.0`.
- PR #1392 resolves on `main` at merge `0031bfb1` and PR #1394 resolves on
  `main` at merge `5ee849a8`; the six commits in `git log v0.14.0..main`
  remain fully accounted for above.
- The preview statement appears before the feature description, and the
  [1.0 compatibility promise](1.0-compatibility-promise.md) remains linked
  rather than restated.
- The bilingual release-notes guard and count guard pass, followed by the full
  Release suite and exact-head CI. Verify the measured-versus-guidance
  distinction before treating supervision deployment as ready.
- The [v0.14.0 notes](release-notes-v0.14.0.md) and
  [v0.13.1 notes](release-notes-v0.13.1.md) remain linked preceding scopes;
  this note does not restate them.
- Prepare-only remains in force: do not create the GitHub Release, tag, or
  package publish until the operator explicitly approves it.

## Publishing v0.15.0

After the readiness gate passes and the operator approves, a maintainer may
create the GitHub Release and publish the package. This preparation PR does
not perform that step.
