# Agent-message orchestration (single-domain vs multi-domain)

← [Create packets & publish issues](04-packets-issues.md) | [docs index](README.md)

This page describes the **primary four-thread model** (design / orchestrator /
implementation / review) and, in particular, how it stays safe when a single
host repository holds **several intent domains**. Choose the supported
`herdr-only` transport for a collocated single-machine team because it has
fewer dependencies, or choose supported, non-retired `agmsg` + herdr for a
distributed team or an existing agmsg investment. Record the choice with
`session-layer set`; neither transport is primary. The authoritative,
paste-ready prompts come from installed intent-cli guidance — do not copy
prompts from this page by hand. Generate the current prompts with:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --mode single-domain|multi-domain --format markdown
```

## Team shape: delivery and authoring-only (G691 — preview-through-1.x)

Team shape is orthogonal to the session-layer transport. The durable record is
written through the canonical command:

```text
intent-cli team-mode show --domain <domain> --team <team> --format json
intent-cli team-mode set --domain <domain> --team <team> --mode delivery|authoring-only --write --format json
intent-cli team-mode validate --domain <domain> --team <team> --format json
```

For read and consumer commands where `--team` is optional, a domain-wide
record wins. If there is no domain-wide record and exactly one team-scoped
record exists for the domain, intent-cli resolves that unique team context;
the effective team is still carried into authoring-only audit and handoff
records. If multiple team-scoped records exist, resolution fails closed with
the named `team-mode-ambiguous` outcome and the caller must supply `--team`.
Omitting `--team` can therefore never silently turn a recorded
authoring-only team into delivery.

Absence means `delivery`, preserving the existing behavior byte-for-byte. In
`authoring-only`, the operator-facing front door shapes/interviews intents,
authors standalone packets, and publishes issues. Bootstrap checks only the
front door plus repository/claim/publish prerequisites; it does not require a
delivery topology or launch delivery seats. `guide next` offers only
shape/interview, packet authoring, publish, improve, inspect, and idle.

The measured bootstrap state is `authoring-only-complete` once the durable
`team_mode=authoring-only` record and front-door shape have been inspected.
Repository, claim, and publish commands remain explicit operator prerequisites;
they are rendered actions, not missing delivery-topology facts.

`notify supervise`, `notify supervise install`, `notify adjudicate`, and
delivery-topology commands return the named `not-applicable-team-mode` outcome.
`notify adjudicate` is not applicable because an authoring-only team has no
delivery seat or adjudication dialog to adjudicate. The G691 gate does not
disable `notify report`, `notify escalate`, `notify status`, or `notify
dispose`; those reporting and settlement surfaces remain usable. No new
publish, delegation, or handoff behavior is introduced by this slice. No
supervisor, worker lifecycle, or transport migration is implied. Delivery
remains the default and the four-thread/supervision contract in this page is
unchanged. See [ADR 0005](../adr/0005-team-mode-authoring-only.md); the parent
intent's ADR-014 remains unchanged until its host-side successor link is
written.

## Authoring-only publish audit and shared diagnostics (G692 — preview-through-1.x)

An authoring-only front door may publish an issue only after the existing
readiness, claim, repository, content, duplicate, and branch-lane gates pass.
The publish command records the design actor, explicit operator-acceptance
evidence, and destination ownership, then writes a durable
`published-external-handoff` record for the created issue. These records make
the handoff observable; they do not authorize a worker or bypass any publish
gate. Delivery-mode issue content, issue creation, and run-event bytes remain
unchanged.

For a packet that declares a lane, authoring-only records the design proposal
and requires a distinct operator confirmation. The operator lane is not an
orchestration impersonation: a confirmation claiming `orchestration` is
refused, and `notify delegate` to a named worker role returns a nonzero
`not-applicable-team-mode` refusal without touching the outbox or transport.
`notify delegate` may omit `--team` only when the domain resolves to one
team-scoped record; intent-cli carries that team into the refusal or delivery
decision and fails before the outbox when no unique team context exists.
`automation stalled-work` keeps `published-not-delegated` visible until the
matching handoff record exists; only that exact destination/issue evidence
silences the observation.

`automation stalled-work`, `automation state-doctor`, `intent status`, and
`status brief` consume one shared team-mode capability matrix. In
authoring-only, worker, review, CI, delegation, and supervisor classes are
explicitly not applicable; authoring, contract/readiness, branch-lane,
branch-routing, publish-durable-state-drift, and knowledge/guide-writeback
remain active. Delivery retains every class and its existing output. This is
a diagnostic judgment only and never weakens publish, claim, or ownership
gates.

## Durable completion continuation chain (G695 — preview-through-1.x)

G695 makes the completion-to-next-action boundary observable without changing
who may act. A delivered `notify report` and an observed herdr
`working→done|blocked|idle` transition each seed an append-only chain at:

```text
.intent-cli/continuation-chains/<domain>/<team>/chains.jsonl
```

The chain is ordered as:

```text
report-received
  → orchestration-wake-attempted
  → wake-delivered-or-observed
  → canonical-state-classified
  → required-continuation-started | named-blocker-recorded
```

Every link is timestamped and the record exposes the exact next missing link.
When `canonical-state-classified` is present without either terminal link,
`next_missing_link` is emitted as
`required-continuation-started|named-blocker-recorded` so a classified-then-stop
chain cannot appear complete or say that nothing is owed.
Inspect all chains, or filter one completion signal, with the read-only
surface:

```text
intent-cli automation continuation-chain --domain <domain> --team <team> \
  [--task-id <task-id>|--completion-signal-id <signal-id>|--chain-id <chain-id>] \
  [--routing-root <host-root>] --format json|markdown
```

The report and supervision writers only record observations. An orchestration
wake can append the later links by supplying the signal to
`automation host-loop-wake` with `--write`; a safe continuation records
`required-continuation-started`, while a refused or judgment-gated path must
record `named-blocker-recorded`. A classification with neither terminal link
is therefore queryable as a silent stop rather than being treated as done.

The measured supervisor also names the three canonical owed-transition shapes
from #1491: `approved-direct-lane-merge-closeout-owed` with exact-head and
all-green evidence, `merged-pr-knowledge-writeback-dispatch-owed` with the
declared write-back targets, and `actionable-queue-next-slice-publication-owed`
with the empty-WIP and issue-cut-ready evidence. Their `owed_transition` and
`evidence` fields are diagnostic findings; neither the chain nor supervision
merges, publishes, relays keys, or otherwise executes the transition.

> **Preview through 1.x (G695).** The chain file, query surface, observed
> transition seed, terminal-link evidence, and the three named findings are
> additive observability. Authority boundaries, message transport, and the
> existing G654/G657/G659/G685 wake contracts remain unchanged.

## Application-front-door bootstrap (G664 — preview-through-1.x)

From a desktop-app conversation, say **`Start this work in a herdr-only team.`**
or **`herdr-only で起動して。`** and render the current pass with:

```text
intent-cli guide bootstrap --domain <domain> --team <team> --target-repo <owner/repo> --routing-root <host-root> --format markdown
```

The six steps stay in order. First ask the human which CLI and model each of
the design, orchestration, implementation, and review seats should run; there
are no defaults. Then emit the herdr workspace/pane/typed-seat commands from
the installed recipe and G637 layout guide, record operator-supplied topology,
emit `notify supervise install`, ask the application kind and whether it has
an inbound app monitor before applying G654's design placement rule, and
delegate the first task to orchestration. The final output explicitly names
the new design thread and says the application conversation remains the
operator's front door, not a loop seat.

Recorded topology selects `join-and-delegate`: do not recreate its workspace
or seats. Partial state is named—for example
`topology-recorded-seats-missing` or
`topology-recorded-supervision-and-handoff-missing`—and only missing commands
are emitted. `guide next --domain <domain> --team <team>` recommends
`bootstrap-resume` for recorded topology without a completed supervision
cycle/application-front-door handoff and clears that recommendation after the
cycle completes. No topology is silent because bootstrap has not started.

This surface executes nothing: intent-cli never invokes herdr, starts a
provider, registers or unregisters an OS scheduler artifact, or adds
application-side integration. It composes the existing recipes, deployment
rule, four-judgment-thread-plus-one-supervision-process formula, and
preview-through-1.x boundaries without changing them.

### Host-local model resolution (G685 — preview-through-1.x)

For bootstrap, seat recovery, and a kind switch, resolve an operator's informal
model/effort name in exactly this order:

1. query the host-local measured ledger with `intent-cli session-layer
   model-resolution query`;
2. if it misses, run `herdr agent list`; retain running entries whose
   `result.agents[].agent` exactly equals the resolved kind, sort by workspace
   and pane, and inspect every selected pane with
   `herdr pane process-info --pane <selected-pane-id>`;
3. read `result.process_info.foreground_processes[].argv` and reuse the full
   invocation only when every selected same-kind seat agrees;
4. if there is no readable agreed argv, ask the human.

Never guess a bare model id and never consult a shipped list. intent-cli ships
only measured stable flag shapes: Codex uses `--model <id> -c
model_reasoning_effort=<level>`, and Claude uses exactly
`claude --model <id> --add-dir <host-root>`. The Claude entry is grammar-only;
it ships no model id or catalogue. No grammar is invented for another kind.

Every rendered launch attempt has one mandatory matching record step before
retry or continuation. After READY, run the rendered `model-resolution record
--outcome verified` command with the captured informal name, kind, exact
launched invocation, and banner/running-argv evidence. After refusal, run the
rendered `--outcome refused` command with the captured exact invocation and
error text. A later query names that negative evidence and prevents retry of
the same invocation. The JSONL ledger is machine-local at
`.intent-cli/model-resolution/ledger.jsonl`, is measurement rather than
configuration, and has no sharing or catalogue mechanism.

Measured on 2026-08-12 during btx-mvc setup: `--model sol` received an
account-shaped HTTP 400. Reading another workspace's running Codex argv supplied
the working full invocation. That provider id remains host-local evidence and
is deliberately absent from intent-cli and these guides.

These commands launch no provider and validate no id against a provider API.
G647 envelope fields and G684's model/effort-as-wish drift semantics are
unchanged.

## Design-thread operating contract (G654 — preview-through-1.x)

Render the agent-kind-neutral contract with `intent-cli guide design-thread`.
`guide commands list` catalogs it and `guide next` names it for the design
role. Its wording is the same in `agmsg` and `herdr-only`, with or without a
named team. Re-read installed guides after a CLI version or session-layer
configuration change, not on every wake.

### Role-contract precedence (G672 — preview-through-1.x)

When `guide next` or `guide onboarding` is invoked with `--role`, the role's
installed operating contract is the first read-before-acting instruction when
that role has one: `design` → `intent-cli guide design-thread`,
`orchestration` → `intent-cli guide orchestrator-thread`, `implementation` →
`intent-cli guide worker issue-to-pr`, and `review` → `intent-cli guide review`.
Roles without an installed contract receive no invented pointer. This only
adds the new pointer's ordering; it does not remove or reorder the existing
procedure, change the contract text, or force a reread on every wake. The
pointer is reread after a CLI-version or session-layer configuration change.

**Measured incident record (attributed field evidence).** Operator-filed
feedback in issue #1441 sections D/B-1 for remote-herdr (48 units) recorded
that, over days, a design seat had not read its own contract; a parallel detector violated
the rule; supervision used an undeclared bound, the default interval, no event
mode, and a session-scoped nohup process that died twice unnoticed; seven
findings were mis-filed. This records the incident without settling the
substantive B-1 question.

### Public project feedback (G705 — preview-through-1.x)

`intent-cli guide feedback --format markdown` (or `--format json`) is the
installed, render-only onboarding route for project feedback. It names the
public GitHub issue channel `J-Tech-Japan/intent-system` and prints this
reviewed command form for a human or design thread to run deliberately:

```bash
gh issue create --repo J-Tech-Japan/intent-system --title "<short summary>" --body-file <reviewed-report.md>
```

The guide never executes that command and provides no send path: no issue
creation, no API POST, no network connection, no subprocess, no
confirmation-based submission, no telemetry write/queue, or child-issue
publishing is performed by intent-cli. Project feedback remains distinct from
execution-unit child issue publishing.

**PUBLIC / WORLD-READABLE PERMANENTLY:** Issues in
`J-Tech-Japan/intent-system` are public and remain world-readable permanently.
Never include credentials or tokens, private hostnames or private paths,
customer or personal data, or internal URLs. Review pasted logs before filing.

The effective report shape is recommendations only; never required gates:
exact installed version string, timestamped observations, expected versus actual
behavior, reproduction context, and a verified-versus-assumed separation. An imperfect report is still fileable; missing a recommendation
does not suppress it. An AI seat may draft only. Deliberate filing is a
per-action act by the design thread or the operator under G701, with no new
standing authority and no confirmation-based shortcut. This is project
feedback, not the execution-unit child issue contract.

### GitHub quota as a named last-net blind spot (G673 — preview-through-1.x)

The periodic `automation stalled-work` check remains the last net and its
wake/supervision class is unchanged. A GitHub API quota failure is not a
healthy empty result: affected surfaces emit
`cause: github-api-quota-exhausted`, the exhausted `resource`, and its
`reset`/`reset_at`. `automation heartbeat` and `stalled-work` expose
`detection_available: false`; `stalled-work` keeps local-only findings with
`partial: true`. Record the reset and let orchestration decide whether to
wait deliberately. There is no automatic retry, sleep, reset scheduling,
request budgeting, transport migration, or cache in this slice.

This is a measured incident record with separate attribution: issue #1442 is
the remote-herdr measurement (`graphql.remaining == 0`, 5,046 requests/hour),
while this host's same-day GraphQL refusal during the G667 publish cycle with
REST core at 4999/5000 is host corroboration, not a re-attribution of #1442.

### REST reads with an explicit GraphQL remainder (G674 — preview-through-1.x)

The #1442 surface inventory now records the exact field mapping for the
issue-list reads: `GET /repos/{owner}/{repo}/issues` over `core`, with
`number`, `title`, `html_url`, `created_at`, `body`, `updated_at`,
`labels[].name`, and `state` mapped to the existing candidate shape. The
`pull_request` marker is only an adapter filter. The PR reads remain
`graphql-bound` where the caller consumes `closingIssuesReferences`; stalled-
work and its heartbeat also retain the GraphQL `CheckRun`/`StatusContext`
`statusCheckRollup` remainder because check-runs alone are not field-complete.

When a degraded state is caused by one of those reads, `dependency` names
`rest-core` or `graphql-bound` and `unverified_fields` names the fields that
prevented migration. This is a transport/quota attribution only: wake and
supervision semantics, caller output, authentication, and mutation paths are
unchanged. There is no cache, batching, budget, retry, sleep, or reset
scheduler in this preview slice.

A design wake has exactly four valid outcome shapes: advance the canonical
workflow; confirm new evidence of real progress; discover the next actionable
design, packet, or issue candidate and hand it to orchestration; or report a
blocker only a human can resolve. `no-actionable`, `running=true`, liveness,
unchanged status, and `no change` are not outcomes while the project is
unfinished. Reports cite evidence of change. A human-action report names the
minimal concrete operation and why automation cannot perform it.

Keep provenance states distinct: candidate, accepted design, packet, queued
unit, published unit, and WIP. Do not use an execution-unit number until it
exists in canonical host state. Before prioritising an external handoff,
record source kind, reference, timestamp, requesting party, and acceptance
state. Read-only inspection needs no approval. Unless an operator says
merge-only, a merge instruction authorises one complete closeout transaction:
merge, verify the merge commit, close the linked issue, transition the queue,
append runs, write back host state, and push host state. Ask once for that
whole transaction, never piecemeal. Publication, contract, priority, and
release changes still require explicit acceptance.

GitHub `reviewDecision` alone never proves a blocker. Attribute and compare
intent-cli workflow labels, the exact PR head, GitHub checks, GitHub
mergeability, and canonical queue state. Delegation verification has three
layers: canonical workflow status; recorded session-layer agent state and
G652 activity sub-verdicts; and real artifacts such as files, commits, and
pull requests. `running=true` alone never proves progress, and terminal
content is never parsed as workflow evidence.

The team formula is four judgment-bearing threads plus one supervision
process. Watcher infrastructure is not a fifth role: it holds no conversation,
makes no judgment, and spends no model tokens. Supervision runs outside the
design conversation and is consulted at most once per design wake.
Orchestration owns detection, classification, and authorised recovery for
every stall class, including review wedges. Design is event-driven and
receives only the escalation set rendered by the guide. Its residual duty is
a low-frequency check of the last completed supervision-cycle record against
the declared supervision-liveness bound, not a conversational heartbeat. The
detection bound must exceed the wake interval plus scheduling jitter.

A design seat whose agent kind has no inbound app monitor must be a recorded
resident herdr seat with the routing root as cwd, where persistent AGENTS rules
apply. A kind with an inbound app monitor may use that external reader. This is
a deployment rule, not a recommendation; it does not assign stall recovery to
design or add a model-backed monitoring role.

## Host-state honesty and complete scaffolds (G661 — preview-through-1.x)

Five host-side edges share one rule: a surface must not claim more than its
evidence. `automation knowledge-writeback-record --write` creates the local
record but always states that other checkouts cannot observe it until the path
is committed and pushed; intent-cli never commits it. `automation stalled-work`
therefore distinguishes an absent record (`knowledge-writeback-pending`) from a
local recorded path (`knowledge-writeback-recorded-uncommitted`) and names the
exact path that still needs commit and push.

`packet retire --reactivate --evidence <text> --write` is the only reactivation
edge. It requires evidence, writes `lifecycle: ready` plus the prior lifecycle,
evidence, and timestamp, and appends `packet-reactivated`. Closeout never infers
this transition: if shipped work still has a non-publishable lifecycle, closeout
emits `shipped-while-retired-contradiction` and leaves the sidecar unchanged.

Issue-cut readiness consumes the existing publish validator's verdict. A TODO
scaffold, including placeholder-only Related Links, is visible as not ready with
the validator's reason and is never offered as `issue-cut-ready`. `packet draft`
also leaves guide reachability commented until the author chooses one of these
accepted forms; a missing-declaration warning prints both fragments verbatim:

```yaml
guide_reachability:
  no_role_facing_surface: false
  routes:
    - guide_surface: guide workflow task implementation-loop
      role: implementation
      target_surface: <role-facing-surface>
```

```yaml
guide_reachability:
  no_role_facing_surface: true
  routes: []
```

On a fresh host, `intent init --write` appends these repository defaults while
preserving existing file content:

```gitattributes
.intent-cli/runs.jsonl merge=union
.intent-cli/**/*.jsonl merge=union
```

```gitignore
.intent-cli/supervision/**/cycles.jsonl
.intent-cli/supervision/**/stalls.jsonl
```

The first pair gives append-only JSONL stores union-merge behaviour; the second
keeps per-team supervision telemetry out of git. For an already initialised
host, `intent init` prints these exact lines as guidance and changes neither
`.gitattributes` nor `.gitignore`; migration is always an explicit operator act.

## Canonical notify workflow

All role-to-role workflow messages use `intent-cli notify`; agents never choose
or invoke agmsg/herdr delivery themselves. The CLI resolves the team's recorded
session-layer mode internally (`agmsg` when unrecorded), validates the logical
roles before delivery, and keeps the command shape unchanged when a team
switches transport.

```bash
# Delegate one bounded task. Repeat --input and --expected-artifact as needed.
intent-cli notify delegate --domain <domain> --team <team> --from <sender-role> \
  --to <receiver-role> --report-to <orchestrator-role> --task-id <task-id> \
  --objective <one-bounded-outcome> --input <canonical-reference> \
  --expected-artifact <inspectable-artifact> --result-nonce <fresh-nonce> \
  --write --format json

# The receiver's final step (the delegate payload supplies this command).
intent-cli notify report --domain <domain> --team <team> --from <receiver-role> \
  --to <orchestrator-role> --task-id <task-id> \
  --status completed|blocked|question --artifact <artifact> \
  --summary <one-line-summary> --write --format json

# Route a design decision to the existing events.jsonl boundary.
intent-cli notify escalate --domain <domain> --team <team> --from <sender-role> \
  --task-id <task-id> --artifact <decision-input> \
  --summary <one-line-summary> --write --format json
```

```bash
# Inspect one dispatched task without supervision or re-dispatch.
intent-cli notify status --task-id <task-id> [--domain <domain> --team <team>] \
  [--routing-root <host-root>] --format json

# Explicitly settle an open delegation when its outcome is known elsewhere.
intent-cli notify dispose --domain <domain> --team <team> \
  --task-id <task-id> --kind superseded|applied-elsewhere --actor <actor> \
  --reason <reason> [--superseding-task-id <task-id>] \
  [--applied-outcome-evidence <evidence>] --write --format json
