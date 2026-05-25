# Organize & maintain intents

> **Ask intent-cli first:** `intent-cli guide start` →
> `intent-cli guide workflow task intent-interview --format json`. ← [docs index](index.md)

**Host/design** work: capture and compile durable intent before cutting any
slice.

```bash
# Durable per-domain Q/A artifact
intent-cli interview next-question
intent-cli interview record-answer ...
intent-cli interview compile

# Suggested end-to-end flow / readiness
intent-cli guide workflow --format json
intent-cli intent status --format json
```

## Ask-intent-cli prompt template

> I'm organizing intents for domain `<name>`. Run `intent-cli guide start` then
> the `intent-interview` workflow guide, and use `intent-cli interview …` to
> record answers. Don't invent rules — ask intent-cli for current guidance.

## Metadata / label safety

- Interview/draft artifacts are written through `intent-cli interview …`
  (`record-answer` is the only mutation here); don't hand-edit the durable Q/A
  files.
- Child implementation agents do **not** read the intent tree (`intents/**`) or
  host metadata — that's host/design territory.

## Intent knowledge-tree layout (tree-v1)

New domains should organize intent into discoverable folders rather than a single flat
file. The **tree-v1** layout defines recommended categories (`identity`, `product`,
`features`, `technology`, `operations`, `decisions`, `clarifications`, `packets`, `links`)
and a manifest schema that supports custom folder names and project types.

```bash
# Get current tree-layout authoring guidance
intent-cli guide intent-work setup \
  --kind tree-layout \
  --domain <name> \
  --target-repo <owner/repo> \
  --format markdown
```

See [Intent knowledge-tree layout (tree-v1)](03a-intent-tree-layout.md) for the full spec,
manifest schema, project type examples, and cross-linking rules.

## Next

[Create packets & publish issues](04-packets-issues.md).
