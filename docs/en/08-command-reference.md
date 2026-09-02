# Command reference (agent-facing / power users)

> English version. 日本語版: [`../ja/08-command-reference.md`](../ja/08-command-reference.md)

This page lists the `intent-cli` command surfaces that AI agents and power users
run on your behalf. You do not need to memorize these for routine use; the
[root README](../../README.md) Quickstart and `intent-cli guide start` cover the
typical path.

The commands below are what the AI agent runs internally. Run
`intent-cli guide commands list --format json` for the authoritative live catalog.

---

## Two agent roles

| Agent | Source of truth | Owns |
| --- | --- | --- |
| **Host / review agent** | parent host `.intent-cli/` state + intent tree | publishing issues, applying `intent-target`, review/approve/merge, next-slice planning, label transitions via `intent-cli automation` |
| **Child implementation agent** | the **GitHub issue/PR + repo-local code** (NOT host metadata) | implementing the issue contract, opening/updating the PR, recording outcomes via `intent-cli worker` |

Child implementation agents are **GitHub-contract-only**: they must not read or
mutate the parent host's queue-state, runs logs, packet directories, or intent
tree, and they treat the GitHub issue body as the standalone contract.

The host can live in a **separate host repository** or in the **same repository
on a dedicated metadata branch** (e.g. `main-metadata`). Both topologies are
fully supported — see [Start a project](02-project-start.md#repository-topology-choices).

## Seat command-form guidance (G696)

The installed CLI exposes the measured command-form rules for each seat kind.
This is a read-only registry: it describes forms and alternatives, but does not
edit seat settings, decide an allowlist, or approve a command.

```bash
intent-cli guide seat-commands --kind claude --format markdown
intent-cli guide seat-commands --kind codex --format json
```

Keep each action in the literal operator-sanctioned prefix and argument order.
Prefix matching can break when a command uses quoted arguments, places flags
before the path, chains differently-prefixed commands with `&&`, expands a
`$VAR`, or wraps commands in a `for` loop. Run each sanctioned step separately
when a composed form would change the prefix.

Measured denied surfaces have these alternatives:

- `gh pr comment` → `gh pr review --body-file <review.md> --comment`.
- `git checkout <branch>` → `git fetch origin <branch>` followed by
  `git diff --check <base>...HEAD`.
- A local `npm` or package-manager build → CI evidence attached to the exact PR
  head SHA.

Review seats also follow the same-account verdict convention. GitHub rejects
`gh pr review --approve` when the reviewer and PR author are the same account;
submit a `COMMENTED` review with the body-file form, then run the canonical
`intent-cli notify report` command. The report is the workflow verdict. See
`intent-cli guide review --format json` for the structured form.

The role-facing routes are structured and tested: `guide review`, `guide next
--role review`, and `guide orchestrator-thread` each name `guide seat-commands`
for the review seat. They also expose the installed `guide
topology-workspace-move` recipe for an intentional topology rebuild.

## Topology workspace move (G697)

When a recorded team is deliberately rebuilt in a new herdr workspace, render
the installed recipe first. It is read-only and gives the operator the exact
inspect → preview → apply → validate → notify-preflight sequence:

```bash
intent-cli guide topology-workspace-move --domain <domain> --team <team> --format markdown
intent-cli session-layer topology show --domain <domain> --team <team> --format json
intent-cli session-layer topology move --domain <domain> --team <team> \
  --workspace-id <new-workspace-id> \
  --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... \
  --dry-run --format json
intent-cli session-layer topology move --domain <domain> --team <team> \
  --workspace-id <new-workspace-id> \
  --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... \
  [--current-digest <digest>] --write --format json
intent-cli session-layer topology validate --domain <domain> --team <team> [--live] --format json
intent-cli notify delegate --domain <domain> --team <team> --from <sender-role> \
  --to <recipient-role> --report-to <orchestrator-role> --task-id <task-id> \
  --objective <bounded-outcome> --input <reference> --expected-artifact <artifact> \
  --result-nonce <nonce> --dry-run --format json
```

The move requires a complete explicit old-to-new pane map for recorded herdr
roles, updates the team and role workspace/pane ids in one atomic operation,
and preserves role membership, cwd, kind, delivery method, readers, profiles,
and all other fields. Multiple logical roles may share one old pane: when that
old pane maps to one new pane, the roles travel together. A mapping from two
distinct old panes to one new pane remains an ambiguity and is refused. It
never queries herdr, discovers a workspace, creates panes, or repairs a
per-role refusal. The writer holds a CAS lock and compares the topology digest
before replacement; a stale `--current-digest` is refused. The per-role
`topology record` workspace-mismatch message points to this working command as
the sanctioned whole-team transition; do not hand-edit the machine-local JSON.

## Topology host-state declaration (G736)

Record the role that owns host-state work and the envelope under which it may
perform that work. The command records explicit authority; it never infers it
from residence, agent kind, external placement, or co-location:

```bash
intent-cli session-layer topology record-host-state \
  --domain <domain> --team <team> --role <role> \
  --envelope <named-host-state-envelope> --write --format json
intent-cli session-layer topology validate \
  --domain <domain> --team <team> --format json
intent-cli guide orchestrator-thread \
  --domain <domain> --team <team> --target-repo <owner/repo> \
  --agent <agent> --format markdown
```

The validate result exposes the declaration and the guide exposes the
discovered route. A legacy topology without `host_state` remains valid and is
not migrated, but gets the informational `host-state-role-missing` finding
before publish. The finding names the host-state workflow work the team
cannot perform and says that a declaration alone does not supply a
non-sandboxed participant. A design role is legitimate when explicitly
declared; only undeclared/ad-hoc routine requests to design are prohibited.

---

## Project setup

```bash
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write
intent-cli intent status
intent-cli guide intent-work setup --format json
```

### Host-local model-resolution ledger (G685 — preview-through-1.x)

```bash
intent-cli session-layer model-resolution query --kind <codex|claude> \
  --informal-name <name> [--candidate-invocation <full-invocation>] --format json
intent-cli session-layer model-resolution record --kind <codex|claude> \
  --informal-name <name> --outcome verified --invocation <full-invocation> \
  --evidence <banner-or-argv-evidence> --write --format json
intent-cli session-layer model-resolution record --kind <codex|claude> \
  --informal-name <name> --outcome refused --invocation <refused-invocation> \
  --error <error-text> --write --format json
```

Resolve in order: ledger hit, currently-running same-kind seat argv, then ask
the human. On a miss, run `herdr agent list`, select running entries whose
`result.agents[].agent` exactly equals the resolved kind, then inspect each with
`herdr pane process-info --pane <selected-pane-id>` and read
`result.process_info.foreground_processes[].argv`. Reuse the full invocation
only when all selected same-kind seats agree; otherwise ask the human.

Every rendered launch attempt has one mandatory matching record step before
retry or continuation. After READY, record the captured exact invocation plus
banner/running-argv evidence as `verified`; after refusal, record the captured
exact invocation plus error text as `refused`. Never guess a bare id or consult
a shipped list. The append-only ledger is host-local; these commands launch no
provider and perform no provider validation.

### Operator-recorded envelope profiles (G686 — preview-through-1.x)

Record a named typed comparator baseline with a current-digest CAS. This is the
only profile write surface; `update-kind`, `update-field`, and generic JSON
editing do not record profiles:

```bash
intent-cli session-layer topology record-profile \
  --domain <domain> --team <team> --profile-name <name> --kind <kind> \
  --sandbox-mode <mode> --approval-mode <mode> --roots-policy <policy> \
  [--writable-root <path>]... --network-access <value> \
  --transport-mode <mode> --evidence <text> \
  [--permission-option <flag>]... [--network-url <url>]... \
  [--role <role> [--role-override]] --current-digest <digest|absent> \
  --confirm-record-profile --write --format json
```

The profile is an operator-recorded fact and is never learned from observed
argv. A role reference (`envelope_profile`) or typed role override takes
precedence over the G684 kind registry for that role. No profile preserves the
registry comparator byte-for-byte. A dangling reference or kind mismatch is a
machine-readable `profile-invalid` finding; it never silently falls back to the
registry. The command is confirmation-, kind-, and digest-guarded, and does not
launch, recover, or mutate a seat. Profile comparison remains detection-only
and retains G684's exact security fields, cadence, model/reasoning exclusion,
and preview-through-1.x status.

## Design / intents

```bash
intent-cli interview next-question
intent-cli interview record-answer ...
intent-cli interview compile
intent-cli guide workflow
```

## Design-thread improve / realignment

A periodic reflection step for a design thread: step back and check whether
recent work still aligns with the original mission, vision, values, ADR/design
notes, and intent tree. In a design thread you can simply paste the
natural-language request and let the agent run the guide internally:

```text
intent-cli で improve プロセスを実行してください。
```

The agent then fetches the current guidance and produces a structured report.
`improve` is a first-class top-level command (with `guide improve` as the
equivalent guide-namespaced form):

```bash
intent-cli improve --domain <domain> --format markdown
intent-cli guide improve --domain <domain> --format markdown
```

Both forms return identical guidance and are discoverable via `intent-cli
--help` and `intent-cli guide commands list`. If the installed CLI has no
`improve` surface, report `improve guidance unavailable` and update the CLI —
do **not** substitute `bug-to-intent-repair`, host-loop recovery,
`state-doctor`, or dirty-state repair.

By default `improve` is **implementation-aware**: when evidence is available it
also inspects related GitHub issues/PRs, implementation diffs, tests, review
findings, and product evidence to find the current top blocker and propose a
corrective backlog (`Implementation Reality Check`, `Blocker Cluster Analysis`,
`Corrective Backlog Candidates`) — intent-tree cleanup alone is not enough when
packet history suggests an unresolved product blocker. Pass `--light` for a
quick intent-only reflection:

```bash
intent-cli improve --domain <domain> --light --format markdown
```

Declare the team's realignment window independently, like a supervision bound,
then append an explicit durable run record after the human/agent performs the
review (G662, `preview-through-1.x`). The run record contains the domain, mode,
timestamp, and artifacts actually touched:

```bash
intent-cli improve window --domain <domain> --days <days> --write --format json
intent-cli improve record --domain <domain> --mode implementation-aware \
  --artifact <touched-path> \
  [--artifact <touched-path> ...] --write --format json
```

The semantic review remains human/agent work. intent-cli records that it ran
and uses its timestamp for recency only; it never scores or grades the review.
This record does not create a scheduler, cron job, auto-run, or stalled-work
debt class.

After operator approval the agent may create the proposed corrective packets and
publish **at most one** first GitHub issue unless asked for more.

`improve` is a **safety net**, not the first line of defense. The normal path is
**packet-time intent maintenance** (G461): when a packet is drafted, the agent is
already prompted to consider intent placement, ADR candidates, diagram candidates,
docs updates, and closeout knowledge writeback — see `intent-cli guide workflow task
packet-draft`. That metadata is optional and backward-compatible (legacy packets
without it stay valid), but capturing it while the design context is fresh keeps the
intent tree, ADRs, and diagrams from drifting behind packet history in the first
place. `improve` then catches whatever the packet-time check missed.

`guide improve` is a design-thread reflection process — **not** a scheduler, a
provider launcher, or a routine host-loop / worker-loop recovery diagnostic.
Operational metadata/label/queue repair stays in the existing operational
surfaces (`automation reconcile`, `automation publish-recovery`,
`review closeout-plan`). It inspects MVV, ADR/design notes, the intent tree,
recent packet history, clarification history, and short-term-loop signals, and
classifies the outcome as one of `aligned`,
`intent-strengthening-recommended`, `clarification-recommended`,
`corrective-packet-recommended`, `adr-update-recommended`,
`short-term-loop-detected`, or `operator-policy-required`. Mutations are
proposed first and applied only after operator agreement through supported
intent-cli / repo paths.

## Grill — persistent interview mode

A user-facing **persistent interview mode** (G463). Once you ask to grill a
topic, the design thread STAYS in grill mode: it generates an open-question
backlog from the current intent context (intents, packets, ADR/design notes,
docs, and relevant implementation evidence) and keeps asking **one question at a
time**, continuing after each answer without you repeating `grill`, until a
structured stop condition is reached.

```bash
intent-cli grill --domain <domain> --format markdown
intent-cli guide grill --domain <domain> --format markdown
```

Both forms return identical guidance and are discoverable via `intent-cli
--help` and `intent-cli guide commands list`. grill is built on the existing
`interview` artifacts (it records answers through `intent-cli interview
record-answer` and reads pending questions through `intent-cli interview
next-question`); it is **not** `clarification` (blocker resolution) and **not**
`improve` (retrospective realignment), and it never auto-publishes packets or
issues.

Stop conditions: `no-more-questions` (returns `今のところ追加質問はありません`
only when the backlog is empty and rediscovery finds nothing), `packet-ready`,
`intent-update-ready`, `clarification-needed`, `blocked-by-user-decision`, and
`too-broad-split-needed`. Packet / issue / intent-update actions are proposed at
a stop condition and applied only after explicit operator acceptance.

## Inspect — evidence-backed observation

A named process for **observing the real product** before cutting tasks (G466).
`inspect` guides an agent to exercise the actual app / CLI / UI / logs / tests,
strictly separate observed evidence from inference, compare against expected
intent, and turn the gaps into packet candidates.

```bash
intent-cli inspect --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide inspect --domain <domain> --target-repo <owner/repo> --format markdown
```

The **Inspect Report** separates `observed_behavior`, `expected_intent`,
`evidence`, `gaps`, `risk_severity`, `recommended_next_action`, and
`packet_candidates`. The first pass is **read-only by default** — it never runs
destructive interactions or auto-publishes — and it *guides how to use*
browser / computer-use / log / test tooling rather than replacing it. Based on
what it finds, an inspect pass routes to **stack** (package the gaps),
**grill** (extract unclear intent), **improve** (systemic drift), **recovery**
(broken operational state), or **no-action** (behavior matches intent). It is
distinct from grill, stack, and improve.

## Next — design-side action advisor

The simplest design-thread question — "what should I do next?" — answered by
`intent-cli next` (G465). It lays out the catalog of design-side processes and
recommends one, so users do not have to remember every command name.

```bash
intent-cli next --domain <domain> --team <team> --target-repo <owner/repo> --format markdown
intent-cli guide next --domain <domain> --team <team> --target-repo <owner/repo> --format markdown
```

Ask it in natural language: `intent-cli に聞いて、次に何をしたらいいか教えてくださ
い。`. It checks the evidence (current intents, open questions, packet backlog,
open PRs / review state, CLI / queue health) and recommends exactly one of
**grill** (extract open questions), **stack** (create packet backlog + publish
first issue), **improve** (retrospective realignment), **inspect**
(evidence-backed observation of real app/CLI/UI/log/test behavior, not just
status checking), **issue-publish** (publish a ready packet), **review** (review
an open PR), **recovery** (repair stale CLI / queue), or **idle** (nothing
actionable).
The output includes the recommended action, the reason, the evidence checked, a
paste-ready suggested prompt, and the safety boundary. `next` is **read-only by
default** and never auto-executes the chosen action — the user decides whether
to run the suggested prompt.

When `--domain` and `--team` are supplied, `next` also reads the team's recorded
topology and supervision cycle. Recorded topology with no completed cycle/front-door
handoff adds `bootstrap-resume` and links the render-only
`intent-cli guide bootstrap`; no topology and a completed cycle are silent. No
recorded cycle independently adds the `supervision-setup` recommendation;
an existing cycle leaves that recommendation silent. The host-init and
design-side loop guides carry the deployment step and link the
[orchestration reference](12-agent-message-orchestration.md); this command only
detects the missing record and never starts or manages the background process.
The bootstrap trigger phrases are `Start this work in a herdr-only team.` and
`herdr-only で起動して。`; its output asks the human for CLI/model and app-kind
choices and executes nothing.

With `--domain`, `next` reads the independently declared realignment window and
the latest append-only improve-run record. Only when no run falls within that
window does it add a paste-ready `realignment` action (run improve, then record
the completed run).
A fresh record makes that recommendation silent immediately. With no window
declaration, `next` invents no cadence. This is timestamp recency only—not a
quality judgment—and it adds no scheduler, cron, auto-run, or stalled-work debt
class.

### GitHub API quota visibility (G673 — preview-through-1.x)

GitHub-consulting commands distinguish a successful empty result from an
unavailable read. On quota exhaustion they emit the machine-readable
`cause: github-api-quota-exhausted`, `resource`, `remaining`, and `reset_at`
(also under `degraded_state`). `worker next-action` uses `action: unavailable`;
`host-loop-next-action` and host review/reconcile surfaces report
`detection-unavailable`. These states are recognized from the structured
`gh api rate_limit` response, not quota words in stderr.

`automation stalled-work` retains findings computable from local state, marks
them `partial: true`, and returns `detection_available: false`; an empty
`items` list under that state is not healthy. `automation heartbeat` carries
the same state and verdict. `automation doctor` reports every observed
resource's `remaining` and `reset`/`reset_at` and returns a non-`ok` verdict
when quota makes GitHub-consulting surfaces inoperable. Callers decide whether
to wait for reset; no command retries, sleeps, schedules a reset, budgets
requests, changes transport, caches, or batches in G673.

### Checkout freshness in host-state reports (G727)

`automation stalled-work` also reports what checkout its answer came from when
that fact is not safely current. It compares the local `HEAD` with the actual
default-branch `HEAD` returned by `git ls-remote --symref origin HEAD`:

- `checkout_freshness: stale` names the local and remote commit IDs and tells
  the operator to sync and rerun the report.
- A genuinely current checkout omits `checkout_freshness` and emits no
  freshness banner. This keeps the notice rare enough to remain useful.
- If the remote cannot be queried (offline, missing remote, or an incomplete
  response), `checkout_freshness: unknown` is emitted with a reason. Unknown
  must not be read as current.
- The remote probe is bounded to three seconds, reads stdout and stderr under
  that same bound, kills the Git process tree on expiry, closes stdin, and
  disables terminal/SSH prompts. A timeout is therefore an actionable
  `unknown`, not a stalled wake.

The probe is read-only: it uses no `fetch`, `pull`, `reset`, or other sync
operation, and it does not change the existing stalled-work finding logic.
`automation heartbeat` wraps `stalled-work` and therefore carries the same
warning. The survey does **not** classify every sibling as unaffected:
`intent status` was independently demonstrated on the same stale clone to
return stale local queue state without checkout provenance. Source inspection
also shows `automation summary`, `automation state-doctor`,
`host-loop-next-action`, and `automation heartbeat` read `context.RepoRoot`, so
they share the unstated-checkout provenance property (heartbeat inherits the
new warning here, while the others remain follow-up work). This slice is
deliberately scoped to `stalled-work` plus heartbeat inheritance. A follow-up
must extend and test an explicit freshness/provenance contract for those
RepoRoot-reading siblings. `status brief` and host-review diagnostics were
also inspected and do not read `RepoRoot` for these answers; they are
unaffected by this specific unstated-checkout path, not globally certified as
current. The survey is not a reason to widen G727 here.

G672 adds an optional invoking-role pointer (preview-through-1.x):

```bash
intent-cli guide next --role design --format markdown
intent-cli guide next --role orchestration --format markdown
intent-cli guide onboarding --role implementation --format markdown
```

When the role has an installed operating contract, this pointer is the first
read-before-acting instruction in `guide next` and onboarding: design reads
`intent-cli guide design-thread`, orchestration reads
`intent-cli guide orchestrator-thread`, implementation reads
`intent-cli guide worker issue-to-pr`, and review reads `intent-cli guide
review`. A role without a contract receives no invented pointer. The existing
procedure and first-call ordering remain unchanged, and this does not force a
reread on every wake; reread after a CLI-version or session-layer configuration
change. The same output records the measured remote-herdr incident attributed
to operator-filed feedback in issue #1441 sections D/B-1 (remote-herdr, 48
units), including the session-scoped nohup process that died twice unnoticed.

