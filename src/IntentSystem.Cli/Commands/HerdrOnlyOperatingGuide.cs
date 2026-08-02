using System.Text.Json.Nodes;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G571/G581: the concrete operating procedure selected by the G570
/// session-layer router. This content is synthetic rather than part of the
/// agmsg-backed guide model so the practiced agmsg rendering remains
/// byte-for-byte on its existing path.
/// </summary>
internal static class HerdrOnlyOperatingGuide
{
    public const string JsonProperty = "herdr_only_operations";

    public static readonly IReadOnlyList<string> Headings =
    [
        "## Herdr-only provisioning and READY gate",
        "## Herdr-only dispatch and artifact handoff",
        "## Herdr-only wake sources",
        "## Herdr-only bounded waiting and success detection",
        "## events.jsonl design boundary",
        "## Herdr-only failure modes and recovery",
    ];

    public static string RenderMarkdown(IReadOnlyList<string> replacedHeadings)
    {
        var replaced = string.Join('\n', replacedHeadings.Select(heading => $"- `{heading.TrimStart('#', ' ')}`"));
        return $$"""
        {{SessionLayerSections.ReplacementHeading}}

        This team runs the **herdr-only** session layer. The agmsg-only sections listed below do not apply and are not rendered; the concrete **G571** herdr-only procedures follow immediately after the list.

        Replaced here (agmsg-only):

        {{replaced}}

        Everything else in this document is mode-independent and applies unchanged: G550 supervision and approval boundaries, G555 isolation, G556 liveness, the wake contract, publish authority, the design↔orchestrator double-check rule, dependency planning, and escalation are properties of the four-thread model, not of its transport.

        ## Herdr-only provisioning and READY gate

        herdr is the only session transport in this mode; do not join agmsg identities, run an agmsg bridge, or mix delivery mechanisms inside the team.

        1. Confirm the installed surface with `herdr workspace --help`, `herdr tab --help`, `herdr pane --help`, and `herdr agent --help`. Use the installed help when a version-specific option is needed.
        2. Use this topology literally: One workspace per team, one tab named after the team, one pane per role, each pane opened with that role's folder as its cwd. This keeps all roles visible to the operator at once and keeps the G550 supervision pane scan from being hidden behind an inactive tab. Create the workspace with `herdr workspace create --cwd <host-repo> --label <team> --no-focus`. In herdr 0.7.5 its `workspace_created` result has top-level `workspace`, `tab`, and `root_pane`; seed the mapping from `workspace.workspace_id`, `tab.tab_id`, and `root_pane.pane_id`, verify `root_pane.cwd`, and ensure that returned tab is the one team-named tab (if needed, use its explicit id with `herdr tab rename <tab-id> <team>`). Assign the root pane to one host-repo role, then make pane creation the DEFAULT for every remaining herdr-resident role: resolve a non-empty mapped pane id and run `herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus`, with design and orchestrator on `<host-repo>`, implementation on `<implementation-repo>`, and review on its isolated review cwd/worktree. Update the mapping from every pane creation result. `herdr tab create --workspace <workspace-id> --cwd <role-cwd> --label <logical-role> --no-focus` is NOT the primary path; use it only when the operator explicitly authorizes a separate role tab for a documented reason such as requiring tab-level lifecycle isolation instead of simultaneous visibility.
        3. Record a durable operator-visible mapping of each herdr-resident logical role (`design`, `orchestrator`, `implementation`, `review`) to its current workspace/tab/pane id and cwd. Workflows address the logical role and NEVER hard-code pane/workspace ids. After the initial workspace creation returns the first ids, every provisioning or mutation command MUST resolve its explicit non-empty pane/workspace target id from this recorded mapping immediately before execution and carry that id on the command. If resolution is missing or empty, fail closed and DO NOT run the command: herdr can otherwise apply a focus-default and mutate the currently focused pane in another team. The existing G555 cross-project attribution rules remain authoritative and unchanged; reference them rather than inventing a second attribution policy. A design frontend outside herdr is recorded as the design reader type, not fabricated as a pane.
        4. Launch the typed agent in the mapped pane with `herdr agent start <logical-role> --kind <claude|codex|...> --pane <pane-id> -- <operator-approved-permission-flags>`. Pass launch and permission flags after `--`; do not inject modifier chords into an interactive prompt. Approvals are NEVER auto-answered: only the G550 MAY-answer classes may be handled, and every other approval is escalated to the operator.
        5. READY is a G556 verified-liveness result, not workspace existence. Approvals surface visibly in the pane and are handled at the supervision boundary, explicitly unlike the agmsg Codex bridge's headless auto-decline. After the startup report, wait a settle delay, then re-check `herdr agent list`, inspect the mapped pane/agent, verify the expected cwd/repository and detected agent kind, and send a bounded ping whose ack is observed in that same pane. An undetected agent, a shell prompt where the agent should be, a mismatched cwd, or no ack is NOT READY; after re-provisioning, repeat this entire settle-and-re-check sequence before declaring READY.

        Role identity in herdr-only is this verified logical-role→pane mapping. There is no agmsg identity or separate role-switching step.

        ## Herdr-only dispatch and artifact handoff

        Submit through the transport-neutral notify surface. The CLI resolves the recorded `herdr-only` mode, validates the logical-role mapping, and invokes the herdr adapter internally; workflow prompts never hand-write `herdr agent prompt`:

        ```bash
        intent-cli notify delegate --domain <domain> --team <team> --from <sender-role> \
          --to <logical-role> --report-to <orchestrator-role> --task-id <task-id> \
          --objective <one-bounded-outcome> --input <canonical-issue-or-reference> \
          --expected-artifact <inspectable-artifact> --result-nonce <fresh-per-dispatch-nonce> \
          --write --format json
        ```

        The command generates and delivers a task block containing these echo-safe marker fields (they are output data, not a hand-written transport invocation):

        ```text
        TASK <task-id>
        result-prefix: ORCH_RESULT
        result-nonce: <fresh-per-dispatch-nonce>
        completion-marker: Concatenate result-prefix, one space, result-nonce, one space, status, one space, and artifact; use completed, blocked, or question. Never include the precomposed wait needle.
        ```

        `notify delegate` generates the structured task block, keeps the result prefix and fresh unpredictable nonce separate, and embeds task id, expected artifact, plus the complete canonical `intent-cli notify report` command as the receiver's required final step. That command carries the transport-neutral `--routing-root`, so an isolated child checkout still resolves the recorded mode internally without learning a transport-specific send instruction. The receiver reports `completed`, `blocked`, or `question` only through that embedded command; it never hand-writes a transport call. Never reuse a nonce or substitute the task id alone. `pane wait-output` searches existing pane output immediately, so a precomposed wait needle in the dispatched task can be echoed and falsely match before the agent does any work. Files, commits, PRs, test logs, and other inspectable artifacts are the handoff; terminal prose is only a signal pointing at them. A repair or re-dispatch goes to the same logical role through `intent-cli notify delegate` and carries the same task id plus the concrete delta. A marker match from any pane buffer is NEVER sufficient: the named artifact must exist and pass its verification.

        ## Herdr-only wake sources

        Herdr-only has two normative wake sources. Neither replaces the composite success gate below:

        1. **Canonical notify report (primary and most informative):** the receiver's embedded `intent-cli notify report` carries task id, status, artifact, and summary directly to orchestration, but it depends on the worker cooperating and running its required final command.
        2. **Observed agent state change (normative SECOND wake source):** independently subscribe to herdr `pane.agent_status_changed` for every watched role. This depends only on herdr observing the agent process, so it still wakes orchestration when a worker omits its report; the event carries no task outcome.

        For the measured herdr 0.7.5 socket API, call `events.subscribe` with one subscription entry per watched pane because `pane.agent_status_changed` requires `pane_id`:

        ```json
        {"method":"events.subscribe","params":{"subscriptions":[{"type":"pane.agent_status_changed","pane_id":"<resolved-pane-id>"}]} }
        ```

        Resolve each `<resolved-pane-id>` from the recorded logical-role→pane mapping at subscription time and after any re-provisioning; NEVER hard-code pane ids. The subscription event frame carries the observed `agent`, `agent_status`, `pane_id`, and `workspace_id`:

        ```json
        {"event":"pane.agent_status_changed","data":{"agent":"<agent>","agent_status":"<working|idle|done|blocked|unknown>","pane_id":"<resolved-pane-id>","workspace_id":"<workspace-id>"} }
        ```

        Track the previous status independently for each logical role. Schedule a wake only when that role transitions from `working` to a settled state (`idle`, `done`, or `blocked`); an initial settled observation, `unknown`, or a settled→settled change does not wake orchestration. Apply a settle delay before the wake, and keep per-role dedupe state so a burst produces only one wake for that role's observed working→settled transition. A newly observed `working` state re-arms that role.

        **A state change means only that something happened, never that a task succeeded.** After EVERY wake from either source, orchestration checks current herdr state and pending approval/question, the exact fresh-nonce completion marker and status, the named verified artifact, and fresh canonical intent-cli/GitHub facts before advancing or concluding the task. A settled pane may still be paused for approval or a question.

        The failure modes are complementary: notify report is the richest signal but depends on worker cooperation; state change depends only on herdr observation but carries no outcome. The periodic `intent-cli automation stalled-work ...` check remains the last net when neither immediate source produces a usable wake. This herdr 0.7.5 shape does not replace the standing rule: consult the installed herdr help/schema for version-specific details before operating it.

        ## Herdr-only bounded waiting and success detection

        Use bounded waits only. For agent state, use `herdr agent wait <logical-role> --until idle --until done --until blocked --timeout <milliseconds>`. For the task signal, use `herdr pane wait-output --match "ORCH_RESULT <fresh-per-dispatch-nonce>" --source recent-unwrapped --timeout <milliseconds> <pane-id>`. Both commands wait indefinitely when `--timeout` is omitted, so omission is forbidden in an orchestrator wake.

        After EVERY wait return—including `idle`, `done`, `blocked`, marker match, and timeout—run `herdr pane read --source recent-unwrapped <pane-id>` and inspect the pane for a pending approval or question before interpreting the result. `idle` can mean approval-paused, not settled. Classify the return as settled, approval/question-paused, or timeout. For an approval/question pause, answer only a G550 MAY class after reading it; escalate every other case, then re-enter the wake and wait again. Never conclude the task while the pane is paused.

        A timeout is a re-entry point: persist progress, return control to the orchestrator, and wait again on the next bounded wake. Prefer a deterministic script that persists its cursor for long multi-wait flows; do not hold one chat turn open indefinitely, because turn death loses the continuation.

        **Composite success is mandatory:** (1) herdr reports a settled state and pane inspection finds no pending approval/question, (2) the exact `ORCH_RESULT` marker matches the fresh dispatch nonce and status, (3) the named artifact exists and passes the task's verification, and (4) fresh canonical intent-cli/GitHub facts confirm the workflow state. The named artifact verification is the final gate only together with fresh canonical facts. State alone and marker alone NEVER mean task success; in particular, herdr `done` or `idle` alone is insufficient, and a marker found anywhere in the pane buffer is only a signal. `blocked` and `question` markers route back to the orchestrator/design decision boundary; they are not failures to hide or successes to assume.

        ## events.jsonl design boundary

        This is a normative, mode-independent design-boundary channel documented here because herdr-only has no separate message bridge. It is NEVER an inter-agent bus and never replaces `intent-cli notify delegate` / `report`, GitHub, or intent-cli workflow state.

        - **Location:** resolve the host repository root at runtime, then use `<host-repo>/.intent-cli/events/<team>.jsonl`. The team name is the agmsg/herdr team name verbatim in one flat filename (example: `intent-cli-dev.jsonl`); there are no team subdirectories and readers never hard-code an absolute path.
        - **Fail closed before path construction:** reject an empty team name, a leading dot, `/` or `\`, and any `..` sequence. Do not sanitize or silently rewrite an invalid name.
        - **Append contract:** the orchestrator is the only writer. Open with append semantics (`O_APPEND`), append exactly one complete JSON object per line, include no embedded newline, and normalize `summary` to one line.
        - **Schema:** `{"timestamp":"<RFC3339>","team":"<team>","kind":"completion|blocked|question|escalation","unit":"<execution-unit-or-task-id>","summary":"<one-line-summary>","artifact":"<repo-relative-path-or-URL>"}`. These six fields are required; `artifact` identifies the inspectable handoff or the decision input.
        - **Writer boundary:** append only design-relevant completion, blocked, question, and escalation events. Routine progress, dispatch traffic, pane output, acknowledgements, and workflow label changes do not belong here.

        Reader recipes:

        Every reader persists a durable watermark across watcher restarts containing the file identity, byte offset, and complete-line count. Before each read it verifies the same file identity and that neither the byte nor line count moved backwards. Rotation, truncation, a backwards count, or file replacement fails closed for operator recovery; a reader NEVER silently resets to the beginning because replay can duplicate a design decision.

        1. **Claude app watcher:** resolve the host root and validated team path, then tail complete lines after its durable file-identity/byte-offset/complete-line-count watermark; surface only unseen design-relevant records and advance the watermark only after successful handling. The watermark survives watcher restarts; rotation, truncation, a backwards byte/line count, or file replacement fails closed and never resumes at the beginning.
        2. **Codex CLI:** when Codex is a herdr pane, address that logical role through `intent-cli notify delegate` / `report`; it does not poll the file for ordinary coordination. If that Codex role acts as a design-boundary reader, it uses the same durable file-identity/byte-offset/complete-line-count watermark across restarts and fails closed on rotation, truncation, a backwards count, or file replacement—never a reset to the beginning.
        3. **Codex Desktop:** run a one-minute-class timer poll. Resolve the host root plus validated team filename on every resumed session, read only complete lines after its durable file-identity/byte-offset/complete-line-count watermark, surface the unseen records, then advance the watermark after successful handling. The watermark survives watcher restarts; rotation, truncation, a backwards byte/line count, file replacement, or malformed JSON fails closed and requires operator recovery; it never resets silently to the beginning and replays decisions.

        ## Herdr-only failure modes and recovery

        - **Modifier-chord injection / launch corruption:** do not type launch control chords into a live prompt. Re-provision or return the pane to a shell, then use `herdr agent start ... -- <permission-flags>` so flags are part of the typed launch.
        - **Post-reboot dead pty wiring:** a pane may still exist while `herdr agent list` cannot detect the agent or the pane is sitting at a shell. Treat it as `agent-undetected`, preserve artifacts, close/re-provision the affected workspace/panes, rebuild the logical-role mapping, and repeat the self-contained settle-and-re-check READY gate above before READY.
        - **Focus-default cross-team mutation:** a missing or empty explicit pane/workspace id can make herdr target the currently focused pane, including a pane in another team. For every provisioning/mutation command after initial workspace creation, resolve a non-empty id from the recorded logical-role mapping, include it explicitly, and do not run when resolution fails. Apply the existing G555 attribution rules unchanged.
        - **Long-wait turn death:** replace unbounded `agent wait`/`pane wait-output` calls with bounded timeouts and re-entry. Use deterministic scripts with persisted task id, pane resolution, and watermark for long loops.
        - **Dispatch-echo false match:** never place the composed `ORCH_RESULT <fresh-per-dispatch-nonce>` wait needle in the task block. Use the split prefix/nonce fields, then require pane inspection and independently verified artifact evidence after a match.
        - **Approval/question pause reported as idle:** read the pane after every wait return. Apply the G550 MAY/escalate boundary to the visible dialog and re-enter the wake; `idle` is not completion evidence.

        """;
    }

