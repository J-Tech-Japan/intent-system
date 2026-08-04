# Start a project

← [docs index](README.md) | → [Getting started: the road to the first packet](02a-getting-started-orchestration.md)

This is **host/design** work. Paste the prompt below into your AI agent
design thread; the agent will run the intent-cli commands and bring back
questions or results.

## Design-thread prompt

Paste this into your AI agent (Claude, Codex, Copilot, etc.):

> I want to start or continue the project on `<owner>/<repo>`, domain `<name>`.
> Ask intent-cli what phase I'm in and what I should decide next.

## What the agent will run (for maintainers and troubleshooting)

> **Note:** The commands below are run by the AI agent internally. You do not normally need to run them manually. Refer to this section for maintenance or troubleshooting.

```bash
# Initialize a host domain (read-only without --write)
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write

# Initialize the intent tree + automation bindings for the domain
intent-cli intent init-tree --domain <name> [--target-repo <owner>/<repo>] --write

# Confirm the host is initialized (expects `ok`, not `partially-initialized`)
intent-cli intent host-check --domain <name> --format json

# Inspect current baseline / WIP / queued packets (read-only)
intent-cli intent status

# Plan the first slice (expects `design-needed` guidance on a fresh domain,
# never a hard `missing-domain-bindings` blocker)
intent-cli intent next-slice --domain <name> --dry-run --format json

# Ask what the work surfaces expect
intent-cli guide intent-work --format json
```

> **First-run note (G441):** `intent init` + `intent init-tree --write` now scaffold
> the durable-state skeletons (`.intent-cli/queue-state.json`, `.intent-cli/runs.jsonl`)
> and the domain automation bindings (`intents/<name>/automation/bindings.md`), so the
> first `host-check` reports `ok` and `next-slice` is not blocked by
> `missing-domain-bindings`. If a command looks stuck, **ask intent-cli for the next
> action** (`host-check`, `next-slice --dry-run`, `automation summary`) — you do not need
> to read intent-cli source code or hand-author `bindings.md` to recover first-run setup.

## Metadata / label safety

- `intent-target`, `intent-pr-*` and other workflow labels are applied by
  `intent-cli automation` / `intent-cli worker` commands — never by hand.
- Canonical state lives in the host repo's `.intent-cli/`; read it through
  intent-cli surfaces, don't edit `queue-state.json` directly.

## Repository topology choices

Before initializing, choose how you want to store host metadata. Two topologies
are fully supported.

### Topology A — separate host repository

The host orchestration repository is a dedicated repo (e.g. `myorg/my-project-host`).
Child implementation work happens in one or more separate target repos
(e.g. `myorg/my-project`).

```
myorg/my-project-host      ← host repo
  .intent-cli/             ← queue-state, config, intent tree
  intents/
  AGENTS.md

myorg/my-project           ← child implementation repo
  <source code>
  (no .intent-cli/)
```

- `intent-cli intent init` is run from the **host repo** checkout.
- The host agent invokes `intent-cli automation` and `intent-cli review` commands
  from the host checkout.
- Child implementation agents clone or work from the implementation repo; they
  never access the host repo.

### Topology B — same repository with a metadata branch

The host and child implementation live in **the same repository**. Host metadata
is kept on a dedicated branch (commonly `main-metadata`) so that implementation
PRs target the implementation base branch (`main`) without carrying metadata.

```
myorg/my-project           ← single repo
  branch: main             ← implementation code, child PRs target here
  branch: main-metadata    ← .intent-cli/, intents/, AGENTS.md (host-only)
```

- `intent-cli intent init` is run from a **metadata branch checkout**.
- Implementation PRs target `main`; metadata stays on `main-metadata`.
- Child implementation agents are **GitHub-contract-only** in both topologies:
  they do not read or modify the metadata branch.

### Which topology to prefer

| Consideration | Separate host repo | Same repo + metadata branch |
|---|---|---|
| Team keeps intent orchestration and implementation clearly separated | ✓ natural boundary | more discipline required |
| Fewer repositories to manage | more repos | ✓ single repo |
| Open-source project where contributors only see implementation | ✓ host repo can stay private | metadata branch is visible to all |
| Existing single repo that you want to add intent-cli to | migration cost | ✓ lower cost |

Both topologies are valid. Pick whichever fits your team's existing conventions.

## Next

[Getting started: the road to the first packet](02a-getting-started-orchestration.md).
