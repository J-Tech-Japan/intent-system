# G281 Align worker next-action PR update selection with automation check under child workdir context

## Why this slice

PR #660 had actionable repair feedback (`intent-target` plus
`intent-pr-request-update`) and `automation check --workdir <child>` selected
it as `pr-comment-fix`, but `worker next-action --repo … --workdir <child>`
returned `none` against the same GitHub state. That made child
implement/update loops miss valid PR repair work whenever the operator
followed the child-loop guide and supplied `--workdir`.

## What changed

- `worker next-action` treats `--workdir` strictly as **child worktree
  context**, not as the parent state root. Selection runs against GitHub
  state for `--repo` and never against the workdir's local filesystem.
  The parent host's `.intent-cli` remains the durable state root from the
  command's cwd.
- Source-issue labels never suppress PR comment-fix selection. A PR with
  `intent-target` + `intent-pr-request-update` (and without
  `intent-pr-update-in-progress` / `intent-pr-created` on the PR) is
  selected even when the source issue carries `intent-pr-created`.
- `--workdir` now emits operator-facing **advisory warnings** (without
  blocking selection) when the supplied path:
  - does not exist (`workdir '<path>' does not exist; selection used
    GitHub state from --repo only`), or
  - is not a git worktree (no `.git` entry).
- `automation check` and `worker next-action` produce identical results
  for the same `--repo` + `--workdir` against the same GitHub state.

## Boundaries

- Read-only. The command never mutates labels or parent files.
- `--workdir` is purely advisory metadata; the lister still queries
  GitHub for `--repo` exactly the same way regardless of workdir.
- No raw `gh` label fallback is introduced.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~WorkerNextAction"

git diff --check
```

Focused tests cover: PR with `intent-target` + `intent-pr-request-update`
selected as `pr-comment-fix` even when `--workdir` is a child worktree
without `.intent-cli` and the source issue carries `intent-pr-created`;
`--workdir` without a `.git` entry emits a warning but selection still
returns `pr-comment-fix`; missing `--workdir` directory emits a warning;
`automation check --workdir <child>` and `worker next-action --workdir
<child>` agree on the chosen PR.