That setup step routes through `intent-cli notify supervise install`, which
emits a current-session launchd, Task Scheduler, or systemd artifact and exact
operator registration/unregistration commands without executing lifecycle
commands. The G712 GUI-session fallback keeps artifacts out of
`~/Library/LaunchAgents`, omits macOS `RunAtLoad`, and therefore has no login or
reboot auto-load. Use `intent-cli notify supervise reconcile --write` (or
`uninstall --write`) to list loaded jobs before/after, boot out managed jobs,
remove artifacts including legacy login-persistent plists, and name the
removals. For ongoing health, compare the age of the team's `cycles.jsonl`
record with its declared bound. Process-name grep is an anti-pattern because
a measured cross-team collision killed one team's supervisor while retaining
another team's process.
Optional `notify supervise --event-mode` keeps blocking per-seat `herdr agent
wait` subscriptions inside that same process for seconds-scale
implementation/review settlement wakes. It is the concrete implementation of
the normative SECOND wake source, herdr `pane.agent_status_changed`; the
independent interval cycle remains the safety floor and both sources
de-duplicate by recorded seat transition. Because install
artifacts embed the invocation, adopting event mode requires re-emitting and
explicitly re-registering the artifact with `supervise install --event-mode`;
an existing artifact remains interval-only. This path is measured with herdr
0.8.0 on macOS; other versions/platforms are unverified.
Supervision and install emission remain previews through 1.x under the
compatibility promise.

