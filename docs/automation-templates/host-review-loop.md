# Host Review Loop Template

Use this prompt at the top of one wake of the **parent-host review
loop**. It looks at child PRs that the implementation repo has
published for review and either records a deterministic review
verdict for the host owner, or flips the PR back to a labeled state
that asks for repair / re-review / closeout — all WITHOUT mixing in
implementation-repo coding-automation work.

This template is the host-side counterpart to
[`coding-automation-loop.md`](./coding-automation-loop.md). The two
loops MUST NOT be combined in a single wake. Host review is about
deciding what to do with finished implementation output; coding
automation is about producing implementation output. They sit on
opposite sides of the host ↔ child boundary and have different
write-surface expectations.

> **Hard rules** (do not paraphrase):
> - This template runs on the **host side** (parent-host repo),
>   reviewing PRs published by the **child** implementation repo.
>   Do NOT use this loop to author implementation changes.
> - `intent-cli` MUST NOT launch any AI provider. Reviewers (host
>   operator and/or AI reviewer) consume `intent-cli` JSON; the CLI
>   itself does not spawn them.
> - Do NOT call `intent-cli run` from this path.
> - Target selection is single-sourced through the deterministic
>   worker / metadata commands. Prompts MUST NOT manually walk labels
>   in natural language.
> - `intent-pr-created` belongs on the source ISSUE, not on the PR.
>   This invariant holds during review-side label decisions too — the
>   review loop never adds `intent-pr-created` to a PR.
> - At most ONE PR is touched per wake (single-target cap).

## Inputs

- `--repo <owner/repo>` — the child implementation repo whose PRs
  are being reviewed.
- A host operator (or AI reviewer) prepared to make a verdict per
  PR.

## Step 1: confirm metadata is reviewable

Before flipping any review-side label, validate the parent-host
packet for the execution unit being reviewed. This step is
read-only.

```bash
intent-cli metadata validate \
  --root "$HOST_ROOT" \
  --execution-unit "$EXEC_UNIT" \
  --format json
```

If `errors[]` is non-empty, treat the review as a clarification stop
on the host side. Do not patch metadata from inside this prompt;
follow [`metadata-safety.md`](./metadata-safety.md) for the bounded
write surface.

## Step 2: ask the worker / next-action selector for context

Run the read-only host selector before manual review judgement:

```bash
intent-cli automation host-review-preflight \
  --repo "<owner>/<repo>" \
  --format json
```

Use `action` as the mechanical host-loop branch:
`review-pr`, `skip-next-slice-due-to-wip`, `candidate-ready`,
`no-actionable-item`, or `clarification-required`. The command does
not mutate labels, claim PRs, create issues, run providers, or perform
semantic review; it only reports target ids and WIP context.

The implementation-side `worker next-action` selector tells you
which child PRs are currently `repair-required`, which are
`ready-to-implement`, and which are idle. Use that read as the
deterministic context for review decisions — even though the review
loop's actual target list is the set of child PRs in
`intent-pr-rereview-ready` / open-default / any host-owned
review-side label states. `intent-pr-created` is NOT a PR-side
state; it is the issue-side completion marker and never appears in
the PR review queue.

```bash
intent-cli worker next-action \
  --repo "<owner>/<repo>" \
  --format json
```

Treat the JSON as advisory: it tells you whether the implementation
side currently has a claim in flight or is idle, which informs your
review pacing. The review loop itself does NOT consume the
`recommendedWorkflow` field for action — it consumes it as a
"don't-step-on-the-implementer" hint.

## Step 3: pick at most one PR for review

From the host's review queue (the set of open PRs published by the
child repo for review), pick at most ONE PR to act on this wake.
Selection criteria (host-owner judgement):

- Is the PR contract-complete? (issue body was a sufficient
  standalone contract; PR scope matches.)
- Does the implementation match the slice's In Scope / Out Of Scope?
- Are the verification artifacts referenced in the PR description
  present and runnable?

If none meet the bar, the wake is **idle on the review side**. Do
not push, do not flip labels.

## Step 4: produce a deterministic review verdict

Before applying any host-owned PR label transition, run the installed
binary preflight:

```bash
intent-cli automation doctor --format json
```

Also run the host review target preflight against the child repo before
label mutation:

```bash
intent-cli automation host-review-preflight \
  --repo "$CHILD_REPO" \
  --format json
```

The preflight is read-only. Before the host loop attempts a GitHub
label mutation it MUST mechanically check the capability JSON emitted by
`automation doctor --format json` (or `automation summary --format json`)
and confirm that `automationCommandCapabilities[]` contains every
required capability name for this loop:

- `pr-transition.review-start`
- `pr-transition.request-update`
- `pr-transition.approved`

