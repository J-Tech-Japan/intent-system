# Local Automation Dry-Run Checklist

Operator-dogfooding pre-flight checklist for the local Claude/Codex
coding automation loop. Run this BEFORE arming any scheduler / cron /
session loop so that the first real wake doesn't fail on wrapper /
PATH / command-shape drift.

This checklist is **read-only by default**. Every step here is either
a `--help`-style discovery call, a JSON-emitting selector that does
not mutate state, a fixture-driven sample render, or a packet
validator that never writes. No step in this checklist creates or
mutates GitHub issues, PRs, labels, branches, runs, or parent-host
queue state, and no step launches Claude, Codex, or any AI provider.

> **Hard rules** (do not paraphrase):
> - `intent-cli` is deterministic support tooling. It MUST NOT launch
>   Claude, Codex, or any AI provider — not from this checklist, not
>   from the local coding automation loop, not from prompts.
> - This local coding automation path MUST NOT call `intent-cli run`.
>   `intent-cli run` is a separate command family with different
>   semantics (integration smoke / deterministic replay / local
>   dogfooding) and is intentionally out of scope here.
> - Prompts MUST NOT manually reimplement label-selection logic when
>   `intent-cli worker next-action` is available. Target selection is
>   single-sourced through the worker selector.
> - `intent-pr-created` belongs on the source ISSUE, not on the PR.
>   `worker result-summary` surfaces this misuse as a warning; the
>   checklist preserves that invariant.

## When to run this checklist

- Before enabling a fresh local automation session for the first
  time on a given machine.
- After upgrading `intent-cli` or rotating the local wrapper that
  exposes it on `PATH`.
- After updating any prompt template under `docs/automation-templates/`.
- When a wake produced an unexpected `worker next-action` shape and
  you want to confirm the installed CLI matches the prompts on disk.

If every step below succeeds with the expected shape, the loop is
safe to arm. If any step fails, fix the underlying configuration —
do NOT paper over it inside the prompt.

## 1. Verify the wrapper / PATH

The local automation loop assumes `intent-cli` resolves through the
operator's `PATH`, optionally via a pinned wrapper.

```bash
command -v intent-cli
intent-cli --version
```

Expected:

- `command -v` prints a single absolute path.
- `--version` prints the installed tool's version string and exits 0.

If `command -v` prints nothing, the wrapper is not on `PATH` — fix
the wrapper / shell configuration and re-run this step. Do not
hard-code an absolute path in the prompt templates.

## 2. Verify command discovery

`intent-cli` exposes the layered worker / metadata surface that the
prompts depend on. Confirm the binary on `PATH` actually exposes
those subcommands.

```bash
intent-cli --help
intent-cli worker --help
intent-cli metadata --help
```

Expected (at minimum, names — exact help text may evolve):

- `worker` lists `issue-preflight`, `pr-review-preflight`,
  `pr-comment-preflight`, `result-summary`, `next-action`.
- `metadata` lists `validate`, `update`.

If any of those names is missing, the installed `intent-cli` is
older than the prompt templates expect. Upgrade before continuing.

## 3. Smoke-test `automation check` (target selection)

`automation check` is the preferred entrypoint for "what does this
wake do?". It infers the repo from the current worktree (or
`--workdir`) and delegates to the same selection semantics as
`worker next-action`. Confirm it returns parseable JSON with the
expected shape, against the live repo, in a no-op state.

```bash
intent-cli automation check \
  --format json
```

Expected:

- exit code 0,
- stdout is a single JSON object,
- top-level keys include at least `recommendedWorkflow` and
  `sourceClassification`.

Pipe through `jq` to confirm parseability:

```bash
intent-cli automation check \
  --format json \
  | jq '{recommendedWorkflow, sourceClassification}'
```

If the call cannot reach GitHub, fix the auth path (`gh auth status`)
before continuing. Do NOT fall back to manual label-walking inside
the prompt body — the loop is not safe to arm until this command
works end-to-end.

## 4. Smoke-test the no-action path

Verify the loop's idle behavior. When there is no eligible target,
`automation check` must be explicit about it; the prompt then
treats the wake as idle and stops without pushing anything.

```bash
intent-cli automation check \
  --format json \
  | jq -r '.recommendedWorkflow'
```

Expected on an idle repo (no `intent-target` issues, no
`intent-pr-request-update` PRs ready for repair): the output is
either `none` or another non-action value documented by the
selector. The local loop interprets that as **idle: do nothing,
push nothing, mutate no labels**.

If the selector instead returns an actionable workflow on an
already-quiet repo, stop and inspect — the prompt must not fabricate
a target on top of a non-action selector result.

## Host transition command smoke

Run this section before enabling command-only parent-host runbooks, or
after upgrading the local `intent-cli` wrapper. These examples are
read-only by default. They verify command availability and planned label
actions without mutating GitHub labels.

Use real, safe identifiers from a staging or currently selected host
target:

```bash
export CHILD_REPO="J-Tech-Japan/intent-system"
export ISSUE_NUMBER="<published-child-issue-number>"
export PR_NUMBER="<published-child-pr-number>"
```

### Host command availability

```bash
intent-cli automation doctor --format json \
  | jq '{status, readOnly, installedCliPath, commands: [.requiredCommands[] | {command, transition, available}]}'
```

Expected:

```json
{
  "status": "ok",
  "readOnly": true,
  "installedCliPath": "/path/to/host/.intent-cli/bin/intent-cli",
  "commands": [
    {"command": "intent-cli automation summary", "transition": null, "available": true},
    {"command": "intent-cli automation host-review-preflight", "transition": null, "available": true},
    {"command": "intent-cli automation issue-publish", "transition": null, "available": true},
    {"command": "intent-cli automation pr-transition", "transition": "review-start", "available": true},
    {"command": "intent-cli automation pr-transition", "transition": "request-update", "available": true},
    {"command": "intent-cli automation pr-transition", "transition": "approved", "available": true}
  ]
}
```

If `status` is `stale-host-cli`, or any command is unavailable, abort
the runbook and refresh the wrapper/tool before using host runbooks; do
not fall back to raw `gh issue edit` or `gh pr edit` label mutation for
installed transitions.

### Host review target preflight

```bash
intent-cli automation host-review-preflight \
  --repo "$CHILD_REPO" \
  --format json \
  | jq '{action, number, url, warnings}'
```

Expected:

- exit code 0,
- `action` is one of the documented host review preflight actions,
- `warnings` is empty or explicitly actionable.

This command is read-only. It must not claim a PR or change labels.

### Issue publish dry-run

```bash
intent-cli automation issue-publish \
  --repo "$CHILD_REPO" \
  --issue "$ISSUE_NUMBER" \
  --format json \
  | jq '{mode, applied, addLabels, removeLabels, warnings}'
```

Expected:

```json
{
  "mode": "dry-run",
  "applied": false,
  "addLabels": ["intent-target"],
  "removeLabels": [],
  "warnings": []
}
```

This dry-run is the safe local smoke. Use `--write` only after the host
has durably written the publish boundary and the issue number is the real
child issue to publish.

### PR transition dry-runs

Run all supported PR transitions without `--write`:

```bash
for transition in review-start request-update approved; do
  intent-cli automation pr-transition \
    --repo "$CHILD_REPO" \
    --pr "$PR_NUMBER" \
    --transition "$transition" \
    --format json \
    | jq '{transition, mode, applied, addLabels, removeLabels}'
done
```

Expected assertions:

- every object has `"mode": "dry-run"` and `"applied": false`,
- `review-start` plans to add `intent-target` and
  `intent-pr-reviewing`,
- `request-update` plans to add `intent-pr-request-update` and remove
  `intent-pr-reviewing`,
- `approved` plans to add `intent-pr-approved` and remove
  `intent-pr-reviewing`,
- no PR transition add/remove plan contains `intent-pr-created`.

Use `--write` only when the host loop has selected that exact PR and
verdict. Do not use `--write` as part of freshness smoke testing.

## 5. Smoke-test `automation complete` (issue-to-PR outcome)

`automation complete` is how each wake's result is normalized into a
stable JSON shape and, only with explicit `--write`, converted into
the supported completion label transition. Confirm it can render an
issue-to-PR outcome shape in dry-run mode.

```bash
intent-cli automation complete \
  --kind issue-to-pr \
  --outcome pr-created \
  --issue 123 \
  --pr 124 \
  --format json
```

Expected:

- exit code 0,
- stdout is a single JSON object with at least `kind`, `outcome`,
  `status`, `recommendedLabelActions[]`, `plannedLabelActions[]`,
  `appliedLabelActions[]`, and `summary`.

Important: this is a **render-only** call. It does NOT touch
GitHub labels unless `--write` is present. Use an issue number that
currently carries the claim label if you want `plannedLabelActions[]`
to show the success transition; otherwise the command may correctly
refuse as stale.

The issue-to-PR `pr-created` example intentionally includes `--pr`.
That value must be the created draft PR number in a real wake so
`automation complete --write` can apply the supported `intent-target`
transition to the PR. If the worker returns a PR URL, extract the number
before completion. Workers must not add that label with `gh pr edit`.

## 6. Verify the primary review-target handoff

After a real issue-to-PR wake creates a draft PR, run completion with
the created PR number and `--write`:

```bash
intent-cli automation complete \
  --kind issue-to-pr \
  --outcome pr-created \
  --issue "$ISSUE_NUMBER" \
  --pr "$CREATED_PR_NUMBER" \
  --write \
  --format json
```

Expected:

- the source issue keeps issue-side state: `intent-issue-in-progress`
  is removed and `intent-pr-created` is added,
- the created PR receives PR-side review state: `intent-target` is
  added by `automation complete --write`,