## Notify — explicit pending-delegation disposition (G671 — preview-through-1.x)

Use the notify lifecycle commands for role-to-role messages. When an open
delegation's outcome was superseded or applied elsewhere without a matching
report, record that fact explicitly:

```bash
intent-cli notify dispose --domain <domain> --team <team> \
  --task-id <task-id> --kind superseded|applied-elsewhere \
  --actor <actor> --reason <reason> \
  [--superseding-task-id <task-id>] \
  [--applied-outcome-evidence <evidence>] --write --format json
```

`superseded` requires a superseding task id; `applied-elsewhere` requires
outcome evidence. The record stores the kind, actor, timestamp, reason, and
applicable evidence. `notify status` exposes `settlement_basis: disposition`
and keeps it distinct from report settlement. Disposed records leave the open
population used by `notify supervise` and `stalled-work`. Disposition is never
automatic or time-based, and the command refuses unknown or already-settled
task ids. A late `notify report` is still delivered, with the disagreement
named and the disposition preserved. This post-freeze surface is previewed
through 1.x under the compatibility promise.

`automation stalled-work` also reports an informational `pending-delegation-open`
item for an open notify record once it crosses the configured stale threshold,
and exposes the unfiltered `open_pending_delegations` count. Report-settled and
disposition-settled records are excluded; the scan remains read-only and never
chooses or writes a disposition.

