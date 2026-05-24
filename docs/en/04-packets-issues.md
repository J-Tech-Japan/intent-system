# Create packets & publish issues

> **Ask intent-cli first:** `intent-cli guide start` →
> `intent-cli guide workflow task packet-draft --format json` and
> `… task issue-publish --format json`. ← [docs index](index.md)

**Host/design** work. A packet scaffolds the canonical files; the publish
boundary turns a reviewed Standalone Child Issue Contract into a GitHub issue.

```bash
# Scaffold the packet (packet.yaml / implementation.md / review-context.md / github-body.md)
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --format markdown

# Enforce the Standalone Child Issue Contract, then publish
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --issue <n> --write --format json
```

## Ask-intent-cli prompt template

> I'm drafting packet `<id>` and publishing its issue to `<owner>/<repo>`. Run
> `intent-cli guide start`, then the `packet-draft` and `issue-publish` workflow
> guides, and apply `intent-target` only via the publish/automation command.

## Metadata / label safety

- **`intent-target` is applied by the publish boundary command, never by hand**,
  and never by a child implementation agent.
- The issue body must be a **standalone contract** — a child agent will treat it
  as the only source of truth (no host metadata access).

## Next

[Implementation loop setup](05-implementation-loop.md).
