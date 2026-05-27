# Flat-to-Tree Restructure (G405)

← [Organize & maintain intents](03-intents.md) | → [Create packets & publish issues](04-packets-issues.md)

This page describes the design-AI-assisted workflow for restructuring an existing flat intent domain into the tree-v1 layout (G403/G404).
To start a restructure, ask `intent-cli` to check the current state in your AI agent design thread.

## When to use this

Use the restructure workflow when an existing flat intent domain grows large enough that:
- A single file is hard to search, review, or cross-link.
- You want to adopt tree-v1 organization without discarding existing content.

Existing flat domains remain valid. Restructuring is gradual and optional.

## Roles and responsibilities

| Actor | Responsibility |
|---|---|
| **intent-cli** | Deterministic analysis, proposed moves, reference maps, safety checks, linting, validation. Does NOT make semantic grouping decisions. |
| **Host/design AI + operator** | Semantic grouping decisions, moving/rewriting content, updating links, committing the result for review. |

## Step-by-step workflow

### 1. Analyze the flat domain (read-only)

```bash
intent-cli intent analyze-tree --domain <name> --format markdown
```

This outputs:
- Flat files found and their sizes
- Proposed category destinations for each H2/H3 heading (keyword heuristics — review and adjust)
- Detected references: markdown links, heading anchors, execution-unit IDs (e.g. G405), packet paths, GitHub issue/PR URLs
- Migration reference map: old path + heading → proposed new path

### 2. Lint before restructuring

```bash
intent-cli intent lint-layout --domain <name> --format markdown
```

Note all `LARGE-FLAT-FILE`, `MISSING-MANIFEST`, and `BROKEN-RELATIVE-LINK` warnings.

### 3. Initialize the tree (if not already done)

```bash
intent-cli intent init-tree --domain <name> --target-repo <owner/repo> --write
```

Add feature folders as needed:

```bash
intent-cli intent add-feature --domain <name> --name <feature> --write
```

### 4. Host/design AI reorganizes with operator

Using the analysis plan as input, the design AI (with operator supervision):
1. Decides final category groupings (intent-cli suggestions are a starting point).
2. Moves or copies content blocks from flat files into the suggested destinations.
3. Updates markdown links and heading anchors.
4. Preserves original references: packet paths, GitHub issue/PR URLs, execution-unit IDs.

### 5. Optional: generate backup + stub files

```bash
intent-cli intent analyze-tree --domain <name> --write
```

Creates:
- `.restructure-backup/` copies of flat files (non-destructive)
- Destination stub files with placeholders

### 6. Lint after restructuring

```bash
intent-cli intent lint-layout --domain <name> --format markdown
```

Verify:
- `BROKEN-RELATIVE-LINK` count has not increased
- `MISSING-FEATURES-INDEX` and `MISSING-FEATURE-OVERVIEW` resolved
- No `LARGE-FLAT-FILE` warnings remain for moved files

### 7. Commit for review

Commit the restructure as a normal reviewable change. The commit should:
- Reference which flat files were reorganized
- List categories created and features added
- Include the migration reference map
- Note that original references (GitHub URLs, execution-unit IDs) are preserved

**Review checklist:**
- [ ] All original headings are traceable to a destination file
- [ ] Markdown links updated and resolve correctly
- [ ] Execution-unit IDs and GitHub URLs still present
- [ ] `manifest.yaml` and `features/index.md` updated
- [ ] Original flat files backed up or removed with explicit confirmation

## Lint codes reference

| Code | Description | Fix |
|---|---|---|
| `MISSING-DOMAIN` | Domain directory does not exist | Run `intent init-tree --write` |
| `MISSING-MANIFEST` | No `manifest.yaml` (flat domain) | Run `intent init-tree --write` |
| `MISSING-CATEGORY-FOLDER` | Category listed in manifest but folder absent | Create folder or re-run `init-tree --write` |
| `LARGE-FLAT-FILE` | Flat file exceeds size threshold | Run `analyze-tree` to plan restructure |
| `BROKEN-RELATIVE-LINK` | Relative link does not resolve | Update link or create missing file |
| `MISSING-FEATURES-INDEX` | `features/` exists but `features/index.md` is missing | Run `add-feature --write` |
| `MISSING-FEATURE-OVERVIEW` | Feature folder missing `overview.md` | Add `overview.md` with goals and criteria |

## Related documents

- [Intent management](03-intents.md)
- [Intent knowledge tree layout (tree-v1)](03a-intent-tree-layout.md)
- [Packet creation and issue publishing](04-packets-issues.md)