```

Delivery is judged once from the recipient's recorded residency, and the same
judgment is consumed by `notify status`, `notify escalate`, and
`notify supervise`:

| Recorded residency | Delivery contract | Output basis |
| --- | --- | --- |
| `external` with a recorded reader | A durable append to that reader is delivery; no pane wake applies. | `recorded-reader-append` |
| `herdr` with a recorded pane | Delivery requires the recorded pane wake; appending an event alone is not delivery. | `recorded-pane-wake` |

Therefore a successful external-reader escalation reports `delivered: true`
and supervision does not open `undelivered-escalation`. On the next cycle it
clears any matching open false-positive record exactly once without rewriting
the 6-field event history. A failed reader append remains `delivered: false`
and is retained as a genuine append-failure finding outside the reader it could
not write, so that finding is not cleared as a migrated false positive. Pane
delivery and the G641/G657 wake and escalation ladder are unchanged.

> **Preview through 1.x (G660).** The shared residency-resolved judgment,
> `delivery_basis` output, append-failure finding, and false-positive
> reconciliation are post-freeze preview behavior outside the 1.0
> compatibility promise.

`notify delegate --write` first appends a durable pending snapshot at
`<routing-root>/.intent-cli/notify/<domain>/<team>/pending.jsonl`. The snapshot
contains the task id, recorded recipient identity, expected artifact, and
dispatch timestamp. If that write fails, no pane prompt or external reader
event is attempted. A matching `notify report` appends the resolution. An
unmatched task id still delivers the report and emits an advisory in both
human-readable and machine output naming the supplied id and stating that no
open pending delegation matched; it creates or resolves no pending record. An
already-settled task id, or a lookup whose identifiers conflict across team
stores, remains refused and names the supplied id plus the known open ids.
`notify status` reads the same recorded identity and
liveness judgment as delegate: herdr uses the exact `agent_running` flag at
the recorded workspace/pane (never the status string), while agmsg uses its
recorded roster. It reports `live` for a running recipient with no report,
`settled` after the matching report regardless of current liveness, and
`lost` only when the recipient is not running, no report arrived, and the
recorded pane has no corroborating foreground process. When herdr has lost the
registration but a process remains, status reports the distinct
`registration-lost-process-present` state and directs an operator to repair the
registration; it does not infer process loss.

An open delegation may be settled explicitly with `notify dispose --write` when
the outcome is known without a report. The command requires an actor,
timestamp, reason, and one of the named kinds: `superseded` requires a
superseding task id, while `applied-elsewhere` requires evidence of the applied
outcome. `notify status` reports this as `settled` with
`settlement_basis: disposition` and exposes the disposition separately from a
report-settled record. Disposed records are not counted as open by
`notify supervise` or `stalled-work`. There is no automatic or elapsed-time
disposition; unknown and already-settled task ids are refused and named.

A late `notify report` for a disposed task is still delivered under the normal
message-carriage rule. It does not erase the disposition or reopen the record;
status and report advisories name the late-report disagreement so that an
operator can reconcile the two outcomes without silently dropping the message.

**Activity evidence (G652 — preview-through-1.x).** A running process is not
evidence of work. For herdr, status also names `agent_status`,
`state_change_seq`, and the last state-change time: `working` requires a
working agent with advancing activity. Before a sequence baseline exists,
status reports `activity-unknown` rather than asserting `live-idle`; a
state-change time after dispatch is sufficient cold-start evidence for
`working`. Supervision records the first baseline without a live-idle finding,
then surfaces an unchanged live-idle recipient with no report once and names
terminal inspection as the remedy; it never reads terminal content or enters
recovery for that finding. A declared bound below the configured interval is a
structural false-alarm warning, emitted at supervise start and on each cycle,
not a value the CLI silently corrects.

**Report outbox (G653 — preview-through-1.x).** `notify report --write`
persists the sender-side outbox entry before it attempts transport. The entry
keeps the task id, result nonce, status, artifact, summary, and delivery
timestamps, so a delivery failure is retained as `undelivered` rather than
discarding completed work. Supervision only surfaces that entry and its
`notify collect` remedy; it never sends it automatically. The recipient-side
terminal `ORCH_RESULT` remains a record for the human, but intent-cli never
parses a terminal. When a visible result has no arrived report, collect the
persisted outbox entry — do not re-delegate or redo the task. Collection sends
only the original report once for its task id and refuses an already-delivered
entry. Entries are scoped to the dispatch generation (the result nonce), so a
re-delegated task id can carry a new report and unmatched reports continue as
messages. A second report for an undelivered current generation fails closed
and names its exact `notify collect` recovery command.

Before opening a new delegation, `notify delegate --write` refuses a task with
an undelivered report entry and names that same collection command, so the
supervision finding and a collectable entry are the same set. It also refuses a
previously settled task-id/result-nonce pair before work starts and requires a
fresh `--result-nonce` or a new task id; an open generation with no outbox can
be resent idempotently.

A report is a message rather than a bookkeeping entry: fail-closed protection
belongs on pending-state mutation, not on carrying the message. Refusing an
unrecognised identifier would silence unsolicited reports and answers to
escalations—the messages whose information the recipient did not know to
request.

> **Preview through 1.x (G629/G671).** Pending-delegation records, explicit
> dispositions, and `notify status` were added after the v0.12.0 freeze. They
> are outside the 1.0 compatibility promise, may change or be withdrawn during
> 1.x, and are formalised only by a later MAJOR release. See the
> [compatibility ledger](1.0-compatibility-ledger.md) preview rows.

### Role-scoped closeout evidence (G698)

Orchestration records mechanical evidence with `--role orchestration`; design
records the intent-tree lesson and guide update with `--role design`:

```text
intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role design --write
intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role orchestration --write
intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --role design --write
intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --role orchestration --write
```

Use `automation stalled-work --role design|orchestration` to verify one role's
debt. Different roles coexist under `records/<role>.json`; legacy `record.json`
remains readable and unattributed, with no automatic migration.

### Guide reachability (G645 — preview-through-1.x)

The keyword-to-guide standard is part of the workflow: handing a thread a
keyword must be enough for that thread to reach the named guide, understand
the surface, and act. Each packet declares guide_surface, role, and
target_surface for every role-facing addition, or explicitly declares
no_role_facing_surface. A missing declaration is not a no-surface decision;
the process never infers a route and never judges guide wording.

At closeout, design records the host update with
intent-cli automation guide-reachability-record --execution-unit <unit>
--commit <host-sha> --write. Until then, automation stalled-work reports
guide-reachability-pending with the execution unit, guide, and role. The debt
does not block merge or closeout, and an explicit no-surface declaration is
silent. This is a preview surface outside the 1.0 promise; see the
compatibility ledger.

### Scoped adjudication authority, live CAS, and recipe drift (G666/G682/G683/G689/G690 — preview-through-1.x)

The unattended approval model has exactly three layers. First, eliminate each
known dialog through agent-side allow configuration recorded in the agent
kind's G636 recipe fields. Second, G683 supervision reads the bottom detection
snapshot of a dialog-blocked pane and emits the agent kind, pane, observed text,
and a stable class only when the ordered literal fragments in that kind's recipe
entry form the current trailing dialog. A stale known dialog followed by newer
unclassified text is class `unknown`: it is never fuzzy-classified and remains
escalate-only.
Third, only a validated recorded pre-approve match wakes the canonical
adjudication surface with the rule, observed dialog, and exact recipe answer
scope. Orchestration durably audits authorization and an execution-pending
transition before executing only that recorded key sequence, then records the
terminal outcome. An unresolved pending transition suppresses answer retry and
becomes reconciliation-required.

G690 replaces the absolute "design never answers" shortcut with a declared
authority boundary. A prompt class and each matched shell scope carry
`answerable_by`; the effective capability is their intersection. A design actor
may use only `intent-cli notify adjudicate` when the exact class is declared
design-answerable, the scope matches, no hard risk-floor tag is present, and
the recorded pane, state-change sequence, and observed-text SHA-256 still
match the live dialog. The command re-reads that CAS immediately before the
bounded `herdr agent send-keys`; any pane, sequence, or text mutation refuses
the answer and records `stale-dialog-cas-refused`. Decision actor and
mechanical executor are separate audit fields. Direct relay, direct
`send-keys`, fuzzy classification, and unscoped forwarding are never a design
authority path. No shipped class or scope is design-answerable in this slice;
the capability boundary is implemented for a future explicitly declared class.
The hard floor is always escalation for `destructive`, `credential`,
`permission-change`, `security`, `product-decision`, or `unverifiable` prompts.

Record the policy on the standing supervisor with repeatable
`--pre-approve <agent-kind>:<prompt-class>` and
`--pre-escalate <agent-kind>:<prompt-class>` flags plus `--write`. The two lists
must be declared together. Every pair is validated against the reviewable
recipe vocabulary; an unknown pair is refused and the error names all known
`kind:class` values. The same pair cannot appear in both lists; recording is
refused, and escalation has fail-closed precedence for a legacy conflicting
record. Producer-covered kinds clear G682's inapplicability
automatically, while uncovered kinds retain
`inapplicable-no-prompt-class-producer`. Matched pre-escalate, unmatched, and
`unknown` observations are all audited as escalate-only and execute no answer.
The initial vocabulary includes `codex:github-comment-post`, the separately
recorded codex launch-hook trust dialog, and
`copilot:launch-limited-permissions` from the G636 launch recipe.

G689 extends that vocabulary with the measured `codex:shell-command` class.
The class producer recognizes the dialog and extracts a command payload; it
does not make the payload answerable. Record shell policy instances through
`--shell-policy <json>` and inspect the shipped inventory with
`intent-cli prompt-class list` or
`intent-cli prompt-class describe codex:shell-command`. A shell answer is
valid only when every compound shell-AST segment is covered by scoped policy
and the audit names the matched scopes. `project-test` binds the recorded
`dotnet test` argv prefix to the recorded cwd/root/path constraints and is
test execution, not read-only. `owned-scratch-delete` requires exact paths
from the same wake's scratch ledger and never authorizes bare `/tmp`.
`exact-command-once` binds one normalized AST digest to the current dialog
hash and is consumed after one bounded answer. Unknown syntax, command
substitution, redirects, and uncovered or out-of-root segments escalate.
Persistent allowances remain operator-only. G689's shipped shell scopes,
including `owned-scratch-delete`, remain orchestration-only and retain the
same-wake scratch-ledger identity requirement; G690 does not weaken that
contract. Any future design-answerable class still has to use the canonical
adjudication surface and live CAS.

Each supervision cycle also compares the structured argv of every running
recorded seat with its kind's recorded recipe. The comparison covers exactly
the security envelope: sandbox mode, approval mode, writable roots/add-dirs,
and network access. Missing bounds, extra roots, and a broader envelope are
`recipe-envelope-alarming`; a narrower envelope is
`recipe-envelope-narrower` and informational. Model and reasoning effort are
operator-choice wish fields excluded by design, so a wish-only difference is
silent. Argument order and whitespace are equivalent. A mismatch emits one
`recipe-drift` finding per seat per cycle and names both the observed and
recorded shapes; a conforming seat is silent. The finding wakes nobody, and
the watcher never restarts or corrects a seat. Its only input operation is the
registry's exact bounded `agent send-keys` sequence after a matching policy,
durable pre-execution audit, and orchestration-only wake; there is no generic
keystroke relay or design answer path. Prompt audits share the existing
`cycles.jsonl` stream and record seat, pane, class, rule, actor, timestamp,
exact scope, and outcome.

G686 adds a second, operator-owned truth layer without changing that G684
algorithm. Use the dedicated, confirmation- and digest-guarded command
`intent-cli session-layer topology record-profile` to record a named typed
envelope profile in the team topology: kind, sandbox mode, approval mode,
roots policy and concrete writable roots, network access, transport mode,
evidence, and `recorded_at` (with an optional digest, Copilot permission
options, and network URLs). This command is intentionally separate from
`update-kind` and `update-field`; generic topology JSON editing is not a
profile-recording path. A role may reference a named profile or carry a typed
override. The role's profile is the comparator baseline; when no profile is
recorded, G684 registry behavior is byte-identical. A missing profile or a
kind-mismatched reference/override is a distinct `profile-invalid` finding and
never silently falls back to the registry. Profiles are recorded facts only:
supervision never learns them from observed argv, never launches or repairs a
seat, and the existing G684 cadence, wish-field exclusion, and no-action
boundary remain unchanged. The profile command and finding are
preview-through-1.x under G628.

This preserves **four judgment-bearing threads plus one supervision process**;
there is no fifth approval seat. On 2026-08-11 in workspace wK, Claude app
safety correctly blocked a design-thread keystroke relay, while advice to use a
nonexistent `/approvals` surface could not recover the seat. Separately,
operator-filed #1469 measured a 0.19.0 cycle with 47 keys and no
prompt/dialog/class/adjudication producer key, a review seat wedged three times
in one day, and orchestration correctly refusing to fabricate a class. It
shares #1465's configured-looking-but-inert failure shape. The interim
in-contract remedy is the recipe-layer agent-side allow configuration above,
recorded in G636 fields, not an answer path. Render the same
contract through both `intent-cli guide orchestrator-thread` and
`intent-cli guide design-thread`, in Markdown or JSON, with or without a team.

### Bounded recipient supervision (G630)

```bash
intent-cli notify supervise --domain <domain> --team <team> \
  [--interval <seconds>] [--auto-redispatch] [--once] [--dry-run|--write] \
  [--routing-root <host-root>] --format json
```

`notify supervise` is a preview wake loop over open pending delegations. A
`live` recipient and a settled record are intentionally silent. A `lost`
recipient is handled only in this order: confirm that the recorded pane is
really gone rather than mid-exit; attribute every foreground process to the
recorded role cwd and verify it is gone; start the recorded unattended-launch
recipe; send the registering prompt; then prove readiness with an exact nonce
on a response line. A nonce found anywhere in the pane, including an unsent
composer echo, is not readiness.

Only after that gate passes does the supervisor send one loss notification to
the delegating role. It names the task id, says that the recipient was
recovered, and says that the in-flight task was lost and must be re-dispatched.
`--auto-redispatch` is off by default; when enabled it invokes the normal
`notify delegate` path only after readiness and includes the outcome in that
notification. Unknown recipes, ambiguous process ownership, an old process
that remains, or an unproven response line stop that recipient fail-closed and
never claim readiness. Use `--once` for a bounded diagnostic pass; `--dry-run`
never kills, starts, prompts, re-dispatches, or appends.

> **Preview through 1.x (G630).** Supervision, recovery, response-line
> readiness evidence, and optional re-dispatch were added after the v0.12.0
> freeze. They are outside the 1.0 compatibility promise, may change or be
> withdrawn during 1.x, and are formalised only by a later MAJOR release. See
> the [compatibility ledger](1.0-compatibility-ledger.md) preview rows.

### Measured recovery supervision (G641 — preview-through-1.x)

```bash
intent-cli notify supervise --domain <domain> --team <team> \
  --repo <owner/repo> --owner-role <logical-role> --bound <seconds> \
  [--interval <seconds>] [--once] [--dry-run|--write] \
  [--routing-root <host-root>] --format json
```

The same bounded invocation consumes the existing recipient-lost, terminal CI
wait, queue/write-back, and recorded-seat inventories. A healthy team has no
findings; a detected condition wakes its owning logical role through the
transport recorded for that team. The supervisor never invents a transport,
assumes an agent kind, or applies an owed transition. `--owner-role` names the
role that owns non-recipient findings, while G630 recipient recovery keeps its
recorded delegating role as owner.

`--bound` records the team's maximum detection interval under
`.intent-cli/supervision/<domain>/<team>/bound.json`. Each cycle is durable in
`cycles.jsonl`, including the measured gap from the previous cycle and whether
the declared bound was met. A restart gap beyond the bound is reported as
`supervisor-not-running` and as `absent_since_last_cycle: true`; a missing
previous cycle is reported as unknown rather than guessed healthy.

If `--bound` is omitted, no declared bound is inferred or recorded. The
cycle's persisted `interval_seconds` is the measured loop cadence, while the
fallback self-absence threshold is `max(2 * cadence, cadence + 60s)` so normal
cycle work and scheduler jitter have headroom. `bound_met` remains null and the
liveness summary states that no detection bound was declared. `started_at` is
the cycle-entry timestamp, `completed_at` is stamped at genuine completion,
and `gap_seconds` measures the previous completion to the current start rather
than treating normal cycle work as downtime.

Every finding has an append-only recovery record in `stalls.jsonl` with
`detectable_at`, `surfaced_at`, and `cleared_at`. If a condition is first seen
after supervision restarted, `detectable_at` is null and
`detectable_at_unknown` is true. Clearing a known record produces a measured
duration; an unknown start never receives a flattering duration. Pane-resident
or failed-append `notify escalate` deliveries are treated as findings, and the loop reports its own
liveness in every bounded `--once` result. Dry-run resolves and previews the
same classes and bound without waking, recording, or clearing anything.
After a wake is delivered for an undelivered escalation, its recovery record is
acknowledged and cleared; the append-only event remains durable, but later
cycles suppress that acknowledged key and return to healthy silence.

> **Preview through 1.x (G641).** Measured supervision, its bound and durable
> recovery records, the undelivered-escalation finding, and self-liveness are
> post-freeze preview behavior outside the 1.0 compatibility promise. They may
> change or be withdrawn during 1.x and are formalised only by a later MAJOR
> release. See the [compatibility ledger](1.0-compatibility-ledger.md).

### Persistent supervisor setup (G658 — preview-through-1.x)

Set up the standing loop through the emit-only installer rather than an ad-hoc
background shell:

```bash
intent-cli notify supervise install --domain <domain> --team <team> \
  --repo <owner/repo> --owner-role <logical-role> --bound <seconds> \
  --interval <seconds> [--startup-bound <seconds>] [--platform macos|windows|linux] \
  [--output <path>] [--routing-root <host-root>] --write --format json