## Stack — packet backlog creation + first issue publish

A named **forward planning** process (G464). `stack` reads the current intents,
creates an ordered backlog of the packets that are ready now (often around ten),
commits and pushes that durable state, and by default publishes **at most the
first** GitHub issue — leaving the rest as a deferred backlog.

```bash
intent-cli stack --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide stack --domain <domain> --target-repo <owner/repo> --format markdown
```

`stack` matches タスクを積む. It is distinct from `improve` (retrospective
realignment from a drift / loop crisis), `grill` (persistent open-question
interview), `clarification` (blocker resolution), and runtime `queue`
transitions. It respects open questions, work-in-progress, and host-only packet
boundaries, commits/pushes durable packet state before issue-publish, and never
hand-applies `intent-target` (the host publish boundary applies it). The output
shape lists `created_packets`, `recommended_first_issue`, `published_issue`, and
`deferred_items`.

## Packets / issues

```bash
intent-cli packet ...
intent-cli issue validate-body ...
intent-cli issue prepare ...
intent-cli issue publish-reviewed ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --write
```

### Bug implementation-repair links (G782)

Record the child repair handoff on the durable bug projection with the command's
own accepted link flags:

```bash
intent-cli bug implementation-repair <bug-id> \
  [--execution-unit <unit>] [--issue-number <n>] [--issue-url <url>] \
  [--actor <name>] [--note <text>]
```

