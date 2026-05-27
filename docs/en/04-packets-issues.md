# Create packets & publish issues

← [docs index](README.md) | → [GitHub workflow labels](04a-workflow-labels.md) | → [Implementation loop setup](05-implementation-loop.md)

This is **host/design** work. When your intent is clear enough to act on, the design thread splits it into **packets** — focused implementation units — and publishes one at a time as a GitHub Issue for a child implementation agent to pick up.

## What a packet is

A **packet** is a focused, reviewable slice of intent that becomes an executable task. The design thread scaffolds a canonical set of files (`packet.yaml`, `implementation.md`, `review-context.md`, `github-body.md`) that describe exactly what needs to be built.

**Issue publish** turns a reviewed packet into a GitHub Issue with a **Standalone Child Issue Contract** — the only source of truth a child implementation agent needs to do the work. The child agent reads the issue body and the repository code; it does not access host metadata.

## Design-thread prompt

Paste this into your AI agent design thread:

> I want to cut the next packet for domain `<name>` and publish its issue to `<owner>/<repo>`.
> Ask intent-cli what I should do next.

The AI agent will:
1. Check the current intent and open work with intent-cli
2. Draft the next packet (scaffolding the canonical files)
3. Help you review the Standalone Child Issue Contract
4. Publish the issue with the correct workflow labels

After publishing, the issue appears in the target repository with `intent-target` applied, ready for a child implementation agent to pick up.

## Ask-intent-cli prompt template

> I'm drafting packet `<id>` and publishing its issue to `<owner>/<repo>`.
> Ask intent-cli what I should do next.

## Metadata / label safety

- **`intent-target` is applied by the publish boundary command, never by hand**,
  and never by a child implementation agent.
- The issue body must be a **standalone contract** — a child agent will treat it
  as the only source of truth (no host metadata access).

## Command reference (for agents, maintainers, and troubleshooting)

> **Note:** The commands below are run by the AI agent internally. You do not normally need to run them manually. Refer to this section if you are debugging the workflow or acting as a host automation maintainer.

```bash
# Scaffold the packet (packet.yaml / implementation.md / review-context.md / github-body.md)
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --format markdown

# Enforce the Standalone Child Issue Contract, then publish
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --issue <n> --write --format json
```

## Next

[Implementation loop setup](05-implementation-loop.md).