```

Without `--platform`, the command emits the current platform's scheduler
definition: a launchd plist on macOS, a `schtasks`-compatible Task Scheduler
XML file on Windows, or a systemd user unit on Linux. `--platform` is an
explicit cross-authoring override. Every artifact is named and labelled
`intent-cli.supervise.<domain>.<team>` and embeds the complete `notify
supervise` invocation, including domain, team, repo, owner role, bound, and
interval. Output names the written path, lifetime, runtime logs, legacy
artifacts removed, and exact registration/unregistration/reconcile commands.
G712 deliberately chooses the permitted GUI-session lifetime: the artifact is
kept under the routing repository's
`.intent-cli/supervision/<domain>/<team>/install/`, never under
`~/Library/LaunchAgents`; the macOS plist omits `RunAtLoad`, so it is not
login-auto-loaded and the GUI domain disappears at logout/reboot. If the
operator wants it active in the current GUI session, the printed command uses
`launchctl bootstrap gui/$(id -u) '<artifact-path>'`. Use
`intent-cli notify supervise reconcile --write` (or `uninstall --write`) to
list loaded jobs before and after, boot out every managed
`intent-cli.supervise.*` job, remove managed artifacts and legacy
login-persistent plists, and name the removed paths. Install authors and
first-cycle-probes only; reconcile/uninstall is the explicit lifecycle
cleanup boundary. Windows and Linux artifacts emitted from the measured macOS
path are explicitly `emitted-but-unverified` and contain no logon/default-
target auto-start trigger.

Install exactly one artifact per team outside the agent seats. The loop,
per-cycle measurement, detection bound, one-wake-per-finding behavior, and
escalation ownership are the existing G630/G641/G657 semantics; installation
does not grant the CLI a new lifecycle or recovery authority.

The canonical per-team liveness check is the age of that team's
`.intent-cli/supervision/<domain>/<team>/cycles.jsonl` record compared with
its declared bound. Process-name grep is an anti-pattern because it cannot
prove team identity. On 2026-08-08, a measured process-name grep conflated
teams, killed the design team's own supervisor, and retained another team's
process. The absence remained unnoticed for about 47 hours until the durable
record showed `absent_since_last_cycle=true`, `gap_seconds=169796`, and a
failed bound. Use record identity and age, never a shared process name, for
supervisor health.

> **Preview through 1.x (G658).** Scheduler artifact emission and its output
> schema are post-freeze preview behavior outside the 1.0 compatibility
> promise. Emission is not registration or runtime verification, and this
> surface makes no release decision.

### Install-time artifact hardening (G704 — preview-through-1.x)

`--bound` must be greater than or equal to `--interval`. A smaller value is
rejected as `bound-below-interval` because a healthy supervisor would be
structurally judged absent (`supervisor-not-running`). The running supervisor
retains its warning for legacy records; the warning is defense in depth, not a
correction of the invalid configuration.

On macOS the generated launchd plist sets `WorkingDirectory` to the routing
root and routes `StandardOutPath` and `StandardErrorPath` below
`.intent-cli/supervision/<domain>/<team>/runtime/`. The install result names the
runtime directory and both log paths. A write reports success only after the
managed process appends a writer-bearing first cycle within the declared
`--startup-bound` (default 30 seconds). Otherwise it fails as
`first-cycle-proof-failed` and names both log paths. The artifact remains for
operator inspection. Install does not execute registration; the explicit
`notify supervise reconcile --write` or `uninstall --write` command is the
operator-approved unload/removal path.

The first-cycle writer is recorded in
`.intent-cli/supervision/<domain>/<team>/installed-supervisor.json`. A later
G676 writer identity that differs from the installed identity is emitted as
`duplicate-supervisor` through G699's recorded repeat-backoff and parked state.
The finding uses cycle writer identity only: it reads no terminal content and
never kills, stops, elects, or ranks a supervisor.
### Event-driven supervision (G659 — preview-through-1.x)

Add `--event-mode` to the one standing `notify supervise` invocation to opt in.
The same supervisor process keeps one blocking `herdr agent wait` per recorded
seat and wakes the owner role within seconds when an implementation or review
seat changes from `working` to `done`, `blocked`, or `idle`. It records the
transition, source, `state_change_seq`, observed latency, and wake result in
`cycles.jsonl`. A wait death or error is recorded as `event-wait` with
`rearm_attempted: true`, then re-armed. No terminal output is parsed.

Event waits and interval cycles are independent observation sources inside one
process. The interval loop remains the safety floor when a wait dies. Both
sources use the same workspace/pane/sequence transition key, so their race
produces one transition and one wake (`1 transition / 1 wake`). Existing wake targets, stall classes,
owner-to-design escalation, recovery authority, and exactly-one-supervisor-per-
team rule are unchanged; event mode does not create a second standing loop.
The concrete wake-source flag is `--event-mode`, and the corresponding herdr
observation is `pane.agent_status_changed` (the normative SECOND wake source).
Measured evidence is `herdr 0.8.0` on macOS. Other herdr versions and platforms
are unverified.

G659 supersedes hand-written transition watchers only when an operator adopts
event mode; intent-cli never finds, kills, or forcibly replaces them. G658
scheduler artifacts embed their invocation, so an installed interval-only
artifact stays interval-only. Adoption requires running `notify supervise
install ... --event-mode` again, inspecting the new artifact, and explicitly
unregistering/re-registering it with the printed operator commands.

> **Preview through 1.x (G659).** Event waits, transition/wait records, and
> event/interval de-duplication are post-freeze preview behavior. They make no
> release decision and may change or be withdrawn during 1.x.

### Scheduler self-sufficiency and truthful transport degradation (G675 — preview-through-1.x)

`notify supervise install` emits a scheduler artifact that resolves the
`intent-cli` executable absolutely. Every runtime transport binary used by the
loop is also emitted as an absolute executable when resolvable; an unresolved
binary is named in the emission result and the artifact carries the recorded
PATH that covers any remaining command name. Install remains authoring and
first-cycle proof only; it does not execute registration. The explicit
`notify supervise reconcile|uninstall --write` surface is the named current-
GUI-session lifecycle boundary: it boots out managed jobs and removes their
artifacts, including legacy login-persistent plists, without replacing or
managing unrelated jobs.

The operator verifies both the live PID and the first cycle record in
`cycles.jsonl`; a loaded PID is not proof that a loop is alive. The guidance
names the loaded-but-silently-exiting / exit-127 shape explicitly. G675's
measured evidence is attributed to host macOS node 08 on 2026-08-12 in two
separate acts: act one observed `/usr/bin/env intent-cli` under launchd's
minimal PATH loaded but silently exiting with exit 127, and a same-machine
audit found four accumulated supervisors; act two observed missing `herdr`
produce ten false recipient losses in one cycle while the recipient was alive
and mid-task. These are scheduler/transport facts, not recipient absence.

When a transport process cannot be started, the failure is classified before
any recipient liveness judgment as one cycle-level `supervision-degraded`
finding with cause `transport-unavailable`, the binary, and the start error.
It never becomes `recipient-lost` and is never repeated once per open
delegation. A healthy recipient remains silent, and G648's genuine
absence/foreground-process corroboration rules remain byte/semantically
unchanged.

The concrete optional second wake-source flag is `--event-mode`; it holds the
blocking herdr wait for `pane.agent_status_changed` while the interval cycle
remains the safety floor. G675 does not add duplicate-supervisor detection,
recovery sequences, new verdict names, wake transports, or emission targets.

> **Preview through 1.x (G675).** Scheduler executable resolution, recorded
> PATH diagnostics, cycle-level transport degradation, and the rendered
> verification guidance are post-freeze preview behavior outside the 1.0
> compatibility promise. They may change or be withdrawn during 1.x.

### Duplicate supervisor detection (G676 — preview-through-1.x)

New supervision cycles add a nullable `writer` object to `cycles.jsonl` with
`pid`, `process_start_time`, and `host`. The reader keeps accepting legacy
cycle records without that object; a legacy record is not evidence of a
duplicate. At the start of a cycle, the loop compares the latest recent cycle
with its own writer identity. A different writer is a duplicate only when its
process is live and its cycle age is within the same declared-bound or
cadence-based recent threshold used for supervision liveness. The loop emits
exactly one `duplicate-supervisor` finding per cycle, naming the current and
other writers, the other cycle's age, the duplicate-wake cost for the same
stall, and the G658 per-team scheduler label
`intent-cli.supervise.<domain>.<team>` as the remedy.

Dead writers, stale cycles, the same writer, and legacy cycles produce no
duplicate finding. This is detection only: intent-cli does not kill, stop,
rank, elect, lock, or lease a supervisor, and duplicate seat processes remain
outside this slice. Before registering a scheduler artifact, the operator
checks for and stops stale hand-run supervisors that survived a session
restart; intent-cli does not perform that stop. The measured G676 incident on
this machine on 2026-08-12 found four concurrent loops for one team and
duplicate wakes for the same stalls. That incident attribution explains why
the per-team G658 artifact is the remedy; it does not grant supervision a new
recovery authority.

> **Preview through 1.x (G676).** Additive writer identity, duplicate-supervisor
> detection, and the stale hand-run cleanup guidance are post-freeze preview
> behavior outside the 1.0 compatibility promise. They may change or be
> withdrawn during 1.x and are formalised only by a later MAJOR release.

### Escalation ladder and CI fallback (G657 — preview-through-1.x)

The complete ladder is: seats do their assigned work; orchestration notices
ordinary stalls and applies only its existing authorised recovery; measured
supervision watches every working seat in its remit, including orchestration.
Design is the operator's rung and is not watched by supervision. A finding
whose `subject_role` is the configured owner role is the one narrow fallback:
supervision sends one `wake_class: escalation` wake to `design` through
design's recorded pane or event-reader transport. Every other subject wakes
the ordinary `owner_role`. A cycle never broadcasts to both rungs.
The durable finding records `subject_role`, `wake_target_role`, and
`wake_class` as provenance.

Design rules on the escalation but gains no recovery authority. It remains the
last resort for supervision-liveness concerns; supervision does not start
watching or recovering design. Health evidence, the G630 recovery sequence,
delivery, settlement, and lifecycle-transition ownership are unchanged.

Terminal CI is similarly action-shaped. A failed exact-head `ci-wait` is
actionable only while that head is still current and no repair is claimed.
Repair/update labels or a new head are evidence of normal repair, so
supervision stays silent. Green checks may also use declared state as a
fallback: an issue labelled `intent-pr-created`, with an open non-draft PR
whose checks are all green and which lacks review-routing labels, produces
`ci-all-green-not-transitioned` even without a `ci-wait` record. Its
`ci_classification_source` is `declared-label-fallback`; a durable wait remains
the richer `ci-wait-record` path.

> **Preview through 1.x (G657).** Subject-based owner escalation and the
> settled-CI classification fallback are post-freeze preview behavior outside
> the 1.0 compatibility promise. They make no release decision.

### Registration-lost but process-present (G648 — preview-through-1.x)

Herdr registration is not process liveness. Before `notify status`, delivery,
or supervision calls a recipient `lost`, the CLI checks the foreground process
at the exact recorded workspace/pane. No registration plus no process remains
the genuine `lost` case and keeps the G630 fail-closed recovery gate unchanged.
No registration plus a process is named
`registration-lost-process-present`: liveness reports the recipient as likely
alive, supervision emits at most one finding per recorded workspace/pane per
cycle outside the G630 recovery path, and delivery returns the same cause with
`resend_permitted: true`. The guidance is to re-register the agent at the
recorded pane; no kill, restart, or automatic re-registration is attempted.
Absence-like prompt text such as `pane-absent` or `agent_not_found` is never
allowed to override this process corroboration. This is a preview surface
through 1.x and is outside the 1.0 compatibility promise.

`notify delegate` embeds the task id, expected artifacts, fresh marker nonce,
and complete canonical report command (including the transport-neutral
`--routing-root` needed from an isolated child checkout) in the delivered task. Running that
report command is the receiver's required final step after all other work, so a
herdr-only completion actively wakes the orchestration role instead of merely
printing into the receiver pane. In herdr-only mode the source of truth is
`<routing-root>/.intent-cli/topology/<domain>/<team>.json`: every sender, recipient,
and delegate `report-to` role must exist in that team's recorded roster, but
**only the recipient must be deliverable**. A `resident: external` role therefore
may send and may be `report-to` without a pane. When that external role is the
recipient, `delegate` and `report` deliver by appending exactly one unchanged
six-field event to its safe routing-root-relative recorded `reader` (`delegate`
uses `question`; `report` maps status `completed|blocked|question` to event kind
`completion|blocked|question`), and return `eventAppended:
true`; a herdr-resident recipient is prompted at its explicit
recorded pane in the team's recorded workspace. Agents found only in another
workspace are never eligible.

`--dry-run` performs the same topology, team-workspace, recipient-state, and
reader resolution as `--write`, returning the same refusal verdict and cause
without prompting or appending. Unknown-role failures name the source actually
consulted, the team/workspace scope, the roles found there, and a corrective
action. All resolution remains fail-closed: there is no fallback to a foreign
workspace or another transport. `notify escalate` continues to append the same
six-field event schema; none of these commands merges, labels, publishes, or
mutates queue state. Direct transport commands remain provisioning/readiness
diagnostics, not workflow send instructions.

## Starting orchestrator mode (design-thread setup)

A design thread that wants to run orchestration can ask intent-cli directly —
`intent-cli guide workflow suggest --goal "I want to start agmsg orchestrator
mode"` (and natural-language variants like `orchestrator を使いたい` /
`新しい intent-cli オーケストレーションを使ってみたい`) routes to the orchestrator
setup guidance.

`guide orchestrator-thread` renders a **setup intake** first, whose visible
outcome is one of `missing-inputs`, `setup-ready`, or `blocked`:

- **missing-inputs** — supply only the missing fields among domain, target repo,
  orchestrator/implementation/review folder, orchestrator/implementer/reviewer
  agent, agmsg team name, delivery mode, and existing-loop stop policy.
- **setup-ready** — the intake emits copy-paste agmsg `join.sh` / `delivery.sh`
  commands and first prompts for all three roles, plus the first validation
  (existing-loop conflict check, read-only first wake, ping/inbox test).
- **blocked** — an existing implementation/review timer loop for this domain/repo
  would race the orchestrator; stop it (or pass `--existing-loop-policy
  will-stop`) before starting. Receivers are never scheduled; when an explicit
  fallback/legacy timer is used (message-driven wakes are the default), the
  orchestrator is the only thread ever scheduled.

```bash
intent-cli guide orchestrator-thread --domain <d> --target-repo <owner/repo> \
  --orchestrator-path <o> --implementation-path <i> --review-path <r> \
  --orchestrator-agent <a> --implementer-agent <a> --reviewer-agent <a> \
  --team <team> --delivery-mode <mode> --existing-loop-policy none --format markdown
```

The full reference checklist follows the intake:

1. **Decide / record** — domain and target repo; host / orchestrator /
   implementation / review paths (each role runs from its own folder, clone, or
   worktree); base branch policy; per-role agents; agmsg team name; delivery
   mode. In herdr-only, ask the human which CLI and model each seat
   (`design`, `orchestrator`, `implementation`, and `review`) should run, then
   record each answer as that seat's `kind`; do not silently choose a default.
2. **Register roles** — register orchestrator, implementation, and review under
   one agmsg team (`join.sh`).
3. **Set delivery** — give each role a way to receive messages, e.g. a streamed
   inbox watch (`delivery.sh` / `watch.sh`).
4. **Paste role prompts** — copy the orchestrator / implementation / review
   prompts from `guide orchestrator-thread` into the matching threads.
5. **First read-only wake** — run one confirm-only orchestrator wake; send
   nothing.
6. **Ping test** — send one agmsg message and confirm it lands in the target
   role's inbox before any real delegation.
7. **Message-driven steady state by default** — implementation/review replies
   over agmsg wake the orchestrator, so routine fast polling is not required;
   receivers stay loopless. Only schedule an orchestrator timer (Codex
   automation 5m or Claude `/loop 5m`) as an explicit fallback/legacy option
   (see [Design-thread watchdog](#design-thread-watchdog-recommended-safety-net)
   for the RECOMMENDED default safety net instead).
8. **Cleanup** — on teardown, leave/despawn the roles through the agmsg scripts
   (`leave.sh` / `despawn.sh`) and stop any inbox watchers.

> **Warning:** never edit the agmsg database or team files directly — provision,
> diagnose, and clean up only through the agmsg scripts; send workflow
> notifications through `intent-cli notify`, which invokes the adapter. Hand-editing
> agmsg state corrupts delivery.

## Terminal-workspace provisioning (building the team)

The setup checklist above assumes each role **already** has its own folder and
its own live terminal session. When it does not — a design thread asked to "set
this team up" from nothing — `guide orchestrator-thread` renders a
**terminal-workspace provisioning** section that creates both, executable with
placeholders only (`<Project>`, host metadata repo `<owner/host-repo>`, target
repo `<owner/repo>`, agmsg team `<team>`, `<workspace-root>`). Generate it with
the same command and work down its checklist; the summary below is orientation,
not a substitute.

**1. Role folders — create them when absent.** Host-side roles (orchestrator,
review) run from clones of the **host metadata repo**; the implementation role
runs from a clone of the **target repo** (implementation is GitHub-contract-only
and never reads host `.intent-cli/` state). Two roles must **never** share a
folder: agmsg identity and the codex monitor bridge are `(project, type)`-scoped
(G521), so two same-type roles in one folder resolve to the same identity and one
of them silently stops receiving. A pane opened at a missing cwd falls back to
the shell's default directory, which produces exactly that collision — so create
the folder first, then verify each is distinct, has the expected `origin`, and is
clean.

**2. Workspace topology.** One workspace per team; one tab named after the team;
one pane per role, each opened with that role's folder as its cwd (set at pane
creation — do not `cd` after launching the agent). The **design thread stays
outside** the workspace it is constructing.

**3. Launch rules.** Launch every agent by **typing into the pane's interactive
shell** (send-text + enter). For codex this is mandatory: the `codex()` shell
shim is what arms the agmsg monitor bridge (G521), and a workspace manager that
exec's the canonical executable directly bypasses it — the session looks healthy
and messages are simply never delivered. claude is launched with the permission
mode the **operator** chose. **Attend** each pane's first run: trust screens and
permission prompts block the session until answered, and where the design thread
is authorized to answer, the answer must produce a **durable** allowlist rather
than a per-invocation approval that re-prompts on the next wake.

> **Authority boundary — unsticking is not deciding.** Attending a pane does not
> make the design thread the decider. It may act **only on pane contents it has
> actually read** — never a blind keystroke into a dialog it has not rendered.
> Operator authorization can extend **only to read-pane trust/allowlist cases**,
> such as the design thread's own hook-trust case. Credential, security, and
> permission prompts are **never** answerable by the design thread: they
> **always** remain unanswered and are **always** escalated to the operator,
> with or without prior authorization — no authorization makes them answerable.
> If answering would grant access, widen a permission mode, or accept a security
> warning, it is the operator's call.

### 3a. Unattended-launch recipes (agent-neutral) (G617)

An unattended-launch recipe is agent-neutral: it states the launch invocation,
the bounded allowed roots derived from that role's actual work, the autonomous
continuation bound, the startup gates the operator must answer, the post-start
interaction (what the agent presents, the answer that preserves the declared
envelope, and whether the default answer is safe), and the denial semantics. A
recipe that stops at the command line is incomplete: an agent can negotiate
authority after launch even when the invocation is correct. The measured
registry currently contains Copilot and Codex entries; unmeasured kinds such as
Cursor and opencode remain placeholders by name only and must not acquire
invented flags.

> **Preview through 1.x (G636).** The post-start interaction field is a
> post-v0.12.0-freeze preview surface. It is outside the 1.0 compatibility
> promise, may change or be withdrawn during 1.x, and is formalised only by a
> later MAJOR release. See the [compatibility ledger](1.0-compatibility-ledger.md)
> entry for this preview surface.

> **Central autopilot supervision rule.** In an unattended autopilot seat, an
> action outside the launch allowlist is silently auto-denied rather than
> surfaced as a G550 supervision dialog. Derive and **record** the allowlist
> from role needs. READY must prove an expected allowed action, reachability of
> the role's canonical reporting surface, and an out-of-scope denial. Review
> evidence must inspect command outputs and the transcript for denials; liveness
> is not proof that a denied step ran. This changes supervision evidence only:
> G556 liveness and notify/delivery semantics are unchanged.

#### Copilot — measured first recipe

```text
herdr agent start <logical-role> --kind copilot --pane <pane-id> -- --mode autopilot --allow-all-tools --add-dir <role-work-root> [--add-dir <host-routing-root>] --max-autopilot-continues 10
```

- **Role-derived roots.** Give every role one bounded `--add-dir <role-work-root>`
  for its checkout or worktree. A reviewer also needs `--add-dir
  <host-routing-root>` because `intent-cli notify report` is its canonical
  reporting surface. Do not add unrelated developer-machine roots. Before
  delegation, the orchestrator compares workspace prerequisites with this
  recorded write envelope and prepares anything outside it under orchestrator
  authority (G655).
- **Continuation bound.** Keep `--max-autopilot-continues 10` explicit. Any
  different bound is an operator decision recorded with the recipe.
- **Inline-payload advisory.** Profile `copilot-autopilot-observed-paste-risk`
  declares `inline_payload_warning_chars: 4096`. It is advisory only: a payload
  above it is likely pasted rather than typed, while a payload below it is not
  promised safe because the real limit is terminal- and agent-dependent.
- **Reference-first limit.** Keep repeated review substance in committed
  `review-context.md` and delegate a terse pointer, but do not present that
  discipline as the paste remedy: a minimal canonical `notify delegate` envelope
  still measures 842 characters over 14 lines and can itself be pasted. It
  reduces duplication, not a paste-sensitive wedge; G619 owns the transport-layer
  remedy.
- **Task-envelope delivery method.** A paste-sensitive herdr seat with an
  existing record declares `delivery_method: file-backed` through the
  registry-limited topology field update: `notify` writes the unchanged envelope under
  host `.intent-cli/tasks/<domain>/<team>/<task-id>-<nonce>.md` before sending
  the pane one line, `Read task envelope: <path>`. Declare `inline` explicitly
  when desired; an absent declaration preserves existing inline delivery.
- **Post-start interaction (G636, preview-through-1.x).** At the first task,
  Copilot 1.0.78 presents `1. Enable all permissions (recommended)` /
  `2. Continue with limited permissions` / `3. Cancel`, with the cursor on
  option 1. Answer `Continue with limited permissions` to preserve the bounded
  `--add-dir` envelope. The default `Enable all permissions` answer is unsafe;
  the recipe records `default_is_safe: false`, and accepting it on restart is a
  supervision failure, not a shortcut.
- **Startup gates.** Folder trust and autopilot-enable are operator provisioning
  gates; launch flags bypass neither. The autopilot-enable dialog appears at the
  **first task** even when launch used `--mode autopilot`. With
  `--allow-all-tools` and bounded roots, choose `Continue with limited
  permissions`; never choose `Enable all permissions`, which discards the
  boundary.
- **Prohibited blanket permissions.** `--yolo` and `--allow-all-paths` are
  **prohibited** for unattended seats on developer machines. Use bounded
  `--add-dir` roots instead.

#### Codex — measured recipe (G647)

```text
herdr agent start <logical-role> --kind codex --pane <pane-id> -- --sandbox workspace-write --ask-for-approval never --add-dir <role-work-root> [--add-dir <host-routing-root>]
```

- **Role-derived roots.** Use one bounded `--add-dir <role-work-root>` for the
  role checkout/worktree; add the host routing root only when that role's
  canonical report surface needs it. The role-work-root is an ordinary-file
  root, not permission to write repository metadata: Codex cannot write `.git`
  even when `.git` is inside a declared root. A non-sandboxed host-state role
  therefore prepares registered worktrees and performs host-state git
  operations before delegation. The orchestrator compares workspace
  prerequisites with this recorded write envelope and prepares anything
  outside it under orchestrator authority via that authorized host-state role
  (G655).
- **Measured bounded invocation.** This invocation was measured on **Codex
  v0.144.1 / macOS**; it is not a universal flag recipe for an unmeasured
  environment.
- **Measured self-update behavior.** Codex can self-update, print **“Please
  restart Codex”**, and exit to the pane's shell. Restart the agent in the
  recorded pane and re-run READY/ping; this is a restart condition, not a
  wedge, and never a reason to widen the envelope.
- **Measured envelope asymmetry.** Writes outside declared roots were denied,
  while reads outside declared roots were not denied. Writes to `.git` are also
  denied even when `.git` is inside a declared root (for example `.git/index`,
  `.git/FETCH_HEAD`, and worktree metadata); another `--add-dir` does not make
  those operations available. Treat these as explicit security facts and route
  repository-metadata work to the non-sandboxed host-state role.
- **Post-start interaction (G636).** No Codex post-start interaction was
  observed on **MyIntentHost** on **2026-08-07**. The structured
  `post_start_interaction` record therefore carries `status: unmeasured`,
  `observed: false`, null prompt/answer/default-safety values, and an explicit
  absence reason; Markdown renders that absence. Do not infer an observed
  prompt, answer, or default safety from the measured launch facts.
- **Registry boundary.** `topology update-kind` surfaces the measured target
  recipe with the requested change. When a target kind has no recorded recipe,
  it says so explicitly and does not invent launch flags. The recorded kind is
  the human's current wish, a human-requested switch is one step, and recovery
  never changes a kind unattended.

**Unattended READY branch.** Run the normal G556 liveness checks and prove all
three additional facts: an expected action inside the recorded roots succeeds;
the role reaches its canonical reporting surface (for review, `intent-cli notify
report` through its host routing root); and a deliberately out-of-scope action
is denied. Capture that denial for review. A live pane or a successful allowed
action alone is **not** READY. If a denial probe unexpectedly succeeds, first
check whether the post-start interaction was answered with its default; an
unsafe default can discard the declared boundary.

**4. Role initialization.** Type the actas form matching the pane's CLI —
`/agmsg actas <role>` for claude, `$agmsg actas <role>` for codex — then confirm
readiness in **three layers that must not be collapsed**:

1. **Delivery configuration** — `delivery.sh status` reporting a mode (e.g.
   `mode=monitor`) proves registration and configuration only. It does **not**
   prove a watcher is alive or that any session is attached; a receiver can
   report `mode=monitor` while nothing is streaming. The converse holds too: a
   pane sitting on a trust screen is **not live-attached and not session-active**
   but its delivery configuration — set with `delivery.sh` before launch — is
   unaffected. Launch-UI state never erases configuration, and configuration
   never implies attachment.
