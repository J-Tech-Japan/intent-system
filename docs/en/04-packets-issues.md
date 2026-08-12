# Create packets & publish issues

← [Organize & maintain intents](03-intents.md) | [docs index](README.md) | → [Agent-message orchestration](12-agent-message-orchestration.md)

This is **host/design** work. When your intent is clear enough to act on, the design thread splits it into **packets** — focused implementation units — and publishes one at a time as a GitHub Issue for a child implementation agent to pick up.

## What a packet is

A **packet** is a focused, reviewable slice of intent that becomes an executable task. The design thread scaffolds a canonical set of files (`packet.yaml`, `implementation.md`, `review-context.md`, `github-body.md`) that describe exactly what needs to be built. `review-context.md` includes a generated **Facet context** section listing the G529 semantic-facet nodes (vocabulary/invariant/decider/acceptance-property) overlapping the packet's `intent_references` — see [Facet-aware context supply (G530)](09-developer-reference.md#facet-aware-context-supply-g530).

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

# Required before publish: retain the actual lexical facet-check result
intent-cli intent facet-check --domain <domain> --packet <id> --format json

# Enforce the Standalone Child Issue Contract, then publish
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
# Apply intent-target by either the recorded unit or its issue number
intent-cli automation issue-publish --execution-unit <id> --write --format json
# Equivalent alternate when the issue number is already known:
intent-cli automation issue-publish --issue <n> --write --format json
```

The facet check is required before publish, but its output must be described
honestly. `no_facet_data: true` means the lexical check **did not run** because
there were no facet-annotated intent nodes; it never means the packet passed.
The current intent-cli domain is the measured example—it has no facet nodes—so
human/agent semantic alignment review remains necessary. Do not author facet
nodes merely to manufacture a green result in this slice.

## Effective PR base branch in a new packet draft

`packet draft` fills the `Expected PR base branch` line in the new
`github-body.md` through the same effective-branch judgment used by the
automation surfaces:

- when `[project] implementation_base_branch` is configured, that branch is
  used;
- when it is absent, the default branch of `base_branch_policy` is used
  (`direct-main` → `main`, `main-ai` → `main-ai`).

This behavior applies only to newly scaffolded packet bodies. `packet draft`
does not rewrite existing packets or published issue bodies, and an absent
`implementation_base_branch` keeps the prior scaffold output byte-for-byte.

## Named branch lanes (G668 — preview-through-1.x)

Hosts may declare one small named branch-lane registry per domain under
`[project.branch_lanes.<domain>]`:

```toml
[project.branch_lanes."intent-cli"]
default_lane = "continuous"
definition_revision = "registry-r1"

[project.branch_lanes."intent-cli".continuous]
start_branch = "develop"
pr_base_branch = "develop"
landing_mode = "direct"

[project.branch_lanes."intent-cli".hotfix]
start_branch = "main"
pr_base_branch = "main"
landing_mode = "direct"

[project.branch_lanes."sekiban-as-a-service"]
default_lane = "release"
definition_revision = "sekiban-r1"

[project.branch_lanes."sekiban-as-a-service".release]
start_branch = "release"
pr_base_branch = "main"
landing_mode = "integration-batch"
```

Run `packet draft --lane hotfix` to choose a lane explicitly. With no `--lane`,
the configured `default_lane` is selected and recorded as
`branch_lane_source: domain-default`; an explicit choice is recorded as
`branch_lane_source: explicit`. The draft materializes `branch_lane` and a
`routing_snapshot` containing the lane id, definition revision, start branch,
PR base branch, and landing mode in `packet.yaml` and `github-body.md`.

That snapshot is the accepted packet's routing fact. Queue seeding, projection
regeneration, review guidance, and worker base-branch checks use the materialized
snapshot; changing the registry later does not retarget an existing packet.
Domain selection resolves only the registry matching the packet's selected
domain. Hosts without a matching registry retain the legacy `direct-main` /
`main-ai` policy names, fields, output, and byte-for-byte packet draft behavior.
The previous singleton `[project.branch_lanes]` spelling remains readable for
compatibility, but is scoped only to the configured project domain. Named lanes
are preview-through-1.x and do not manage or create branches.

## Lane decision records and the publish gate (G669 — preview-through-1.x)

A lane declaration is a routing fact, not a judgment. Before a
lane-declaring packet can cross the publish boundary, design records a
proposal with the lane id, resolved branches, rationale, actor, timestamp,
evidence, definition revision, and a fingerprint:

```text
intent-cli automation branch-lane-propose-record \
  --execution-unit G669 --actor design --rationale "..." --evidence "..." --write
```

Orchestration independently confirms that proposal. Confirmation is a
separate record with its own actor, timestamp, evidence, and the same routing
fingerprint; prose in `packet.yaml` or `github-body.md` never counts as either
record:

```text
intent-cli automation branch-lane-confirm-record \
  --execution-unit G669 --actor orchestration --evidence "..." --write
```

The records live under
`.intent-cli/branch-lane-decisions/<execution-unit>/propose.json` and
`confirm.json`. A confirm without a proposal is refused, and publish refuses
missing, mismatched, malformed, or non-independent records before any GitHub
operation. Legacy packets without `branch_lane` retain the previous publish
path unchanged.

`automation stalled-work` reports
`branch-lane-decision-pending` only for an aged queued lane item whose
confirmation is absent. It reports `branch-routing-conflict` immediately when
the packet, issue body, queue snapshot, and observed PR base branch disagree;
the conflict includes every observed value and remains detectable for a
closed PR. Neither classification is emitted for a legacy packet.

## Alternative: timer-loop setup

Use [Implementation loop setup](05-implementation-loop.md) and then
[Review / next-slice loop setup](06-review-next-slice-loop.md) only when you
choose the timer-loop alternative.

## Next

[Agent-message orchestration](12-agent-message-orchestration.md).