If `automationCommandSurfaceVersion` is missing, if either preflight
reports `stale-host-cli`, or if any required capability above is absent
from the JSON, stop and refresh the installed CLI using the host-local
procedure in
[`README.md`](./README.md#refreshing-the-host-local-installed-cli);
do not fall back to raw `gh pr edit` label mutation for installed
transitions, and do not infer command availability from `--help` text.

**CI-pending is a defer condition, not a request-update finding.**
At or immediately after candidate discovery, inspect the PR's required
checks against its current head SHA
(`gh pr view <n> --json headRefOid` + `gh pr checks <n>` or the
`statusCheckRollup`). When required checks are still pending / queued /
in-progress, DEFER the PR: do not acquire `intent-pr-reviewing` solely
to wait on CI, and if `review-start` was already applied this wake,
release it cleanly with `intent-cli automation pr-transition
--transition review-release --write` rather than leaving a stale review
lease. A pending check is never an implementation finding and MUST NOT
become a `request-update` comment — the implementer cannot repair
pending CI from the PR branch. Only a concluded FAILED required check is
a request-update finding.

**Closeout runs through installed intent-cli surfaces only.** Review,
approval, merge verification, and closeout are driven by installed
commands — `intent-cli review closeout-plan`, `intent-cli guide review`,
`intent-cli automation pr-transition`, `gh pr view --json merged`, and
`intent-cli closeout pr`. Local runtime skills (for example a historical
`intent-review-closeout` skill) are convenience adapters, not canonical
dependencies; routine host review and closeout MUST NOT require them and
MUST NOT fail a wake just because such a skill is not installed.

Choose ONE of the following review verdicts. Each has an explicit
label transition. For host-owned review-start, request-update, and
approved transitions, the normal installed path is
`intent-cli automation pr-transition --write`; this prompt does not
hand-apply those labels.

| Verdict                | Label transition                                                                          |
|------------------------|--------------------------------------------------------------------------------------------|
| accept-and-merge       | remove `intent-pr-reviewing`; add `intent-pr-approved`; the PR is then merged via the host's merge step. |
| request-update         | remove `intent-pr-reviewing` (if present); add `intent-pr-request-update` with comment(s).|
| accept-as-rereview-ready | (post-repair) remove `intent-pr-update-in-progress`; add `intent-pr-rereview-ready`.   |
| reject-clarification   | leave a host clarification comment with the cooldown marker; do NOT flip review labels.    |

Notes:

- `intent-pr-created` MUST NOT appear in the add column for any
  verdict above. It is an issue-side completion marker, not a
  PR-state marker.
- `intent-target` is owned by the host's labeling policy. The
  child-side automation NEVER adds or removes it; host review uses the
  installed transition command for review-start.
- Review start MUST use the installed command path:

```bash
intent-cli automation pr-transition \
  --repo "$CHILD_REPO" \
  --pr "$PR_NUMBER" \
  --transition review-start \
  --write \
  --format json
```

- Request-update MUST use the installed command path:

```bash
intent-cli automation pr-transition \
  --repo "$CHILD_REPO" \
  --pr "$PR_NUMBER" \
  --transition request-update \
  --write \
  --format json
```

- Approval MUST use the installed command path:

```bash
intent-cli automation pr-transition \
  --repo "$CHILD_REPO" \
  --pr "$PR_NUMBER" \
  --transition approved \
  --write \
  --format json
```

`review-start` adds `intent-target` and `intent-pr-reviewing` while
clearing stale `intent-pr-rereview-ready` and legacy `rereview-ready`.
Those rereview-ready labels are optional cleanup labels; if either is
already absent, the installed command treats it as already cleared.
`request-update` removes `intent-pr-reviewing` and adds
`intent-pr-request-update`. `approved` removes `intent-pr-reviewing` and
adds `intent-pr-approved`. These commands are the supported installed
path for those host-owned PR label transitions.

## Step 5: write the review verdict comment

The review verdict must be recorded as a PR comment that the
implementation repo's child loop can consume on its next wake. The
comment language matches the verdict:

- **accept-and-merge**: state the merge intent and reference the
  passed verification.
- **request-update**: state the narrow acceptable fix the
  implementer should apply on next wake. Be precise: list the file
  / behavior / single-line change so the child loop can apply only
  the narrow fix without widening scope.
- **accept-as-rereview-ready**: state that the repair was acceptable
  and re-review is requested.
- **reject-clarification**: leave the cooldown marker
  `<!-- intent-automation-cooldown YYYY-MM-DDTHH:MM:SSZ -->` so the
  child loop can detect a stop.

## Step 6: optional — emit a structured next-slice classification

If the review verdict is `accept-and-merge`, the host loop hands off
to the next-slice loop. See
[`host-next-slice-loop.md`](./host-next-slice-loop.md) for the
intent of that handoff. Do NOT inline next-slice planning in this
review prompt.

## What this template forbids

- Authoring implementation changes from the host loop. Host review
  describes what should change; child coding automation makes the
  change.
- Adding `intent-pr-created` to a PR. It is an issue-side label.
- Adding or removing `intent-target` from the **child** loop. The
  host's review loop may flip it; the child's coding-automation
  loop never does.
- Calling `intent-cli run`. The local coding automation path and
  the host review path both exclude `intent-cli run`.
- Mass-editing metadata. Use `metadata update` only with an
  explicit supported transition mode (see
  [`metadata-safety.md`](./metadata-safety.md)).
- Asking `intent-cli` to spawn an AI provider.

## Boundary against the coding-automation loop

| Concern                  | Host review loop (this template) | Coding automation loop ([`coding-automation-loop.md`](./coding-automation-loop.md)) |
|--------------------------|----------------------------------|---------------------------------------------------------------------------------------|
| Side                     | host                             | child / implementation                                                                |
| Purpose                  | verdict on finished work         | produce work                                                                          |
| Label authority          | review-side labels + `intent-target` (host-owned) | implementation-side labels (`intent-issue-in-progress`, `intent-pr-update-in-progress`, `intent-pr-rereview-ready`, `intent-pr-created` issue-side) |
| Touches `intent-cli run` | no                               | no                                                                                    |
| Spawns AI provider via CLI| no                              | no                                                                                    |
| Writes to parent host    | yes — via bounded `metadata update` only | no — host packet is read-only from the child side                              |

If a wake's intent is genuinely "review what the child built", use
this template. If it is "build the next slice", use
[`coding-automation-loop.md`](./coding-automation-loop.md). Never
mix.