When supplied, these values are stored as `repair_execution_unit`,
`repair_issue_number`, `repair_issue_url`, `recorded_by`, `note`, and
`recorded_at`. Re-running without link flags preserves a prior recorded link;
re-running with new values replaces it and names the prior values in the
result. If both `--issue-number` and `--issue-url` are present, the URL's final
URI path segment must exactly equal that number or the command refuses and
names both supplied values. A query string or fragment does not change the
final path segment, so `.../issues/1705?repair=1706` is rejected for issue
number `1706`.

`intent-cli bug implementation-issue <bug-id>` gives a recorded
`repair_execution_unit` precedence and uses only
`.intent-cli/issues/<unit>/packet.yaml` as its target. A packet rooted in the
G337 `implementation_issue_packet` schema is not the legacy
`ProjectionPacketRuntimeReader` `execution_unit` schema expected by this
handoff; publish it first with `intent-cli issue publish-flow <unit> --repo <owner/repo> --write` and retry.

## Implementation & review loops

```bash
# Fetch the complete loop prompt for an AI agent:
intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>
intent-cli guide oneshot --kind host-review-next-slice    --domain <name>
```

Operator-dogfooding prompt templates that wire these loops entirely through the
deterministic worker/metadata commands live under
[`docs/automation-templates/`](../automation-templates/README.md).

