# Intent knowledge-tree layout (tree-v1)

← [docs index](README.md) | ← [Organize & maintain intents](03-intents.md)

This page describes the **tree-v1** flexible intent knowledge-tree layout for new
domains. Existing flat-file domains do not need to migrate immediately — tree-v1
is a recommended default for new domains, not a hard requirement for existing ones.

## How the Intent Storming conversation maps to the tree

Answers from the [Intent Storming conversation](03-intents.md#what-is-intent-storming)
are organized into the folders below.

| Conversation content | Tree location |
|---|---|
| Product goal, users, non-goals | `product/` |
| Mission/value/vision, principles | `identity/` |
| Feature requirements, user stories | `features/<slug>/` |
| Technical choices, architecture, libraries | `technology/` |
| ADR-style decisions | `decisions/` |
| Unresolved questions | `clarifications/open.md` |
| Implementation/review loop policy, release policy | `operations/` |
| Executable slices | `packets/` → GitHub issue |

Not all folders need to be populated in one conversation. Intent Storming can be repeated
as the project evolves.

## Why tree-v1

Flat intent files work for small, early-stage projects. As a domain grows, a single
file becomes hard to search, review, link, and analyze. Tree-v1 organizes intent
into discoverable folders so mission/vision/values, feature requirements, technology
choices, loop operations, decisions, and clarifications are findable by path and
cross-linked.

## Manifest

Each domain following tree-v1 provides a manifest at
`intents/<domain>/manifest.yaml`:

```yaml
version: "1"
layout_version: tree-v1
project_type: product-app   # product-app | library-tool | infrastructure | research-prototype
target_repo: <owner/repo>
branch_policy: direct-main
metadata_policy: host-metadata
entrypoints:
  - identity/mission.md
  - README.md
categories:
  identity: identity/
  product: product/
  features: features/
  technology: technology/
  operations: operations/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
  links: links/
```

Category paths under `categories:` are configurable — rename or omit any
category that does not fit your project type (see [Project type examples](#project-type-examples)).

## Recommended categories

| Category | Path | Purpose |
|---|---|---|
| `identity` | `identity/` | Mission, vision, values, principles, glossary |
| `product` | `product/` | Overview, users, journeys, non-goals |
| `features` | `features/` | One subfolder per feature — overview, requirements, acceptance, decisions, open questions, packets, links |
| `technology` | `technology/` | Architecture, languages, libraries, frontend, backend, data, cloud, security, testing, observability, deployment |
| `operations` | `operations/` | Implementation loop, review loop, release, recovery |
| `decisions` | `decisions/` | ADR-style records (`<NNNN>-<slug>.md`) |
| `clarifications` | `clarifications/` | `open.md`, `answered.md`, `log.md` |
| `packets` | `packets/` | Roadmap, backlog, waves |
| `links` | `links/` | GitHub repos, external docs, related projects |

All category names are optional and configurable.

## Feature folder layout

Each feature lives in `features/<feature-slug>/`:

```
features/
  auth/
    overview.md          # Goal, motivation, acceptance summary
    requirements.md      # Detailed requirements
    acceptance.md        # Acceptance criteria
    decisions.md         # Feature-specific design decisions
    open-questions.md    # Unresolved questions (link to clarifications/)
    packets.md           # Execution unit list (link to packets/ or GitHub issues)
    links.md             # References
```

## Cross-linking rules

Cross-links keep the tree navigable and prevent duplication:

- **Feature overview pages** must link to relevant decisions, clarifications,
  packets, and GitHub issues.
- **Decision records** must link to the feature(s) and clarification(s) that
  motivated them.
- **Clarification entries** must link back to the feature or decision they block.
- **Packet pages** must link to the GitHub issue once published.
- Do not duplicate content across files — use relative Markdown links instead.

## Project type examples

### `product-app`

Use all recommended categories. No changes needed from the manifest default.

### `library-tool`

Replace `product/` with `api/` and `users/`; omit `links/` if unused:

```yaml
categories:
  identity: identity/
  api: api/
  users: users/
  features: features/
  technology: technology/
  operations: operations/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
```

### `infrastructure`

Replace `product/` with `environments/`; add `runbooks/` under operations:

```yaml
categories:
  identity: identity/
  environments: environments/
  features: features/
  technology: technology/
  operations: operations/
  runbooks: runbooks/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
```

### `research-prototype`

Replace `product/` with `hypothesis/`; replace `operations/` with `experiments/`:

```yaml
categories:
  identity: identity/
  hypothesis: hypothesis/
  features: features/
  technology: technology/
  experiments: experiments/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
```

## Agent guidance

Agents authoring or updating intent content should:

1. Run `intent-cli guide intent-work setup --kind tree-layout --domain <name> --target-repo <owner/repo>` for current guidance.
2. **Prefer tree placement** over appending to a flat file — put new content in the appropriate category folder.
3. **Add cross-links** when creating or updating feature pages, decisions, or clarifications.
4. **Present changes to the operator** before writing the manifest or migrating existing flat files.
5. Never publish a GitHub issue in the same wake as tree-layout authoring.

## Related docs

- [Organize & maintain intents](03-intents.md)
- [Create packets & publish issues](04-packets-issues.md)