2. **Live attachment — agent-specific.** For **claude**, the proof is the Claude
   Code Monitor markers in that receiver's own session: `Monitor(agmsg inbox
   stream)` in the transcript, footer `1 monitor` (**not** `1 shell` — a
   background `watch.sh` is diagnostic/fallback only), and `Monitor event` lines
   as messages arrive (see [Monitor tool vs delivery-mode](#monitor-recovery)).
   For **codex**, it is the bridge-alive marker where the bridge applies:
   `delivery.sh status` showing `Codex bridge: <team>/<role> alive (pid N)` —
   noting the bridge arms on the first turn sent to the session, not at startup.
3. **End-to-end** — the [ping test](#receiver-readiness) ack is the **only**
   end-to-end proof. Layers 1 and 2 are preconditions, never a substitute; when
   the live markers are unavailable, fall back explicitly (`turn` delivery or
   manual `inbox.sh`), say so, and still require the ack.

**Verified liveness — a startup report is not readiness.** Provisioning
concludes on verified liveness, not on a message. A role is provisioned when its
startup report has arrived **and**, after a **settle delay**, all three of these
still pass:

1. **The pane still hosts the agent TUI** — read the pane. A shell prompt means
   the agent exited, however recently it reported. The pane is ground truth; a
   message is a claim about the past.
2. **An agmsg ping-pong round trip succeeds** — ping now, require the pong now.
   The earlier readiness ack proves only that the receiver was alive *then*.
3. **For codex, the bridge is armed and the app-server attachment is stable** —
   a codex TUI attaches to a per-folder app-server over a `--remote` websocket,
   so the attachment can die while the pane and the bridge both looked fine a
   moment ago.

> **A startup report is not readiness.** Field incident (2026-07-29): two codex
> agents reported startup-complete and died **seconds** later when their shared
> app-server was lost — and the supervising thread went on *"waiting for startup
> reports"* while every agent was already dead. Never conclude provisioning on
> the report alone.

The settle delay matters: the failure this catches happens seconds *after* the
report, so verifying instantly re-observes the moment the report described and
proves nothing new.

**Early death is a normal mode.** Its signature is the TUI **exiting to a shell
prompt**, typically leaving a resume hint on screen, after a websocket
**transport reset** dropped its app-server connection — a pane that looks like
an ordinary terminal, which is why a scan looking only for dialogs misses it.
When a check fails, **re-check and recover; do not wait for another report** — a
dead agent sends nothing, so waiting is waiting forever.

> **Shared app-server death mode.** Killing an app-server **takes down every
> attached TUI at once**, including agents belonging to other teams that had
> nothing to do with whatever prompted the kill. The 2026-07-29 double death was
> exactly this. Prevention is the attribution rule below: verify a process's own
> cwd before stopping any app-server, and never act on a process you cannot
> attribute. This is the second-order cost of an attribution violation — the
> victim is not the process you killed, it is everything attached to it.

**5. Exclusivity and handover.** Exactly one live session may hold a role; a
second actas attempt is refused, and that refusal is correct. Replacing a
session goes through the **graceful drop** — the current holder drops the role
(with its operator confirmation) and only then does the successor claim it,
followed by readiness + ping test again.

**6. herdr is the reference workspace manager.** The surfaces a design thread
drives are `workspace create`, `pane split`, `pane send-text` / `send-keys`,
`agent prompt`, and `agent wait`. intent-cli does not own, ship, or wrap herdr —
consult herdr's own documentation for internals, exactly as this guide links out
for agmsg internals. **Any** equivalent workspace manager may be substituted
provided the same rules hold: one dedicated folder per role as the pane cwd,
shim-safe typed launch, attended first-run prompts, actas + readiness before the
ping test, and one holder per role with a graceful drop on handover.

## Team workspace layout convention (G637 — preview-through-1.x)

> **Preview through 1.x.** This workspace-layout guide was added after the
> v0.12.0 freeze. It is outside the 1.0 compatibility promise, may change or
> be withdrawn during 1.x, and is formalised only by a later MAJOR release.
> See the [compatibility ledger](1.0-compatibility-ledger.md) preview entry.

Every team workspace uses one visible three-seat shape: `orchestration` owns
the left pane at 40% width and full height; `implementation` is the top-right
pane; and `review` is the bottom-right pane. The two right panes therefore
split the remaining 60% evenly (30% each). Labels use the recorded topology
role vocabulary: `orchestration`, `implementation`, and `review`. If the
third seat is genuinely a design seat, label that pane `design`; the slot
convention does not change the seat's identity.

The guide consumes an operator-observed shape and explicit IDs. It never lists
or queries a live workspace and never executes herdr:

```text
intent-cli guide workspace-layout --workspace-id <workspace-id> --tab-id <tab-id> \
  --shape canonical|three-column|mirrored|unknown \
  --orchestration-pane <orchestration-pane> --implementation-pane <implementation-pane> \
  --review-pane <review-pane> --temporary-tab-id <temporary-tab-id> --format markdown
```

For a conforming workspace with canonical labels, the command reports that no
change is needed. For a different shape it prints, in order, a temporary-tab
round trip (`herdr pane move` to the temporary tab and back targeting the pane
it should sit under), pane renames, and the resize calls that establish the
40% / 60%-even split. Supply the observed ratios when known so the resize
amounts are minimal directional deltas. Resolve every explicit ID immediately
before running a printed command; the guide is a plan, not an executor.

Same-tab `herdr pane move` is a no-op returning `changed: false` on the measured
herdr 0.8.0 on macOS baseline. The temporary-tab round trip was also measured
on herdr 0.8.0 on macOS: the pane was reparented rather than recreated and all
seventeen running agent processes survived. Verify that round trip on a scratch
tab first, confirm every agent is still present, then apply it to a workspace
holding working agents and confirm presence again. These are measured facts,
not claims about unmeasured herdr versions or platforms. A single-pane
workspace has no arrangement to standardise and is outside this convention.

## Herdr-only operating procedure (preferred — fewer dependencies)

This section is operative only when the team has recorded `herdr-only`. It is
the concrete counterpart to the agmsg provisioning/receiver sections. It is
the preferred transport because it has fewer dependencies; agmsg + herdr
remains supported and is not retired. Exactly one transport runs per team;
mixed agmsg and herdr delivery is a contract violation.

### Human seat-kind intake and measured registry (G647)

Before launching any herdr-only seat, ask the human which CLI and model each
seat (`design`, `orchestrator`, `implementation`, and `review`) should run, then
record each answer as that seat's `kind`. Do not silently choose a default. The
recorded kind is the human's current wish; a requested switch is one step, and
recovery never changes a kind unattended. `topology update-kind` shows the
target's measured recipe with the change or names its absence explicitly.

The registry contains only measured kinds. Codex's measured entry is:

```text
herdr agent start <logical-role> --kind codex --pane <pane-id> -- --sandbox workspace-write --ask-for-approval never --add-dir <role-work-root> [--add-dir <host-routing-root>]
```

On **MyIntentHost**, measured **2026-08-07**, **Codex v0.144.1 / macOS**, the
measured facts are: the bounded invocation uses workspace-write, never-ask
approval, and role-derived roots; self-update can print **“Please restart
Codex”** and leave the pane at a shell (restart and re-run READY/ping; this is
not a wedge); and writes outside declared roots are denied while reads outside
them are not. The rendered and structured measurement entries carry
`host: MyIntentHost` and `date: 2026-08-07` for each fact. Cursor and opencode
have no measured entry and remain placeholders by name only.

> **Reachability discipline (G650).** Source presence is not reachability:
> record that this G647 guidance is reachable only after rendering the
> team-scoped guide under the recorded session layer on the build the role
> actually runs. A missing typed fragment is a fail-closed rendering defect,
> not a reason to weaken the typing rule.

### Provision and prove READY

Use this topology literally: **one workspace per team, one tab named after the
team, one pane per role, each pane opened with that role's folder as its cwd.**
This keeps all roles visible to the operator at once and keeps the G550
supervision pane scan from being hidden behind an inactive tab.

Create the workspace first:

```text
herdr workspace create --cwd <host-repo> --label <team> --no-focus
```

Measured on herdr 0.8.0, the `workspace_created` result has top-level `workspace`, `tab`,
and `root_pane`; seed the mapping from `workspace.workspace_id`, `tab.tab_id`,
and `root_pane.pane_id`, and verify `root_pane.cwd`. The returned tab is the one
normal tab for the team. Ensure it is named `<team>`; if needed, use the
returned explicit tab id:

```text
herdr tab rename <tab-id> <team>
```

Assign the root pane to one host-repo role. **Pane creation is the default** for
every remaining herdr-resident role. Resolve a non-empty pane id from the
recorded mapping, split from that explicit pane, and set the new role cwd:

```text
herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus
```

Use `<host-repo>` for design/orchestrator, the child checkout for implementation,
and an isolated review cwd/worktree for review. Update the mapping from every
pane creation result. `herdr tab create --workspace <workspace-id> --cwd <role-cwd> --label <logical-role> --no-focus` is **not** the primary path. Use
it only when the operator explicitly authorizes a separate role tab for a
documented reason, such as requiring tab-level lifecycle isolation instead of
simultaneous visibility.

Same-tab `herdr pane move` is unsupported. To change a same-tab layout, recreate
the affected pane and update the logical-role mapping; do not assume an in-place
move preserves the role's placement.

Record an operator-visible logical-role→pane-id/cwd mapping; workflows never
hard-code pane/workspace ids. After initial workspace creation returns the first
ids, every provisioning or mutation command must resolve its explicit non-empty
pane/workspace target id from that recorded mapping immediately before it runs
and carry the id on the command. If resolution is missing or empty, fail closed
and do not run the command: herdr can otherwise focus-default to the currently
focused pane, including a pane in another team. The existing G555 cross-project
attribution rules remain authoritative and unchanged; reference them rather
than inventing another attribution policy. An external design frontend is
recorded as a reader type rather than fabricated as a pane.

Persist that machine-scoped, team-specific topology at
`<host-repo>/.intent-cli/topology/<domain>/<team>.json`. The CLI writes a
directory-local ignore file under `.intent-cli/topology`, so pane ids and absolute
working paths remain local and never require editing the repository root `.gitignore`.
Each record carries its `domain` and `team`; a copied record whose path and identity
disagree fails closed. `session-layer-mode.json` remains the tracked multi-team truth.
Re-recording on the destination machine—not copying machine values—is the migration.
Record the team
`workspace_id`; under `roles`, give each pane-backed role `resident: herdr` plus
its explicit `workspace_id` and `pane_id`, and give a role outside herdr
`resident: external` plus its routing-root-relative `reader` (normally
`.intent-cli/events/<domain>/<team>.jsonl`; legacy
`.intent-cli/events/<team>.jsonl` records remain valid). All recorded roles may be senders and
delegate report targets. On receipt, herdr residents require a running agent at
the recorded pane in that exact team workspace; external residents receive the
canonical delegate/report event through the recorded reader. A missing/unsafe
reader, stale pane, foreign-workspace-only name, or ambiguous mapping fails
closed with no prompt or append.

When the new per-team file is absent only, a reader may consult the legacy fixed
file with a deprecation warning naming `topology record` as the re-record remedy.
If both paths exist and disagree, it fails closed rather than preferring either.

Write and inspect this artifact through the canonical topology surface, never
by hand:

```text
intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident herdr --workspace-id <workspace-id> --pane-id <pane-id> --cwd <role-cwd> [--kind <agent-kind>] --write
herdr pane rename <pane-id> <logical-role>
intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident external --reader <routing-root-relative-path> [--frontend <frontend>] --write
intent-cli session-layer topology update-kind --domain <domain> --team <team> --role <role> --current-kind <kind> --new-kind <kind> --confirm-update-kind --write
intent-cli session-layer topology update-field --domain <domain> --team <team> --role <role> --field delivery_method --current <absent|inline|file-backed> --new <inline|file-backed> --confirm-update-field --write
intent-cli session-layer topology retire-legacy --domain <domain> --team <team> --evidence <named-fleet-migration-evidence> --confirm-retire-legacy --write
intent-cli session-layer topology validate --domain <domain> --team <team> --live --format json
intent-cli session-layer topology show --domain <domain> --team <team> --format json
intent-cli guide topology-workspace-move --domain <domain> --team <team> --format markdown
intent-cli session-layer topology move --domain <domain> --team <team> --workspace-id <new-workspace-id> --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... --dry-run --format json
intent-cli session-layer topology move --domain <domain> --team <team> --workspace-id <new-workspace-id> --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... [--current-digest <digest>] --write --format json
```

G697 adds the intentional workspace rebuild path. The installed
`guide topology-workspace-move` recipe is reachable from `guide review`,
`guide next --role review`, and `guide orchestrator-thread`; it renders the
complete inspect → dry-run preview → explicit write → validate → notify
preflight sequence. The move requires a complete operator-supplied old-pane to
new-pane map for herdr roles, holds a CAS lock, compares the topology digest,
and atomically updates the team and role workspace/pane ids while preserving
all other role fields. It never queries herdr, creates panes, changes
membership, or repairs a per-role conflict; that refusal names this move
command as its sanctioned whole-team transition.

Agent kind is whatever herdr can start: Claude, Codex, Copilot, Cursor, OpenCode,
and others are examples, not a supported-set restriction. Logical role defaults
are `implementation`, `review`, `interview`, and `clarify`; existing explicit
role mappings, including legacy product-named mappings, remain valid. The three
update/retire mutation commands emit JSON and support only `--format json`. For `update-kind`,
an explicit `--dry-run` takes precedence over `--write` in either flag order and
never writes.

The legacy fixed `role-pane-mapping.json` compatibility read is removed. If a
host still has that file but lacks its per-team record, readers fail closed and
name `topology record --domain <domain> --team <team> ... --write` and
`topology retire-legacy --domain <domain> --team <team> --evidence <evidence>
--confirm-retire-legacy --write`; no reader auto-migrates it. After a successful `retire-legacy`, the CLI appends one fleet-citable entry to
`<host-repo>/.intent-cli/legacy-topology-retirements.jsonl`, outside the ignored
machine-local topology directory. Its defined fields are `timestamp_utc`, `host`,
`domain`, `team`, `retired_path`, and named `evidence`, so a later ledger decision
can cite accumulated retirements without changing the current legacy-reader
disposition.

Immediately after recording a herdr pane mapping, run `herdr pane rename
<pane-id> <logical-role>` so the human can identify the pane they are about to
supervise. Herdr owns this display label; intent-cli never calls pane rename or
sets labels as a topology-record side effect. `record` uses only values supplied
by the operator. It never queries herdr, guesses ids, provisions resources, or
repairs an existing conflict: an exact match is an idempotent no-op and a
different existing role is refused without rewriting the file. `validate` is
read-only and returns `valid: true|false` plus every finding in one answer; with
`--live`, it also reads `herdr pane list --workspace <workspace-id>` and reports
a missing label on a recorded pane as an informational finding without changing
`valid` or setting the label. Each finding names its role, field, cause, and
message, including missing/unsupported residence, missing `pane_id`, unsafe
reader, and team-workspace mismatch. `show` is also read-only and resolves each
pane or reader through the same delivery-target function used by `notify`, with
no prompt, append, or herdr query. `automation doctor` carries this health when
the mapping exists or herdr-only requires it, and notify topology refusals point
back to `topology validate` / `record` as the remedy. Invalid state always stays
fail-closed; these commands add knowledge and a controlled writer, never a
fallback.

`update-field` is the narrow path for declaring a field that a recorded role
never carried, or for changing its already recorded value. It requires the
role, the field name, the stated current value, the new value, explicit
confirmation, and `--dry-run` or `--write`; state `--current absent` only when
the field is actually absent. A stale statement is refused in both directions.
The registry initially permits only `delivery_method`, so an unknown or dotted
name is refused rather than becoming an arbitrary JSON-path editor. The command
changes only that field. It does not relax `record`: re-recording a different
shape still refuses its conflict and has no force flag.

#### Visible, generated mode markers

The mode record remains the only source of truth, but a team should not have to
remember to query it before noticing which transport it uses. Put one explicit
empty managed block for each `(domain, team)` in an agent-startup file
(`AGENTS.md` or `CLAUDE.md`), then generate its display from the recorded mode:

```text
<!-- intent-cli:session-layer-marker:start domain="<domain>" team="<team>" -->
<!-- intent-cli:session-layer-marker:end -->