## Session-scoped supervision setup (G712)

The declared supervision setup route is executable from a bare directory with
no `.intent-cli/config.toml` or host metadata:

```bash
intent-cli guide workflow task supervision-setup --format json
intent-cli guide workflow task supervision-setup --format markdown
```

It renders the shipped session-scoped contract: `notify supervise install`
authors and proves the artifact without registering a process, the printed
`launchctl bootstrap gui/$(id -u) '<artifact-path>'` is an explicit current-GUI
session action, and `notify supervise reconcile --write` / `uninstall --write`
reports before/after state and removes only managed drift. The route is
read-only and does not execute any of those lifecycle commands.

## Recovery

```bash
intent-cli worker issue-preflight       --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight  --repo <owner>/<repo> --pr <n>    --format json
intent-cli automation doctor --format json
intent-cli automation doctor --domain <domain> --team <team> --format json
```

The bare doctor keeps an empty anonymous root unjudged. Supply `--domain` and
`--team` together to require the shared record-first session-layer preflight for
that named team; an unrecorded mode is configuration-incomplete, never
not-required.

---

## Command group overview

`intent-cli guide commands list` is a **role-based catalog** (G467): every
command group carries an operator-role category — **design** (improve / grill /
stack / next / inspect / intent / interview / packet / clarify), **host-review**
(review / closeout / automation / issue), **child-implementation** (worker),
**recovery-diagnostics** (automation doctor / metadata / queue), and
**advanced-developer** (task) — alongside its `primary`/`support` lifecycle
classification. `intent-cli guide help` explains the same role buckets and points
to the loop-prompt generators (`guide workflow task implementation-loop` /
`review-next-slice-loop`).

