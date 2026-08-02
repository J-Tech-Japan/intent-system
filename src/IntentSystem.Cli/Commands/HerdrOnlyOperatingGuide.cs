using System.Text.Json.Nodes;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G571: the concrete operating procedure selected by the G570 session-layer
/// router. This content is synthetic rather than part of the agmsg-backed
/// guide model so the practiced agmsg rendering remains byte-for-byte on its
/// existing path.
/// </summary>
internal static class HerdrOnlyOperatingGuide
{
    public const string JsonProperty = "herdr_only_operations";

    public static readonly IReadOnlyList<string> Headings =
    [
        "## Herdr-only provisioning and READY gate",
        "## Herdr-only dispatch and artifact handoff",
        "## Herdr-only bounded waiting and success detection",
        "## events.jsonl design boundary",
        "## Herdr-only failure modes and recovery",
        "## Session-layer switch checklists",
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
        2. Create a team workspace with `herdr workspace create --cwd <host-repo> --label <team> --no-focus`. Create role tabs/panes with the role cwd: design and orchestrator use `<host-repo>`, implementation uses `<implementation-repo>`, and review uses its isolated review cwd/worktree. `herdr tab create --workspace <workspace-id> --cwd <role-cwd> --label <logical-role> --no-focus` is the typed tab surface; `herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus` is available when a split is preferred.
        3. Record a durable operator-visible mapping of each herdr-resident logical role (`design`, `orchestrator`, `implementation`, `review`) to its current workspace/tab/pane id and cwd. Workflows address the logical role and resolve this mapping at dispatch time; they NEVER hard-code a pane id. A design frontend outside herdr is recorded as the design reader type, not fabricated as a pane.
        4. Launch the typed agent in the mapped pane with `herdr agent start <logical-role> --kind <claude|codex|...> --pane <pane-id> -- <operator-approved-permission-flags>`. Pass launch and permission flags after `--`; do not inject modifier chords into an interactive prompt. Approvals are NEVER auto-answered: only the G550 MAY-answer classes may be handled, and every other approval is escalated to the operator.
        5. READY is a G556 verified-liveness result, not workspace existence. Check `herdr agent list`, inspect the mapped pane/agent, verify the expected cwd/repository and detected agent kind, then send a bounded ping and observe its ack in that same pane. An undetected agent, a shell prompt where the agent should be, a mismatched cwd, or no ack is NOT READY.

        Role identity in herdr-only is this verified logical-role→pane mapping. There is no agmsg identity or separate role-switching step.

        ## Herdr-only dispatch and artifact handoff

        Submit one structured task block with `herdr agent prompt <logical-role> <task-block>`. Every block has this shape:

        ```text
        TASK <task-id>
        role: <logical-role>
        objective: <one bounded outcome>
        inputs:
          - <canonical issue/PR/path/reference>
        expected-artifacts:
          - <file, commit, PR, report, or other inspectable artifact>
        completion-marker: Print exactly `ORCH_RESULT <task-id> <status> <artifact>` when the artifact is ready; use status completed, blocked, or question.
        ```

        Files, commits, PRs, test logs, and other inspectable artifacts are the handoff; terminal prose is only a signal pointing at them. A repair or re-dispatch goes to the same logical role after resolving its current pane mapping and carries the same task id plus the concrete delta. Never infer completion from screen text that lacks the task-specific marker and artifact.

        ## Herdr-only bounded waiting and success detection

        Use bounded waits only. For agent state, use `herdr agent wait <logical-role> --until done --until blocked --timeout <milliseconds>`. For the task signal, use `herdr pane wait-output --match "ORCH_RESULT <task-id>" --source recent-unwrapped --timeout <milliseconds> <pane-id>`. Both commands wait indefinitely when `--timeout` is omitted, so omission is forbidden in an orchestrator wake.

        A timeout is a re-entry point: inspect state and pane output, persist progress, return control to the orchestrator, and wait again on the next bounded wake. Prefer a deterministic script that persists its cursor for long multi-wait flows; do not hold one chat turn open indefinitely, because turn death loses the continuation.

        **Composite success is mandatory:** (1) herdr reports a settled state, (2) the exact `ORCH_RESULT` marker matches the task id and status, and (3) the named artifact exists and passes the task's verification. State alone NEVER means task success; in particular, herdr `done` or `idle` alone is insufficient. `blocked` and `question` markers route back to the orchestrator/design decision boundary; they are not failures to hide or successes to assume.

        ## events.jsonl design boundary

        This is a normative, mode-independent design-boundary channel documented here because herdr-only has no separate message bridge. It is NEVER an inter-agent bus and never replaces direct herdr dispatch, GitHub, or intent-cli workflow state.

        - **Location:** resolve the host repository root at runtime, then use `<host-repo>/.intent-cli/events/<team>.jsonl`. The team name is the agmsg/herdr team name verbatim in one flat filename (example: `intent-cli-dev.jsonl`); there are no team subdirectories and readers never hard-code an absolute path.
        - **Fail closed before path construction:** reject an empty team name, a leading dot, `/` or `\\`, and any `..` sequence. Do not sanitize or silently rewrite an invalid name.
        - **Append contract:** the orchestrator is the only writer. Open with append semantics (`O_APPEND`), append exactly one complete JSON object per line, include no embedded newline, and normalize `summary` to one line.
        - **Schema:** `{"timestamp":"<RFC3339>","team":"<team>","kind":"completion|blocked|question|escalation","unit":"<execution-unit-or-task-id>","summary":"<one-line-summary>","artifact":"<repo-relative-path-or-URL>"}`. These six fields are required; `artifact` identifies the inspectable handoff or the decision input.
        - **Writer boundary:** append only design-relevant completion, blocked, question, and escalation events. Routine progress, dispatch traffic, pane output, acknowledgements, and workflow label changes do not belong here.

        Reader recipes:

        1. **Claude app watcher:** resolve the host root and validated team path, then tail complete lines from `<host-repo>/.intent-cli/events/<team>.jsonl`; surface only unseen design-relevant records and retain the read offset across watcher restarts.
        2. **Codex CLI:** when Codex is a herdr pane, prompt that logical role directly with `herdr agent prompt`; it does not poll the file for ordinary coordination. The events file remains available only when that Codex role is acting as a design-boundary reader.
        3. **Codex Desktop:** run a one-minute-class timer poll. Resolve the host root plus validated team filename on every resumed session, read only complete lines after a durable byte-offset watermark, surface the unseen records, then advance the watermark after successful handling. Rotation/truncation or malformed JSON fails closed and requires operator recovery; it never resets silently and replays decisions.

        ## Herdr-only failure modes and recovery

        - **Modifier-chord injection / launch corruption:** do not type launch control chords into a live prompt. Re-provision or return the pane to a shell, then use `herdr agent start ... -- <permission-flags>` so flags are part of the typed launch.
        - **Post-reboot dead pty wiring:** a pane may still exist while `herdr agent list` cannot detect the agent or the pane is sitting at a shell. Treat it as `agent-undetected`, preserve artifacts, close/re-provision the affected workspace/panes, rebuild the logical-role mapping, and repeat the complete G556 verified-liveness gate before READY.
        - **Long-wait turn death:** replace unbounded `agent wait`/`pane wait-output` calls with bounded timeouts and re-entry. Use deterministic scripts with persisted task id, pane resolution, and watermark for long loops.

        ## Session-layer switch checklists

        One team runs exactly one session-layer mode at a time. Simultaneous agmsg and herdr-only delivery is a mixed-delivery CONTRACT VIOLATION.

        **agmsg → herdr-only**

        1. Drain or explicitly park every in-flight delegation; record artifacts and unresolved blockers.
        2. Gracefully drop outgoing agmsg roles and stop their watchers/bridges. Verify no agmsg receiver can still deliver for this team.
        3. Provision the herdr workspace, role cwds, typed agents, and logical-role→pane mapping above; validate approvals and the events path.
        4. Pass the complete G556 verified-liveness READY gate for every incoming role and verify bounded dispatch/marker/artifact detection.
        5. As the FINAL canonical step, run `intent-cli session-layer set --domain <domain> --team <team> --mode herdr-only --write`.

        **herdr-only → agmsg**

        1. Drain or explicitly park every in-flight delegation; append any final design-relevant event and record artifacts/blockers.
        2. Gracefully stop agents or retain/close the outgoing herdr workspace according to the operator's workspace policy; ensure it cannot keep delivering tasks for this team.
        3. Provision agmsg roles, transport configuration, and any approved watcher/bridge; do not reuse a stale role hold. Keep `events.jsonl` as the mode-independent design boundary, not as an agmsg bus.
        4. Pass the complete G556 verified-liveness READY gate and end-to-end delivery ack for every incoming role.
        5. As the FINAL canonical step, run `intent-cli session-layer set --domain <domain> --team <team> --mode agmsg --write`.
        """;
    }

    public static JsonObject CreateJson(IReadOnlyList<string> replacedProperties) => new()
    {
        ["replaces_agmsg_properties"] = new JsonArray(
            replacedProperties.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        ["provisioning"] = new JsonObject
        {
            ["workspace"] = "Create the team workspace and role tabs/panes with role-specific cwds; consult installed herdr help for version-specific options.",
            ["mapping"] = "Record an operator-visible logical-role-to-current-pane-id-and-cwd mapping; resolve it at dispatch time and never hard-code pane ids.",
            ["typed_launch"] = "herdr agent start <logical-role> --kind <agent-kind> --pane <pane-id> -- <operator-approved-permission-flags>",
            ["approval_boundary"] = "Approvals are never auto-answered; the G550 MAY/escalate boundary governs.",
            ["ready_gate"] = "READY only after G556 verified liveness: expected agent/cwd/repo, detected same-pane process, and bounded probe response.",
        },
        ["dispatch"] = new JsonObject
        {
            ["command"] = "herdr agent prompt <logical-role> <task-block>",
            ["required_fields"] = new JsonArray("task-id", "role", "objective", "inputs", "expected-artifacts", "completion-marker"),
            ["marker"] = "ORCH_RESULT <task-id> <status> <artifact>",
            ["artifact_first"] = "Files, commits, PRs, and verification logs are the handoff; terminal text points to them.",
            ["redispatch"] = "Resolve the same logical role's current pane and send the task id plus concrete repair delta.",
        },
        ["waiting"] = new JsonObject
        {
            ["agent"] = "herdr agent wait <logical-role> --until done --until blocked --timeout <milliseconds>",
            ["marker"] = "herdr pane wait-output --match \"ORCH_RESULT <task-id>\" --source recent-unwrapped --timeout <milliseconds> <pane-id>",
            ["bounded_rule"] = "Timeouts are re-entry points; never leave a wait unbounded. Persist cursors in deterministic scripts for long loops.",
            ["success"] = "Composite success requires settled state + matching task marker + existing verified artifact; state alone never concludes success.",
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
            ["readers"] = new JsonObject
            {
                ["claude_app"] = "Tail complete unseen lines from the resolved host path and retain the read offset.",
                ["codex_cli"] = "Prompt a herdr-resident Codex pane directly; no file poll for ordinary coordination.",
                ["codex_desktop"] = "One-minute-class timer poll using a durable byte-offset watermark; fail closed on truncation or malformed JSON.",
            },
        },
        ["failure_recovery"] = new JsonArray(
            "Modifier-chord injection: relaunch with typed agent-start permission flags.",
            "Post-reboot dead pty: detect agent-undetected panes, re-provision, rebuild mapping, repeat G556.",
            "Long-wait turn death: bounded waits, re-entry, and deterministic persisted loops."),
        ["switches"] = new JsonObject
        {
            ["exclusivity"] = "Exactly one mode per team; mixed delivery is a contract violation.",
            ["agmsg_to_herdr_only"] = "Drain; gracefully drop roles/bridges; provision herdr; G556 verify; set herdr-only as the final canonical step.",
            ["herdr_only_to_agmsg"] = "Drain; handle workspace; provision roles/bridge; G556 verify; set agmsg as the final canonical step.",
        },
    };
}
