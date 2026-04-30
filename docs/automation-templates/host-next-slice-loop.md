# Host Next-Slice Planning Loop Template

Use this prompt at the top of one wake of the **parent-host
next-slice planning loop**. It runs AFTER a child PR has been
accepted and merged (per
[`host-review-loop.md`](./host-review-loop.md)) and decides which
slice (if any) to publish next as a child-issue contract. It does
NOT publish issues itself; that step belongs to the host's
issue-publish surface and is intentionally out of scope here.

This template is a planning prompt, not a coding prompt. It NEVER
opens a child PR, NEVER mutates child branches, and NEVER calls the
implementation-repo coding-automation loop directly.

> **Hard rules** (do not paraphrase):
> - This template runs on the **host side**. It plans next-slice
>   work but does NOT author or push implementation changes itself.
> - `intent-cli` MUST NOT launch any AI provider. Planners (host
>   operator, AI planner) consume `intent-cli` JSON; the CLI does
>   not spawn them.
> - Do NOT call `intent-cli run` from this path.
> - Target selection for implementation-side wakes remains
>   single-sourced through `intent-cli worker next-action`. This
>   loop must NOT author "claim by hand" instructions for the child
>   loop.
> - `intent-pr-created` belongs on the source ISSUE. The next-slice
>   loop does not flip PR-side labels at all; it operates on the
>   parent-host packet surface and (eventually) the host's child
>   issue surface.
> - At most ONE next-slice candidate is promoted per wake.

## Inputs

- `--root <host-root>` — parent-host packet root (e.g. an
  `MyIntentHost`-style `.intent-cli/` tree).
- The execution unit ID just merged (or `none`, if this is a
  cold-start planning wake).

## Step 1: post-merge metadata closeout (when applicable)

If a child PR was just accepted and merged, the host packet for
that execution unit needs a closeout transition. Use the bounded
controlled writer:

```bash
intent-cli metadata update \
  --root "$HOST_ROOT" \
  --execution-unit "$EXEC_UNIT" \
  --mode completed-closeout \
  --linked-pr "$PR_NUMBER" \
  --linked-pr-repo "$REPO" \
  --linked-pr-url "$PR_URL" \
  --head-sha "$HEAD_SHA" \
  --merge-commit "$MERGE_COMMIT" \
  --format json
```

Boundaries (see [`metadata-safety.md`](./metadata-safety.md)):

- `--root` is REQUIRED. There is no fallback to the process
  working directory.
- The only supported mode for now is `completed-closeout`. Any
  other transition needs a new G2xx slice that adds the mode +
  tests.
- Refuse-to-clobber: `metadata update` will refuse if the queue
  item is already completed or `publish.yaml` already has a `pr:`
  block.

If the merge state is unclear, do NOT run `metadata update`. Stop
and open a clarification on the host side.

## Step 2: validate the metadata graph after closeout

Run the read-only validator to confirm the post-closeout graph is
self-consistent (queue / publish / runs / review-context).

```bash
intent-cli metadata validate \
  --root "$HOST_ROOT" \
  --execution-unit "$EXEC_UNIT" \
  --format json
```

If `errors[]` is non-empty after `metadata update`, treat as a
host clarification stop — do not patch around it from this prompt.

## Step 3: pick at most one next-slice candidate

Inspect the host packet root for execution units that are:

- contract-ready (issue body would pass the Child Issue Contract
  gate, with all required standalone sections present);
- not blocked by an unresolved dependency;
- not already published (no open or merged child PR for the
  execution unit);
- not in cooldown.

If multiple candidates exist, prefer the one whose acceptance is
the smallest scope and whose verification is the cheapest to run.
This is host-owner judgement; the prompt's job is to make the
choice explicit, not to invent ranking automation here.

If NO candidate is ready, the next-slice wake is **idle**. Do not
mutate metadata, do not write child issues, do not push. Idle
wakes leave the host packet root byte-identical.

## Step 4: dry-run the publish for the chosen candidate

Use the host's existing planning surface (NOT this template) to
prepare the candidate for publication. Stop short of mutating any
external system from inside this prompt. The published-issue surface
on the host side is responsible for actually creating the child
issue with the standalone contract sections; this template
intentionally describes the planning step, not the publish step.

If the host has a packet `--dry-run` shape for the publish step,
emit it here and end the wake. Otherwise the wake's deliverable is
the chosen `execution_unit` plus the rationale.

## Step 5: hand off (do not call the child loop directly)

The next-slice loop's output is a planning artifact:

- the chosen execution unit ID,
- the dependency / sequencing rationale,
- a pointer to the host packet under `--root`.

The actual publish step happens via the host's child-issue
publication surface. The implementation-repo coding-automation loop
will then pick the published issue up via `intent-cli worker
next-action` on its next wake; this template MUST NOT shortcut that
boundary by calling the child loop directly.

## What this template forbids

- Authoring implementation changes. Next-slice planning describes
  what to publish; coding-automation produces the implementation.
- Mutating child PRs / branches / issues from this prompt.
- Adding or removing `intent-target` from the **child** loop's
  perspective. The host's review-loop / publish-step / next-slice
  step may flip `intent-target`; this is the host-side label
  authority, not the child-side coding-automation authority.
- Adding `intent-pr-created` anywhere. That label is an issue-side
  completion marker applied during child-side
  `worker complete --kind issue --outcome pr-created`; the
  next-slice loop does not touch it.
- Calling `intent-cli run`.
- Asking `intent-cli` to launch an AI provider.
- Mass-editing parent-host packet content. Use `metadata update`
  only with an explicit supported mode.

## Boundary against the coding-automation loop

| Concern                  | Host next-slice loop (this template) | Coding automation loop ([`coding-automation-loop.md`](./coding-automation-loop.md)) |
|--------------------------|--------------------------------------|---------------------------------------------------------------------------------------|
| Side                     | host                                 | child / implementation                                                                |
| Purpose                  | choose what to publish next          | implement what is already published                                                   |
| Touches host packet      | yes — bounded `metadata update`      | no                                                                                    |
| Touches child PR         | no                                   | yes — via `gh-issue-to-pr` / `gh-fix-pr-comment`                                      |
| Calls `intent-cli run`   | no                                   | no                                                                                    |
| Spawns AI provider via CLI| no                                  | no                                                                                    |
| Output                   | a planning artifact (which slice)    | a draft PR (or a repair commit)                                                       |

When in doubt, ask: "Is the deliverable a planning decision, or a
code change?" Planning lives here; code changes live in
[`coding-automation-loop.md`](./coding-automation-loop.md).

## Boundary against the host review loop

| Concern                  | Host review loop ([`host-review-loop.md`](./host-review-loop.md)) | Host next-slice loop (this template) |
|--------------------------|--------------------------------------------------------------------|--------------------------------------|
| Trigger                  | a child PR is awaiting review                                     | a child PR was just merged, OR cold-start planning |
| Output                   | review verdict + label transition + comment                       | chosen next execution unit (planning artifact)     |
| Mutates                  | review-side labels on the child PR                                | host packet metadata under `--root`                |

Both run on the host side, but they should be separate wakes /
prompts. Mixing them obscures which transition actually fired and
makes failure modes harder to diagnose.