- no `intent-pr-created` label is added to the PR,
- no worker step uses `gh pr edit --add-label intent-target`,
- the host review loop can select the created PR through the primary
  `intent-target` PR selector path, without relying on fallback label
  repair.

Keep the issue and PR checks separate. The issue-side completion marker
is evidence that implementation finished; the PR-side `intent-target`
is evidence that review is queued.

## 7. Smoke-test `automation complete` (PR comment-fix outcome)

Repeat the same render-only check for the PR repair workflow. This
is the path the loop uses after a `gh-fix-pr-comment`-style fix.

```bash
intent-cli automation complete \
  --kind pr-comment-fix \
  --outcome repair-pushed \
  --pr 124 \
  --format json
```

Expected:

- exit code 0,
- the JSON's `plannedLabelActions[]` reflects the
  update-in-progress → rereview-ready handoff,
- the same planned transition removes `intent-pr-request-update` so
  the PR cannot remain both repair-requested and rereview-ready,
- the planned transition does NOT add a fresh `intent-pr-created` on
  the PR,
- `intent-pr-created` does not appear as a recommended add on a PR.
  If it does, treat it as a bug — `intent-pr-created` belongs on
  the source ISSUE, not on the PR.

## 8. Validate metadata against a sample / selected root

`metadata validate` is a pure read against parent-host packet
artifacts under a host-style `--root`. It NEVER writes. Use this
step to confirm both that the binary supports the expected packet
schema and that the operator's host root is wired correctly.

```bash
intent-cli metadata validate \
  --root "$HOST_ROOT" \
  --execution-unit "$EXEC_UNIT" \
  --format json
```

Expected:

- exit code 0 when the packet is valid.
- exit code non-zero on hard inconsistency. The JSON includes
  `valid`, `executionUnit`, `errors[]`, `warnings[]`, and
  `checkedFiles[]`.

If `$HOST_ROOT` / `$EXEC_UNIT` is not yet known, run against a
known-good fixture or skip this step — but do NOT arm the loop with
unverified metadata wiring on a path that the loop is expected to
touch.

This step performs no GitHub mutation, no file mutation, no queue
or runs mutation, no branch / worktree creation, no PR / issue
creation, no comment posting, no merge, and no provider launch.

## 9. Confirm controlled metadata-update boundaries

`metadata update` is a bounded controlled writer. It is **not**
part of dry-run smoke; it is mentioned here so the operator
internalizes its boundaries before the loop is armed.

Allowed:

- `metadata update --root <host> --execution-unit <ID> --mode completed-closeout …`
  with the full set of required arguments. Pre-validates with G207;
  refuses to clobber an already-completed item or a `publish.yaml`
  that already has a `pr:` block.

Not allowed (treat any of these as a clarification stop, not a
prompt patch):

- calling `metadata update` without `--root` (the flag is REQUIRED;
  there is no fallback to the process working directory),
- using `metadata update` for transitions other than the supported
  mode (new transitions go through a new G2xx slice that adds the
  mode + tests),
- mass-editing arbitrary packet content,
- touching parent host git state from this child repository's
  prompts.

See [`metadata-safety.md`](./metadata-safety.md) for the canonical
description of the bounded write surface.

## 10. Confirm prompt-template invariants

Before arming the loop, re-read the per-template hard rules:

- [`README.md`](./README.md) — index, layered surface, and the
  global "no provider launch / no `intent-cli run` / single source
  of target selection / `intent-pr-created` is an issue label /
  single-target cap" rules.
- [`coding-automation-loop.md`](./coding-automation-loop.md) — the
  combined per-wake loop that gates on `worker next-action`.
- [`issue-to-pr-execution.md`](./issue-to-pr-execution.md) —
  issue handoff + `worker result-summary` on completion.
- [`pr-comment-fix-execution.md`](./pr-comment-fix-execution.md) —
  PR repair handoff + `worker result-summary` on completion.
- [`metadata-safety.md`](./metadata-safety.md) — validate before /
  controlled update only on an explicit supported mode.

If any prompt body has drifted away from those invariants — for
example by reintroducing manual label-walking, or by calling
`intent-cli run`, or by recommending `intent-pr-created` on a PR —
fix the prompt before arming the loop.

## 11. Confirm what this checklist does NOT do

Explicit out-of-scope, by design:

- Does NOT register a scheduler, cron job, session loop, or daemon.
- Does NOT launch Claude, Codex, or any AI provider.
- Does NOT create or mutate GitHub issues, PRs, labels, comments,
  branches, or worktrees.
- Does NOT mutate parent-host queue state, runs logs, or packet
  files.
- Does NOT change `intent-cli run` behavior. The local coding
  automation path does not call `intent-cli run`.
- Does NOT distribute these templates as a public package.

If a future slice adds a machine-readable readiness command (e.g.
`intent-cli worker readiness`) the steps above are the manual
shape that command would encode; until then, this checklist is the
authoritative dry-run gate.
