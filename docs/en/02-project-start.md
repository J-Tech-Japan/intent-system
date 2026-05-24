# Start a project

> **Ask intent-cli first:** `intent-cli guide start` → then the
> `design-and-intent` / project-start guidance it points at. ← [docs index](index.md)

This is **host/design** work (you may touch metadata, but ask intent-cli for the
current command before hand-editing it).

## Initialize and inspect

```bash
# Initialize a host domain (read-only without --write)
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write

# Inspect current baseline / WIP / queued packets (read-only)
intent-cli intent status

# Ask what the work surfaces expect
intent-cli guide intent-work --format json
```

## Ask-intent-cli prompt template

> Before starting on `<owner>/<repo>` domain `<name>`, run
> `intent-cli guide start` and `intent-cli intent status`, then follow the
> guide command for the phase I'm in. Use intent-cli transitions for any
> label/metadata change; never hand-edit.

## Metadata / label safety

- `intent-target`, `intent-pr-*` and other workflow labels are applied by
  `intent-cli automation` / `intent-cli worker` commands — never by hand.
- Canonical state lives in the host repo's `.intent-cli/`; read it through
  intent-cli surfaces, don't edit `queue-state.json` directly.

## Next

[Organize & maintain intents](03-intents.md).