intent-cli session-layer marker generate --domain <domain> --team <team> --file <AGENTS.md|CLAUDE.md> --write
```

The generated block carries the domain, team, mode, canonical `session-layer
show` verification command, and a hash of the resolved canonical record. It is
never a host-global or bare mode claim. The writer reads only the record and
updates only that delimited block; it refuses an unrecorded team (naming
`session-layer set ... --write`), an absent block, or malformed markers, and
never writes `session-layer-mode.json`.

The shared preflight discovers managed markers in `AGENTS.md` / `CLAUDE.md`.
An unmarked recorded team produces informational `marker-not-generated` with
the generating command. A marker whose mode or record hash differs produces
`marker-drift`, names the file, claim, and canonical truth, and makes the
verdict `configuration-incomplete`; regenerate after every mode switch. A
marker is a signpost, never a substitute for the canonical record.

When provisioning a herdr workspace, include the recorded mode in the workspace
label (for example, `<team> · herdr-only`). That label is human-facing and
non-authoritative: intent-cli neither writes herdr state nor reads the label as
mode evidence.

#### Mode switches require a manual migration review

When `session-layer set --write` actually changes a recorded mode, it emits an
ordered **manual migration plan**. Review the other mode's session hooks, inbox
watchers or monitors, then regenerate the G601 visibility marker. Each item is
an operator action: intent-cli never deletes, rewrites, or disables user
configuration. A no-op set emits no plan.

The shared preflight checks only declared locations for known other-mode
residue. For example, project-level agmsg session hooks in `.codex/hooks.json`
are reported on a herdr-only team. Such `other-mode-residue` findings name the
path and owning mode, state the one-mode exclusivity contract and removal
guidance, but remain advisory: residue is a hazard rather than proof of active
mixing and never infers, flips, or overrides the canonical mode record.

#### Shared record-first session-layer preflight

`automation doctor`, the guide's READY definition, and `notify` consume one
machine-readable `session_layer_preflight` result from the same production
predicate. They do not maintain three look-alike checks. Its passive structural
phase contacts no receiver and its active receiver phase is reported
separately; skipping the active phase does not invalidate a passing passive
verdict.

For a named team, absence of a mode record is
`configuration-incomplete`—check not completed, never `not-required`. Record
the intended mode explicitly, then validate it:

```text
intent-cli session-layer set --domain <domain> --team <team> --mode agmsg|herdr-only --write
intent-cli automation doctor --domain <domain> --team <team> --format json
```

A bare anonymous root remains `unjudged` until an expected domain/team is
declared. `cannot-determine` is never green. Preflight never infers or repairs a
mode from live herdr state: once a mode is recorded it probes only that
transport, and contradictory evidence is diagnostic detail only. A role-pane
topology describes `herdr-only`; if the team records `agmsg`, the preflight
reports a mismatch naming the team, recorded mode, and topology mode. Agmsg
teams therefore do not need herdr installed.

Launch with the typed surface:

```text
herdr agent start <logical-role> --kind <agent-kind> --pane <pane-id> -- <operator-approved-permission-flags>
```

Permission flags belong to the launch, not injected modifier chords. Approvals
are never auto-answered; G550's MAY/escalate boundary still governs. Approvals
are pane-visible and handled at the supervision boundary, explicitly unlike the
agmsg Codex bridge's headless auto-decline. READY first requires the shared
passive preflight to be `ready`, then applies the G556 active receiver proof.
After the startup report, wait a settle delay, re-check the expected cwd/repo
and agent kind plus same-pane detection, and send a bounded unattended probe
whose working transition and fresh settled acknowledgement are observed.
Repeat the entire record-first settle-and-re-check sequence after
re-provisioning. Workspace existence, a shell prompt, agent state alone, or an
unattended prompt that stays idle is not READY. In herdr-only the verified
logical-role→pane mapping is the role identity; there is no separate agmsg
identity step.

### Dispatch, wait, and verify the artifact

Use the [canonical notify workflow](#canonical-notify-workflow): run
`intent-cli notify delegate ...` with the target logical role. The CLI resolves
herdr-only internally, validates the role mapping, and generates the structured
task block; do not hand-write `herdr agent prompt`.

**Reference-first dispatch.** Review substance belongs in the committed canonical
`review-context.md`; the delegate carries a terse pointer to that file. Add any
consideration outside the packet to `review-context.md`, push it, then reference
it — do not inline the substance into a pane prompt. This is the packet structure
working as intended, not a new packet meaning.

The measured limit matters: a minimal canonical `notify delegate` envelope is
842 characters over 14 lines and is itself a paste. Reference-first reduces
duplicated substance; it does not prevent a paste-sensitive seat from wedging.
G619 owns the transport-layer remedy.

For a paste-sensitive herdr seat whose recorded role lacks `delivery_method`,
use `topology update-field` with `--field delivery_method --current absent --new
file-backed` and explicit confirmation; use the same path with the recorded
current value for a later allowed change. `notify` writes the unchanged envelope to an addressable durable task
file under `.intent-cli/tasks/<domain>/<team>/<task-id>-<nonce>.md` before it
sends the pane the single-line `Read task envelope: <path>` pointer. The file is
not deleted, so a restarted recipient can read the same task. Declare `inline`
only to choose it explicitly: without a declaration, the established inline
delivery remains byte-identical.

The recipient recipe's `inline_payload_warning_chars` profile is **advisory**,
not a universal safe-paste limit. When a delegate's inline payload exceeds its
resolved threshold, `notify` warns with the payload size, threshold, and the
reference-first remedy in both human and machine output, but still delivers the
same payload. It never refuses or truncates it. Observed on a peer team: a large
paste can leave broken bracketed-paste state in a terminal and terminate some
agent processes; recover with a fresh agent start. This is an observation, not a
universal size limit or a claim about every terminal or agent.

For a settled pane, notify first uses bounded `agent prompt --wait --until
working` semantics, then a separate bounded `agent wait` for
idle/done/blocked. The observed unattended working transition is the delivery
verdict: once observed, `delivered: true` cannot be negated by the later settle
check. `settle_outcome` reports that independent acknowledgement as `observed`,
`pending`, or `not-applicable`; `resend_permitted` is the
machine-actionable retry verdict. An idle pane that stays idle is
`receiver_state_outcome: idle-stays-idle`, `working_transition: not-observed`,
`settle_outcome: not-applicable`, and not delivered, so
`resend_permitted: true`. A pane that enters working and is still working when
the bounded settle observation ends is a successful non-terminal dispatch:
`receiver_state_outcome: working-observed-in-progress`,
`working_transition: observed`, `settle_outcome: pending`, and
`resend_permitted: false`; do not resend while the recipient is working. A pane
already working when notify starts is still delivered after prompt submission,
but reports `receiver_state_outcome: already-working`,
`working_transition: unobservable`, `settle_outcome: not-applicable`, and
`resend_permitted: false`; it never attributes the active turn to the new
prompt. Dry-run keeps the active phase `skipped` and never prompts.

`--to` continues to name the topology's logical role, but a logical role name
is independent of the globally unique herdr agent name. Recipient identity is
the recorded workspace plus pane: notify requires exactly one running agent at
that pane inside that workspace and uses its name only as diagnostic detail. No
agent, several running agents, or a pane reported only in a foreign workspace
fails closed while naming the team, recorded workspace, and recorded pane;
there is never an agent-name match fallback.

Generate a fresh unpredictable nonce for each dispatch; never reuse one or use
the task id alone. `pane wait-output` searches existing output immediately, so
a precomposed wait needle in the task block can be echoed and falsely match
before work starts. The generated split fields keep that literal out of the dispatch.
Files, commits, PRs, and verification logs are the handoff; screen prose only
points to them. Repairs return to the same logical role with the task id and
concrete delta through `intent-cli notify delegate`. A marker match from any buffer
is never sufficient; the named artifact must exist and pass verification.

### Two normative wake sources

Herdr-only orchestration has two normative wake sources. The canonical
`intent-cli notify report` is primary and most informative: it carries the task
id, status, artifact, and summary, but depends on the worker cooperating and
running its required final command. The normative **SECOND wake source** is
herdr's observed `pane.agent_status_changed`: it depends only on herdr observing
the process, so it still wakes orchestration when the worker omits its report,
but it carries no task outcome.

The herdr 0.8.0 socket API measured on this host uses `events.subscribe`. Include one
subscription entry per watched pane because `pane.agent_status_changed`
requires `pane_id`:

```json
{"method":"events.subscribe","params":{"subscriptions":[{"type":"pane.agent_status_changed","pane_id":"<resolved-pane-id>"}]}}
```

Resolve every `<resolved-pane-id>` from the recorded logical-role→pane mapping
when subscribing and after re-provisioning; never hard-code pane ids. The event
frame carries `agent`, `agent_status`, `pane_id`, and `workspace_id`:

```json
{"event":"pane.agent_status_changed","data":{"agent":"<agent>","agent_status":"<working|idle|done|blocked|unknown>","pane_id":"<resolved-pane-id>","workspace_id":"<workspace-id>"}}
```

Track the prior status independently for each logical role. Wake only on that
role's `working`→settled transition, where settled is `idle`, `done`, or
`blocked`; an initial settled sample, `unknown`, or settled→settled change does
not wake orchestration. Apply a settle delay before waking and per-role dedupe
so a burst produces one wake for that observed transition. A new `working`
observation re-arms that role.

**A state change means only that something happened, never that a task
succeeded.** After every wake from either source, orchestration checks current
herdr state and any approval/question pause, the exact fresh completion marker
and status, the named verified artifact, and fresh canonical intent-cli/GitHub
facts. The two sources cover complementary failures: notify report is richest
but depends on worker cooperation; state change needs only herdr observation but
carries no outcome. The periodic `intent-cli automation stalled-work ...` check
is the last net. Its non-informational `approved-not-merged` kind makes an open,
non-blocked PR carrying `intent-pr-approved` past the configured stale threshold
actionable even if every immediate wake source failed; it reports age and points
to the canonical merge-then-`closeout pr` path. This measured shape does not
replace the standing rule to consult installed herdr help/schema for
version-specific details.

Every wait is bounded:

```text
herdr agent wait <logical-role> --until idle --until done --until blocked --timeout <milliseconds>
herdr pane wait-output --match "ORCH_RESULT <fresh-per-dispatch-nonce>" --source recent-unwrapped --timeout <milliseconds> <pane-id>
```

After every wait return—including `idle`, `done`, `blocked`, marker match, and
timeout—run `herdr pane read --source recent-unwrapped <pane-id>` and inspect
for a pending approval or question. `idle` can mean approval-paused. Classify
the outcome as settled, approval/question-paused, or timeout. For a pause,
answer only a pane-read G550 MAY class; escalate everything else, then re-enter
the wake and wait again. A timeout is likewise a re-entry point: persist
progress, return control, then resume on a later wake. Deterministic scripts
with persisted cursors are preferred for long flows. Composite success requires
approval/question-free settled state + the exact fresh-nonce marker and status +
an existing verified artifact + fresh canonical intent-cli/GitHub facts.
Artifact verification plus canonical facts is the final gate;
neither state nor marker alone means success.

### Normative `events.jsonl` design boundary

Resolve the host root at runtime and write
`<host-repo>/.intent-cli/events/<domain>/<team>.jsonl` (G681,
`preview-through-1.x`). Domain and team are verbatim validated path segments;
two domains that share a team name therefore write distinct files. Readers
check the scoped file first and fall back to legacy
`<host-repo>/.intent-cli/events/<team>.jsonl` only when the scoped file is
absent. New writes never use the legacy file. Before constructing the path,
fail closed on an empty name, a leading dot, `/` or `\`, or any `..` sequence.
Never sanitize an invalid name.

Migration is operator-owned and optional; intent-cli never moves a host file.
From the host repository root, replace the placeholders and run exactly:

```sh
mkdir -p .intent-cli/events/<domain> && mv .intent-cli/events/<team>.jsonl .intent-cli/events/<domain>/<team>.jsonl
```

An existing external-reader topology may retain its legacy reader value: the
same scoped-first read fallback and scoped write placement apply without a
topology edit. Keep the existing durable watermark and its unchanged file
identity/replacement checks after an operator move; never reset or replay it
automatically.

The canonical `intent-cli notify` surface is the only writer; callers never
append by hand. The orchestrator normally writes delegate/escalate events, and
a receiver's canonical report may append when its recorded recipient is
external. Open with `O_APPEND`, append one complete JSON object per line, permit
no embedded newline, and normalize `summary` to one line. The required schema
is:

```json
{"timestamp":"<RFC3339>","team":"<team>","kind":"completion|blocked|question|escalation","unit":"<execution-unit-or-task-id>","summary":"<one-line-summary>","artifact":"<repo-relative-path-or-URL>"}
```

Write only canonical notifications addressed to a recorded external reader and
design-relevant completion, blocked, question, and escalation events. A
delegation to an external reader uses `question`; an external report uses its
`completed|blocked|question` status to write event kind
`completion|blocked|question`. Pane-resident dispatch, routine
progress, pane output, and acknowledgements are never mirrored here. This
mode-independent channel remains the explicit external-reader/design boundary,
never a fallback inter-agent bus and never a replacement for `intent-cli
notify`, GitHub, or intent-cli workflow state.

Every reader persists a durable watermark across watcher restarts containing
the file identity, byte offset, and complete-line count. Before each read it
verifies the same identity and that neither byte nor line count moved backwards.
The durable byte-offset watermark is always paired with that file identity and
complete-line count; none of the three values is restart-local.
Rotation, truncation, a backwards count, or file replacement fails closed for
operator recovery; readers never silently reset to the beginning because replay
can duplicate a design decision.

- Claude app watcher: tail complete unseen lines after its durable
  file-identity/byte-offset/complete-line-count watermark, advance only after
  successful handling, and preserve it across watcher restarts. Rotation,
  truncation, backwards byte/line count, or file replacement fails closed and
  never resumes at the beginning.
- Codex CLI in a herdr pane: use `intent-cli notify delegate` / `report` and do
  not poll this file for ordinary coordination. When acting as a design-boundary
  reader, use the same durable restart-surviving watermark and fail closed on
  rotation, truncation, backwards count, or file replacement—never reset to the
  beginning.
- Codex Desktop: poll at a one-minute-class cadence, consume only complete lines
  after its durable restart-surviving file-identity/byte-offset/complete-line-count
  watermark, and advance after successful handling. Rotation, truncation,
  backwards byte/line count, file replacement, or malformed JSON fails closed
  and never resets to the beginning.

### Recovery and mode switches

The operating baseline is the latest stable herdr on macOS/Linux; Windows support
is beta and is not assumed. Use `herdr --skill` only to discover the bundled herdr
agent skill; it is subordinate to intent-cli guide authority. Continue to consult
installed herdr help/schema for version-specific details.

- Live server update: use `herdr server live-handoff` to preserve running panes.
  An `events.subscribe` consumer treats stream EOF as a resubscribe trigger, not
  an error. An approval near the handoff can be re-presented: re-read the pane and
  re-judge the same dialog—never assume the earlier answer was consumed or blind
  re-answer. Pane PTY sizes can shrink until a TUI client reattaches; reads remain
  valid, headless resize/zoom does not restore the PTY, and operator TUI reattach
  is the remedy rather than a headless repair.

- Modifier-chord launch corruption: return to a shell/re-provision and use the
  typed `agent start ... -- <permission-flags>` surface.
- Post-reboot dead pty wiring: `server_not_running` diagnoses a stopped server;
  headless servers resume restored agent sessions without waiting for a TUI client.
  An undetected agent or shell-only pane still requires preserving artifacts,
  re-provisioning, rebuilding the mapping, and repeating the self-contained
  settle-and-re-check READY gate above.
- Focus-default cross-team mutation: a missing or empty explicit pane/workspace
  id can mutate the currently focused pane in another team. For every
  provisioning/mutation command after initial workspace creation, resolve a
  non-empty id from the recorded logical-role mapping, carry it explicitly, and
  do not run when resolution fails. Apply the existing G555 attribution rules
  unchanged.
- Long-wait turn death: use bounded waits, re-entry, and deterministic persisted
  loops.
- Dispatch-echo false match: keep the composed wait needle out of the task
  block, inspect the pane after the return, and independently verify the named
  artifact.
- Approval/question pause reported as idle: inspect the pane after every wait,
  apply the G550 MAY/escalate boundary, and re-enter the wake.

### Session-layer switch checklist

For **agmsg → herdr-only**: drain/park work; gracefully drop roles and stop
watchers/bridges. Turn off or remove the outgoing transport's
per-project agmsg hook configuration and delivery mode, then verify it cannot
deliver. This is not cosmetic: the observed leftover hook caused the next-launch hook-trust screen
to block the next Codex launch. Provision herdr, its mapping, and the validated
events path; pass G556 and marker/artifact detection; finally run
`intent-cli session-layer set --domain <domain> --team <team> --mode herdr-only
--write`.

For **herdr-only → agmsg**: drain/park work and append any final design event;
stop or retain/close the workspace according to operator policy so it cannot
keep delivering; provision agmsg roles and approved watcher/bridge; pass G556
and end-to-end delivery; finally run `intent-cli session-layer set --domain
<domain> --team <team> --mode agmsg --write`. The mode flip is the final
canonical step in both directions.

## Design-thread workspace supervision (keeping the team moving)

Provisioning builds the team; **supervision keeps it moving** — and that half is
what the operator relies on daily. Under authority the operator **grants** it,
the design thread drives the team's **session layer** through the workspace
manager: provisioning (above), session lifecycle, and stall supervision. It
answers a blocking dialog only inside an explicit boundary and only after
**reading that dialog from the pane**. This adds a session-layer role; it moves
**no** workflow authority.

**Granted authority — session layer only.** Two layers, two owners. The
**session** layer (panes, processes, holds, blocking dialogs) is what the
operator grants. The **workflow** layer — labels, queue-state, publication,
delegation, CI/review gating, closeout — is not granted and never moves: it
stays with intent-cli, GitHub, and the orchestrator, and the
[design↔orchestrator double-check rule](#role-boundary--design-authors-orchestrator-coordinates)
applies exactly as before. Supervising a session never authorizes a workflow
transition, and a stuck pane is never a reason to move a label by hand. The
authority is **granted, not assumed**: outside a grant the design thread
observes and reports rather than acts.

**Session lifecycle.** An unresponsive session is a session-layer fault the
design thread may repair — repair meaning a correctly held, live session again,
not taking over the role's work. Read the pane first (an "unresponsive" session
is usually blocked on a dialog, not dead), distinguish a delivery problem from a
dead session, confirm the role is still held, and prefer the least invasive fix;
replacement is the last step. Replace through the **graceful drop** — the
incumbent releases the role, then the successor claims it and re-runs readiness
plus the ping test — honoring **one holder per role** throughout. The drop's
confirmation is **operator-visible**: retiring a live session is the operator's
decision, and the confirmation records it.

**Three supervision layers.** Each catches what the others structurally cannot:

| layer | purpose | cadence |
| --- | --- | --- |
| real-time message monitor | inbound agmsg replies, blockers, escalations | continuous (a live attached stream) |
| blocking-UI pane scan | panes stuck on approval / selection / trust prompts, **and panes showing a shell prompt where an agent should be** (`agent-absent`) — the failure modes that emit no message at all | sub-minute class |
| periodic state watchdog | canonical intent-cli/GitHub state vs expected progress; the existing [design-thread watchdog](#design-thread-watchdog-recommended-safety-net) | tens-of-minutes class |

**What the pane scan is looking for.** Two stuck states rank together:

- **blocking dialog** — an approval, selection, or trust prompt waiting for
  input; handle it under the dialog rules below.
- **`agent-absent`** — a shell prompt where an agent should be. Recovery is a
  **shim-based relaunch**: type the launch into the pane's interactive shell
  (never spawn the executable), recreating the app-server first when that is
  what died, then run the **full verified-liveness sequence** again. Set the
  permission mode with the **launch flag** (e.g. `--permission-mode`) rather
  than switching it afterwards — a workspace manager's synthetic key injection
  cannot be relied on for mode switching: plain keys are delivered, but modifier
  chords such as shift+tab are not delivered faithfully (observed across
  multiple teams).

> **Re-arm across restarts.** Supervision schedulers are session-scoped: a
> `/loop`, an automation, or an attached monitor dies with the design session
> hosting it, and nothing announces that it stopped. Every layer must survive a
> design-session restart or be **re-armed as the first act of the new session**.
> Field cost of forgetting: a claim lost inside a session-restart window left a
> published issue stalled for **5.5 hours** because no layer happened to be
> running.

**Blocking dialogs — the boundary.** The **verified-read rule** governs
everything here: the design thread may answer a dialog **only after reading its
content from the pane** and being able to state what it is approving. A blind
keystroke into an unrendered dialog is prohibited however routine it looks; if
the content cannot be read or verified, the dialog is an escalation.

It **may answer** exactly four kinds, each only after that read:

1. **Confirmations of work it itself requested** — the prompt must match an
   action this design thread just initiated (same target, same operation).
2. **Command approvals verified read-only** (non-shell attended cases only) —
   the exact command shown must be read and verified read-only; anything that
   writes, deletes, installs, publishes, or mutates escalates ("probably
   read-only" is not verified). The G689 `codex:shell-command` class is
   excluded from this design-thread authority: even `project-test` is not
   read-only and all shell answers are orchestration-only through scoped
   policy and audit.
3. **Trust screens for hooks it itself installed** — its own hook-trust case;
   a trust screen for anything it did not install is not its to accept.
4. **Operator-preauthorized mode changes** — preauthorization must be specific
   and prior, and the read pane must show that same change; it is never inferred
   from a general grant to supervise sessions.

It **must escalate** four categories: **unreadable or unverifiable** dialogs
(nothing to base an answer on); **destructive or irreversible** approvals (the
cost of a wrong answer is unrecoverable); choices that **embed a product or
design decision** (design content goes through the operator and the
double-check); and **credential, security, and permission waits** — never
answerable, with or without prior authorization.

> **Unsticking a session is not deciding for it.** The design thread's job is to
> keep the session layer alive so the role can do its own work — not to make the
> role's choices, and not to make the operator's.

The [watchdog safety rules](#design-thread-watchdog-recommended-safety-net)
apply to all supervision verbatim: no duplicate delegation, no clearing a
permission prompt, no cancelling or resetting in-flight work, no force-closing an
issue/PR, and no speculative durable-state surgery.

### Repeated-observation emission hygiene (G699)

The measured supervisor keeps detection and wake authority unchanged while
making repeated observations readable and bounded. Start the route from the
built CLI with an explicit, recorded policy:

```text
intent-cli notify supervise --domain <domain> --team <team> --repo <owner/repo> --interval 300 --repeat-backoff-seconds 1800 --debounce-consecutive-observations 3 --once --write --format json
intent-cli notify supervise --domain <domain> --team <team> --repo <owner/repo> --once --format markdown
```

`--interval <seconds>` is the full observation cadence. Repeated findings use
`--repeat-backoff-seconds <seconds>` (alias `--backoff-seconds`, default
`1800` seconds), and pane status classification uses
`--debounce-consecutive-observations <count>` (alias
`--status-debounce-consecutive`, default `3`). Write mode records the resolved
values at `.intent-cli/supervision/<domain>/<team>/emission-policy.json` and
each cycle repeats them; these are configuration values, not hidden constants.

An unchanged same-key observation remains an active named `parked` record with
`first_seen`, `last_seen`, `repeat_count`, and
`emission_cadence_seconds`. Resolution clears the active record; a changed
condition fingerprint resets its counters; and a genuinely new key emits
immediately even when another key is parked. A one-poll blocked/idle flap is
not a settled transition; only the recorded consecutive threshold classifies
it. The supervisor never silently removes a parked key or infers resolution.
Detection predicates and the G695 continuation-chain record remain unchanged;
parking suppresses duplicate findings only and never performs a workflow
transition. This is an observation-only policy: intent-cli/GitHub remain
authoritative, and the supervisor may record, wake, and surface evidence but
does not clear work, merge, close out, or change labels.

### Same-cycle observation corroboration (G707)

Before emitting `registration-lost-process-present` or `live-idle-no-report`,
the supervisor consults the non-terminal observations already collected for the
same recorded workspace/pane. A same-cycle `agent_status=working` or `idle`, or
`interactive_ready=true`, contradicts the corresponding registration/live-idle
conclusion. The contradiction is emitted exactly once as the named
`observation-conflict` finding for that seat; it is not silently discarded.

Every finding in this contract is self-verifying and carries
`registration_definition`, `registration_lookup`, `registration_result`, and
`consulted_observations`. An inconclusive conflict starts with verification:
compare the named producers and recorded workspace/pane before deciding any
recovery. It authorizes no automatic action. When no same-cycle non-terminal
seat observation exists, a genuinely absent recorded seat remains eligible for
the verified `seat-absent` or `registration-lost-process-present` finding.

`observation-conflict` is a normal same-key observation, so G699's recorded
repeat backoff and park state apply; a new seat key remains immediate. This
corroboration changes only the observation classification and evidence. It does
not change workflow ownership, canonical intent-cli/GitHub state, or the
observation-only authority boundary.

### Closeout runs write-truth and explicit repair (G708)

The installed `guide orchestrator-thread` route exposes this contract from a
bare metadata-free directory. `intent-cli closeout pr` reports what it actually
wrote, not the events it planned:

```text
intent-cli closeout pr --repo <owner/repo> --pr <n> --pr-merged true --write --format json
```

- When no `runs.jsonl` append occurred, `runs_events` is `[]`,
  `runs_appended` is `false`, and `runs_skip_reason` names the reason (for
  example `queue-already-completed` or `dry-run-no-write`).
- When an append occurred, `runs_appended` is `true` and `runs_events` contains
  exactly the lines appended by that invocation. It never contains a planned
  line or an already-existing line. JSON and Markdown carry the same facts.
- A queue-completed item whose matching `pr-merged` or `closeout-recorded`
  event is absent emits the named finding
  `queue-completed-missing-closeout-runs-events`. The normal closeout path does
  not repair it automatically.

The only repair is an explicit runs-only command:

```text
intent-cli closeout pr --repo <owner/repo> --pr <n> --pr-merged true --repair-runs --write --format json
```

It appends only the missing matching events. It never re-completes the queue
item, writes queue-state, or repairs packets, lifecycle records, or any other
record. The queue-state bytes must be identical before and after the repair;
the appended tail must equal `runs_events`. A second invocation appends
nothing, reports `runs_appended: false` with
`runs_skip_reason: runs-events-already-present`, and is idempotent. This is an
operator-selected action, not an automatic repair or a guide-side mutation.

## Cross-project isolation on a shared machine

**Assume you are not alone on this machine.** Several project teams run
simultaneously, and every substrate below is shared across all of them. The two
sections above describe how to build and keep **one** team; this one keeps that
team from damaging another. It narrows the **objects** you may act on to your
own team's — it does not change what you may **do**, so the
[supervision authority boundary](#design-thread-workspace-supervision-keeping-the-team-moving)
applies unchanged.

Operator incident (2026-07-29): with several teams live at once, one project's
design thread damaged another project's resources and the operator had to
intervene by hand. A near-miss of the same class was avoided earlier that week
only by ad-hoc discipline — verifying each pid's cwd before killing anything —
discipline that lived in one session transcript rather than in this guide.

**Attribution before mutation.** Before **injecting keys into a pane**, **killing
a process**, **closing or restructuring a workspace**, or **removing or rewriting
a state file**, establish that the object belongs to *your* team. Attribution is
a positive result from all four keys — not the absence of evidence that it
belongs to someone else:

| key | how to check |
| --- | --- |
| workspace label | the workspace carries **your** team/project name; one you did not create and cannot name is not yours |
| pane cwd | the pane's working directory is one of **your** team's dedicated role folders |
| process cwd | read the cwd **per pid** before any kill — a pid list filtered by process *name* attributes nothing |
| agmsg `(team, role)` file naming | run-directory state files are named per `(team, role)`; a file whose team segment is not yours is another team's bridge/watcher state, however broken it looks |

> **Unverifiable attribution = read-only.** If you cannot positively establish
> ownership you may look and you may report — you may not mutate. Escalate to
> the operator instead of guessing: a wrong guess here is another team's outage,
> and the cost is theirs rather than yours.

**Workspace and folder exclusivity.** **One workspace per team**, labelled with
the team/project name — never reuse, repurpose, or borrow another team's
workspace or panes, not even an idle-looking one. **One folder belongs to exactly
one team** — never launch your agents in another team's folders. That is the same
folder-scoping fact that forbids two roles sharing a folder *within* a team
(G521): agmsg identity and the codex bridge are folder-scoped, so an agent
started in another team's folder takes over **their** identity and delivery.

**Shared substrates and who owns what:**

| substrate | sharing unit | ownership rule |
| --- | --- | --- |
| workspace-manager server (e.g. the herdr server) | one server process serving **every** workspace on the machine | ownership is per **workspace**, never the server — never restart, reconfigure, or kill it |
| agmsg run directory (`~/.agents/skills/agmsg/run`) | one directory holding bridge / watcher / app-server state for **all** teams | ownership is per `(team, role)` **file** — never clear the directory wholesale to fix your own delivery |
| codex app-servers | one app-server per **folder**, and folders belong to teams | ownership follows the folder — verify the process's cwd before stopping one |
| host repo | one repo holding **every** domain's metadata | ownership is per **domain path**; queue-state is protected against concurrent writers by G548's no-item-loss invariant, which is a safety net, not a licence to hand-edit another domain's state |

**Non-destructive recovery.** When you find damage — including damage you caused
— **preserve and set aside** the other project's artifacts. Rename them, move
them aside, or leave them in place and report; never delete another team's
workspace, panes, folders, process state, or files, however broken they look. A
broken artifact is still its owner's evidence. Then **rebuild your own fresh**
rather than repairing in place: new workspace, new panes, new role folders, and
re-run provisioning.

> **Recovery defaults to recreate, not cleanup.**

## Design-decision holds and bounded authority

A hold blocked on a **design decision** must be **visible** and **bounded**.
Measured cost of neither: the G551 review held its final verdict for **nine
hours** on a one-line wording ruling while every technical check was green, the
pending item was mechanically fact-checkable, both threads knew the answer — and
the hold lived only in agmsg messages, so `automation stalled-work` reported
`stalled: false` throughout. Fourth design-absence stall in the field record.

**Clarification-backed holds.** When the orchestrator or reviewer blocks on a
design decision it **records a clarification artifact** through the canonical
clarify surface (`intent-cli clarify open`), in addition to any agmsg message:
domain, blocking execution unit, the question stated so someone outside the
thread can answer it, and — when the asking thread believes it knows the answer
— the recommended answer with the facts supporting it. The artifact is what
makes the hold detectable; the message is only a notification.

> **An agmsg-only hold is a contract violation.** A block that exists only as
> messages is invisible to `stalled-work`, to `heartbeat`, and therefore to
> every watchdog and every operator glance. If you are waiting on design, the
> artifact exists; if the artifact does not exist, you are not waiting, you are
> stalled.

The OPEN artifact itself carries that content — an agmsg message may notify,
but it can never substitute for the durable record:

```bash
intent-cli clarify open <execution-unit> \
  --question "<the actual design-blocking question, answerable by someone outside the thread>" \
  --recommended-answer "<what you believe the answer is, when you believe you know it>" \
  --evidence "<the repository facts that support the recommendation>"
