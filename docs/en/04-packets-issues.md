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
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --team <team> --format markdown

# Required before publish: retain the actual lexical facet-check result
intent-cli intent facet-check --domain <domain> --packet <id> --format json

# Project the verified packet into the queue using the same claim judgment
intent-cli automation queue-seed-from-packet --execution-unit <id> --target-repo <owner>/<repo> --team <team> --format json

# Enforce the Standalone Child Issue Contract, then publish
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --team <team> --write --format json
# Apply intent-target by either the recorded unit or its issue number
intent-cli automation issue-publish --execution-unit <id> --write --format json
# Equivalent alternate when the issue number is already known:
intent-cli automation issue-publish --issue <n> --write --format json
```

On a claims-enabled host, acquire `execution-unit:<id>` by successful plain
push before the first scaffold, then pass the invoking team to draft, queue
seed, publish, next-slice, and worker selection. They all consult the same
read-only claim judgment. An unheld scope or another team's record is refused
with scope, holder, and holder team named. If `.intent-cli/claims/` does not
exist, these surfaces keep the legacy single-team output byte-for-byte.

Number allocation is claim-then-draft: compute N, claim
`execution-unit:<N>`, then scaffold only after winning. A loser fast-forwards,
recomputes the next number, and retries that new scope exactly once. A second
loss stops. GitHub labels remain visibility and defence in depth, not the
ownership fact.

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
landing_mode = "operator-merge"

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

### Human landing authority (G678 — preview-through-1.x)

`landing_mode = "operator-merge"` keeps review and approval unchanged, but
changes who performs the irreversible landing step. Once the lane's PR is
approved and its exact-head checks are green, automation exposes
`awaiting-operator-merge` with the PR, lane, and approval evidence. This is a
patient state, not review debt or a stall: design is notified once on entry,
and supervision never urges, reminds, or age-escalates the wait.

Only a human merges that PR. No intent-cli path may merge an operator-merge
lane. When GitHub reports the human merge, supervision replaces the patient
state with an immediate closeout-only continuation and the ordinary closeout
flow resumes. `direct`, `integration-batch`, and lanes with no registry retain
their existing behavior and output.

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

## JSON-payload issue-body gate (G847)

The `issue create`, `queue dispatch`, and `bug implementation-issue` routes
serialize `{title, body}` and submit that JSON document through `gh api
--input`. Their shared `IssueBodySizeLimits` declares
`HardLimitBytes = 65536` and `WarningThresholdBytes = 58000`. The hard
boundary is inclusive for the submitted body content: 65,535 and 65,536
bytes are accepted, while a body over 65,536 bytes is refused with exit code
1 and this plain message:

```text
Issue body is <n> bytes, which exceeds the 65536-byte limit.
```

This gate measures the exact decoded string passed to `CreateIssue`, using
`Encoding.UTF8.GetByteCount`. It measures submitted body content, not bytes on
the wire. The JSON payload also contains the title and escapes the body, so
the JSON overhead is content-dependent; no byte range for that overhead is a
contract. **65,536 is intent-cli's own conservative limit on the submitted
body content.** GitHub's boundary, inclusivity, unit, and treatment of a JSON
payload were not verified; the only remote datum is the roughly 96,000-character
failure reported on 2026-09-16.

Every body-feeding read on these three JSON-payload routes uses the shared
`StrictUtf8FileReader.ReadText(path)` helper: `IssueCreateCommand.cs:88`,
`QueueDispatchCommand.cs:124`, and
`BugImplementationIssueCommand.cs:72`, `:152`, `:445`, `:599`, `:607`, and
`:615`. It reads bytes and strictly decodes UTF-8. Malformed input is refused
before the size check with exit code 1 and:

```text
Issue body is not valid UTF-8 at byte offset <k> in <file>.
```

For this JSON-payload mechanism only, exactly one leading UTF-8 BOM is removed
after decoding, preserving today's submitted content for UTF-8 files. A file
starting with a UTF-16 or UTF-32 BOM is refused. This BOM behavior is scoped to
the JSON-payload mechanism: sibling G845's `--body-file` routes hand `gh` the
file bytes, so their counting rule is different. This unit adds no `--format`
option or result surface. The ledger's generic `--format json` rows for these
three commands are a recorded pre-existing inaccuracy and are deliberately not
fixed here.

The contract hedge is: **65,536 is intent-cli's own conservative limit on the
submitted body content; GitHub's boundary, inclusivity, unit and treatment of
a JSON payload were not verified.**

## `--body-file` transmission gate (G845)

The four file-body routes—`issue publish-flow`'s normal and declared-team
create paths, `issue publish-reviewed`, and `issue sync-body --write`—strictly
decode UTF-8 and then count the raw bytes that will be staged for the
`gh --body-file` call. A UTF-8 BOM is not stripped on these routes: it is part
of the file bytes counted and staged. 65,535 and 65,536 bytes are accepted;
65,537 bytes are refused with exit code 1. This is the route-scoped
`--body-file` rule, not a rule for the JSON-payload routes.

Malformed UTF-8 is refused before the size check, naming the offending byte
offset. `issue publish-flow` reports causes `issue-body-too-large` and
`issue-body-invalid-utf8`; `issue sync-body --write` reports
`body-too-large` and `body-invalid-utf8`. Its write-only gate runs after the
remote read, dry-run return, and concurrent-edit check, immediately before the
equality/no-op decision. Its exact refusal summaries are:

```text
refused (body-too-large): Issue body is <n> bytes, which exceeds the 65536-byte limit. The issue body was read, but no update was sent.
refused (body-invalid-utf8): Issue body is not valid UTF-8 at byte offset <k>. The issue body was read, but no update was sent.
```

`issue publish-reviewed` has no `--format` handling and deliberately keeps its
plain refusal lines: `source body is <n> bytes, which exceeds the 65536-byte
limit` and `source body is not valid UTF-8 at byte offset <k>`, each with exit
code 1 and no result surface.

The gate is deliberately UTF-8-only. A UTF-16 or UTF-32 BOM body may be
accepted by `issue validate-body`'s BOM-aware decoder, but the four transmission
routes refuse it as invalid UTF-8 at the gate and make no transmission call.
`validate-body` may accept a body that this gate refuses; therefore, aligning
those surfaces is a separate follow-up. A 65,539-byte UTF-8-BOM file is validly
decoded but refused for size because the BOM is part of the counted
`--body-file` bytes.

All four routes write the counted byte array to a fresh file in a private
directory created under the user's temp directory, then hand that staged path
to `gh`. On non-Windows platforms the directory is created with mode 0700 and
the file with mode 0600 at creation; Windows receives no Unix mode setting.
Cleanup is best-effort. The guarantee is only that, assuming no other process
writes into the command's private staging directory, the staged bytes equal
the counted array when `gh` is invoked; this is not an immutability claim
against a concurrent writer.

**65,536 is intent-cli's own conservative limit. GitHub's boundary, inclusivity and unit were not verified. The only remote datum is the roughly 96,000-character failure reported on 2026-09-16.** The statement above is scoped to these `--body-file` routes; it does not claim that GitHub refuses a body at this boundary or that `--body-file` transmits a file verbatim.

## Early issue-body size reporting and refusal (G846)

The issue-body workflow counts the raw bytes in `github-body.md`, including a
UTF-8 BOM when one is present; it does not count decoded characters. The shared
`IssueBodySizeLimits` values are `HardLimitBytes = 65536` and
`WarningThresholdBytes = 58000`. The warning band is inclusive from 58,000
through 65,536 bytes. A body over 65,536 bytes is refused by the local
create-path gates.

**58,000 is a budget choice: the self-imposed drafting budget held by hand since 2026-09-16, not a GitHub limit.**

`issue validate-body` reports `body_bytes`, `body_too_large`,
`body_size_warning`, and `body_size_reason`. `packet draft` always reports a
top-level `warnings` array, refuses an oversized body in both default and
`--dry-run` modes with `issue-body-too-large`, and preserves its scaffolding.
`issue publish-flow` refuses an oversized unpublished create before creator or
durable writes, but an already-published body remains idempotent and reports
`issue-body-too-large` as its size warning. `issue sync-body` reports
`warnings` and refuses locally before any remote read with `reason_code:
body-too-large`. The warning literal `issue-body-size-warning` is retained in
all four command surfaces.

**65,536 is intent-cli's own conservative limit. GitHub's boundary, inclusivity and unit were not verified. The only remote datum is the roughly 96,000-character failure reported on 2026-09-16.**

## Alternative: timer-loop setup

Use [Implementation loop setup](05-implementation-loop.md) and then
[Review / next-slice loop setup](06-review-next-slice-loop.md) only when you
choose the timer-loop alternative.

## Next

[Agent-message orchestration](12-agent-message-orchestration.md).
