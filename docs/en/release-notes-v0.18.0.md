# Release Notes — intent-cli v0.18.0

> **prepare-only / UNRELEASED.** This change prepares version state, release
> notes, and readiness documentation only. It does not create a GitHub Release
> or tag, publish a package, run the release workflow, or perform the
> post-release roll.

Install verification: `JTechJapan.IntentSystem.Cli --version 0.18.0`.
After the separate operator release action, the Release will be published at
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.18.0.
The preceding shipped scope remains in the
[v0.17.0 notes](release-notes-v0.17.0.md); it is linked rather than restated.

## Preview lane — read before the feature description

`guide bootstrap` and the advisor's `bootstrap-resume` action are
`preview-through-1.x`, outside the
[1.0 compatibility promise](1.0-compatibility-promise.md). They may change or
be withdrawn during 1.x and are not part of the 1.0 compatibility commitment.

## The application conversation is the front door

v0.18.0 contains exactly one merged unit. The list was independently derived
with `git log v0.17.0..main`; every commit in that three-commit range is
accounted for below, and the listed merge resolves on `main`.

- G664 — PR #1435, merge commit `40081137` (verified on `main`): the
  application-front-door bootstrap turns one request into a guided herdr-only
  team genesis and delegates the first task to that team.

Start the guide with either exact trigger phrase:

- English: `Start this work in a herdr-only team.`
- Japanese: `herdr-only で起動して。`

The rendered pass keeps these six steps in order:

1. Ask the human which CLI and model each design, orchestration,
   implementation, and review seat should run. A missing answer is a named gap;
   there are no defaults.
2. Emit the herdr workspace, pane, and typed-seat commands from the installed
   per-kind recipes and the linked G637 layout guide.
3. Record operator-supplied topology through the canonical topology writer,
   then validate and show the roster.
4. Emit `notify supervise install` so the human can inspect and register the
   scheduler artifact.
5. Ask the human for the application agent kind and whether it has an inbound
   app monitor, then apply the linked G654 design-seat placement rule. The CLI
   never assumes either answer.
6. Delegate the first task to orchestration with a fresh task id and result
   nonce; do not run that task in the application conversation.

The rendered output ends with the explicit statement:

> **HANDOFF:** State which recorded thread is now the design seat. The
> application conversation remains the operator's front door for new requests;
> it is not a design, orchestration, implementation, review, or supervision
> loop seat.

Recorded topology selects the idempotent `join-and-delegate` path. The guide
does not recreate the workspace or already recorded seats; it names partial
state such as `topology-recorded-seats-missing` and
`topology-recorded-supervision-and-handoff-missing`, then emits only missing
steps. `guide next` recommends `bootstrap-resume` when topology is recorded but
the supervision-cycle/front-door handoff is incomplete. Absent topology is
silent because bootstrap has not started, and a completed cycle clears the
recommendation.

## Merged-tree design verification

On merged head `40081137`, design built the CLI and rendered the guide against
this host's real data in Markdown and JSON, with and without `--team`: eight
consecutive renders exited 0. The rendered questions asked the human for the
seat CLI/model and application kind, the join and named partial-state paths
were present, the final output was the HANDOFF statement, and `guide next`
showed and cleared `bootstrap-resume` along the recorded lifecycle. This
verification used the merged tree and real host data, not a diff-only reading.
It verifies the one-keyword claim on the shipped tree. A source audit also
confirmed that the command's only `Process` occurrence is a string field name;
there is no execution path behind the guide.

## Three-commit derivation

This table accounts for every commit returned by `git log v0.17.0..main`. The
post-release roll is range context, not a release execution unit.

| account | commit | release-unit treatment |
|---|---|---|
| post-release roll to 0.17.1 | `c2746f26` | accounted for; not a unit |
| G664 implementation | `229e5522` | implementation commit for G664 |
| G664 merge | `40081137` | the one merged execution unit |

## Deliberate boundaries

- This preparation changes version policy, notes, readiness documentation, and
  release guards only; it makes no product-code or runtime-behavior change.
- The guide emits questions and command text. intent-cli executes nothing: it
  never invokes herdr, starts a provider or seat, or registers/unregisters an
  OS scheduler artifact.
- No application-side integration code is added. The application conversation
  reads the guide and remains the operator's front door.
- Existing per-kind recipes, G637 layout, G654 design placement, topology,
  supervision-install, and delegation contracts are linked and composed; the
  release does not restate or change those rules.
- Join is idempotent, partial state is preserved, and no recorded team is
  casually forked or recreated.

## Why this is a minor release

`guide bootstrap` and the advisor's `bootstrap-resume` lifecycle were absent at
v0.17.0. They are new preview surfaces, not patch-only corrections to the
frozen v0.17.0 contract.

## Release-readiness gate

Before the separate operator release action:

- `eng/version.json` records stable `0.17.0` and next `0.18.0`.
- PR #1435 / merge `40081137` resolves on `main`, and all three commits in
  `git log v0.17.0..main` remain accounted for by the table.
- The preview statement precedes the feature description and links the 1.0
  compatibility promise.
- The bilingual release-notes and count guards pass, followed by the full
  Release suite, `git diff --check`, and exact-head CI.
- The [v0.17.0 notes](release-notes-v0.17.0.md) remain a link to the preceding
  shipped scope rather than content repeated here.
- Prepare-only remains in force: this PR creates no Release or tag, performs no
  package publish, and does no post-release roll.

## Publishing v0.18.0

Release creation remains a distinct operator action after this preparation is
merged and its readiness evidence is green. Conditional approval
`v0180-preapproved-001` is recorded for that separate action once its condition
holds; it does not turn this preparation PR into a release action. This
implementation PR does not perform that action.
