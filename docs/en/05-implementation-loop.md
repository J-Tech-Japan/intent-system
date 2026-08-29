# Implementation loop setup

← [docs index](README.md) | → [Review / next-slice loop setup](06-review-next-slice-loop.md)

Do not copy the steps from this page directly to create an implementation loop.
The authoritative conditions come from installed intent-cli guidance.
Ask the AI agent in your design thread to generate the current loop creation prompt.

## Folder separation (understand this first)

Before starting an implementation loop, understand the three folder roles:

| Folder | Role |
|---|---|
| **Design/host folder** | Stores intent metadata and packets. The design thread runs here. |
| **Implementation folder** | The child implementation loop edits code and creates/updates PRs here. |
| **Review folder** | The host review/next-slice loop reviews PRs and publishes the next issue here. |

> **Warning — wrong cwd is a common failure mode.**
> Starting an implementation loop from the design/host folder, or a review loop
> from an implementation folder, causes misbehavior. Always verify your working
> directory before starting a loop.

**Same-repository metadata topology** (using a `main-metadata` branch): even when
all three roles target the same repository, run each loop from a **separate
folder, clone, or worktree**. Sharing a folder across roles causes branch
operations and metadata changes to interfere with each other.

## How to create a loop

1. **In the design thread**, ask the AI agent to ask intent-cli for the current implementation loop creation prompt.
2. Provide the domain, target repo, **path to the implementation folder**, and the PR base branch.
3. Paste the generated prompt into a separate thread opened in the **implementation folder**.

## Design-thread prompt (to request a loop creation prompt)

Paste this into your design thread (the AI agent running in the design/host folder):

> Ask intent-cli to generate the prompt I need to create a child implementation loop
> for `<owner>/<repo>` using Claude Code `/loop 5m`.
> The domain is `<domain>`, the working folder is `<implementation-folder>`,
> and the implementation PR base branch is `<branch>`.
> The generated prompt should delegate detailed conditions to intent-cli guidance.

Paste the generated prompt into a **separate thread opened in the implementation folder**.
The loop's detailed conditions come from intent-cli guidance — you do not need to
copy a long loop body from this document.

## Child implementation loop principles

- **GitHub-contract-only and metadata-free**: the issue/PR and repo-local code are the only source of truth
- Never read or mutate host `.intent-cli/`, queue-state, metadata branches, or `intents/**`
- Select work only via `intent-cli worker next-action`; process at most one action per wake
- All label transitions go through `intent-cli worker` — never raw `gh ... --add-label`

## G757: external-role cursor-based receive

An external-resident role can receive a durable event without already knowing
the task id. Use the role-scoped read mode:

```text
intent-cli notify collect --domain <domain> --team <team> --role <role> \
  [--since <opaque-cursor>] [--wait --timeout-ms <milliseconds>] --format json
```

The role must have a recorded external reader. The command resolves that
reader through `NotifyEventWriter.TryResolveReadPath`, so scoped-versus-legacy
reader compatibility stays in one place. It performs one immediate read unless
`--wait` is supplied; `--wait` always requires a bounded `--timeout-ms` and
returns `cause: "no-new-events"` with a non-error exit when the bound expires.
A missing reader file is `cause: "no-events"`, not an error.

The cursor is opaque and caller-owned: the CLI stores no acknowledgement or
read position. Pass the returned `next_cursor` to the next call to resume
without loss or duplication. If the cursor no longer identifies an intact
position in the same reader, the command returns the explicit
`cursor-unhonourable` result and never resets to the beginning or skips to the
end. The existing `--task-id` collection path and result shape remain
unchanged. Role collection is a short-lived command; it leaves no watcher,
timer, process, or other receive state behind.

## Metadata / label safety

- A child agent never manually applies workflow labels to a PR. For issue-to-PR `pr-created`, canonical `worker complete --kind issue --outcome pr-created --github-only --write` may apply target-repository `intent-target` to the PR; `intent-pr-created` remains an issue-side marker.
- `linked_pr_synced: false` from `worker complete` is the expected child-cwd warning — record it and move on

## G733: contractual seat/host duty boundary ([ADR 0010](../adr/0010-seat-host-duty-route.md))

The implementation seat owns the child repository end to end. From the
assigned GitHub issue it reads the standalone contract, runs `git fetch origin
main`, creates a branch from `origin/main`, edits and tests the child code,
commits, pushes, opens a **ready-for-review** PR with `Closes #<issue>`, and
reports the result. The child path uses the target repository's GitHub facts
and `intent-cli worker ... --github-only`; it does not need a host round trip
for those steps.

