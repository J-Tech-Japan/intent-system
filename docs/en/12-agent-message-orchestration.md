# Agent-message orchestration (single-domain vs multi-domain)

← [Review standing policy](11-review-standing-policy.md) | [docs index](README.md)

This page describes the **primary** agmsg-backed four-thread orchestrator
model (design / orchestrator / implementation / review) and, in particular,
how it stays safe when a single host repository holds **several intent
domains**. The authoritative, paste-ready prompts come from installed
intent-cli guidance — do not copy prompts from this page by hand. Generate the
current prompts with:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --mode single-domain|multi-domain --format markdown
```

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

`notify delegate` embeds the task id, expected artifacts, fresh marker nonce,
and complete canonical report command (including the transport-neutral
`--routing-root` needed from an isolated child checkout) in the delivered task. Running that
report command is the receiver's required final step after all other work, so a
herdr-only completion actively wakes the orchestration role instead of merely
printing into the receiver pane. Unknown roles and delivery failures fail
closed with a named cause. `notify escalate` appends the unchanged six-field
event schema; none of these commands merges, labels, publishes, or mutates queue
state. Direct transport commands remain provisioning/readiness diagnostics,
not workflow send instructions.

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
   mode.
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

## Herdr-only (PREVIEW) operating procedure

This section is operative only when the team has recorded `herdr-only`. It is
the concrete counterpart to the agmsg provisioning/receiver sections. PREVIEW
qualifies the transport, not the four-thread model. Exactly one transport runs
per team; mixed agmsg and herdr delivery is a contract violation.

### Provision and prove READY

Use this topology literally: **one workspace per team, one tab named after the
team, one pane per role, each pane opened with that role's folder as its cwd.**
This keeps all roles visible to the operator at once and keeps the G550
supervision pane scan from being hidden behind an inactive tab.

Create the workspace first:

```text
herdr workspace create --cwd <host-repo> --label <team> --no-focus
```

In herdr 0.7.5, the `workspace_created` result has top-level `workspace`, `tab`,
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

Launch with the typed surface:

```text
herdr agent start <logical-role> --kind <agent-kind> --pane <pane-id> -- <operator-approved-permission-flags>
```

Permission flags belong to the launch, not injected modifier chords. Approvals
are never auto-answered; G550's MAY/escalate boundary still governs. Approvals
are pane-visible and handled at the supervision boundary, explicitly unlike the
agmsg Codex bridge's headless auto-decline. A role is READY only after the
self-contained G556 verified-liveness gate: after the startup report, wait a
settle delay, then re-check the expected cwd/repo and agent kind, same-pane
detection, and a bounded probe whose response is observed. Repeat the entire
settle-and-re-check sequence after re-provisioning. Workspace existence, a shell
prompt, or agent state alone is not READY. In herdr-only the verified
logical-role→pane mapping is the role identity; there is no separate agmsg
identity step.

### Dispatch, wait, and verify the artifact

Use the [canonical notify workflow](#canonical-notify-workflow): run
`intent-cli notify delegate ...` with the target logical role. The CLI resolves
herdr-only internally, validates the role mapping, and generates the structured
task block; do not hand-write `herdr agent prompt`.

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

The measured herdr 0.7.5 socket API uses `events.subscribe`. Include one
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

Resolve the host root at runtime and use
`<host-repo>/.intent-cli/events/<team>.jsonl`. `<team>` is the agmsg/herdr team
name verbatim in one flat filename (`intent-cli-dev.jsonl`, for example); no
team subdirectories and no hard-coded absolute paths. Before constructing the
path, fail closed on an empty name, a leading dot, `/` or `\`, or any `..`
sequence. Never sanitize an invalid name.

The orchestrator is the only writer. Open with `O_APPEND`, append one complete
JSON object per line, permit no embedded newline, and normalize `summary` to one
line. The required schema is:

```json
{"timestamp":"<RFC3339>","team":"<team>","kind":"completion|blocked|question|escalation","unit":"<execution-unit-or-task-id>","summary":"<one-line-summary>","artifact":"<repo-relative-path-or-URL>"}
```

Write only design-relevant completion, blocked, question, and escalation
events. This mode-independent channel is the design boundary only—never an
inter-agent bus and never a replacement for `intent-cli notify`, GitHub, or
intent-cli workflow state.

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

- Modifier-chord launch corruption: return to a shell/re-provision and use the
  typed `agent start ... -- <permission-flags>` surface.
- Post-reboot dead pty wiring: an undetected agent or shell-only pane requires
  preserving artifacts, re-provisioning, rebuilding the mapping, and repeating
  the self-contained settle-and-re-check READY gate above.
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
2. **Command approvals verified read-only** — the exact command shown must be
   read and verified read-only; anything that writes, deletes, installs,
   publishes, or mutates escalates ("probably read-only" is not verified).
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
- **Orchestrator owns** — inspect canonical intent-cli/GitHub state; publish
  exactly **one already-authored, `issue-cut-ready` packet per wake**; delegate
  implementation/review; wait for CI/review; close out approved PRs through the
  canonical review surfaces; report blockers and missing packets back to design.

When a needed packet is absent, incomplete, or would require product/release/
design judgment, the orchestrator does **not** invent it — it sends a structured
`packet-needed` message to design and **waits** for design to author/update the
packet (or give an explicit instruction):

```json
{"to":"design","type":"packet-needed","domain":"<domain>","need":"<what is needed>","reason":"<why the orchestrator cannot proceed>","blocking":"<the work that is waiting>"}
```

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
| **orchestrator-message mode** | a fourth orchestrator thread | **PRIMARY.** The practiced, maintained model: the orchestrator paces the implementation/review threads over agmsg; at steady state this is message-driven, with a 30-minute-class design-thread watchdog loop as the RECOMMENDED default safety net (an orchestrator-side long-interval automation is the selectable alternative). An explicit 5-minute orchestrator timer remains supported as a fallback/legacy option. |
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
- **Publish + delegate in the SAME wake (G524).** If you publish a ready
  next-slice issue this wake, verify it exists, THEN delegate it to the
  implementation thread within this same wake — never defer the delegation
  to an unscheduled "next wake"; nothing else will ever trigger it (this was
  the single largest measured stall class: ~60 hours across four slices in
  one field trace).
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
GitHub checks are authoritative. Re-check the required checks on each wake;
pending CI by itself never triggers a request-update label, a repair message,
or an operator question. Always re-verify required checks immediately before
delegating review, merge, or closeout — an earlier green read can go stale.

- **pending / running** — wait and re-check next wake. No message, no
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

## Next-slice publication

Routine next-slice issue publication is an **orchestrator responsibility**, not
an operator question. When intent-cli reports a candidate as `issue-cut-ready`
and all safety gates pass, the orchestrator publishes it itself through
canonical intent-cli commands rather than stopping to ask the operator to create
the GitHub issue. **At most one issue per wake**, then verify, THEN **delegate
that same issue to implementation in the SAME wake (G524)** — publish and
delegate complete together; never defer the delegation to an unscheduled
next wake.

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

Publish through the canonical surfaces only — `intent-cli issue publish-flow`
and `intent-cli automation issue-publish` — never raw `gh issue create` or
`gh ... --add-label`. After publishing, verify via intent-cli / GitHub (not
chat) that the issue exists with the expected body and the `intent-target`
label and that durable state reflects it, then, **in this same wake**,
delegate implementation through `intent-cli notify delegate` (G524) — do not stop after publishing to
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
  wait and re-check next wake; keep the dependent held.
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
- CI waiting (pending checks are an active wait state);
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

## Managed worktree cleanup

Orchestrated work creates temporary worktrees for implementation and review.
Allocate them under a **managed, allowlisted root** inside the workspace and
clean them up with `git worktree remove` — **never** a raw `rm -rf` of an
arbitrary `/tmp/intent-review-...` path. Safe cleanup design, not disabling
approvals, is the right default: a destructive `rm -rf` approval prompt is the
symptom of an unmanaged workspace.

- **Managed root** — allocate under the `[project] worktree_root` (default
  `.intent-cli/worktrees/`, git-ignored), not arbitrary `/tmp` paths. Create
  each with `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`,
  one per role/unit.
- **Safe cleanup** — remove only with `git worktree remove` (it refuses a dirty
  worktree); validate the target is inside the allowlisted root, is a registered
  git worktree (`git worktree list`), and is clean; then `git worktree prune`.
- **Refuse cleanup** when the target is outside the allowlisted root, is the
  repo root / `$HOME` / a system path, is not a registered worktree, or has
  uncommitted/untracked user work — stop and surface it; never delete user work.
- **Approval policy** — `approval_policy=never` / `danger-full-access` is **not**
  a substitute for safe cleanup design. Keep least-privilege approvals as the
  default; the goal is to never need a destructive `rm -rf` prompt, not to
  suppress it.

## Review delegation — managed worktrees and design alignment

Review delegation must carry the managed-worktree policy and require
design-alignment evidence **up front** — not leave the reviewer to discover
it. Dogfooding showed a reviewer allocate a raw `/tmp/...review...` worktree
and Codex correctly ask to approve a destructive `rm -rf` — the **right**
safety behavior for the **wrong** workflow. The fix is a managed root, **not**
weakening approval settings.

- **Managed worktree root** — review worktrees use the **same** managed,
  workspace-local root as the rest of orchestrated work — the `[project]
  worktree_root` (default `.intent-cli/worktrees/`), e.g.
  `.intent-cli/worktrees/review-<unit>` — **never** an arbitrary
  `/tmp/...review...` path.
- **Prohibited pattern** — a raw `/tmp/...` review worktree, and a
  `rm -rf /tmp/... && git worktree add ...` cleanup chain, are **prohibited**
  as the normal path. Reaching for this pattern is the signal to stop and
  allocate under the managed root instead — not to ask the operator to
  approve the `rm -rf`.
- **Cleanup rule** — cleanup is `git worktree remove <managed-path>` for a
  **registered, clean** worktree only (confirm via `git worktree list` and a
  clean `git status` first).
- **Unsafe/stale path rule** — a stale path that is not a registered git
  worktree, is outside the managed root, or is dirty/unsafe is **never** an
  operator `rm -rf` approval prompt — it is a **structured blocker** agmsg
  reply to the orchestrator (`status: blocked`) so the orchestrator can route
  the repair, not something the reviewer resolves by force-deleting an
  unmanaged path.

Review delegation example (orchestrator → review):

```json
{"delegate":{"domain":"<domain>","execution_unit":"<unit>","target_repo":"<owner/repo>","pr":"<n>","review_cwd":"/review/<domain>","managed_worktree_policy":"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never /tmp","design_alignment_required":true,"destination_thread":"review@<domain>"}}
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

