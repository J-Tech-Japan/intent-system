# Getting started: the road to the first packet

← [Start a project](02-project-start.md) | [docs index](README.md) | → [Organize & maintain intents](03-intents.md)

## First decision: choose your 2×2 onboarding pattern

Choose where host metadata lives and whether the project is brand-new or
existing. Do this first; each linked page is deliberately self-contained, so
you never combine instructions from two patterns.

| Host metadata | Brand-new project | Adding intent-cli to an existing project |
| --- | --- | --- |
| Separate host repository | [Separate host × brand-new](02b-separate-host-brand-new.md) | [Separate host × existing](02c-separate-host-existing.md) |
| Same repository, metadata branch | [Same repo × brand-new](02d-same-repo-brand-new.md) | [Same repo × existing](02e-same-repo-existing.md) |

Each pattern starts with exactly two coexisting paste-ready initial prompts.
Prefer `herdr-only` when all four agents are collocated on one machine because
it has fewer dependencies. Choose the supported, non-retired `agmsg` + herdr
transport for a distributed team or an existing agmsg investment. Both are
recorded with `session-layer set`; the **four-thread model** is primary, never
either transport. After the initial prompt, follow the recorded mode and the
current installed guides instead of combining transport-specific instructions.

This page is the **orchestration-first** route from that minimal start to the
first published packet. [Start a project](02-project-start.md) remains
authoritative for topology, and the [agent-message orchestration contract](12-agent-message-orchestration.md)
remains authoritative for session-layer semantics.

## What you are setting up

The four-thread model is the **primary** model: design authors intent,
orchestration coordinates, implementation delivers the child PR, and review
checks it. For a collocated team on one machine, this route recommends the
`herdr-only` transport because it has fewer dependencies. `agmsg` + herdr
remains supported and is not retired for its distributed/existing-agmsg fit;
the four-thread model is primary, not either transport.

## 1. Choose repositories and folders

First choose topology A or B using [02's repository-topology comparison](02-project-start.md#repository-topology-choices).
Do not copy its table here; it decides where durable host state belongs.

For a separate host repository, a practical four-role layout is:

```text
~/work/my-project-host/          # design and orchestration open this host checkout
~/work/my-project/               # implementation opens this target-repo checkout
~/work/my-project-review/        # review opens this isolated target-repo checkout
```

Design and orchestration share the host checkout because they own host-side
intent and workflow decisions. Implementation opens only the implementation
checkout and follows the GitHub issue/PR contract. Review uses its own checkout
so inspection, test artifacts, and a potential repair never disturb an active
implementation worktree.

## 2. Install and initialize the host

Follow [Install](01-install.md), then use [02's initialization flow](02-project-start.md#what-the-agent-will-run-for-maintainers-and-troubleshooting)
from the host checkout. When the host check is complete, stand up a **new** team
as follows.

> **New team only.** This order records new truth before it is displayed. An
> existing team changing transports follows [doc 12's Session-layer switch
> checklist](12-agent-message-orchestration.md#session-layer-switch-checklist),
> where `session-layer set --write` is the final canonical step. Do not use the
> new-team sequence as a transport-switch procedure.

### 2.1 Record the transport

Paste this prompt to the design/orchestration agent:

> In the host checkout, record `herdr-only` for new team `<team>` in domain
> `<domain>`. Run `intent-cli session-layer set --domain <domain> --team <team> --mode herdr-only --write --format json` and show the returned JSON. Do not
> infer a mode from residue or a workspace.

In an isolated scratch host, the shipped `intent-cli 0.11.0-7b3800e-G606`
returned this success shape (dynamic timestamps and migration items omitted):

```json
{
  "domain": "onboarding",
  "team": "docs-team",
  "mode": "herdr-only",
  "source": "recorded",
  "command_mode": "write",
  "applied": true,
  "changed": true,
  "summary": "team `docs-team` in domain `onboarding`: session layer is herdr-only (preferred — fewer dependencies) (recorded)."
}
```

The observed first record also returned a `migration_plan` array. It is output
from the mode change; use doc 12, not this page, for an existing team's
migration procedure.

### 2.2 Record topology for every role

Paste this prompt to the design/orchestration agent after the real herdr
workspace and pane IDs are known:

> Record the `design`, `orchestration`, `implementation`, and `review` roles
> for domain `<domain>` and team `<team>`. For each role run
> `intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident herdr --workspace-id <workspace-id> --pane-id <pane-id> --cwd <role-cwd> --write --format json`. Pass the explicit domain,
> the operator-supplied workspace/pane IDs, and the folder from the layout
> above; do not guess IDs or edit topology files.

Each run in the scratch host returned the same success shape, with `role`
equal to the role just recorded:

```json
{
  "team": "docs-team",
  "role": "design",
  "resident": "herdr",
  "mode": "write",
  "record_path": ".intent-cli/topology/onboarding/docs-team.json",
  "applied": true,
  "changed": true,
  "already_recorded": false,
  "conflict": false,
  "summary": "Recorded operator-supplied role 'design' for team 'docs-team'."
}
```

The observed runs recorded all four roles before continuing. The record command
is a controlled writer; the exact residency variants and validation rules stay
in [doc 12](12-agent-message-orchestration.md).

### 2.3 Generate the visible marker

Before generating, add the one empty managed marker block for this domain/team
to `AGENTS.md` or `CLAUDE.md` exactly as [doc 12's generated-marker
section](12-agent-message-orchestration.md#visible-generated-mode-markers)
specifies. Then paste this prompt:

> Generate the marker for the recorded domain `<domain>` and team `<team>`.
> Run `intent-cli session-layer marker generate --domain <domain> --team <team> --file AGENTS.md --write --format json`, then show the JSON. Change only the
> managed marker block.

The scratch-host run returned this success shape (the record hash is dynamic):

```json
{
  "written": true,
  "file": "AGENTS.md",
  "domain": "onboarding",
  "team": "docs-team",
  "mode": "herdr-only",
  "verify_command": "intent-cli session-layer show --domain onboarding --team docs-team",
  "summary": "Generated the managed session-layer marker for team 'docs-team' in 'AGENTS.md'."
}
```

### 2.4 Confirm structural readiness

Paste this prompt to the design/orchestration agent:

> Check the new team's shared preflight with `intent-cli automation doctor --domain <domain> --team <team> --format json`. Report the
> `session_layer_preflight` result; do not claim delivery readiness from doctor.

The scratch-host run was structurally ready:

```json
{
  "status": "ok",
  "topology_health": { "status": "valid", "required": true },
  "session_layer_preflight": {
    "verdict": "ready",
    "ready": true,
    "passive_phase": { "status": "ready", "contacted_receiver": false },
    "active_phase": { "status": "skipped", "contacted_receiver": false }
  }
}
```

`ready` here is the shared **passive structural** verdict. A delivery surface
performs its own bounded receiver check before it claims delivery.

## 3. Create the first packet

The session layer is now recorded and visible. Continue with [Organize &
maintain intents](03-intents.md), then [Create packets & publish
issues](04-packets-issues.md). Those pages take over when the first packet is
ready to publish.

## Alternatives

- **agmsg + herdr:** for the distributed or existing-agmsg choice, use the
  [agent-message orchestration contract](12-agent-message-orchestration.md).
- **timer-loop:** use [Implementation loop setup](05-implementation-loop.md)
  and [Review / next-slice loop setup](06-review-next-slice-loop.md). It is an
  alternative to the orchestration-first route above.

## Next

[Organize & maintain intents](03-intents.md).