```

The question lands in the artifact's `QuestionText`; the recommendation and its
evidence land in the artifact's `Reason` under `Recommended answer:` and
`Evidence:` labels. All three flags are optional — omit them and the pre-G552
packet-derived behavior is unchanged. **No clarification schema change.**

### Design-judgment wait recording duty

When progress blocks on a design
judgment, opening a judgment-wait record is a duty, not an option. At the
start of that wait, record design as the owner; the record is queryable and
surfaces in `heartbeat` / `stalled-work` instead of living in scrollback:

```bash
intent-cli judgment-wait open --record <design-wait-id> \
  --domain <domain> --team <team> --owner design \
  --blocking-reference <issue|pr|unit|release> \
  --action-needed "<design judgment needed>" --evidence "<facts>" \
  --write --format json
intent-cli judgment-wait query --domain <domain> --team <team> --format json
```

Whoever supplies the judgment **must resolve** that same record with the answer
and evidence:

```bash
intent-cli judgment-wait resolve --record <design-wait-id> \
  --resolution-evidence "<answer and evidence>" --write --format json
```

An answered-but-open record is a lie: the existing lifecycle is not complete
until its answerer resolves it. This records the design-owned wait without
adding a helper or changing the clarification lifecycle above.

**Reviewer hold rule (refined).** Technical checks green and the only pending
item non-semantic and mechanically fact-checkable → resolve it under bounded
default authority, log the verifying facts, and proceed. Anything else →
record the clarification and keep the hold as a **visible pending state**.
There is no third option in which the reviewer simply waits and says so in a
message.

**Bounded default authority.** The operator may pre-delegate a small
enumerated set of decision classes that are settled by *checking repository
facts* rather than by judgment:

| decision class | what verifies it |
| --- | --- |
| count and enumeration corrections | the count is derivable from repository facts both threads can read (e.g. a slice count from the merged PR list) |
| wording corrections that follow from a cited fact | the wording is entailed by a repository fact, and reviewer and orchestrator agree on both the fact and the correction |
| cross-reference and link corrections | the target exists (or does not) as cited — verifiable by reading it |
| identifier and metadata mismatches against a canonical source | the canonical source is named and read; it wins, and the resolution cites it |

It is bounded in every direction: **granted** (never assumed — absent an
operator grant every design decision goes to design as before), **enumerated**
(the table above is the whole MAY scope), **evidence-logged** (a resolution
records what was decided, which facts entail it, and which threads agreed — an
unlogged resolution is a violation, not a resolution), and **amendable**
(design may review the evidence afterwards and reverse it; the authority buys
latency, not finality).

**The evidence sink is `clarify record --from-file`** — the entry lands under
`## Recently Resolved` in the domain's clarification return path
(`intents/<domain>/clarifications/open.md`), where **Question** identifies the
pending item, **Decision** records the decided value, and **Rationale** records
the verified repository facts plus the reviewer/orchestrator agreement. The
entry stays readable there, which is what makes design's post-hoc amendment
possible — and a later amendment adds to the trail rather than erasing what it
amends:

```bash
cat > /tmp/authority-decision.md <<'EOF'
## Question
<the pending item, identified so design can find it later>

## Decision
<the decided value>

## Rationale
<the verified repository facts that entail it, and which threads agreed>
EOF

intent-cli clarify record --domain <domain> --from-file /tmp/authority-decision.md
```