- orchestrator = Claude
- implementer = Claude
- reviewer = Codex
- design = manual-inbox Codex
- runtime / implementation / review receivers = monitor (when supported)

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
  `issue-cut-ready` and all publish gates pass, the orchestrator creates/
  publishes **one** GitHub issue itself via canonical intent-cli commands
  (`issue publish-flow` / `automation issue-publish`) — it does **not** ask
  design to do each step. At most one issue per wake; verify before delegating.
- **Escalation boundary** — routine delegation (publish, delegate, CI wait,
  review, closeout) stays orchestrator↔receivers. Return to **design** only for
  human decisions (product/design clarification, release/credentials/security,
  destructive actions, an unresolved blocker) using the structured escalation
  message.
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
heartbeat` and, when `stale=true`, sends **at most one** canonical nudge to
the orchestrator using the returned `message_body` — completely silent
otherwise. It runs **inside** a live, human-monitored agent session rather
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
  `intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json`;
  when the result's `stale` field is `true`, send its `message_body` to the
  orchestrator with exactly **one** `intent-cli notify report` call (`--from
  design --to orchestrator --status question`, a fresh heartbeat task id, and
  the heartbeat evidence artifact);
  when `stale` is `false`, send nothing and exit quietly — silence is
  reserved for this healthy case **only**. A heartbeat command execution
  failure or malformed/non-object output is **never** silent: state the
  failure explicitly in this wake's own turn output, visible to the operator
  watching this live session — the exact advantage an in-session watchdog
  has over the retired invisible external scheduler (see Retired below) —
  while still never fabricating or sending a notify nudge from broken input;
  only a genuine `stale=true` result ever produces a sent message.
- **Failure visibility** — silence is reserved for a healthy `stale=false`
  heartbeat result ONLY. A heartbeat command execution failure or
  malformed/non-object output must be surfaced **visibly** in the watchdog's
  own turn output this wake — never silently swallowed or silently retried,
  since silent failure is exactly the defect this slice retires the external
  OS scheduler for — while still never fabricating or sending a notify nudge
  from broken input; only a genuine `stale=true` result ever produces a sent
  message.
- **Checks** — the design/HITL inbox for unread human-facing escalations
  (`inbox.sh` on the design role); orchestrator staleness via read-only
  intent-cli/GitHub facts (`worker next-action --github-only`, open PR/CI/
  label state) compared against the last known orchestrator activity; and,
  as the RECOMMENDED primary check, `intent-cli automation heartbeat`
  itself — it wraps `automation stalled-work` (G523) and returns a
  ready-to-send `message_body` naming every stale item and its canonical
  next command.
- **Action** — when staleness, an unanswered HITL message, or a heartbeat
  `stale=true` result is detected, send **at most one** canonical
  repair/status request or heartbeat nudge to the orchestrator:

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
calls `automation heartbeat` itself and, when stale, acts on the returned
state in the **same** wake — there is no design-to-orchestrator message hop,
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
  `intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json`
- **Setup prompt** (paste into the orchestrator thread) — run a Codex
  automation or Claude same-thread `/loop` firing every 30-60 minutes in the
  orchestrator thread; each wake runs `automation heartbeat` in addition to
  the normal orchestrator wake checks, and when `stale` is `true` treats the
  returned `message_body` as this wake's repair/escalation signal (still at
  most one message per wake, per the G524 wake contract).

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
  domain/repo (not another visible domain). If issue-cut-ready and safe, the
  orchestrator should publish one issue itself rather than wait.
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
