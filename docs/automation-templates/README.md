# Local Coding Automation Prompt Templates

Operator-dogfooding-grade prompt templates that drive the local
Claude/Codex coding automation loop entirely through the deterministic
`intent-cli` worker and metadata commands. The templates are
intentionally narrow in scope: they capture the prompt text an operator
hands to the AI worker, not the AI worker behavior itself.

These templates assume:

- `intent-cli` is on the operator's `PATH`, or invoked through a pinned
  wrapper exposed by the operator's `MyIntentHost` setup.
- The local automation loop reads an `intent-cli worker next-action`
  classification and runs at most ONE workflow per wake.
- All target selection, label policy, and outcome normalization is done
  by `intent-cli`; prompts must NOT reimplement label-selection logic in
  natural language.

## Layered intent-cli surface (consumed by these templates)

| Slice | Command | Role |
|-------|---------|------|
| G202  | `intent-cli worker issue-preflight`     | "Is this issue actionable?" |
| G203  | `intent-cli worker pr-review-preflight` | "Is this PR ready to review?" |
| G204  | `intent-cli worker pr-comment-preflight`| "Does this PR need repair?" |
| G205  | `intent-cli worker result-summary`      | Normalize a worker run's outcome |
| G206  | `intent-cli worker next-action`         | Pick at most one target |
| G207  | `intent-cli metadata validate`          | Mechanical packet validator |
| G208  | `intent-cli metadata update`            | Bounded controlled writer |

## Template index

| File | Path | What it covers |
|------|------|----------------|
| [`coding-automation-loop.md`](./coding-automation-loop.md)         | combined loop | One wake: choose between PR repair / issue-to-PR / none |
| [`issue-to-pr-execution.md`](./issue-to-pr-execution.md)           | issue handoff | Run `gh-issue-to-pr` workflow on the returned issue URL, summarize result |
| [`pr-comment-fix-execution.md`](./pr-comment-fix-execution.md)     | PR repair handoff | Apply repair on the returned PR URL, summarize result |
| [`metadata-safety.md`](./metadata-safety.md)                       | metadata boundaries | Validate before / after, controlled update only when explicit |
| [`dry-run-checklist.md`](./dry-run-checklist.md)                   | operator dry-run | Read-only pre-flight to confirm wrapper / commands / no-action / metadata wiring before arming the loop |

## Hard rules these templates enforce

- **Single source of truth for target selection**: prompts must call
  `intent-cli worker next-action --format json` and act on its result.
  No manual label-walking in the prompt body.
- **No provider launch from `intent-cli`**: `intent-cli` is deterministic
  support tooling; it must NEVER spawn Claude / Codex / any AI provider.
  External AI workers run separately and consume `intent-cli` JSON.
- **Do not call `intent-cli run`** from this local coding automation
  path. `intent-cli run` is a separate command family with different
  semantics and is out of scope here.
- **Label policy invariant**: `intent-pr-created` belongs on the source
  ISSUE, not on the PR. Prompts and post-run summaries that imply
  otherwise are a bug — `worker result-summary` will surface this as a
  warning automatically when it detects misuse.
- **Single-target cap**: at most ONE branch/PR is touched per wake. If
  `worker next-action` returns `none`, the wake is idle and ends without
  pushing anything.

## What is intentionally out of scope here

- new worker commands;
- changes to `intent-cli run`;
- scheduler / cron registration;
- mutating GitHub issues or PRs from prompts;
- writing into the parent-host `MyIntentHost` repository from this
  child repository's prompts;
- distributing these templates as a public package.