| Surface | Role |
|---------|------|
| `intent-cli guide …` | Ask-first guidance: collaboration model, workflow, prompt-template catalog, one-shot prompts |
| `intent-cli status brief` | Compact AI-thread context input |
| `intent-cli clarify draft` / `clarify record` | Owner clarification flow |
| `intent-cli issue validate-body` | Standalone Child Issue Contract enforcement |
| `intent-cli issue prepare` / `issue publish-reviewed` | Reviewed issue body publish boundary (never applies `intent-target`) |
| `intent-cli worker next-action` / `claim` / `result-summary` / `complete` | Child implementation loop selector + bounded label transitions |
| `intent-cli automation summary` | Provider-neutral label-driven automation contract emitter |
| `intent-cli safety nested-provider-handoff` | Artifact-only nested-provider safety guard (never spawns providers) |

---

## Rules of thumb

- **Use `intent-cli` transition commands, not raw edits.** Do not directly edit
  queue-state, workflow labels, packet publish metadata, or other host artifacts
  when an `intent-cli automation` / `intent-cli worker` command owns that
  transition. Apply labels through those commands, never `gh ... edit
  --add-label`.
- **Ask, don't read-and-guess.** Prefer `intent-cli guide ...` over reading
  local rule files; the guidance reflects the installed CLI's current contract.
- **`intent-cli` does not launch AI providers.** It emits deterministic
  guidance, validates contracts, and performs bounded GitHub/metadata
  transitions. The AI agent stays in the driver's seat.