> **Semantic and product decisions are excluded, absolutely.** Intent shaping,
> packet content and acceptance criteria, release scope, and prioritization
> rulings always go to design through the
> [design↔orchestrator double-check rule](#role-boundary--design-authors-orchestrator-coordinates),
> whose scope this contract does not touch. If settling the question requires
> deciding what *should* be true rather than checking what *is* true, this
> authority does not reach it.

**Periodic design-reminder loop.** While a clarification stays open the
**orchestrator** re-sends a reminder to design from its existing long-interval
automation — no new scheduler, receivers stay loopless — at a **30–60 minute
class** interval, **at most one reminder per interval per open clarification**,
**stopping when it is answered**. The design thread runs in the **operator app**
by preference, which makes the reminder land either way: an open session
receives it immediately through its monitor, a closed one finds it in the inbox
on resume. Nothing here requires design to be resident in the team workspace.

**Detection.** `automation stalled-work` reports each open clarification as
`design-decision-pending` with its age, blocking unit, and question summary,
and `automation heartbeat` carries it in `message_body` like any other kind —
see [09-developer-reference.md](09-developer-reference.md). If a hold is real
but the kind is absent, the artifact was never recorded: that is the contract
violation above, not a detector bug.

## Role boundary — design authors, orchestrator coordinates

**Design creates packets; the orchestrator moves ready packets through the
workflow.** The orchestrator must **not** silently become the product/release/
design author.

- **Design owns** — intent shaping and clarifications; ADRs and design
  decisions; release scope and version selection; packet content and acceptance
  criteria (the durable packet files).
- **Orchestrator owns** — inspect canonical intent-cli/GitHub state; coordinate
  exactly **one already-authored, `issue-cut-ready` packet per wake** through
  the recorded host-state route; delegate implementation/review; wait for
  CI/review; route host-state publication, pushes, and approved-PR closeout to
  the non-sandboxed host-state role and verify the result; report blockers and
  missing packets back to design.

**Host-state duty routing (Codex herdr-only).** A herdr-started Codex
orchestrator uses the bounded `--sandbox workspace-write --ask-for-approval
never` recipe. It cannot write `.git` even inside a declared root. The
non-sandboxed host/review runtime or operator seat with the host-repo write
envelope owns `intent-cli issue publish-flow ... --write`,
`intent-cli automation issue-publish --write`, host-state pushes, and
`intent-cli closeout pr ... --write`. Codex routes one bounded request, waits
for the returned JSON, and re-verifies intent-cli/GitHub facts. If that route
is unavailable, fail closed: do not widen the sandbox, write `.git`, ask
design to perform routine workflow transitions, or improvise a clone.

When a needed packet is absent, incomplete, or would require product/release/
design judgment, the orchestrator does **not** invent it — it sends a structured
`packet-needed` message to design and **waits** for design to author/update the
packet (or give an explicit instruction):

```json
{"to":"design","type":"packet-needed","domain":"<domain>","need":"<what is needed>","reason":"<why the orchestrator cannot proceed>","blocking":"<the work that is waiting>"}
```

This is a design-judgment wait, not an inbox-only state: when it starts, the
orchestrator opens the judgment-wait record with `--owner design`, queries
the existing record while waiting, and whoever answers resolves it with
evidence. The complete lifecycle is specified in
[Design-judgment wait recording duty](#design-judgment-wait-recording-duty).

**Release-prep is design-owned:** design decides the release version and scope
and authors the release-prep packet; the orchestrator may publish and coordinate
it only **after** it exists and is `issue-cut-ready` — it must not pick the
version, decide scope, or author the release notes/packet from a vague "prepare a
release" instruction.

## What agmsg is (and is not)

agmsg is a **message / progress / completion / blocker signal layer only**. It
carries natural-language delegation and reply signals between threads.

`intent-cli` and GitHub remain **authoritative** for queue-state, issue/PR
facts, labels, CI, review, closeout, and recovery. A signal is never workflow
state: the orchestrator re-verifies every claim against intent-cli / GitHub
before acting on it. intent-cli never launches Claude/Codex or any AI provider.

## Two driver modes (pick one per domain/repo)

| Mode | Driver | Notes |
|---|---|---|
| **orchestrator-message mode** | a fourth orchestrator thread | **PRIMARY four-thread model.** The practiced, maintained model: when the recorded transport is agmsg + herdr, the orchestrator paces the implementation/review threads over agmsg; at steady state this is message-driven, with a 30-minute-class design-thread watchdog loop as the RECOMMENDED default safety net (an orchestrator-side long-interval automation is the selectable alternative). An explicit 5-minute orchestrator timer remains supported as a fallback/legacy option. |
| **timer-loop mode** | recurring timers | **ALTERNATIVE.** Fully supported, simpler setup for a domain/repo that does not run an orchestrator thread. Implementation/review threads self-schedule and read `worker next-action` / host review-next-slice. No orchestrator required. |

Do **not** run both modes for the same domain/repo. In orchestrator-message
mode, do not also launch the implementation/review recurring timer loops — two
drivers would race on the same GitHub state.

## Scheduled orchestrator cadence

In orchestrator-message mode the normal steady state is **message-driven**:
implementation/review receivers already send accepted/progress/completed/
blocked replies to the orchestrator, and those replies wake the orchestrator
path — routine fast polling is **not** required. An orchestrator timer remains
**supported** but only as an explicit **fallback/legacy** polling option for an
operator who intentionally wants scheduled polling instead of message-driven
wakes. Either way, the implementation and review threads are long-lived but
**loopless receivers** — they act only when the orchestrator delegates and
never start their own recurring timer for the same domain/repo. The
RECOMMENDED default safety net for the message-driven steady state is a
30-minute-class
[design-thread watchdog](#design-thread-watchdog-recommended-safety-net), not
a fast orchestrator loop.

If an explicit fallback/legacy timer is used, schedule the orchestrator one of
two ways:

- **Codex automation (every 5m, optional)** — run one orchestrator wake per
  fire: check design progress and replies, ask intent-cli for state, verify
  the GitHub facts, then send at most one message and exit.
- **Claude same-thread `/loop 5m` (optional)** — in the orchestrator thread run
  `/loop 5m` so the same thread re-wakes every 5 minutes for one pass each.

Do **not** also run `/loop` or a Codex automation in the implementation or
review threads — those are loopless receivers regardless of whether the
orchestrator runs message-driven or on a fallback/legacy timer.

### Each orchestrator wake

Generate the authoritative wake prompt from intent-cli. A wake is triggered
either by an incoming agmsg reply from implementation/review (the
message-driven steady state) or by the optional fallback/legacy timer firing —
either trigger runs exactly one pass:

- Check design-side progress (new packets/issues, intent status changes).
- Read pending agmsg replies (signals only — re-verify against intent-cli /
  GitHub).
- Ask intent-cli for worker state (`worker next-action --github-only`).
- Check host review readiness (`automation host-review-preflight`).
- Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge
  state, closeout/label state.
- Detect stale blockers and no-reply receivers.
- **Route publish + delegate in the SAME wake (G524).** If a ready next-slice
  issue is found this wake, route one bounded publish request to the recorded
  host-state role, verify its result, THEN delegate it to the implementation
  thread within this same wake — never defer the delegation to an unscheduled
  "next wake"; nothing else will ever trigger it (this was the single largest
  measured stall class: ~60 hours across four slices in one field trace). A
  sandboxed Codex orchestrator never performs the write-bearing host-state
  step itself.
- **Per-wake cap is at most one delegation per receiver, not
  at-most-one-message (G524).** A wake may include a publish + its
  same-wake delegation, one repair message per stalled receiver, one
  operator escalation, and handling of any pending receiver reports — all
  together.
- **Verify the recipient roster before dispatch (G524).** Before sending any
  notification, use `intent-cli notify`; it validates the recipient against the
  active transport's role source before delivery and fails closed with
  `unknown-role` rather than guessing (a legacy `review` vs the registered
  `reviewer` silently lost messages before this surface existed).
- **End the wake with a stalled-work check (G523/G524).** Run
  `intent-cli automation stalled-work --domain <domain> --repo <owner/repo>
  --format json` and process every actionable item it reports before
  sleeping — a wake must never end leaving an actionable transition for an
  unscheduled next wake; escalate explicitly if an item is genuinely blocked
  on an operator decision.

### Repair vs escalate

- **Repair** routine off-rail states yourself by messaging the appropriate
  thread back onto the official intent-cli workflow — a receiver that stalled,
  skipped `worker complete`, applied a label by hand, or has not replied.
  Routine recovery is a repair message, not an escalation.
- **Escalate** to the operator only for: product/design judgment, credentials
  or security, a destructive local action, or an unresolved canonical ambiguity
  (intent-cli/GitHub facts genuinely conflict or are missing).

### CI wait state

A PR with pending/running CI is an **active wait state**, not a blocker.
GitHub checks are authoritative. Use the mode's named re-check producer below;
pending CI by itself never triggers a request-update label, a repair message,
or an operator question. Always re-verify required checks immediately before
delegating review, merge, or closeout — an earlier green read can go stale.

- **timer-loop** — the configured recurring timer produces the exact-head CI
  re-check; timer-loop behavior is unchanged.
- **herdr-only** — before yielding on pending CI, explicitly arm an exact-head
  CI-completion watch with `gh pr checks <pr> --repo <owner/repo> --watch`.
  A controller outside intent-cli owns the watch. When it reaches a terminal
  result, wake the recorded orchestration role at the pane ID resolved from the
  team's logical-role mapping; never hard-code a pane ID. intent-cli does not
  launch or manage this background process. The wake only says the wait ended:
  re-read `stalled-work` and exact-head GitHub facts to determine success or
  failure.
- **agmsg orchestrator-message** — an explicitly configured fallback
  orchestrator timer may produce the re-check; without one, arm the same
  exact-head `gh pr checks ... --watch` surface. A receiver report alone does
  not prove that CI completed.

- **pending / running** — wait using the named producer above. No message, no
  request-update, no operator question; track the PR as in-flight and move on.
- **green** — all required checks passed. Delegate review/closeout through
  intent-cli review surfaces; re-verify green at delegation time.
- **red** — a required check failed. Route by ownership: a test/build/lint
  failure the implementation thread can fix gets one repair message; anything
  needing product/design or canonical judgment escalates. Never delegate
  merge/closeout while a required check is red.
- **stuck / ambiguous** — checks never started, hung well past a reasonable
  window, or report conflicting/unknown status. Escalate one operator decision
  (fail closed); do not guess green.

`intent-cli automation stalled-work` reports the same PR as informational
`ci-pending` while any exact-head check is running, as actionable
`ci-all-green-not-transitioned` when all checks are terminal without a failure, or
as actionable `ci-failed-not-transitioned` when any terminal check failed.
Each CI-aware item includes `pr_head_sha`, a pass/fail/skip/pending breakdown,
and a stable kind + PR + head-SHA `dedupe_key`. This inventory is strictly
read-only: it never delegates, relabels, or writes queue-state.

G638 adds a durable, **preview-through-1.x** wait record. When checks finish,
record the exact observed head and the transition that is owed:
`intent-cli automation ci-wait record --domain <d> --repo <owner/repo> --pr <n> --head <sha> --transition <t> --write`.
The record is an obligation for the next message-driven wake, not a polling
loop; `automation ci-wait show` reads it and the canonical `automation
pr-transition` clears it after the transition is applied. G638 originally
named a different current head as actionable `ci-head-moved`; G657 narrows
that case to silence because a new head is advancing-repair evidence. The old
head's green or red checks are never treated as current.

When `notify report` resolves a recorded pane whose role is not running, it
emits an advisory `recipient_warning` naming the role and observed liveness,
then still delivers the report to that pane. The report remains unread until
the sleeping role wakes; liveness is never a refusal reason for a report.

## Next-slice publication

Routine next-slice issue publication is an **orchestrator responsibility**, not
an operator question. The coordination responsibility is to route one bounded
publish request to the recorded host-state role. When intent-cli reports a
candidate as `issue-cut-ready` and all safety gates pass, that role executes the
canonical intent-cli commands from the host repository with its write envelope;
a sandboxed Codex orchestrator never performs the write-bearing step itself.
**At most one issue per wake**, then verify, THEN **delegate that same issue to
implementation in the SAME wake (G524)** — publish and delegate complete
together; never defer the delegation to an unscheduled next wake.

Publish only when **all** of these hold:

- same-domain context, or an explicitly routed multi-domain delegation (never
  publish a cross-domain candidate without explicit routing);
- the packet contract is complete (no missing required sections);
- no open clarification or contract ambiguity;
- dependencies are satisfied — never publish ahead of an uncut dependency;
- under the WIP cap;
- clean host-sync / preflight and an unambiguous target repo/domain.

Otherwise **hold or escalate** — missing contract sections, open clarification,
dependency mismatch, WIP cap reached, host-sync blocker, or ambiguous target
repo/domain are all blockers.

The authorized host-state role executes the canonical surfaces only —
`intent-cli issue publish-flow` and `intent-cli automation issue-publish` —
never raw `gh issue create` or `gh ... --add-label`. After publishing, verify
via intent-cli / GitHub (not chat) that the host-state result is terminal, the
issue exists with the expected body and the `intent-target` label, and durable
state reflects it, then, **in this same wake**, delegate implementation through
`intent-cli notify delegate` (G524) — do not stop after routing the publish to
wait for a future wake. The implementation receiver still derives its target
from `intent-cli worker next-action`, not the notification text.

## End-of-wake check (G523/G524)

Every orchestrator wake ends with a read-only stalled-work check — a wake
must never end leaving an actionable pending transition for an unscheduled
"next wake" that nothing would trigger. This closes the measured
publish-then-sleep and silent-completion stall classes without adding any
timer.

```text
intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --format json
```

- **Never defer** — process every actionable item the check reports in THIS
  wake (delegate, repair, or route to closeout) before sleeping. Do not
  announce work for a future wake unless an explicit fallback/legacy timer
  is actually scheduled to run it; message-driven wakes have no other
  trigger to pick deferred work back up.
- **Escalate instead of defer** — if an item is genuinely blocked on an
  external/operator decision, escalate it explicitly to the design thread
  now via the design-thread escalation filter; do not silently defer it and
  do not leave it unprocessed.

## Dispatch verification (G524)

Send workflow messages only with `intent-cli notify`. It validates every
recipient before delivery: the agmsg adapter checks the team roster and the
herdr-only adapter resolves the logical-role mapping plus running agent/pane.
An unknown role or unavailable receiver returns non-zero with a named cause and
never claims delivery. Fix the role registration/mapping before retrying; never
guess or approximate a role name, and never bypass this check with a handwritten
transport invocation.

Field-observed loss: 8 dispatches addressed to `review` were silently lost
when the registered role was `reviewer` — agmsg neither delivered nor
reported the mismatch.

## Dependency planning

Unmet dependencies are **normal orchestration work** when explicit and
resolvable — not an operator stop. When the next candidate depends on work that
is not yet complete, the orchestrator does **not** pause for operator judgment;
it plans the chain deterministically: hold the dependent candidate and route
this wake's action to the **earliest unmet** same-domain (or explicitly routed)
dependency.

Routing by dependency status:

- **dependency-publish-ready** — the earliest unmet dependency is
  `issue-cut-ready` with no GitHub issue → publish it this wake (one issue per
  wake, under the next-slice publication gates); keep the dependent held.
- **dependency-actionable** — the dependency already has an issue or PR that can
  move → route it (implementation, review, closeout, or repair) using
  intent-cli / GitHub facts.
- **dependency-waiting** — the dependency is in flight (e.g. PR CI pending) →
  wait using the CI wait state's named mode-specific re-check producer; keep
  the dependent held.
- **dependency-ambiguous** — cannot be resolved deterministically (missing
  dependency packet, conflicting GitHub linkage, cross-domain with no route
  mapping) → escalate one operator decision.
- **dependency-cycle** — the dependencies form a cycle → escalate (fail closed).

The dependent candidate stays held until every dependency is completed/cut.
**Escalate only** for: a missing dependency packet, a dependency cycle, a
cross-domain dependency without route mapping, conflicting GitHub linkage,
destructive recovery, credentials/security, or a human product/design judgment.

## Stale-thread health check

Receivers are loopless, so **silence is ambiguous** — a receiver may be working,
waiting for CI, waiting for a permission prompt, blocked, completed without a
reply, or truly stale. When a receiver has had no reply past the threshold
(default **30 minutes**, configurable), the orchestrator runs a **safe**
liveness check: ask before acting, verify authoritative facts, and never
auto-cancel work, auto-clear a permission prompt, or duplicate a task.

Procedure:

1. Send **one non-destructive status-request** — ask, do not retry/cancel/reset.
2. Check read-only intent-cli / GitHub facts (`worker next-action`, issue/PR
   state, CI, labels).
3. If facts show progress (new commits, PR updated, CI running), **keep
   watching** — do not re-send the work.
4. If the receiver replies `waiting-permission`, it is an **operator notice** —
   surface it; never auto-clear the prompt.
5. Only after repeated no-reply **and** no progress, send **at most one
   idempotent re-entry** referencing the same issue/PR.
6. Escalate after repeated silence with no progress, or any unsafe case
   (cancel/reset, destructive git, credentials).

The status-request asks the receiver to reply with one of: `working`,
`waiting-ci`, `waiting-permission`, `blocked`, `completed`, `idle`. A health
check never clears a permission prompt, cancels/resets work, mutates labels, or
runs destructive git. (Timer-loop mode is unaffected — this applies only to
orchestrator-message receivers.)

## Design-thread escalation filter

The **design thread** is the primary human communication surface. Humans mainly
talk to the design thread; implementation and review run through the
orchestrator, and only **human-needed** decisions return to the design thread.
This is a **noise filter, not a failure filter** — never hide a failure that
needs a human.

Kept internal by default (no design-thread message):

- normal progress / accepted / in-flight delegations;
- CI waiting (pending checks are an active wait state); when the exact head
  becomes terminal, the end of the wait is a legitimate orchestration wake
  signal and must be classified as green or failed rather than deduped as the
  pending wait;
- successful implementation (PR opened, CI green);
- successful review / approval;
- closeout of an already-approved PR;
- idle wakes with no actionable change.

Escalate to the design thread only when:

- clarification is required (ambiguous issue/packet contract);
- product intent ambiguity or a design decision;
- permission / credentials / security;
- a destructive action would be required;
- repeated no-reply / no-progress after the safe stale-thread health check;
- unresolved canonical state (intent-cli / GitHub facts conflict or are missing);
- a release / public publish decision;
- an explicit policy decision the operator owns.

A design escalation carries a concise reason, the **current authoritative
state** read from intent-cli/GitHub, the supporting evidence, options only when
useful, and the exact decision needed — so the human can decide without
re-deriving the state:

```json
{"to":"design","type":"escalation","ref":"issue#<n>|pr#<n>","reason":"<clarification|product-ambiguity|permission|destructive|no-progress|canonical-conflict|release|policy>","current_state":"<the current AUTHORITATIVE state read from intent-cli/GitHub: labels, PR/CI/review/merge state, queue position>","evidence":"<the intent-cli/GitHub facts that establish that state>","options":"<OPTIONAL: candidate choices, only when useful>","decision_needed":"<the exact decision or action requested from the human>"}
```

- `reason` — which human-needed category triggered the escalation.
- `current_state` — the current **authoritative** state, read from intent-cli /
  GitHub (labels, PR/CI/review/merge state, queue position). **Required** — the
  receiver must not have to re-derive it; generic evidence wording does not
  substitute for the explicit state.
- `evidence` — the intent-cli / GitHub facts that establish the current state.
- `options` — **optional** candidate choices, included only when they help.
- `decision_needed` — the exact decision or action requested from the human.

## Pre-delegation workspace prerequisites (G655)

> **Preview through 1.x.** This post-v0.12.0 guidance surface is outside the
> 1.0 compatibility promise and may change or be withdrawn during 1.x.

Prerequisites travel with the delegation, not with the receiver's privileges.
Before delegating, the orchestrator identifies every workspace prerequisite the
task needs and never assumes a bounded receiver can create worktrees, checkout
state, or directories outside its recorded write envelope.

1. Identify the required worktrees, checkouts, checkout state, and directories.
2. Compare every prerequisite write with the selected recipe's recorded write
   envelope (its role-derived roots). A path the receiver cannot write is
   orchestrator-owned preparation, not receiver work.
3. Create or repair the prerequisite under orchestrator authority, following
   the existing managed-worktree and safe-cleanup policy where applicable.
4. Verify the prepared cwd, checkout/branch state, managed-worktree
   registration, and required writable directories.
5. Delegate only after verification, carrying the prepared path and state with
   the same logical task.

> **Prepare and resume.** A receiver permission failure is a **routing signal,
> not a retry target**. The orchestrator prepares and verifies the missing
> prerequisite, then resumes the **same PR and same logical task** from the
> prepared path. The receiver's envelope stays bounded, and G630's rule that
> recovery never changes the seat kind unattended remains unchanged.

> **Anti-pattern:** re-delegating the identical failing step that the receiver's
> recorded write envelope cannot perform. Do not loop the failure, widen the
> envelope, mint a replacement PR/task, or switch the seat kind as a workaround.

This is guidance-first orchestration and adds no command. intent-cli does not
create or verify worktrees and executes no git operation; the human/orchestrator
performs preparation with its existing authority. The worktree-metadata failure
and retry loop were reported with transcript by the **remote-herdr team on
2026-08-08**. The Codex write-envelope asymmetry remains separately attributed
to the **MyIntentHost measurement on 2026-08-07**.

## Managed worktree cleanup

Orchestrated work creates temporary workspaces for implementation and review.
A non-sandboxed host-state role registers managed worktrees under the workspace
and cleans them up with `git worktree remove`. A sandboxed Codex seat **must not**
run `git worktree add` because it cannot write `.git` even inside a declared
root. Do not call an arbitrary `/tmp` checkout a managed worktree or delete it
with raw `rm -rf`; when a delegation explicitly supplies a unit-scoped
role-work-root (for example `/private/tmp/<role>-<unit>`), that is an ordinary
temporary checkout and the host-state role owns cleanup. Safe routing, not
disabling approvals, is the right default.

- **Managed root** — the non-sandboxed host-state role allocates registered
  worktrees under the repo/workspace-scoped `[project] worktree_root` (default
  `.intent-cli/worktrees/`), and the exact managed root **must be listed in the
  target repo's `.gitignore` before allocation**. A sandboxed Codex seat receives
  a prepared registered path and never runs `git worktree add` or creates a
  nested clone under that root. The managed root is allowlisted, predictable,
  and removable with `git worktree remove`; it is not an arbitrary
  `/tmp/intent-review-...` path.
- **Approval policy** — `approval_policy=never` / `danger-full-access` is **not**
  a substitute for safe routing or cleanup design. Keep least-privilege
  approvals as the default; route `.git` work to the non-sandboxed host-state
  role and use an explicitly declared ordinary temporary checkout when
  required, rather than suppressing a destructive `rm -rf` prompt.
- **Allocation** — the host-state role adds `.intent-cli/worktrees/` (or the
  configured `[project] worktree_root`) to the target repo `.gitignore`, then
  creates each registered worktree with `git worktree add
  .intent-cli/worktrees/<role>-<unit> <branch>`. The sandboxed Codex seat uses
  the prepared path and does not write `.git` or create a nested clone. A
  sandbox-safe fallback is an explicitly supplied ordinary checkout under the
  declared role-work-root, such as `/private/tmp/<role>-<unit>`; it is not a
  registered worktree.
- **Safe cleanup** — the host-state role removes a registered worktree only
  with `git worktree remove` (it refuses a dirty worktree), never raw `rm -rf`.
  A sandboxed Codex seat does not remove registered worktrees or mutate `.git`;
  it reports cleanup/blocker state to the host-state role. For an explicitly
  delegated ordinary temporary checkout, the host-state role performs approved
  cleanup after verifying the unit path. Validate the target is inside the
  allowlisted root, confirm registration with `git worktree list`, confirm a
  clean state, and run `git worktree prune` after removal.
- **Refuse cleanup** when the target is outside the allowlisted root, is the
  repo root / `$HOME` / a system path, is not a registered worktree, or has
  uncommitted/untracked user work. Also refuse when a sandboxed seat is being
  asked to register a worktree, write `.git`, or create a nested clone — route
  it to the host-state role and never delete user work.

## Review delegation — managed worktrees and design alignment

Review delegation must carry the managed-worktree policy and require
design-alignment evidence **up front** — not leave the reviewer to discover
it. Dogfooding showed a reviewer allocate a raw `/tmp/...review...` worktree
and Codex correctly ask to approve a destructive `rm -rf` — the **right**
safety behavior for the **wrong** workflow. For a sandboxed Codex reviewer, the
host-state role prepares the registered path. The fix is correct routing and a
git-ignored managed root, **not** weakening approval settings.

- **Managed worktree root** — the host-state role prepares each registered
  review worktree under the **same** managed, workspace-local root as the rest
  of orchestrated work — the `[project] worktree_root` (default
  `.intent-cli/worktrees/`), which **must be git-ignored**, for example
  `.intent-cli/worktrees/review-<unit>`. A sandboxed Codex reviewer receives
  that prepared path and never runs `git worktree add`; if the delegation
  instead supplies an ordinary role-work-root checkout such as
  `/private/tmp/review-<unit>`, use it as a temporary checkout, not as a
  registered worktree.
- **Prohibited pattern** — a raw `/tmp/...` path presented as a registered
  review worktree, a `rm -rf /tmp/... && git worktree add ...` cleanup chain, a
  nested clone under `.intent-cli/worktrees/`, or asking a sandboxed Codex seat
  to run `git worktree add`. A declared ordinary role-work-root checkout may
  be used only when the delegation names it and the host-state role owns
  cleanup; an unmanaged path is a signal to stop and route the repair, not to
  ask for `rm -rf` approval.
- **Cleanup rule** — the non-sandboxed host-state role performs cleanup with
  `git worktree remove <managed-path>` for a **registered, clean** worktree
  only (confirm via `git worktree list` and a clean `git status` first). A
  sandboxed Codex reviewer reports completion or a blocker; it does not mutate
  `.git` or delete the path itself.
- **Unsafe/stale path rule** — a stale path that is not a registered git
  worktree, is outside the managed root, or is dirty/unsafe is **never** an
  operator `rm -rf` approval prompt — it is a **structured blocker** reply to
  the orchestrator (`status: blocked`) so the host-state role can prepare or
  clean up the path, not something the reviewer resolves by force-deleting an
  unmanaged path.

Review delegation example (orchestrator → review):

```json
{"delegate":{"domain":"<domain>","execution_unit":"<unit>","target_repo":"<owner/repo>","pr":"<n>","review_cwd":"/review/<domain>","managed_worktree_policy":"host-prepared registered path under git-ignored [project] worktree_root; sandbox-safe ordinary role-work-root fallback only when explicitly supplied; never an arbitrary /tmp review worktree","design_alignment_required":true,"destination_thread":"review@<domain>"}}
```

A review `completed` reply must include design-alignment evidence:

```json
{"status":"completed","thread":"review","ref":"pr#<n>","note":"approved; closeout done","design_alignment_checked":true,"design_alignment_sources_checked":["packet","review-context","intent-tree","adr-decision-notes","relevant-docs"],"managed_worktree_policy":"compliant — .intent-cli/worktrees/review-<unit>, removed after review"}
```

Design-alignment sources a review reply may cite as checked: the **packet**
(content and acceptance criteria), the **review-context** artifact for the
PR/unit, the relevant **intent tree** entries, any linked **ADR / decision
notes**, and **relevant docs** the change touches.

**Review-incomplete rule:** a review `completed` reply that omits
`design_alignment_checked: true` and the checked-source list is
**incomplete** — the orchestrator does not route merge/closeout on that reply
alone. The only exception is when an authoritative **prior** approval state
already proves equivalent design-alignment review (the orchestrator must
point to that specific prior evidence, not assume equivalence).

## Receiver readiness

Monitor configuration is **not enough**. A registered team plus a configured
delivery mode does **not** mean a receiver will see your message — a newly
launched or restarted session may not pick up messages sent before its
monitor/watch path was active. Confirm each receiver is **ready** with a
ping/ack before sending real work.

### Startup order

Follow this order strictly — a send is not a delivery:

1. Join the three roles to the team (`join.sh`).
2. Set the delivery mode for each role (`delivery.sh set`).
3. Launch or restart the receiver CLI sessions (implementation, review, and the
   orchestrator).
4. Wait for the monitor/bridge to attach in each receiver session before
   sending anything.
5. Send a ping to each receiver only **after** its session is active.
6. Require an ack — or confirm receipt manually with `inbox.sh` — before
   proceeding.
7. Only then send the first real delegation.

> **Send-before-ready:** messages sent before a receiver is ready may be stored
> in agmsg history but **not** visibly delivered to a freshly launched/restarted
> session. An unacked message is **receiver-not-ready**, not a successful
> delegation. Recover by resending after the ack, or have the receiver read its
> queue with `inbox.sh`.

Copy-paste operator message when receivers were launched **after** the initial
messages were sent:

```text
Heads up: your session started AFTER I sent earlier messages, so they may be in agmsg history but not visibly delivered to you. Read your queue now with `inbox.sh` to catch anything you missed. Any prior unacked message is receiver-not-ready (NOT a delegation you must act on) — reply `ack` to this ping and I will (re)send the current delegation.
```

Readiness states:

- **registered** — the role joined the team (it appears in `team.sh`).
- **delivery-configured** — the delivery mode is set (`delivery.sh status`).
- **watcher-alive** — the monitor/watch process is running for the role.
- **receiver-session-active** — a launched/restarted receiver session is
  actually attached to the monitor path (a session started before delivery was
  active may not receive earlier messages).
- **ping-acknowledged** — the receiver replied to a ping; the only end-to-end
  proof the channel works.

**Ping/ack is required** for the orchestrator, implementer, and reviewer before
any real delegation, and must be re-done after any launch/restart. A missing ack
is **not-ready** — do not send real work. If a receiver was not ready, messages
sent earlier may have been missed: resend after the ack, or read what is queued
with `inbox.sh`; re-confirm `team.sh` and `delivery.sh status` first.

Boundaries:

- **`watch.sh`** streams a role's inbox live but **occupies a terminal** — it is
  a debug/fallback option, not the default setup requirement. The normal path is
  the monitor delivery hook.
- **Codex Desktop app threads are not agmsg monitor receivers by default** — a
  different execution surface from a CLI session. Use a CLI session as the
  receiver (or read with `inbox.sh`).

Diagnose with agmsg scripts only: `team.sh` (registration), `delivery.sh status`
(delivery), `inbox.sh` (queued messages), `send.sh` (ping → ack).

## Design / human receiver (optional)

When human-needed escalations should be deliverable over agmsg, add a **fourth
logical role**: a **design / human receiver**. Routine progress stays internal
to orchestrator / implementation / review; only human-needed decisions go to the
design thread (see the design-thread escalation filter). The design receiver is
**optional** for routine operation but **recommended** so escalations reach the
human reliably, and it may receive manually by checking its inbox.

Four logical roles when design receiving is enabled:

- **orchestrator** — paces the other roles over agmsg; message-driven by
  default, with an explicit timer only as a fallback/legacy option.
- **implementation receiver** — loopless; acts on delegations only.
- **review receiver** — loopless; acts on delegations only.
- **design / human receiver** — optional; receives only human-needed escalations
  and is also loopless (the human reads on demand, e.g. via `inbox.sh`).

Setup:

- Register the design role in the **same** agmsg team —
  `agmsg join.sh <team> design <agent> <design-folder>` — or simply address
  escalation messages to the existing design thread.
- Optional streamed delivery: `agmsg delivery.sh set <mode> <agent> <design-folder>`;
  otherwise the design thread reads on demand with `inbox.sh`.
- The design receiver needs no recurring loop — like implementation/review it is
  loopless; the human reads when prompted.

Minimal manual inbox trigger prompt (paste into the design thread):

```text
agmsg の inbox を確認してください。あなたは `<team>` の design です。 (Check your agmsg inbox — you are the `design` role of team `<team>`. Read pending escalations with `inbox.sh`; routine progress is intentionally not sent here.)
```

> **Pre-start messages:** messages sent before the design receiver's monitor
> started may be in agmsg history but not visibly delivered — the design thread
> should read its inbox with `inbox.sh` to catch earlier escalations, exactly
> like the other receivers (see Receiver readiness / startup order).

## Setup intake form

When the user asks only "I want to use orchestrator mode", elicit or infer the
setup facts, then produce the concrete commands/messages. Ask for what is
missing; apply the recommended defaults for the rest.

Ask for / infer: domain and target repo; orchestrator cwd + agent type;
implementation receiver cwd + agent type; review receiver cwd + agent type;
design cwd + agent type (and whether design is manual-inbox or monitored);
delivery mode per role.

Recommended defaults when inputs are incomplete:

- orchestrator = operator-chosen herdr-startable kind
- implementer = operator-chosen herdr-startable kind
- reviewer = operator-chosen herdr-startable kind
- design = manual-inbox or monitored, using an operator-chosen herdr-startable kind
- runtime / implementation / review receivers = monitor (when supported)

These are logical-role defaults, not a product pairing. New configuration
defaults are `implementation`, `review`, `interview`, and `clarify`; any
existing explicit role mapping remains valid during this compatible migration.

Design may be a manual-inbox receiver (reads with `inbox.sh` on demand) or a
monitored receiver; either way it receives **only** human-decision escalations or
explicit summaries, never routine progress.

Role startup messages — assume the agmsg role, then paste that role's prompt:

- **Claude**: `/agmsg actas <role>` (slash command)
- **Codex**: `$agmsg actas <role>`

## Design handoff (start / resume)

Setup does not stop at role registration. After the agmsg roles are registered
and ready, the **design thread** starts (or resumes) orchestration by sending
**one** message to the orchestrator; the orchestrator then drives the loop
autonomously and returns to design only for human decisions.

First message — design → orchestrator (paste into the design thread):

```json
{"to":"orchestrator","type":"start","domain":"<domain>","target_repo":"<owner/repo>","requested_action":"<e.g. publish the next ready slice and drive it to a PR>","constraints":"one action per wake; escalate to design ONLY for human decisions (product/clarification, release/credentials/security, destructive actions, unresolved blockers)"}
```

- **Autonomous publish** — if `intent-cli` reports the next slice
  `issue-cut-ready` and all publish gates pass, the orchestrator routes **one**
  bounded publish request to the recorded host-state role. That role performs
  the canonical intent-cli commands (`issue publish-flow` /
  `automation issue-publish`) from the host repository; a sandboxed Codex
  orchestrator does **not** execute the write-bearing step itself. It does not
  ask design to perform routine workflow transitions. At most one issue per
  wake; verify the host-state result before delegating.
- **Escalation boundary** — routine delegation (host-state publish routing,
  delegate, CI wait, review, closeout) stays orchestrator↔receivers/host-state
  role. Return to **design** only for human decisions (product/design
  clarification, release/credentials/security, destructive actions, an
  unresolved blocker) using the structured escalation message.
- **Design inbox workflow** — the design thread is a loopless receiver and reads
  on demand; check the design inbox with `inbox.sh` to pick up escalations,
  especially when monitor delivery did not appear live or the design session
  started after the orchestrator sent.

## Design-thread watchdog (recommended safety net)

In the message-driven steady state, implementation/review replies already
wake the orchestrator, so a fast orchestrator loop is redundant — but
something must still notice a stall the message-driven path itself cannot
self-report. The **RECOMMENDED DEFAULT** safety net (G539, superseding G526's
external-scheduler recommendation) is a watchdog loop run from the **design**
thread at a **30-minute-class** interval: it calls `intent-cli automation
heartbeat` as the one scheduler-agnostic decision surface. Each valid result
has exactly one verdict: `healthy-active-wait`, `actionable-stall`,
`operator-required`, or `cannot-determine`. The external loop owns cadence,
watermark, and dedupe persistence; intent-cli never schedules, sleeps, sends,
or persists poll state. It runs **inside** a live, human-monitored agent session rather
than an invisible external process, needs no separate credential/keychain
setup (it authenticates the same way the rest of the session does), and is
visible on the operator's screen the moment it breaks.

- **Frequency** — 30-minute class (e.g. every 30 minutes): quiet enough to
  stay out of the way, frequent enough to bound a stall far below what the
  field trial measured. A faster watchdog loop recreates the same churn the
  message-driven model removes.
- **Loop setup prompt** (paste into the design thread) — run `/loop 30m`
  (Claude same-thread) or a Codex automation firing every 30 minutes, with a
  prompt that on each wake runs
  `intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --team <team> --format json`.
  Key external dedupe on the returned `dedupe_key` (the stable dedupe key), never age or poll time.
  For `healthy-active-wait`, send nothing: the result names the awaited
  condition, observable end signal, and finite bound; re-evaluate when the
  signal occurs or the bound expires. For `actionable-stall`, run the returned
  canonical `intent-cli notify` command at most once for that key. For
  `operator-required`, surface the named record and action to the operator,
  never nudge orchestration. For `cannot-determine`, visibly repair or escalate
  the named monitor/routing failure; it is never a healthy/silent result. A heartbeat command execution
  failure or malformed/non-object output is **never** silent: state the
  failure explicitly in this wake's own turn output, visible to the operator
  watching this live session — the exact advantage an in-session watchdog
  has over the retired invisible external scheduler (see Retired below) —
  while still never fabricating or sending a notify nudge from broken input;
  only an `actionable-stall` verdict with its returned canonical notify command
  ever produces a sent nudge. Do not use `stale` or `message_body` to infer the
  action; the closed verdict is the sole decision trigger.
- **Failure visibility** — silence is reserved for `healthy-active-wait`
  ONLY. A heartbeat command execution failure or
  malformed/non-object output must be surfaced **visibly** in the watchdog's
  own turn output this wake — never silently swallowed or silently retried,
  since silent failure is exactly the defect this slice retires the external
  OS scheduler for — while still never fabricating or sending a notify nudge
  from broken input; only an `actionable-stall` verdict with its returned
  canonical notify command ever produces a sent nudge.
- **Checks** — the design/HITL inbox for unread human-facing escalations
  (`inbox.sh` on the design role); orchestrator staleness via read-only
  intent-cli/GitHub facts (`worker next-action --github-only`, open PR/CI/
  label state) compared against the last known orchestrator activity; and,
  as the RECOMMENDED primary check, `intent-cli automation heartbeat`
  itself — it wraps `automation stalled-work` (G523), judgment-wait,
  and recorded topology into one verdict, evidence/age basis, stable dedupe
  key, owner, and canonical notify command.
- **Action** — act only on that one verdict: wait for the named signal within
  its bound, run at most one returned canonical notify command for an
  `actionable-stall` key, route `operator-required` to a human, and visibly
  repair/escalate `cannot-determine`. Do not infer an action from any other
  field or send a hand-written transport request.

  ```json
  {"type":"status-request","to":"orchestrator","from":"design-watchdog","ask":"non-destructive liveness check: reply with current state and next action, or confirm idle"}
  ```

- **Stop condition** — stop or archive the watchdog once both the backlog and
  the human-decision (HITL) queues are drained.

Watchdog safety rules **prohibit** (unchanged, verbatim):

- duplicate delegation — the watchdog never re-sends or re-creates a
  delegation itself; only the orchestrator delegates;
- clearing a permission prompt — `waiting-permission` stays an operator
  notice; the watchdog never auto-clears it;
- cancelling or resetting in-flight work;
- force-closing an issue/PR or any other terminal action;
- speculative durable-state surgery — no hand-editing labels, queue-state, or
  any host metadata.

An explicit orchestrator timer (Codex automation every 5m, or Claude
same-thread `/loop 5m`) remains **supported** as fallback/legacy polling when
an operator intentionally wants scheduled polling instead of the
message-driven steady state — measured weakness: this is fast polling the
operator explicitly does not want in steady state, which is exactly why the
design-thread watchdog is the recommended default instead. The design-thread
watchdog (recommended), the orchestrator-side long-interval automation
(alternative, see below), and the 5-minute orchestrator fallback timer
(legacy/discouraged) are alternative safety nets, not all required together.

**Measured weakness** — field trial (2026-06-28..07-14): the design
session — where this watchdog runs — died 8-9 times in 16 days, its monitor
dead until manually restored each time; several stalls were only discovered
when that session happened to restart on its own. This remains a known
limitation to weigh, but G539's field evidence (2026-07-15..07-20) showed the
alternative — a session-independent external OS scheduler — is **strictly
worse**: it failed **silently on every run for five continuous days**
(credential-store access; see Retired below), versus a session that dies
visibly and gets restarted by the operator. A watchdog that occasionally
restarts but is visible when broken is a stronger guarantee than one that
runs invisibly until an operator happens to check its logs.

## Orchestrator-side long-interval automation (alternative safety net)

The **selectable alternative** to the design-thread watchdog: the same
`intent-cli automation heartbeat` call, run directly from a long-interval
automation **in the orchestrator's own thread** (Codex automation or Claude
same-thread `/loop`) rather than from the design thread. On each wake it
calls `automation heartbeat` itself and acts on its closed verdict in the
**same** wake — there is no design-to-orchestrator message hop,
because the orchestrator is the one running the check.

- **Frequency** — 30-60 minute class — the same low-frequency band as the
  recommended design-thread watchdog, never the fast 5-minute fallback timer.
- **Trade-off** — design-side (recommended) keeps the orchestrator strictly
  loopless — it only ever wakes from an inbound agmsg message, matching its
  normal message-driven model, at the cost of one extra hop (design watchdog
  to orchestrator). Orchestrator-side automation removes that hop (the
  orchestrator wakes and acts on its own heartbeat check directly) but
  requires the orchestrator itself to run a recurring loop — exactly the
  pattern orchestrator-message mode is designed to avoid in steady state.
  Choose orchestrator-side only when an operator has a specific reason to
  prefer one fewer hop over keeping the orchestrator loopless.
- **Command** —
  `intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --team <team> --format json`
- **Setup prompt** (paste into the orchestrator thread) — run a Codex
  automation or Claude same-thread `/loop` firing every 30-60 minutes in the
  orchestrator thread; each wake runs `automation heartbeat` in addition to
  the normal orchestrator wake checks. It waits for `healthy-active-wait`,
  runs the returned canonical notify command only for `actionable-stall`,
  routes `operator-required` to the operator, and visibly repairs/escalates
  `cannot-determine` (still at most one nudge per dedupe key, per the G524
  wake contract). It never infers this from `stale` or `message_body`.

### Retired: external OS-scheduler heartbeat (G526 → G539)

**Retired.** The external cron/launchd OS-scheduler recommendation added by
G526 is retired. Reasons:

1. **Credential-store access** — the wrapper's `gh`/agmsg auth commonly lives
   in a login keychain a cron job cannot reach, so it fails at the
   credential step, not the logic.
2. **Invisible failure** — a failed cron run writes to an OS log nobody
   watches, so it is not actually a safety net.
3. **Outside the agmsg model** — intent-cli coordinates through agmsg and
   holds no thread of its own; an OS scheduler sits entirely outside that
   model.

**Field evidence**: every run failed silently from installation
(2026-07-15) through 2026-07-20 — five continuous days — and a 105-minute
stall on 2026-07-20 (G538 / PR #1179) went unrecovered even though
`automation stalled-work` correctly detected it
(`pr-created-not-reviewing, age=105m`); only a human ping surfaced it.

`intent-cli automation heartbeat` itself is **unchanged** and remains
scheduler-agnostic — any scheduler, including cron, can still call it — the
guide simply no longer **recommends** an external OS scheduler as the
mechanism.

## Monitor recovery

- **Monitor did not start** — restart the receiver session so the monitor/watch
  hook attaches on a fresh turn; verify with `delivery.sh status` and a
  ping/ack. Until then, read with `inbox.sh`.
- **Message not visible** — it may be queued but not delivered live; read the
  role's queue with `inbox.sh`, re-confirm `team.sh` / `delivery.sh status`, then
  resend after an ack.
- **Receiver started after the message was sent** — earlier messages are in
  history but not delivered live; read them with `inbox.sh`, or resend after the
  receiver acks.
- **Orchestrator idle despite a packet existing** — confirm the orchestrator
  received the design start/resume message (`inbox.sh`) and that `worker
  next-action` / `intent status` report an actionable item for **this**
  domain/repo (not another visible domain). If issue-cut-ready and safe, route
  one bounded publish request to the recorded host-state role rather than wait;
  a sandboxed Codex orchestrator never performs that write-bearing step itself.
- **`mode=monitor` but no live stream** — `delivery.sh status` `mode=monitor` is
  configuration only, not proof a Claude Code `Monitor` is attached. Verify the
  live-attachment success markers (`1 monitor` / `Monitor event`), check Windows
  Git Bash startup, and work the bounded fallback ladder (restart → verify trust
  → Git Bash on Windows → compare known-good → `turn`/manual `inbox.sh` or
  escalate). Full checklist:
  [Orchestrator-message mode — Monitor tool vs delivery-mode](orchestrator-message-mode.md).
- **`ToolSearch select:Monitor` finds no Monitor tool at all** — this is a Claude
  Code tool-surface problem *before* it is an agmsg problem, regardless of
  `mode=monitor`. Compare `.claude/settings.json` / `.claude/settings.local.json`
  / `~/.claude.json` against a known-good folder and remove suspect project-level
  `env` overrides (e.g. `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true`),
  preserving agmsg hooks, then restart and re-verify. See
  [Missing-Monitor project-settings diagnosis](orchestrator-message-mode.md#missing-monitor-project-settings-diagnosis).

## Codex monitor (beta) failure modes

The agmsg Codex monitor (beta) is a different delivery backend from the Claude
Code `Monitor` tool above — it bridges agmsg into a Codex CLI session instead.
intent-cli does not own or modify agmsg internals; this section only covers
what an operator needs to set up a Codex receiver and recognize/recover from
the two field-verified failure modes. See the
[agmsg codex-monitor-beta doc](https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md)
for implementation internals.

> Observed at agmsg 1.1.6 / Codex v0.144.1 (macOS, `codex()` shim launch) — the
> setup preflight, healthy-state markers, and troubleshooting entries below
> are observations from that tested environment, not a permanent bridge
> contract. Re-verify against the installed agmsg/Codex versions after an
> upgrade before trusting the exact mechanics (e.g. retry interval, thread
> attachment order) described here.

**Setup preflight** — before launching a Codex receiver, verify the (project,
codex) pair resolves to exactly **one** identity: `whoami.sh <project> codex`
should print a single `agent=` line. Clean up any stale registration first
(e.g. a leftover `actas` registering another role into the project) — more
than one identity blocks the bridge launcher silently.

**Healthy-state markers:**

- `delivery.sh status` shows `Codex bridge: <team>/<role> alive (pid N)`.
- The bridge arms on the **first turn** sent to the session, not at Codex
  startup — do not expect delivery before that first turn.
- An already-running Codex session stays unmonitored until it is restarted
  after the bridge is enabled.

**Troubleshooting:**

- **`mode: monitor` but the Codex bridge never starts** — the (project, codex)
  pair resolves to more than one identity; `codex-bridge-launcher.sh` proceeds
  only when there is exactly one, and otherwise retries silently every 0.3s
  forever (e.g. a stale `actas` registration for another role). Check the
  identity count with `whoami.sh <project> codex`, remove the stale
  registration, then relaunch.
- **Bridge alive (pid shown) but the Codex TUI never moves / never reacts to
  messages** — the shared Codex app-server accumulates loaded threads across
  sessions, and `codex-bridge.js` attaches to the FIRST (oldest) entry of
  `thread/loaded/list` — turns are injected into an old background thread
  while the visible TUI never reacts. Recover by: quit the TUI, stop the
  app-server/bridge/launcher processes, remove the recorded app-server/bridge
  state files (`codex-app-server.*.{pid,port,version}` and the bridge
  `{pid,appserver,meta}` files), relaunch codex, then send one turn to
  re-arm.
- **Responses to one message appear twice across a restart window** —
  suspect a doubled bridge; verify only one bridge pid exists
  (`delivery.sh status`) before relaunching.

## Design traffic-controller playbook

The design thread acts as a **traffic controller**, not an implementer. It
coordinates through the orchestrator and only surfaces human-needed items.

1. Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.
2. Check intent-cli / GitHub **read-only** state (`intent status`, `worker
   next-action`, PR/issue/labels) to ground any decision — never trust an agmsg
   message as state.
3. Send the orchestrator a state update or a nudge (start/resume); do not drive
   implementation/review yourself.
4. Do **not** directly mutate implementation/review work, labels, or host
   metadata — that is the orchestrator/receivers' job through intent-cli.
5. Summarize **only** human-needed items to the human; keep routine progress
   internal.
6. If progress is waiting on a design judgment, make that wait durable before
   waiting: open judgment-wait with `--owner design`, query the record, and
   resolve it with evidence when you supply the answer. An answered-but-open
   record is a lie, not a completed design handoff.

**"Orchestrator appears idle" diagnostic** (before escalating): confirm the
orchestrator is scheduled and on a fresh turn; confirm it received your last
message (`inbox.sh`) — a pre-monitor send may be queued, not live, so resend
after an ack; confirm intent-cli actually reports an actionable item for **this**
domain/repo (idle may be correct); only then escalate to the human.

> **Context-only:** the design thread may send context to a receiver thread but
> must mark it `context-only: <text>` unless the orchestrator delegated the
> action — receivers act only on orchestrator delegations, not on design context.

## Preflight (all three cwds)

Before mutating anything, preflight **all three checkouts** (orchestrator,
implementation, review cwds). A receiver acting in the wrong repo, on the wrong
branch, or over dirty user work is the most common orchestration failure.

- For each cwd, confirm `git status` is clean — no uncommitted/untracked work a
  checkout/branch switch would clobber.
- Confirm each cwd's git remote is the **expected repo** for its role (the
  implementation/review receivers must point at the delegated target repo).
- Confirm each cwd is on the **expected branch/base** — a receiver on a stale
  branch implements against the wrong base.
- If a checkout exposes multiple domains, the orchestrator must **filter by the
  requested domain/target repo** before publishing or delegating (visibility is
  not authorization).
- **Existing-loop conflict check** — no timer-loop may be running for this
  domain/repo; orchestrator-message mode and timer-loop mode must not run
  together for the same route.

## Troubleshooting

- **Message not received by a receiver** — confirm registration (`team.sh`) and
  delivery (`delivery.sh status`); the receiver may have missed it (monitor not
  yet active) — read its queue with `inbox.sh`, or resend after a ping/ack.
- **Monitor/delivery configured after the session started** — a session started
  before its monitor/watch path was active will not pick up earlier messages
  live; restart the receiver session (or read with `inbox.sh`), then re-confirm
  with a ping/ack before delegating.
- **Codex Desktop app thread is the receiver** — Codex Desktop app threads are
  not agmsg monitor receivers by default; they receive manually only. Use a CLI
  session, or have the Desktop thread read with `inbox.sh`.
- **Receiver cwd sees a different repo/domain than delegated** — stop; do not
  claim. The receiver's cwd/worktree, git remote, and delegated domain must
  match the routing; reply blocked and re-route. An execution-unit prefix
  mismatch alone is not the signal — compare packet/domain metadata.
- **Codex asks to approve `rm -rf /tmp/...review...`** — this is the **right**
  safety behavior for the **wrong** workflow: the review worktree was
  allocated at an unmanaged `/tmp` path instead of the managed root. The fix
  is the managed root (`.intent-cli/worktrees/review-<unit>`), **not**
  weakening approval settings — re-allocate under the managed root (see
  [Review delegation](#review-delegation--managed-worktrees-and-design-alignment)),
  and do not approve the `rm -rf` for the stale `/tmp` path; reply blocked to
  the orchestrator instead so it can route the cleanup as a repair.

## Draft PR reviewability

A **draft PR may still be reviewable** depending on domain guidance — a reviewer
may perform review feedback on a draft when the domain's review policy allows it.
But the reviewer must use the **canonical intent-cli review surfaces**
(`review closeout-plan`, `guide review`, `automation pr-transition`, `closeout
pr`); merge/approval stays gated by those surfaces. A draft is never
approved/merged by hand or by raw label edits, and never via host-metadata
editing.

## Single-domain vs multi-domain orchestration

A host checkout can legitimately contain **several** intent domains (for
example `sekiban-as-a-service`, `sekiban-wasm-runtime`, and `intent-cli`), and
**more than one domain may target the same GitHub repository**. Visibility is
not authorization. The orchestrator therefore operates in one of two modes.

### Single-domain orchestrator

- Only the selected domain is in scope.
- Other-domain queue items that are **visible** in the same host repo are
  **out of scope** — do not publish, delegate, or repair them, even when they
  target the same repository.
- Escalate to the operator to switch domain/mode instead of treating a visible
  other-domain item as delegable.

### Multi-domain orchestrator

- Intentionally coordinates several domains.
- Requires **explicit routing metadata for each delegation** before
  publishing, delegating, reviewing, or repairing.
- Routes each execution unit only to the thread that owns that domain's
  checkout.

Every multi-domain delegation must carry:

- domain
- execution unit
- target repo
- implementation cwd/worktree
- review cwd/worktree
- base branch policy
- destination thread

Example delegation payload (note one repo serving two domains):

```json
{"delegate":{"domain":"sekiban-as-a-service","execution_unit":"G491","target_repo":"J-Tech-Japan/intent-system","impl_cwd":"/work/sekiban-saas","review_cwd":"/review/sekiban-saas","base_branch_policy":"direct-main","destination_thread":"implementation@sekiban-as-a-service"}}
```

### Execution-unit prefix is not a routing signal

An execution-unit ID prefix that differs from the domain name (e.g. a `G###`
unit whose number does not encode the domain) is **not by itself** a wrong-repo
signal. Compare the **packet/domain metadata** and the **routing context** to
decide ownership — never the prefix string alone.

## Implementation thread: verify your checkout before claiming

The implementation thread is driven by orchestrator delegations, but the
worker target still comes from receiver-side
`intent-cli worker next-action --repo <owner/repo> --github-only` — **not** from
the agmsg text. Before claiming:

1. Verify your local checkout context — cwd/worktree, git remote repo, and the
   delegated domain — matches the routing you were handed.
2. If the checkout does not match the delegated repo/domain, **stop and reply
   blocked** instead of claiming.
3. Remember that a prefix mismatch alone is not a wrong-repo signal; confirm
   ownership via packet/domain metadata and the routing context.

Implementation threads stay **GitHub-contract-only**: they do not read or
mutate host metadata (`.intent-cli/**`, `intents/**`). All label transitions go
through `intent-cli worker` / `intent-cli automation`.

**Reporting completion or blocked status to the orchestrator is a REQUIRED
FINAL STEP of every delegation (G524)** — it is not optional, and the
orchestrator cannot discover a silent completion on its own (a PR opened
with no report reaching the orchestrator is lost work from the
orchestrator's perspective; one field case sat undiscovered for 88 minutes
until a manual GitHub check found it). Send exactly this shape when done:

```json
{"status":"completed","thread":"implementation","ref":"pr#<n>","note":"PR opened, Closes #<n>, CI green"}
```

or the `blocked` shape naming one operator action. The same required-final-step
rule applies to the review thread, whose `completed` reply additionally
carries `design_alignment_checked` and the checked-source list:

```json
{"status":"completed","thread":"review","ref":"pr#<n>","note":"approved; closeout done","design_alignment_checked":true,"design_alignment_sources_checked":["packet","review-context","intent-tree","adr-decision-notes","relevant-docs"]}
```

## Safety boundaries (summary)

- agmsg is a signal layer only; intent-cli and GitHub are authoritative for all
  workflow state.
- No raw label mutation; every transition runs through intent-cli
  worker/automation.
- No hand-editing of queue-state, runs logs, packets, or host metadata.
- agmsg never replaces semantic review or authorizes a merge.
- Domain isolation: visibility is not authorization. Single-domain
  orchestrators ignore/escalate other-domain items; multi-domain orchestrators
  require explicit per-delegation routing.
- Fail closed on duplicate orchestrators or when a signal conflicts with
  intent-cli/GitHub facts — stop and escalate, never guess.
- Per-wake cap is **at most one delegation per receiver** (implementation,
  review) — NOT at-most-one-message: a publish's same-wake delegation, repair
  messages, an escalation, and receiver-report handling may all happen in one
  wake (G524); never defer a publish's delegation to an unscheduled future
  wake.
- Use `intent-cli notify` for every workflow send; it validates the active
  transport's role source and fails closed on unknown or unavailable recipients
  (G524/G578).
- End every wake with a stalled-work check (`automation stalled-work`, G523)
  and process any actionable item before sleeping; escalate explicitly
  rather than deferring silently.

## Guide primacy, standard herdr layout, and dialog authority (G701)

Guide surfaces are the primary interface for both human and AI executors. A
missing, wrong, or stale guide route means the capability is operationally
unshipped, even when the implementation and functional tests are green.
Executing the declared guide route from the built CLI is acceptance substance
equal to functional tests. G645 per-unit reachability records are the
per-unit enforcement; this decision does not change their mechanics.

The installed `guide orchestrator-thread` renders the structured registry
`herdr-standard-layout/v1` for the standard herdr setup:

- exactly one team-named tab;
- `orchestration` on the left;
- `implementation` above `review` in the right column; and
- pane labels `orchestration`, `implementation`, and `review`.

The registry renders the exact creation commands and the measured repair
shape `herdr pane move --tab --split right|down --target-pane`, followed by
`herdr pane rename`. Its named `layout-and-labels` check reports visible
incompleteness, is read-only, never executes herdr, and never hard-blocks
READY. The operator supplies and verifies explicit ids.

Setup and `guide design-thread` render `dialog-answering/v1` with exactly
three tiers:

1. a gate over state the provisioner created is answered by the provisioner;
2. an action already approved by the human in conversation is mechanically
   answered by design through the session layer only after exact dialog/action
   match, with the human as decision actor and the conversation approval as
   recorded grounds; a per-action approval never generalizes to a class; and
3. an unapproved, unknown-origin, uncertain, or mismatching dialog escalates
   through design to the human with grounds.

G690 is distinct: its hard risk floor bounds what design may decide alone; it
does not block execution of a human decision already recorded in conversation.
The guide remains observation-only and preserves the no-provider and
no-terminal-mutation boundaries.

## Terminal observation and keystroke boundary (G706)

`guide design-thread` makes the terminal boundary explicit. Terminal pane
reading is permitted only for **operational liveness diagnosis** — determining
whether a seat is alive or responding after an explicit operator or authorized
orchestration diagnostic request. Terminal content is never parsed, promoted,
or cited as **canonical workflow evidence**; canonical evidence remains
intent-cli/GitHub state, recorded activity, and real artifacts. A liveness
observation never transfers detection, classification, or authorized recovery
ownership from orchestration to design.

If orchestration cannot read panes, use the existing recorded observation
route:

```text
intent-cli notify status --task-id <task-id> --domain <domain> --team <team> --routing-root <host-root> --format json
```

Then use the configured non-destructive `status-request`/canonical report
route as applicable. Treat the returned liveness as observation only and
escalate unresolved silence; do not infer workflow state or recovery ownership
from it.

Keystrokes follow the G701 `dialog-answering/v1` three-tier boundary, not a
generic design relay: the provisioner answers self-provisioned gates; design
may mechanically answer only an exact dialog/action match already approved by
the human through the session layer, with the human as decision actor and no
per-action class generalization; every unapproved, unknown-origin, uncertain,
or mismatching dialog goes through design to the human with grounds.