The canonical child completion is part of that seat-owned path. For an
issue-to-PR `pr-created` outcome, `worker complete --kind issue --outcome
pr-created --github-only --write` applies `intent-pr-created` to the source
issue and `intent-target` to the target-repository PR. This is an
intent-cli-owned target-repository transition, not raw `gh` label mutation.
Host-state linkage/publication, queue synchronization, and closeout remain
host duties; child-cwd completion reports `linked_pr_synced: false` for that
follow-up.

The host role owns host state: `.intent-cli/` queue-state, claims, runs,
packets, metadata branches, host-repository Git refresh/push, and
host-repository credentials/API operations and host-state linkage/publication.
Execution-unit claim acquisition
is a host duty and is not inferred from a lifecycle label or from a local
file. The host role must return both of these canonical JSON results before
the seat edits:

```bash
intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json
intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json
```

The evidence must show `status=acquired`, `push_succeeded=true`, matching
scope/actor/team and the pushed `commit`, followed by `passed=true` and
`status=owned`. This preserves the G679 Git compare-and-swap: pull, immutable
claim record, commit, and plain push; only the successful remote push acquires
ownership. Labels, a local claim file, a local commit, or preflight output are
not ownership, and no force-push, time expiry, or inferred takeover is valid.

### Exact host-duty request

When that evidence is missing, or any other host-owned operation is required,
the implementation seat sends this request through the team's canonical
message channel. It does not enter the host repository or hand-write agmsg or
herdr transport:

```bash
intent-cli notify report --domain <domain> --team <team> --from implementation --to orchestration --task-id <task-id> --status question --artifact <child-artifact> --summary 'HOST DUTY REQUEST: run intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json; then intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json; return the JSON evidence, pushed commit, and owned verdict.' --routing-root <host-routing-root> --report-root . --write --format json
```

The seat still cannot read or mutate host `.intent-cli/`, queue-state,
claims, runs, packets, metadata branches, or host Git; acquire/release/take
over an execution-unit claim; use host-repository credentials or the
host-repository GitHub API; or perform host-state publish, linkage, review, or
closeout transitions. If a canonical host-aware preflight refuses because the
seat cannot refresh host `FETCH_HEAD`, report the exact command and refusal as
the host duty. Do not widen roots, create an improvised clone, or retry by
entering the host repo.

This rule remains necessary on a co-located machine. Host files and
credentials may appear readable today only because the seats share a machine;
remote-herdr can place the implementation seat on another VM with neither a
shared filesystem nor host credentials. Co-location is therefore not a
contractual capability and must never become a child dependency.

Seat-owned verification emits all three boundaries: the child
`worker ... --github-only` selection/claim/complete JSON plus branch, test,
commit, push, and PR evidence; the exact `notify report` host-duty request and
the host claim JSON; and a still-refused boundary probe such as
`touch <host-routing-root>/.intent-cli/probe-should-fail`, which must exit
nonzero with `Operation not permitted`. Use a test-owned sentinel and never
delete it if the negative probe unexpectedly succeeds.

## G736: topology records host-state capacity ([ADR 0011](../adr/0011-topology-host-state-role.md))

The session-layer topology may explicitly record the host-state role and its
named envelope:

```bash
intent-cli session-layer topology record-host-state --domain <domain> --team <team> --role <role> --envelope <named-host-state-envelope> --write --format json
```

This is an authority declaration, not an inference from `resident`, `kind`,
external placement, or co-location. `topology validate` keeps legacy records
valid and does not migrate them, but reports informational
`host-state-role-missing` before publish when no declaration exists. The
finding says that the team cannot perform required host-state publication or
repository-Git work. A declaration does not create a non-sandboxed
participant; an actually capable participant and the explicit declaration are
both required. The orchestrator discovers the role and envelope from the
record. A design role explicitly declared as host-state is legitimate; the
existing prohibition is limited to undeclared or ad-hoc routine requests.

Durable emitted record, validation, and rendered-discovery evidence is in
[the G736 verification transcript](../g736-topology-host-state-verification.md).

## G724: worker domain identity on a multi-domain host

The startup marker is display evidence, not a worker binding. In host context,
`worker complete --kind issue --outcome pr-created` resolves the execution-unit
domain from the durable queue/packet record. A domain-B worker therefore remains
eligible even when the shared `CLAUDE.md` currently displays domain A. The JSON
result reports the selected `domain`, `domain_source` (normally
`queue-record`), and `execution_unit`; worker completion does not rewrite the
marker.

If a host invocation supplies `--domain`, it must match the durable queue
domain, or the authoritative session-layer record for a legacy domain-less
queue row. Missing, contradictory, unreadable, or ambiguous durable identity
is a fail-closed result with an exact re-invocation using `--domain <name>`.
Use that worker-surface recovery after repairing/selecting the canonical queue
or packet record. Do not hand-edit the marker, apply labels manually, or use
PR-linkage recovery as a domain-identity workaround. Child `--github-only`
remains metadata-free and does not read host queue state.