    public static JsonObject CreateJson(IReadOnlyList<string> replacedProperties) => new()
    {
        ["replaces_agmsg_properties"] = new JsonArray(
            replacedProperties.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        ["provisioning"] = new JsonObject
        {
            ["topology"] = "One workspace per team, one tab named after the team, one pane per role, each pane opened with that role's folder as its cwd.",
            ["operator_visibility"] = "This keeps all roles visible to the operator at once and keeps the G550 supervision pane scan from being hidden behind an inactive tab.",
            ["workspace"] = "Create one team workspace; the returned root tab is the one team-named tab and the root pane is assigned to one host-repo role.",
            ["workspace_result_mapping"] = "Seed from workspace.workspace_id, tab.tab_id, and root_pane.pane_id; verify root_pane.cwd and update from later tab/pane creation results.",
            ["tab_name"] = "Ensure the returned tab is named for the team; if needed use its explicit id with herdr tab rename <tab-id> <team>.",
            ["pane_default"] = "DEFAULT: for every remaining herdr-resident role, resolve a non-empty mapped pane id and run herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus, then update the mapping from the result.",
            ["tab_exception"] = "herdr tab create --workspace <workspace-id> --cwd <role-cwd> --label <logical-role> --no-focus is not the primary path; use it only when the operator explicitly authorizes a separate role tab for a documented reason such as requiring tab-level lifecycle isolation instead of simultaneous visibility.",
            ["mapping"] = "Record an operator-visible logical-role-to-current-pane-id-and-cwd mapping; never hard-code pane/workspace ids.",
            ["target_id_rule"] = "After initial workspace creation returns the first ids, every provisioning/mutation command resolves an explicit non-empty pane/workspace target id from the recorded logical-role mapping immediately before execution and carries it on the command; empty or missing resolution fails closed and the command does not run. This prevents herdr focus-default mutation of the currently focused pane in another team and references the unchanged G555 attribution rules.",
            ["typed_launch"] = "herdr agent start <logical-role> --kind <agent-kind> --pane <pane-id> -- <operator-approved-permission-flags>",
            ["approval_boundary"] = "Approvals surface visibly in the pane and are handled at the supervision boundary, unlike the agmsg Codex bridge's headless auto-decline; the G550 MAY/escalate boundary governs.",
            ["ready_gate"] = "READY only after G556 verified liveness: after the startup report, wait a settle delay, then re-check the expected agent/cwd/repo and detected same-pane process before observing a bounded probe response; repeat after re-provisioning.",
        },
        ["dispatch"] = new JsonObject
        {
            ["command"] = "intent-cli notify delegate --domain <domain> --team <team> --from <sender-role> --to <logical-role> --report-to <orchestrator-role> --task-id <task-id> --objective <outcome> --input <reference> --expected-artifact <artifact> --result-nonce <nonce> --write --format json",
            ["required_fields"] = new JsonArray("domain", "team", "from", "to", "report-to", "task-id", "objective", "inputs", "expected-artifacts", "result-prefix", "result-nonce", "completion-marker"),
            ["reporting_contract"] = "The generated payload embeds task id, expected artifact, and the complete intent-cli notify report command (including transport-neutral --routing-root for isolated child checkouts) as the receiver's required final step; never hand-write a transport call.",
            ["marker_construction"] = "Concatenate result-prefix, space, fresh-per-dispatch result-nonce, space, status, space, artifact; never embed the composed wait needle in the task block.",
            ["echo_hazard"] = "pane wait-output searches existing output immediately, so an echoed precomposed marker can falsely match before work begins.",
            ["artifact_first"] = "Files, commits, PRs, and verification logs are the handoff; terminal text points to them.",
            ["redispatch"] = "Use intent-cli notify delegate for the same logical role with the task id plus concrete repair delta.",
        },
        ["wake_sources"] = new JsonObject
        {
            ["notify_report"] = "Primary and most informative: carries task id, status, artifact, and summary, but depends on the worker running the embedded final intent-cli notify report command.",
            ["state_change"] = new JsonObject
            {
                ["role"] = "Normative SECOND wake source; depends only on herdr observation and carries no outcome.",
                ["measured_version"] = "herdr 0.7.5",
                ["method"] = "events.subscribe",
                ["subscription"] = "{\"type\":\"pane.agent_status_changed\",\"pane_id\":\"<resolved-pane-id>\"}",
                ["cardinality"] = "One subscription entry per watched pane; resolve pane_id from the recorded logical-role mapping and never hard-code it.",
                ["event"] = "{\"event\":\"pane.agent_status_changed\",\"data\":{\"agent\":\"<agent>\",\"agent_status\":\"<working|idle|done|blocked|unknown>\",\"pane_id\":\"<resolved-pane-id>\",\"workspace_id\":\"<workspace-id>\"}}",
                ["transition"] = "Wake only on a per-role working-to-settled transition: idle, done, or blocked; initial settled, unknown, and settled-to-settled observations do not wake.",
                ["settle_and_dedupe"] = "Apply a settle delay and per-role dedupe so one observed working-to-settled transition produces one wake; a new working observation re-arms the role.",
            },
            ["semantic_boundary"] = "A state change means only that something happened, never that a task succeeded.",
            ["composite_check"] = "After every wake from either source, check current herdr state and approval/question pauses + exact fresh-nonce completion marker/status + verified artifact + fresh canonical intent-cli/GitHub facts.",
            ["last_net"] = "The periodic intent-cli automation stalled-work check remains the last net when neither immediate source produces a usable wake.",
            ["version_rule"] = "Consult installed herdr help/schema for version-specific details before operating the measured 0.7.5 shape.",
        },
        ["waiting"] = new JsonObject
        {
            ["agent"] = "herdr agent wait <logical-role> --until idle --until done --until blocked --timeout <milliseconds>",
            ["marker"] = "herdr pane wait-output --match \"ORCH_RESULT <fresh-per-dispatch-nonce>\" --source recent-unwrapped --timeout <milliseconds> <pane-id>",
            ["post_wait_inspection"] = "After every wait return, run herdr pane read --source recent-unwrapped <pane-id> and classify settled, approval/question-paused, or timeout; idle can be approval-paused.",
            ["paused_reentry"] = "For a visible approval/question, apply the G550 MAY/escalate boundary, then re-enter the wake and wait again.",
            ["bounded_rule"] = "Timeouts are re-entry points; never leave a wait unbounded. Persist cursors in deterministic scripts for long loops.",
            ["success"] = "Composite success requires approval/question-free settled state + matching fresh-nonce marker/status + existing verified artifact + fresh canonical intent-cli/GitHub facts; artifact verification plus canonical facts are the final gate, and neither state nor marker alone concludes success.",
        },
        ["events_jsonl"] = new JsonObject
        {
            ["scope"] = "Mode-independent design boundary only; never an inter-agent bus.",
            ["path"] = "<host-repo>/.intent-cli/events/<team>.jsonl",
            ["team_encoding"] = "Team name verbatim as one flat filename; no subdirectories.",
            ["validation"] = "Reject empty, leading-dot, path separators, and any '..' sequence before path construction.",
            ["append"] = "Orchestrator-only O_APPEND writer; one object per line, no embedded newline, summary normalized to one line.",
            ["schema"] = "timestamp, team, kind, unit, summary, artifact",
            ["kinds"] = new JsonArray("completion", "blocked", "question", "escalation"),
            ["watermark_invariant"] = "Every reader persists file identity, byte offset, and complete-line count durably across watcher restarts; rotation, truncation, backwards byte/line count, or file replacement fails closed and never resets to the beginning because replay can duplicate a design decision.",
            ["readers"] = new JsonObject
            {
                ["claude_app"] = "Tail complete unseen lines after a durable file-identity/byte-offset/complete-line-count watermark that survives watcher restarts; fail closed on rotation, truncation, backwards count, or file replacement, never reset to the beginning.",
                ["codex_cli"] = "Use intent-cli notify delegate/report for ordinary coordination; when acting as a design-boundary reader, use the same durable restart-surviving file-identity/byte-offset/complete-line-count watermark and fail closed on rotation, truncation, backwards count, or file replacement, never reset to the beginning.",
                ["codex_desktop"] = "One-minute-class timer poll using a durable restart-surviving file-identity/byte-offset/complete-line-count watermark; fail closed on rotation, truncation, backwards count, file replacement, or malformed JSON, never reset to the beginning.",
            },
        },
        ["failure_recovery"] = new JsonArray(
            "Modifier-chord injection: relaunch with typed agent-start permission flags.",
            "Post-reboot dead pty: detect agent-undetected panes, re-provision, rebuild mapping, repeat G556.",
            "Focus-default cross-team mutation: every provisioning/mutation command after initial workspace creation resolves and carries a non-empty explicit pane/workspace id from the recorded mapping; empty resolution fails closed and the command does not run; G555 attribution remains unchanged.",
            "Long-wait turn death: bounded waits, re-entry, and deterministic persisted loops.",
            "Dispatch-echo false match: keep the composed wait needle out of the task block and require independently verified artifact evidence.",
            "Approval/question pause: inspect the pane after every wait return, apply G550 MAY/escalate, and re-enter the wake."),
    };
}
