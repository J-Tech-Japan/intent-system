# Metadata Safety: Validate Before / Update With Intent

These guidelines apply when a coding-automation prompt is about to
operate against a parent-host packet root (e.g. `MyIntentHost`-style
`.intent-cli/issues/<execution-unit>/`). They keep packet metadata
consistent without giving the prompt free-form write authority.

> **Hard rules** (do not paraphrase):
> - Read-only validation FIRST. Always run `intent-cli metadata
>   validate` against the selected execution unit before invoking any
>   metadata writer.
> - Use `intent-cli metadata update` only with an EXPLICIT supported
>   transition mode. Do not use it to mass-edit packet content.
> - `--root <path>` is REQUIRED for `metadata update`. There is NO
>   silent fallback to the process working directory.

## When to call validate

| Situation                                              | Call                                              |
|--------------------------------------------------------|---------------------------------------------------|
| Before queueing or routing a packet                    | `metadata validate --root <host> --execution-unit <ID>` |
| Before a closeout / merge step                         | `metadata validate --root <host> --execution-unit <ID>` |
| After applying a `metadata update` transition          | `metadata validate --root <host> --execution-unit <ID>` (sanity check) |
| When G207 surfaces warnings via `worker result-summary` | run `metadata validate` to capture full finding list |

Sample call:

```bash
intent-cli metadata validate \
  --root "$HOST_ROOT" \
  --execution-unit "$EXEC_UNIT" \
  --format json
```

Treat any `errors[]` item as a hard stop unless an explicit transition
mode is intended to fix it (see G208's filter for
`consistency.queue.completed.missing_closure`).

## When to call update

`metadata update` is a controlled writer. This slice (G208) supports
exactly one transition mode:

| Mode                  | What it does                                                                                       |
|-----------------------|---------------------------------------------------------------------------------------------------|
| `completed-closeout`  | On the selected queue item: set `state=completed`, attach object-shaped `linked_pr`, write `head_sha` + `merge_commit`; append a `pr:` block to `publish.yaml`; append one `metadata-update.completed-closeout` event to `runs.jsonl`. Refuse-to-clobber if the queue item is already completed or `publish.yaml` already has a `pr:` block. |

Sample call:

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

Pre-validation runs automatically inside `metadata update`; it refuses
on any hard error other than the one it exists to fix.

## Bounded write surface

`metadata update` modifies ONLY these files under `--root`:

- `.intent-cli/queue-state.json` (matching item only)
- `.intent-cli/issues/<execution-unit>/publish.yaml` (append `pr:` block)
- `.intent-cli/runs.jsonl` (append one event line)

A whole-workspace byte-snapshot test in the G208 implementation locks
this invariant. Any other file is byte-identical before and after.

## What this template forbids

- Calling `metadata update` without `--root`. The flag is required;
  there is no fallback to the process working directory.
- Using `metadata update` for transitions other than the supported
  modes. New transition needs go through a new G2xx slice that adds
  the mode + tests.
- Calling `metadata update` to "fix" arbitrary metadata drift. If a
  packet is hard-invalid, treat it as a clarification stop and let the
  parent-host owner repair the source.
- Touching the parent host repository's git state from this child
  repository's prompts. The metadata writer mutates the packet files
  in place; the operator (or the host's own automation) commits them.
- Asking `intent-cli` to launch any AI provider during validation or
  update.