## Preview: Git-backed cross-clone scope claims (G679)

The decision and its boundaries are recorded in
[ADR 0003](../adr/0003-git-push-cas-work-ownership.md).

`worker claim` remains the GitHub issue/PR lifecycle transition above. The
separate preview `claim` group coordinates one named unit of work across host
clones without a server:

```bash
intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json
intent-cli claim acquire --scope release-prep:<owner/repo>:<version> --actor <actor> --team <team> --write --format json
intent-cli claim verify --scope <scope> --team <team> --format json
```

Acquisition is exactly `git pull --ff-only` → create the immutable record under
`.intent-cli/claims/` → commit → plain push. Only push success means acquired.
If the same scope appeared after a rejected push, the result is `held` and names
the holder; an unrelated advance is reapplied from a fresh base with a bounded
retry. Release and takeover require explicit actor/team/reason attribution, and
takeover names the displaced holder. Age never expires or transfers a claim;
`automation stalled-work` only reports `claim-stale` with actor, team, scope,
age, and last evidence.

Fresh hosts put these exact lines in `.gitattributes`, in this order:

```gitattributes
.intent-cli/runs.jsonl merge=union
.intent-cli/**/*.jsonl merge=union
.intent-cli/claims/** -merge
```

Existing hosts are not migrated automatically. Add the final, more-specific
line after broad union rules only through an explicit reviewed commit.

### Preview: claim-aware start surfaces (G680)

Packet draft, queue seed/publish-flow, worker next-action, and release-prep use
the same `claim verify` judgment. In a Git worktree the verifier uses the same
remote-default-branch resolver as claim acquire, fetches the canonical branch
from origin, and reads the claims tree from its fresh `origin` ref; the reading
checkout's current branch, local absence, or a stale local record is never
proof of ownership or no-store. This also gives a detached checkout the same
canonical answer. If the default branch cannot be resolved or fetched, the
verifier fails closed with `canonical-unavailable`; a genuine canonical
no-store host keeps legacy single-team output byte-identical. A configured
store requires the invoking team to hold the matching scope;
unheld and other-team refusals name scope, holder, and holder team. Next-slice
uses that same judgment in recommendation mode: unheld and own-team units
remain candidates, while claimed-elsewhere units are excluded with holder
evidence, so it never urges what start will refuse.

Numbering is claim-then-draft. Claim `execution-unit:<N>` before scaffolding;
after losing N, fast-forward, recompute, and retry the next number exactly once.
The GitHub lifecycle label remains visible defence in depth but is not the
acquisition fact. Review/closeout gates and `worker complete` are unchanged.

### Preview: bounded host-state Git lock retry (G700)

The sanctioned `claim` transaction is the only surface covered by this retry
policy. For its intent-cli-initiated host-state Git writes (`pull`, `add`,
`commit`, `push`, and the invoking-clone refresh), intent-cli retries only the
recognizable `.git/index.lock` contention failure. Read-only Git inspection and
agents' free-form Git commands are outside the policy; there is no queue or
daemon.

The declared default configuration is
`max_attempts=4, window=2000ms, initial_delay=25ms, max_delay=250ms,
jitter=25ms`. A contention that later succeeds includes
`git_write_retry.outcome=succeeded` and the actual `attempts`. Exhaustion is a
terminal error containing `attempts`, `elapsed_milliseconds`, the exact
`lock_path`, the original Git error, and `manual_remediation`. The lock is
never deleted, renamed, moved, truncated, or repaired by intent-cli. Inspect
the named path and confirm that no Git process owns it before an operator
manually removes a stale lock. Non-lock Git errors are returned without retry.

## Command reference (for agents, maintainers, and troubleshooting)

> **Note:** The commands below are run by the AI agent internally. The authoritative
> loop conditions come from `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>`.
> You do not normally need to run these commands manually.

```bash
# Select exactly one target (no manual label-walking)
intent-cli worker next-action --repo <owner>/<repo> --team <team> --github-only --format json

# issue-to-pr: claim, implement smallest change, open ready-for-review PR
intent-cli worker claim --kind issue --number <n> --repo <owner>/<repo> --github-only --write --format json
#    PR body MUST contain `Closes #<n>`; start from origin/main.
intent-cli worker result-summary --kind issue-to-pr --repo <owner>/<repo> --issue <n> --pr <pr> --outcome <outcome> --format json
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> --github-only --outcome <outcome> --pr <pr> --write --format json
```

For a host-side completion that needs explicit domain selection, add
`--domain <durable-domain>` to the same `worker complete` command. It must
agree with the queue/packet record; the startup marker never supplies or
overrides it.

## Next

[Review / next-slice loop setup](06-review-next-slice-loop.md) | [docs index](README.md)
