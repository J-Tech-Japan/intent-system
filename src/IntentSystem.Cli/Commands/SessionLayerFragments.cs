namespace IntentSystem.Cli.Commands;

/// <summary>
/// G570 seventh repair: the EXPLICIT, hand-authored content model for every
/// fragment the orchestrator-thread guide renders inside a section that mixes
/// mode-independent canon with agmsg transport mechanics.
///
/// The sixth repair typed fragments with <c>ClassifyFragment</c> — a cue/token
/// heuristic. Review rejected it for two reasons that both hold:
///
/// 1. It guessed. An instruction phrased outside the cue vocabulary classified
///    as description and survived into herdr-only output. That failure mode is
///    invisible from inside the suite, because the suite asked the same
///    heuristic what the answer was.
/// 2. The tests used the production classifier as their own oracle, so they
///    could only ever confirm the classifier agreed with itself.
///
/// This table replaces the guess with a decision. Every non-structural rendered
/// fragment is declared here, verbatim, with a type a human assigned. Lookup is
/// EXACT and FAILS CLOSED: a fragment that reaches a renderer without a
/// declaration throws rather than defaulting, so adding or rewording a sentence
/// in the guide is a test failure that demands a typing decision — it can no
/// longer leak silently, which was the one limitation the sixth repair could
/// not close.
///
/// Structural lines (blank, heading, fence marker, table scaffolding) carry no
/// semantics and are recognised mechanically; they are not declared here.
///
/// The types, and what each one means for herdr-only rendering:
/// <list type="bullet">
///   <item><c>CanonDescriptive</c> — mechanism, history, substrate identity.
///     Kept byte-identically; the section's descriptive label tells the reader
///     the agmsg mechanics named in it are illustration.</item>
///   <item><c>ModeIndependentOperative</c> — an instruction that binds in BOTH
///     modes (intent-cli / GitHub / four-thread-model rules). Kept
///     byte-identically. Declared apart from description so the guards can
///     prove instructions were never quietly filed as prose.</item>
///   <item><c>TransportOperative</c> — an instruction to drive the agmsg
///     transport. This is the ONLY type that is routed: it is replaced with a
///     non-procedural G571 pointer under herdr-only.</item>
/// </list>
///
/// Declaration text is stored with the caller's inputs as SENTINELS and is
/// expanded with the actual values at lookup time, so one declaration serves
/// every invocation shape. See <see cref="Interpolations"/> for why sentinels
/// rather than angle-bracket placeholders.
/// </summary>
internal static class SessionLayerFragments
{
    private const string S0 = "## Setup intake";
    private const string S1 = "## Design-thread workspace supervision (G550)";
    private const string S2 = "## Design-decision holds and bounded authority (G552)";
    private const string S3 = "## Cross-project isolation on a shared machine (G555)";
    private const string S4 = "## Scheduled orchestrator cadence";
    private const string S5 = "## Next-slice publication";
    private const string S6 = "## Dispatch verification (G524)";
    private const string S7 = "## Design handoff (start / resume)";
    private const string S8 = "## Design traffic-controller playbook";
    private const string S9 = "## Managed worktree cleanup";
    private const string S10 = "## Review delegation — managed worktrees and design alignment";
    private const string S11 = "## Orchestrator first wake";
    private const string S12 = "## Safety boundaries";

    /// <summary>One declared fragment: the section (markdown) or property
    /// (JSON) it belongs to, its verbatim text in placeholder form, and the
    /// type a human assigned to it.</summary>
    internal readonly record struct FragmentDeclaration(
        string Section,
        string Text,
        SessionLayerSections.FragmentType Type,
        IReadOnlyList<FragmentClause>? Clauses = null);

    /// <summary>
    /// One independently typed clause of a fragment whose text mixes semantics.
    /// G570 eighth repair: a markdown table row such as
    /// <c>| agmsg run directory | … | ownership is per (team, role) FILE. Touch
    /// only files whose team segment is yours … |</c> carries a DESCRIPTIVE
    /// substrate identity and an OPERATIVE rule in one line. A single type for
    /// the whole row cannot express that, and whichever type is chosen either
    /// files a binding rule as prose or strips an identity the reader needs.
    /// The clauses concatenate back to the fragment's text exactly, so the
    /// document is never restructured — only made addressable.
    /// </summary>
    internal readonly record struct FragmentClause(
        string Text,
        SessionLayerSections.FragmentType Type);

    /// <summary>
    /// Declares a fragment as an ordered list of independently typed clauses at
    /// SENTENCE granularity.
    ///
    /// G570 ninth repair: the eighth repair typed whole fragments and split only
    /// five table rows, so a multi-sentence fragment that mixed mechanism with a
    /// binding duty still carried ONE type — and the clause model was never read
    /// by either renderer, which made it decorative. Every fragment is now a
    /// clause list, every clause is one sentence (or the scaffolding between
    /// sentences and table cells), and the clauses concatenate back to the
    /// fragment's text exactly. The renderers consume the clause list, so
    /// routing and the descriptive label act at the granularity the types were
    /// decided at.
    /// </summary>
    private static FragmentDeclaration Fragment(string section, params FragmentClause[] clauses)
    {
        var text = string.Concat(clauses.Select(c => c.Text));
        var type = clauses.Any(c => c.Type == SessionLayerSections.FragmentType.TransportOperative)
            ? SessionLayerSections.FragmentType.TransportOperative
            : clauses.Any(c => c.Type == SessionLayerSections.FragmentType.ModeIndependentOperative)
                ? SessionLayerSections.FragmentType.ModeIndependentOperative
                : SessionLayerSections.FragmentType.CanonDescriptive;
        return new FragmentDeclaration(section, text, type, clauses);
    }

    private static FragmentClause Descriptive(string text) =>
        new(text, SessionLayerSections.FragmentType.CanonDescriptive);

    private static FragmentClause Operative(string text) =>
        new(text, SessionLayerSections.FragmentType.ModeIndependentOperative);

    private static FragmentClause Transport(string text) =>
        new(text, SessionLayerSections.FragmentType.TransportOperative);

    private static FragmentClause Scaffold(string text) =>
        new(text, SessionLayerSections.FragmentType.Structural);

    /// <summary>
    /// Every non-structural fragment rendered inside a mixed section, across
    /// every invocation shape the command supports (inputs supplied or missing,
    /// each <c>--existing-loop-policy</c> and <c>--mode</c> value). Rows marked
    /// <c>hand-typed</c> carry a transport mechanic and required a semantic
    /// decision; the rest name no mechanic at all, so there is nothing to route
    /// and they are canon by construction.
    /// </summary>
    public static readonly IReadOnlyList<FragmentDeclaration> Declarations =
    [
        Fragment(
            S0,
            Descriptive("- session layer: Recorded session layer for this setup: agmsg + herdr (supported, not retired) (recorded)."),
            Scaffold(" "),
            Operative("Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team <team> --mode agmsg|herdr-only --write`."),
            Scaffold(" "),
            Descriptive("A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.")),
        Fragment(
            S0,
            Descriptive("- session layer: Recorded session layer for this setup: agmsg + herdr (supported, not retired) (recorded)."),
            Scaffold(" "),
            Operative("Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team __TEAM__ --mode agmsg|herdr-only --write`."),
            Scaffold(" "),
            Descriptive("A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.")),
        Fragment(
            S0,
            Descriptive("- session layer: Recorded session layer for this setup: agmsg + herdr (supported, not retired) (default — nothing recorded yet)."),
            Scaffold(" "),
            Operative("Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team <team> --mode agmsg|herdr-only --write`."),
            Scaffold(" "),
            Descriptive("A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.")),
        Fragment(
            S0,
            Descriptive("- session layer: Recorded session layer for this setup: agmsg + herdr (supported, not retired) (default — nothing recorded yet)."),
            Scaffold(" "),
            Operative("Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team __TEAM__ --mode agmsg|herdr-only --write`."),
            Scaffold(" "),
            Descriptive("A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.")),
        Fragment(
            S0,
            Descriptive("- session layer: Recorded session layer for this setup: herdr-only (preferred — fewer dependencies) (recorded)."),
            Scaffold(" "),
            Operative("Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team <team> --mode agmsg|herdr-only --write`."),
            Scaffold(" "),
            Descriptive("A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.")),
        Fragment(
            S0,
            Descriptive("- session layer: Recorded session layer for this setup: herdr-only (preferred — fewer dependencies) (recorded)."),
            Scaffold(" "),
            Operative("Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team __TEAM__ --mode agmsg|herdr-only --write`."),
            Scaffold(" "),
            Descriptive("A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.")),
        Fragment(
            S0,
            Descriptive("- setup-ready (herdr-only) — concrete provisioning, logical-role→pane mapping, typed launch, and G556 READY procedures are present in the herdr-only operating sections.")),
        Fragment(
            S0,
            Descriptive("PRIMARY four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over the session layer this team runs — herdr-only here."),
            Scaffold(" "),
            Descriptive("The session layer carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout."),
            Scaffold(" "),
            Descriptive("Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation)."),
            Scaffold(" "),
            Descriptive("The concrete herdr-only operating sections below cover provisioning, dispatch, bounded completion detection, the events boundary, recovery, and both switches.")),
        Fragment(S0, Operative("- missing-inputs — supply the 1 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 2 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 3 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 4 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 5 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Descriptive("- **status: `missing-inputs`**")),
        Fragment(S0, Operative("- missing-inputs — supply the 6 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 7 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 8 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 9 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 10 missing field(s) below to get a setup-ready plan.")),
        Fragment(S0, Operative("- missing-inputs — supply the 11 missing field(s) below to get a setup-ready plan.")),
        Fragment(
            S0,
            Operative("- blocked — shared session-layer preflight did not pass."),
            Scaffold(" "),
            Operative("Record and validate the intended mode before declaring READY or notifying.")),
        Fragment(S0, Transport("- The implementation and review threads are loopless agmsg receivers — they must NOT run their own `/loop` or recurring timer for the same domain/repo, whether the orchestrator runs message-driven (the default, woken by agmsg replies) or on an explicit fallback/legacy timer (Codex 5m / Claude `/loop 5m`).")),
        Fragment(S0, Descriptive("- orchestrator folder")),
        Fragment(S0, Descriptive("- implementation folder")),
        Fragment(S0, Descriptive("- review folder")),
        Fragment(S0, Transport("- agmsg team name")),
        Fragment(S0, Transport("- delivery mode")),
        Fragment(S0, Descriptive("- existing-loop stop policy")),
        Fragment(S0, Descriptive("- target repo")),
        Fragment(S0, Descriptive("- orchestrator agent")),
        Fragment(S0, Descriptive("- implementer agent")),
        Fragment(S0, Descriptive("- reviewer agent")),
        Fragment(
            S0,
            Descriptive("PRIMARY four-thread orchestrator model over agmsg + herdr (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over agmsg. agmsg carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout."),
            Scaffold(" "),
            Descriptive("Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation).")),
        Fragment(
            S1,
            Descriptive("G666/G689/G690 preview-through-1.x applies the three-layer approval model plus scoped adjudication authority."),
            Scaffold(" "),
            Descriptive("G689's shell-command class extracts command payloads but keeps all shipped shell answer authority orchestration-only; G690 resolves any future capability only through class, scope, risk-floor, audit, and live-CAS checks."),
            Scaffold(" "),
            Descriptive("Under authority the OPERATOR granted it, the design thread drives the team's SESSION LAYER through the workspace manager: it provisions the team (see `Terminal-workspace provisioning`), keeps the sessions alive and correctly held, and supervises for stalls."),
            Scaffold(" "),
            Operative("It records blocking waits and routes them to the canonical adjudication boundary; it never relays keystrokes or bypasses class, scope, risk-floor, audit, or live-CAS checks."),
            Scaffold(" "),
            Descriptive("This adds a session-layer role — it moves NO workflow authority.")),
        Fragment(
            S1,
            Descriptive("Two layers, two owners."),
            Scaffold(" "),
            Descriptive("The SESSION layer (panes, processes, holds, blocking dialogs) is what the operator grants the design thread."),
            Scaffold(" "),
            Descriptive("The WORKFLOW layer (labels, queue-state, publication, delegation, closeout) is not granted and never moves — it stays with intent-cli, GitHub, and the orchestrator exactly as before.")),
        Fragment(
            S1,
            Operative("- **authority is granted, not assumed** — the design thread supervises the session layer because the operator asked it to, and the grant's scope is what the operator stated."),
            Scaffold(" "),
            Operative("Outside a grant the design thread observes and reports rather than acts."),
            Scaffold(" "),
            Operative("A grant to supervise sessions is never read as a grant to decide workflow, product, or security questions.")),
        Fragment(S1, Descriptive("The design thread operates the session layer:")),
        Fragment(S1, Operative("- PROVISIONING — build the team's workspace, folders, panes, launches, and role initialization per `Terminal-workspace provisioning` (G549); supervision references that section rather than repeating it.")),
        Fragment(S1, Operative("- SESSION LIFECYCLE — investigate an unresponsive session and, when it must be replaced, do so through the graceful drop that honors one-holder exclusivity.")),
        Fragment(S1, Operative("- STALL SUPERVISION — run the three supervision layers below so a stall is noticed by a layer that is actually running, not by luck.")),
        Fragment(S1, Operative("- BLOCKING DIALOGS — detect and record the wait, then route it to the canonical adjudication boundary; design never relays keys or bypasses class, scope, risk-floor, audit, or live-CAS checks.")),
        Fragment(
            S1,
            Descriptive("> **Workflow state ownership:** workflow state ownership does not move."),
            Scaffold(" "),
            Descriptive("Labels, queue-state, publication, delegation, CI/review gating, and closeout remain with intent-cli, GitHub, and the orchestrator; the design↔orchestrator double-check rule and the orchestrator's ownership of workflow transitions apply exactly as before."),
            Scaffold(" "),
            Operative("Supervising a session never authorizes a workflow transition, and a stuck pane is never a reason to move a label by hand.")),
        Fragment(S1, Operative("A session that stops responding is a session-layer fault, and the design thread may repair it — but repair means restoring a correctly held, live session, not taking over the role's work or its decisions.")),
        Fragment(
            S1,
            Operative("- READ the pane first — an \"unresponsive\" session is most often blocked on a dialog, a trust screen, or a prompt waiting for input, not dead."),
            Scaffold(" "),
            Operative("Diagnose from what the pane actually shows.")),
        Fragment(S1, Transport("- Distinguish the layers: a live session that is merely not attached to delivery is a delivery problem (re-check the readiness layers), not a reason to replace the session.")),
        Fragment(S1, Operative("- Confirm the role is still held by that session before concluding anything — a role silently dropped elsewhere looks identical to a dead session from the outside.")),
        Fragment(S1, Transport("- Prefer the least invasive authorized repair that restores liveness: route a residual dialog to orchestration, re-arm delivery, or restart the session — replacement is the last step, not the first.")),
        Fragment(
            S1,
            Descriptive("- **one holder per role** — replacing a session never means two sessions holding the same role for even a moment."),
            Scaffold(" "),
            Transport("The successor claims only after the incumbent's hold is released; a refused actas is the exclusivity rule working, not an obstacle to route around.")),
        Fragment(
            S1,
            Transport("- **graceful drop** — Replace through the GRACEFUL DROP: the incumbent drops the role (releasing its exclusivity lock and registration), then the successor claims it and re-runs readiness plus the ping test."),
            Scaffold(" "),
            Operative("Never kill a pane and assume the hold cleared, and never force a role away from a live session.")),
        Fragment(
            S1,
            Descriptive("- **operator-visible confirmation** — The drop's confirmation is OPERATOR-VISIBLE: the handover surfaces to the operator rather than happening silently inside the design thread."),
            Scaffold(" "),
            Operative("The design thread may request and sequence the handover; the decision to retire a live session remains the operator's, and the confirmation is what records it.")),
        Fragment(S1, Transport("- **real-time message monitor**")),
        Fragment(S1, Transport("- purpose — Catch inbound agmsg traffic as it arrives — replies, blockers, and escalations that should wake the design thread immediately.")),
        Fragment(S1, Transport("- cadence — continuous / real-time (a live attached inbox stream, not a poll).")),
        Fragment(
            S1,
            Transport("- note — This layer is what the message-driven steady state assumes."),
            Scaffold(" "),
            Transport("It sees only what is SENT — it cannot notice a session that went quiet or a pane blocked on a dialog, which is why the other two layers exist.")),
        Fragment(S1, Operative("- **blocking-UI pane scan**")),
        Fragment(
            S1,
            Operative("- purpose — Notice panes that are stuck with nothing to say."),
            Scaffold(" "),
            Descriptive("TWO EQUAL stuck states: a pane blocked on an approval, selection, or trust prompt, AND a pane showing a shell prompt where an agent should be (`agent-absent`, G556)."),
            Scaffold(" "),
            Descriptive("Both produce no message at all — a blocked agent is waiting and a dead one cannot speak — so no message-driven layer can ever detect either.")),
        Fragment(S1, Operative("- cadence — sub-minute class (e.g. every few tens of seconds) — a blocking dialog stalls a role for its entire lifetime, and an agent that died seconds after reporting stays dead until someone looks, so this layer is the fast one.")),
        Fragment(
            S1,
            Operative("- note — Scanning uses structured process state, and what the scan finds routes by STATE, not by one rule for everything."),
            Scaffold(" "),
            Operative("A blocking dialog is recorded and routed to the canonical adjudication surface under the durable per-team policy; design cannot bypass exact class, scope, risk-floor, audit, or live-CAS checks."),
            Scaffold(" "),
            Operative("An `agent-absent` shell prompt is NOT a dialog and must never be routed through dialog handling: it goes to the shim-safe relaunch recovery (recreating the app-server when that is what died), followed by the COMPLETE verified-liveness re-check — report, settle delay, all three checks."),
            Scaffold(" "),
            Descriptive("See `What the pane scan is looking for` for both recoveries.")),
        Fragment(S1, Descriptive("- **periodic state watchdog**")),
        Fragment(S1, Operative("- purpose — Compare canonical intent-cli/GitHub state against expected progress and nudge the orchestrator when work has gone stale — the existing design-thread watchdog (`intent-cli automation heartbeat --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`).")),
        Fragment(S1, Operative("- cadence — tens-of-minutes class (e.g. every 30 minutes) — quiet enough to stay out of the way, frequent enough to bound a stall.")),
        Fragment(
            S1,
            Operative("- note — This is the existing watchdog, not a second one: its safety rules apply verbatim (see the watchdog safety-rules reference below)."),
            Scaffold(" "),
            Operative("One canonical nudge per wake, never a batch.")),
        Fragment(
            S1,
            Operative("- Measured `intent-cli notify supervise` keeps the interval cycle as the safety floor. "),
            Operative("The optional SECOND wake source is enabled only by the concrete `--event-mode` flag: it holds blocking herdr waits for `pane.agent_status_changed` and re-arms after wait death/error. "),
            Operative("It does not add a second supervisor or change finding, recovery, or wake-target semantics.")),
        Fragment(
            S1,
            Descriptive("G699: measured supervision keeps the detector authoritative while making repeated observations readable and bounded."),
            Scaffold(" "),
            Descriptive("The first finding is emitted at the full configured cadence; an unchanged same-key observation remains a named active parked record and later findings are emitted no more often than the recorded repeat backoff cadence."),
            Scaffold(" "),
            Descriptive("G704 hardens supervise install with structural bound validation, launchd log paths, bounded first-cycle proof, and duplicate-writer attribution.")),
        Fragment(S1, Operative("- `intent-cli notify supervise --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --interval 300 --repeat-backoff-seconds 1800 --debounce-consecutive-observations 3 --once --write --format json`")),
        Fragment(S1, Operative("- `intent-cli notify supervise --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --once --format markdown`")),
        Fragment(S1, Operative("- `intent-cli notify supervise install --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --write --format json`")),
        Fragment(S1, Operative("- `intent-cli notify supervise install --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --write --format json`")),
        Fragment(S1, Operative("- `intent-cli notify supervise --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --interval 300 --repeat-backoff-seconds 1800 --debounce-consecutive-observations 3 --once --write --format json`")),
        Fragment(S1, Operative("- `intent-cli notify supervise --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --once --format markdown`")),
        Fragment(S1, Operative("- full cadence: `--interval <seconds>`; default when omitted: 30s")),
        Fragment(S1, Operative("- repeat emission backoff: `--repeat-backoff-seconds <seconds>` (alias `--backoff-seconds`); default: 1800s")),
        Fragment(S1, Operative("- pane status debounce: `--debounce-consecutive-observations <count>` (alias `--status-debounce-consecutive`); default: 3 consecutive observations")),
        Fragment(S1, Operative("- write mode records the resolved values at `.intent-cli/supervision/<domain>/<team>/emission-policy.json` and repeats them on every cycle")),
        Fragment(S1, Operative("- G704 bound rule: `--bound` must be >= `--interval`; otherwise `bound-below-interval` names the structural `supervisor-not-running` consequence while the runtime warning remains")),
        Fragment(S1, Operative("- G704 startup proof: `--startup-bound <seconds>` (default 30s) must observe a writer-bearing first cycle before a write reports success")),
        Fragment(S1, Operative("- G704 macOS artifact: WorkingDirectory is the routing root; StandardOutPath and StandardErrorPath are under `.intent-cli/supervision/<domain>/<team>/runtime/`; installed writer identity is recorded beside bound/emission state")),
        Fragment(S1, Operative("G712 chooses the permitted GUI-session lifetime: supervision may be explicitly bootstrapped into the current GUI domain, but it is never login-auto-loaded and does not survive logout or reboot.")),
        Fragment(S1, Operative("- **install** — `intent-cli notify supervise install --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --platform macos --write --format json`")),
        Fragment(S1, Operative("- **install** — `intent-cli notify supervise install --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --platform macos --write --format json`")),
        Fragment(S1, Operative("- **current-session registration** — `launchctl bootstrap gui/$(id -u) '<artifact-path>'`")),
        Fragment(S1, Operative("- **reconcile** — `intent-cli notify supervise reconcile --write --format json`")),
        Fragment(S1, Operative("- **uninstall** — `intent-cli notify supervise uninstall --write --format json`")),
        Fragment(S1, Operative("- **artifact location** — Artifacts remain under `.intent-cli/supervision/<domain>/<team>/install/`; no managed artifact is emitted to `~/Library/LaunchAgents`.")),
        Fragment(S1, Operative("- The generated macOS plist omits `RunAtLoad`; install output names the artifact, lifetime, runtime logs, and legacy artifacts removed.")),
        Fragment(S1, Operative("- Reconcile/uninstall records `loaded_before`, `unloaded`, `removed_artifacts`, `loaded_after`, and `artifacts_after`; use it for the three-loaded-jobs/one-plist drift shape.")),
        Fragment(S1, Operative("- Reconcile removes only `intent-cli.supervise.*` jobs and artifacts, including legacy login-persistent plists; it never kills, replaces, or mutates unrelated jobs.")),
        Fragment(
            S1,
            Operative("> **Lifecycle authority boundary:** Install authors and first-cycle-probes only. "),
            Operative("Registration is an explicit operator action; reconcile/uninstall is the bounded current-session unload/removal command and does not grant workflow or recovery authority.")),
        Fragment(S1, Operative("- same-key observations never disappear: the active record names `parked` and exposes `first_seen`, `last_seen`, `repeat_count`, and `emission_cadence_seconds`")),
        Fragment(S1, Operative("- resolution clears the active record; a later reappearance starts a fresh first_seen/repeat_count sequence")),
        Fragment(S1, Operative("- a changed condition fingerprint resets first_seen, last_seen, repeat_count, and emission eligibility immediately")),
        Fragment(S1, Operative("- a genuinely new observation key is emitted immediately even while another key is parked")),
        Fragment(S1, Operative("- a pane status flap below the recorded consecutive threshold is not classified; the threshold-consecutive settled state is classified once with the existing observation-only boundary")),
        Fragment(S1, Operative("- detection predicates and G695 continuation-chain recording remain unchanged; parking suppresses duplicate findings only and never performs a lifecycle transition")),
        Fragment(S1, Operative("- G704 duplicate-supervisor compares G676 writer identities with the installed record and routes the same key through G699 backoff/park")),
        Fragment(S1, Operative("- first-cycle proof failure is named `first-cycle-proof-failed` and names both log paths; a post-install cycle with writer identity is the only success evidence")),
        Fragment(S1, Operative("- do not treat one blocked/idle poll as a settled transition")),
        Fragment(S1, Operative("- do not silently remove a parked key or infer auto-resolution")),
        Fragment(S1, Operative("- do not use a repeated same-key finding to authorize a merge, closeout, label change, or other workflow mutation")),
        Fragment(S1, Operative("- do not accept a bound below the interval, infer first-cycle liveness from a loaded service/PID, or hide startup failure in an unlabeled log")),
        Fragment(S1, Operative("- do not use terminal content to prove duplicate supervision and do not auto-kill, stop, elect, or rank a writer")),
        Fragment(
            S1,
            Operative("> **Authority boundary:** This is an observation and wake-hygiene policy only."),
            Scaffold(" "),
            Operative("`intent-cli`/GitHub remain authoritative for workflow state; the supervisor may record, wake the owning role, and surface evidence, but never clears work or changes lifecycle state.")),
        Fragment(S1, Descriptive("G707: before emitting a registration or live-idle finding, supervision corroborates it against contradictory non-terminal observations already collected in the same cycle.")),
        Fragment(S1, Operative("- **same-cycle rule** — Consult the recorded seat-state agent_status and interactive_ready observations for the same-cycle workspace/pane before emission; idle or working is evidence that the registration/live-idle conclusion needs corroboration.")),
        Fragment(S1, Operative("- **contradicting observations**")),
        Fragment(S1, Operative("- registration-lost-process-present versus same-cycle seat-state agent_status=working or idle")),
        Fragment(S1, Operative("- live-idle-no-report versus same-cycle seat-state agent_status=working")),
        Fragment(S1, Operative("- a same-cycle interactive_ready=true observation also contradicts either conclusion")),
        Fragment(S1, Operative("- **conflict kind** — `observation-conflict`")),
        Fragment(S1, Operative("- **self-verifying fields** — `registration_definition`, `registration_lookup`, `registration_result`, `consulted_observations`")),
        Fragment(S1, Operative("- **inconclusive rule** — An inconclusive conflict begins with verification and names the consulted producers; it prescribes no automatic action and never authorizes destructive automation.")),
        Fragment(S1, Operative("- **genuine absence rule** — When no same-cycle non-terminal seat observation exists, a verified absent seat remains eligible to emit seat-absent or registration-lost-process-present.")),
        Fragment(S1, Operative("- **recurrence rule** — The single observation-conflict per recorded seat is a same-key observation: G699 repeat backoff and park state apply, while a new key remains immediate.")),
        Fragment(
            S1,
            Operative("> **Corroboration authority boundary:** Corroboration changes only the observation classification and evidence."),
            Scaffold(" "),
            Operative("It does not alter canonical workflow state, ownership, or the observation-only boundary.")),
        Fragment(
            S1,
            Descriptive("- **blocking dialog** — the scan sees: an approval, selection, or trust prompt waiting for input."),
            Scaffold(" "),
            Operative("Recovery: record it and route it to the canonical adjudication surface under the durable per-team policy; direct design relay is forbidden and absent policy is escalate-only.")),
        Fragment(
            S1,
            Descriptive("- **agent-absent** — the scan sees: a SHELL PROMPT where an agent should be — the pane looks like an ordinary terminal, often with a resume hint left on screen."),
            Scaffold(" "),
            Descriptive("The agent exited; it may have reported startup successfully seconds earlier."),
            Scaffold(" "),
            Operative("Recovery: RELAUNCH THROUGH THE SHIM: type the launch into the pane's interactive shell (never spawn the executable), recreating the app-server first when it is the thing that died."),
            Scaffold(" "),
            Operative("Set the permission mode with the LAUNCH FLAG (e.g."),
            Scaffold(" "),
            Operative("`--permission-mode`) rather than trying to switch it afterwards: a workspace manager's synthetic key injection cannot be relied on for mode switching — plain keys are delivered, but modifier chords such as shift+tab are not delivered faithfully (observed across multiple teams)."),
            Scaffold(" "),
            Transport("Then run the FULL verified-liveness sequence again — report, settle delay, all three checks.")),
        Fragment(
            S1,
            Transport("> **Re-arm across restarts:** supervision schedulers are session-scoped: a `/loop`, an automation, or an attached monitor dies with the design session that hosts it, and nothing announces that it stopped."),
            Scaffold(" "),
            Transport("Every supervision layer must either survive a design-session restart or be RE-ARMED as the first act of the new session — treat re-arming as part of starting the session, not as an optional follow-up."),
            Scaffold(" "),
            Descriptive("Field cost of forgetting: a claim-now lost inside a session-restart window left a published issue stalled for 5.5 HOURS because no supervision layer happened to be running.")),
        Fragment(
            S1,
            Operative("> **Detection, adjudication, and escalation boundary (G682/G683/G690):** the canonical adjudication surface is the only bounded answer path; design may use it only for a declared class/scope capability with no hard-floor tag and matching pane/state-sequence/text-hash CAS."),
            Scaffold(" "),
            Operative("G683 supervision emits exact recipe-registry classes plus observed text; G689 additionally verifies the extracted shell AST and every segment against a scoped policy; unknown text stays escalate-only; unknown syntax and uncovered shell segments stay escalate-only."),
            Scaffold(" "),
            Operative("Only a validated matched pre-approve, declared capability, absent hard-floor tag, durable audit, and live CAS permit a bounded exact-scope answer; no shipped class or scope is design-answerable in this slice.")),
        Fragment(S1, Operative("- **confirmations of work the design thread itself requested** — verify: answer only through canonical `notify adjudicate` when an exact registry class, validated pre-approve, declared capability, absent hard-floor tag, and live pane/state-sequence/text-hash CAS all match; shipped classes remain orchestration-only.")),
        Fragment(S1, Operative("- **command approvals verified read-only** — verify: for non-shell attended cases only, use the same exact registry + validated pre-approve + durable-audit path through canonical adjudicate; G689 shell-command is not read-only authority even for project-test, so every AST segment remains orchestration-only; otherwise escalate.")),
        Fragment(
            S1,
            Operative("- **trust screens for hooks the design thread itself installed** — verify: eliminate it through agent-side allow configuration recorded in the G636 kind recipe fields."),
            Scaffold(" "),
            Operative("If it remains, only the exact validated/audited orchestration path may answer it; otherwise escalate.")),
        Fragment(
            S1,
            Operative("- **operator-preauthorized mode changes** — verify: preauthorization alone is insufficient: require exact registry class, validated rule, durable audit, declared capability, hard-floor check, live CAS, and bounded execution."),
            Scaffold(" "),
            Operative("Unknown text escalates; no shipped class is design-answerable.")),
        Fragment(S1, Operative("- **unreadable or unverifiable dialogs** — if the pane content cannot be read, or the claim it makes cannot be verified, there is nothing to base an answer on — answering would be guessing on the operator's behalf.")),
        Fragment(S1, Operative("- **destructive or irreversible approvals** — deletions, force operations, overwrites, and anything else that cannot be undone are the operator's call — the cost of a wrong answer is unbounded and unrecoverable.")),
        Fragment(S1, Operative("- **choices that embed a product or design decision** — a dialog that picks behavior, scope, or defaults is design content, and design content goes through the operator and the design↔orchestrator double-check — not through whoever happens to be unblocking a pane.")),
        Fragment(
            S1,
            Operative("- **credential, security, and permission waits** — these are in G690's hard risk floor and are never answerable by the design thread, with or without prior authorization: they always remain unanswered and escalate to the operator."),
            Scaffold(" "),
            Operative("No grant makes them answerable.")),
        Fragment(
            S1,
            Operative("> **Boundary:** UNSTICKING A SESSION IS NOT DECIDING FOR IT."),
            Scaffold(" "),
            Operative("The design thread's job is to keep the session layer alive so the role can do its own work — not to make the role's choices, and not to make the operator's."),
            Scaffold(" "),
            Operative("It never relays keystrokes or bypasses the canonical adjudication checks."),
            Scaffold(" "),
            Descriptive("This preserves four judgment-bearing threads plus one supervision process."),
            Scaffold(" "),
            Descriptive("Measured 2026-08-11 in workspace wK, Claude app safety blocked the relay and nonexistent `/approvals` advice failed; recipe-first launch plus orchestration-owned policy is the durable remedy.")),
        Fragment(S1, Transport("- **provisioning** — Provisioning is NOT repeated here — see `Terminal-workspace provisioning` for role folders, workspace topology, shim-safe launch, actas/readiness, and the exclusivity/handover rules this section supervises.")),
        Fragment(
            S1,
            Operative("- **watchdog safety rules** — The watchdog safety rules apply to ALL supervision verbatim: no duplicate delegation, no clearing a permission prompt, no cancelling or resetting in-flight work, no force-closing an issue/PR, and no speculative durable-state surgery (no hand-edited labels, queue-state, or host metadata)."),
            Scaffold(" "),
            Descriptive("See `Design-thread watchdog (recommended safety net)`.")),
        Fragment(
            S2,
            Operative("A hold blocked on a DESIGN DECISION must be visible and bounded."),
            Scaffold(" "),
            Operative("Visible: it is recorded as a clarification artifact through the canonical clarify surface, so `automation stalled-work` and `automation heartbeat` can see it — an agmsg message alone is invisible to every supervision layer."),
            Scaffold(" "),
            Descriptive("Bounded: the operator may pre-delegate enumerated, mechanically fact-checkable decision classes so a correction both threads can verify from repository facts does not wait on design at all."),
            Scaffold(" "),
            Descriptive("Measured cost of getting this wrong: a nine-hour hold on a one-line wording ruling while every technical check was green and `stalled-work` reported `stalled=false` throughout.")),
        Fragment(
            S2,
            Operative("When the orchestrator or the reviewer blocks on a design decision, it RECORDS A CLARIFICATION ARTIFACT through the canonical clarify surface, in addition to whatever agmsg message it sends."),
            Scaffold(" "),
            Descriptive("The artifact is what makes the hold detectable; the message is only a notification.")),
        Fragment(S2, Operative("Record these fields:")),
        Fragment(S2, Descriptive("- domain — the blocked domain (`__DOMAIN__`), so the artifact is scoped to the right pipeline.")),
        Fragment(S2, Descriptive("- blocking execution unit — the unit that cannot proceed until this is answered.")),
        Fragment(S2, Operative("- question — what design must decide, stated so someone who was not in the thread can answer it.")),
        Fragment(S2, Operative("- recommended answer — when the asking thread already believes it knows the answer, state it and cite the facts that support it; design then confirms or overrides rather than starting from scratch.")),
        Fragment(
            S2,
            Operative("> **Contract violation:** An agmsg-only hold is a CONTRACT VIOLATION, not a shortcut."),
            Scaffold(" "),
            Descriptive("A block that exists only as messages is invisible to `stalled-work`, to `heartbeat`, and therefore to every watchdog and every operator glance — which is exactly how a nine-hour hold passed unnoticed with the pipeline reporting healthy."),
            Scaffold(" "),
            Descriptive("If you are waiting on design, the artifact exists; if the artifact does not exist, you are not waiting, you are stalled.")),
        Fragment(S2, Operative("- record the hold — `intent-cli clarify open` (the canonical clarify surface; never hand-write the artifact)")),
        Fragment(S2, Operative("- see what is open — `intent-cli clarify list`")),
        Fragment(S2, Operative("- answer it — `intent-cli clarify answer` (design, or the operator on escalation)")),
        Fragment(S2, Operative("- confirm it is visible — `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json` reports `design-decision-pending`")),
        Fragment(S2, Descriptive("Paste-ready — the OPEN artifact carries the real content, not a packet-derived synthesis:")),
        Fragment(S2, Operative("intent-cli clarify open <execution-unit> \\")),
        Fragment(S2, Operative("--question \"<the actual design-blocking question, answerable by someone outside the thread>\" \\")),
        Fragment(S2, Operative("--recommended-answer \"<what you believe the answer is, when you believe you know it>\" \\")),
        Fragment(S2, Operative("--evidence \"<the repository facts that support the recommendation>\"")),
        Fragment(
            S2,
            Descriptive("The reviewer's hold rule is refined so a green-technical review never becomes an untracked wait."),
            Scaffold(" "),
            Operative("Evaluate what is actually pending before holding.")),
        Fragment(
            S2,
            Operative("- **resolve under granted authority when** — Technical checks are GREEN and the only pending item is NON-SEMANTIC and MECHANICALLY FACT-CHECKABLE from repository facts — resolve it under bounded default authority (below), log the resolution with the verifying facts, and proceed."),
            Scaffold(" "),
            Operative("Do not hold a green review on a question whose answer both threads can derive and cite.")),
        Fragment(
            S2,
            Operative("- **record a clarification otherwise** — Anything else — a semantic or product question, a fact you cannot verify, or a class the operator has not delegated — becomes a recorded clarification and a VISIBLE pending state."),
            Scaffold(" "),
            Descriptive("The review is still held; the difference is that the hold is now on disk and detectable.")),
        Fragment(
            S2,
            Operative("> **Never an untracked wait:** there is no third option where the reviewer simply waits and says so in a message: either the item is resolved under granted authority with its evidence, or a clarification artifact exists."),
            Scaffold(" "),
            Descriptive("Silence with a message attached is the failure mode this rule exists to remove.")),
        Fragment(
            S2,
            Descriptive("BOUNDED DEFAULT AUTHORITY lets the operator pre-delegate a small, enumerated set of decision classes that can be settled by checking repository facts rather than by judgment."),
            Scaffold(" "),
            Descriptive("It exists so a count correction does not cost nine hours."),
            Scaffold(" "),
            Descriptive("It is bounded in every direction: granted, enumerated, evidence-logged, amendable, and never semantic.")),
        Fragment(
            S2,
            Operative("- **operator grant required** — GRANTED, never assumed."),
            Scaffold(" "),
            Operative("The authority applies only to classes the OPERATOR has explicitly pre-delegated for this domain."),
            Scaffold(" "),
            Operative("Absent a grant, every design decision goes to design as before — the default is unchanged, and no thread may infer a delegation from the fact that an answer seems obvious.")),
        Fragment(
            S2,
            Operative("- **count and enumeration corrections** — verify: the count is derivable from repository facts both threads can read — e.g. a slice count derived from the merged PR list and the issue's own enumeration."),
            Scaffold(" "),
            Operative("Cite the list and the derivation.")),
        Fragment(
            S2,
            Operative("- **wording corrections that follow from a cited fact** — verify: the corrected wording is entailed by a fact in the repository (a merged PR title, a label state, a retired unit's own record), and the reviewer and orchestrator AGREE on both the fact and the correction."),
            Scaffold(" "),
            Operative("Disagreement is not fact-checkable — it escalates.")),
        Fragment(S2, Operative("- **cross-reference and link corrections** — verify: the target exists (or does not) in the repository as cited — verifiable by reading the referenced file, heading, issue, or PR.")),
        Fragment(
            S2,
            Operative("- **identifier and metadata mismatches against a canonical source** — verify: the canonical source is named and read — e.g. a version in `eng/version.json`, a unit id in a packet, a label in the canonical palette."),
            Scaffold(" "),
            Operative("The canonical source wins; the resolution cites it.")),
        Fragment(
            S2,
            Operative("- **evidence logging** — MANDATORY EVIDENCE LOGGING."),
            Scaffold(" "),
            Operative("A resolution taken under this authority is recorded in the durable trail with the facts that verify it — what was decided, which repository facts entail it, and which threads agreed."),
            Scaffold(" "),
            Operative("An unlogged resolution is not a granted-authority resolution; it is an undocumented decision, and it is a violation of this contract.")),
        Fragment(
            S2,
            Descriptive("- **evidence sink** — The sink is the CANONICAL `clarify record` surface: the entry lands under `## Recently Resolved` in the domain's clarification return path (`intents/<domain>/clarifications/open.md`), where `Question` identifies the pending item, `Decision` records the decided value, and `Rationale` records the verified repository facts plus the reviewer/orchestrator agreement."),
            Scaffold(" "),
            Descriptive("The entry is durable and stays readable there, which is exactly what makes design's post-hoc amendment possible — design reads the recorded evidence and amends or reverses from it.")),
        Fragment(
            S2,
            Descriptive("- **post-hoc amendment** — DESIGN MAY AMEND POST HOC."),
            Scaffold(" "),
            Descriptive("A granted-authority resolution is provisional in design's eyes: design can review the logged evidence afterwards and amend or reverse the decision."),
            Scaffold(" "),
            Descriptive("The authority buys latency, not finality — proceeding does not close the question against design.")),
        Fragment(S2, Descriptive("Paste-ready evidence operation:")),
        Fragment(S2, Operative("cat > /tmp/authority-decision.md <<'EOF'")),
        Fragment(S2, Operative("<the pending item, identified so design can find it later>")),
        Fragment(S2, Operative("<the decided value>")),
        Fragment(S2, Operative("<the verified repository facts that entail it, and which threads agreed>")),
        Fragment(S2, Operative("EOF")),
        Fragment(S2, Operative("intent-cli clarify record --domain <domain> --from-file /tmp/authority-decision.md")),
        Fragment(
            S2,
            Operative("> **Semantic exclusion:** SEMANTIC AND PRODUCT DECISIONS ARE EXCLUDED, absolutely."),
            Scaffold(" "),
            Operative("Intent shaping, packet content and acceptance criteria, release scope, prioritization rulings, and anything requiring product or design judgment always go to design through the design↔orchestrator double-check rule, whose scope this contract does not touch."),
            Scaffold(" "),
            Operative("If settling the question requires deciding what SHOULD be true rather than checking what IS true, it is not fact-checkable and this authority does not reach it.")),
        Fragment(
            S2,
            Descriptive("While a clarification stays open, the design thread is reminded on a fixed cadence."),
            Scaffold(" "),
            Descriptive("A recorded hold that nobody re-surfaces is still a slow hold — the artifact makes it detectable, the reminder makes it noticed.")),
        Fragment(
            S2,
            Operative("- **sender** — The ORCHESTRATOR sends the reminder from its long-interval automation — the same wake that already runs the heartbeat check."),
            Scaffold(" "),
            Descriptive("No new scheduler, and the receivers stay loopless.")),
        Fragment(
            S2,
            Operative("- **interval** — 30–60 minute class — the same low-frequency band as the heartbeat and the design-thread watchdog."),
            Scaffold(" "),
            Descriptive("Faster polling recreates the churn the message-driven model removes; slower lets a hold sit past the point an operator would want to know.")),
        Fragment(
            S2,
            Operative("- **one per interval per clarification** — AT MOST ONE reminder per interval PER OPEN CLARIFICATION."),
            Scaffold(" "),
            Descriptive("Two open clarifications produce at most two reminders in a wake; one clarification never produces two reminders in the same interval no matter how many wakes fire."),
            Scaffold(" "),
            Descriptive("This is the same one-message discipline the watchdog already follows.")),
        Fragment(
            S2,
            Operative("- **stop on answer** — STOP ON ANSWER."),
            Scaffold(" "),
            Descriptive("Once the clarification is answered (or applied, or cancelled) it is no longer open, `design-decision-pending` clears on its own, and the reminders stop."),
            Scaffold(" "),
            Operative("Never keep reminding against an answered clarification, and never re-open one to keep a thread's attention.")),
        Fragment(
            S2,
            Descriptive("- **operator app** — The design thread runs in the OPERATOR APP by preference, which is what makes a reminder land either way: an OPEN design session receives the reminder immediately through its monitor, and a CLOSED one finds it waiting in the inbox on resume."),
            Scaffold(" "),
            Descriptive("Neither case requires design to be resident in the team workspace — there is no workspace-residency requirement here.")),
        Fragment(
            S2,
            Descriptive("- **detection** — Detection is `design-decision-pending` in `automation stalled-work`: it reads the domain's OPEN clarification artifacts and reports each with its age, blocking execution unit, and question summary, and `automation heartbeat` carries it in `message_body` like any other kind."),
            Scaffold(" "),
            Operative("Confirm a hold is visible with `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`; if the hold is real but the kind is absent, the clarification artifact was never recorded — which is the contract violation above, not a detector bug.")),
        Fragment(
            S3,
            Operative("Assume you are NOT alone on this machine."),
            Scaffold(" "),
            Descriptive("Several project teams run simultaneously, and every substrate below is shared across all of them — the workspace manager's server, the agmsg run directory, the codex app-servers, the host repo."),
            Scaffold(" "),
            Descriptive("`Terminal-workspace provisioning` and `Design-thread workspace supervision` describe how to build and keep ONE team; this section is what keeps that team from damaging another."),
            Scaffold(" "),
            Operative("It narrows the OBJECTS you may act on to your own team's; it does not widen or narrow what you may DO, so the supervision authority boundary applies unchanged."),
            Scaffold(" "),
            Descriptive("Operator incident (2026-07-29): with several teams live, one project's design thread damaged another project's resources and the operator had to intervene by hand.")),
        Fragment(
            S3,
            Operative("Before you touch anything, establish that it belongs to YOUR team."),
            Scaffold(" "),
            Operative("Attribution is a positive result from the keys below — not the absence of evidence that it belongs to someone else, and not a name that merely looks familiar.")),
        Fragment(S3, Operative("Attribution is required before any of these:")),
        Fragment(S3, Descriptive("- injecting keys or text into a pane")),
        Fragment(S3, Descriptive("- killing a process")),
        Fragment(S3, Descriptive("- closing or restructuring a workspace")),
        Fragment(S3, Descriptive("- removing or rewriting a state file")),
        Fragment(S3, Operative("Verify ownership with all four keys:")),
        Fragment(
            S3,
            Operative("- **workspace label** — the workspace is labelled with YOUR team/project name."),
            Scaffold(" "),
            Descriptive("A workspace you did not create and cannot name is not yours.")),
        Fragment(
            S3,
            Operative("- **pane cwd** — the pane's working directory is one of YOUR team's dedicated role folders."),
            Scaffold(" "),
            Descriptive("A pane whose cwd you do not recognize belongs to someone.")),
        Fragment(
            S3,
            Operative("- **process cwd** — the process's own working directory — read it per pid before any kill, exactly as the 2026-07-27 migration did when it spared another project's processes."),
            Scaffold(" "),
            Descriptive("A pid list filtered only by process NAME attributes nothing.")),
        Fragment(S3, Descriptive("- **agmsg `(team, role)` file naming** — agmsg run-directory state files are named per `(team, role)`; a file whose team segment is not yours is another team's bridge/watcher state, however broken it looks.")),
        Fragment(
            S3,
            Operative("> **Unverifiable = read-only:** if you cannot positively establish ownership, the object is READ-ONLY to you: you may look and you may report — you may not mutate."),
            Scaffold(" "),
            Operative("Escalate to the operator instead of guessing: a wrong guess here is another team's outage, and the cost is theirs rather than yours, which is exactly why the default has to be refusal.")),
        Fragment(
            S3,
            Operative("- **one workspace per team** — one workspace per team, labelled with the team/project name."),
            Scaffold(" "),
            Operative("Never reuse, repurpose, or borrow another team's workspace or its panes — not even an idle-looking one."),
            Scaffold(" "),
            Descriptive("A workspace is the unit an operator reads to know whose work is whose; sharing one collapses that.")),
        Fragment(
            S3,
            Operative("- **team-exclusive role folders** — one folder belongs to exactly ONE team."),
            Scaffold(" "),
            Operative("Never launch your agents in another team's folders."),
            Scaffold(" "),
            Transport("This is the same folder-scoping fact that forbids two roles sharing a folder within a team (G521) — agmsg identity and the codex bridge are folder-scoped, so an agent started in another team's folder takes over THEIR identity and delivery, not just its own.")),
        Fragment(
            S3,
            Operative("When you find damage — including damage you caused — recovery is NON-DESTRUCTIVE."),
            Scaffold(" "),
            Descriptive("The instinct to tidy up is the failure mode: a broken artifact belonging to another team is still their evidence, and deleting it destroys their ability to diagnose what happened.")),
        Fragment(
            S3,
            Operative("- **preserve theirs** — PRESERVE and SET ASIDE another project's damaged artifacts — rename, move aside, or simply leave them in place and report."),
            Scaffold(" "),
            Operative("Never delete another team's workspace, panes, folders, processes' state, or files, however broken they look."),
            Scaffold(" "),
            Operative("Tell the operator and the affected team's thread what you found and what you set aside.")),
        Fragment(
            S3,
            Transport("- **rebuild yours** — REBUILD YOUR OWN fresh rather than repairing in place: create a new workspace, new panes, new role folders as needed, and re-run provisioning."),
            Scaffold(" "),
            Descriptive("Your own damaged artifacts may also be set aside rather than deleted when they carry evidence worth keeping.")),
        Fragment(S3, Descriptive("> **Recovery defaults to RECREATE, NOT CLEANUP.**")),
        Fragment(
            S4,
            Descriptive("In orchestrator-message mode the normal steady state is MESSAGE-DRIVEN: implementation/review receivers already send accepted/progress/completed/blocked replies to the orchestrator, and those replies wake the orchestrator path — routine fast polling is NOT required."),
            Scaffold(" "),
            Descriptive("An orchestrator timer (Codex automation every 5m, or Claude same-thread `/loop 5m`) remains SUPPORTED but only as an explicit FALLBACK/LEGACY polling option for an operator who intentionally wants scheduled polling instead of message-driven wakes."),
            Scaffold(" "),
            Descriptive("Either way the implementation and review threads stay long-lived LOOPLESS receivers."),
            Scaffold(" "),
            Descriptive("The RECOMMENDED default safety net for message-driven steady state is a 30-minute-class design-thread watchdog (see Design-thread watchdog), not a fast orchestrator loop.")),
        Fragment(S4, Descriptive("- scheduled thread when an explicit timer is used: `orchestrator` (the only thread ever scheduled)")),
        Fragment(
            S4,
            Operative("- **receivers are loopless** — Implementation and review threads are loopless receivers: do NOT start a recurring timer/loop in a receiver thread for this domain/repo."),
            Scaffold(" "),
            Transport("A receiver waits for an agmsg delegation, acts once, replies once, and waits again."),
            Scaffold(" "),
            Descriptive("Receivers are NEVER scheduled; when an explicit fallback/legacy timer is used (message-driven wakes are the default), the orchestrator is the only thread ever scheduled.")),
        Fragment(
            S4,
            Transport("OPTIONAL fallback/legacy polling — Codex automation (run every 5 minutes) for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__AGENT__`: on each run perform exactly ONE orchestrator wake — check design-side progress and agmsg replies, ask intent-cli for state (`intent status`, `worker next-action --github-only`, `automation host-review-preflight`), verify the GitHub facts (CI/approval/merge/closeout), then send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER (implementation, review), NOT at-most-one-message overall, so a publish plus its same-wake delegation, one repair per stalled receiver, and one operator escalation may all go out together — and exit."),
            Scaffold(" "),
            Transport("Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling."),
            Scaffold(" "),
            Operative("Do not run implementation/review loops; they are loopless receivers.")),
        Fragment(
            S4,
            Transport("OPTIONAL fallback/legacy polling — Claude same-thread setup for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__`: in the orchestrator thread run `/loop 5m` with the orchestrator prompt so the same thread re-wakes every 5 minutes."),
            Scaffold(" "),
            Transport("Each wake does exactly one orchestrator pass (read replies, check intent-cli / GitHub state, send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER, NOT at-most-one-message overall)."),
            Scaffold(" "),
            Transport("Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling."),
            Scaffold(" "),
            Transport("Do NOT also launch `/loop` in the implementation or review threads — those are loopless receivers driven only by your delegations.")),
        Fragment(S4, Transport("- A wake is triggered either by an incoming agmsg reply from implementation/review (the message-driven steady state) or by the optional fallback/legacy timer firing — either trigger runs exactly one orchestrator pass below.")),
        Fragment(S4, Operative("- Check design-side progress: newly published packets/issues and intent status changes via `intent-cli intent status --domain __DOMAIN__ --format json`.")),
        Fragment(S4, Transport("- Read pending agmsg replies from the implementation/review receivers (signals only — re-verify against intent-cli / GitHub).")),
        Fragment(S4, Operative("- Ask intent-cli for worker state: `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.")),
        Fragment(S4, Operative("- Check host review readiness: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json`.")),
        Fragment(S4, Operative("- Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge state, and closeout/label state.")),
        Fragment(
            S4,
            Operative("- Classify each open PR's CI: pending = wait using the named mode-specific CI re-check producer (no message); green = delegate review/closeout; red = repair or escalate by ownership; stuck = escalate."),
            Scaffold(" "),
            Descriptive("Pending CI is normal progress, not a reason to message the operator.")),
        Fragment(S4, Operative("- Detect stale blockers and no-reply receivers: a delegation with no accepted/progress reply within the expected window, or a thread stuck off the official workflow.")),
        Fragment(S4, Operative("- On a no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check: send one non-destructive status-request, check read-only intent-cli/GitHub facts, keep watching if there is progress, treat waiting-permission as an operator notice (never auto-clear), and only after repeated no-reply with no progress send one idempotent re-entry or escalate.")),
        Fragment(S4, Operative("- If intent-cli reports an `issue-cut-ready` candidate and all gates pass (same-domain or routed, complete contract, no open clarification, dependencies satisfied, under WIP, clean host-sync/preflight), publish ONE issue this wake via canonical publish-flow / issue-publish, verify it, THEN delegate that same issue to implementation in THIS SAME WAKE (G524) — do not ask the operator to create it, and do not stop after publishing to wait for a future wake to send the delegation.")),
        Fragment(S4, Operative("- If the candidate has unmet dependencies, plan the chain instead of pausing: act on the EARLIEST unmet resolvable dependency (publish or route it), keep the dependent held, and escalate only ambiguous/cycle/cross-domain-unrouted cases.")),
        Fragment(S4, Operative("- The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message overall (G524): this wake's actions may include a publish plus its same-wake delegation, one repair message per stalled receiver, one operator escalation, and handling any pending receiver reports, all together.")),
        Fragment(S4, Operative("- Send workflow notifications only through `intent-cli notify`; it resolves the recorded session-layer mode and validates the recipient before delivery, failing closed on an unknown role (G524/G578).")),
        Fragment(
            S4,
            Operative("- When an outcome is applied elsewhere, a review round supersedes the predecessor, or a recovery/re-dispatch path makes a report no longer owed, explicitly record the open delegation's disposition with `intent-cli notify dispose --domain <domain> --team <team> --task-id <task-id> --kind applied-elsewhere|superseded --actor <actor> --reason <reason> [--applied-outcome-evidence <evidence>|--superseding-task-id <task-id>] --write --format json`."),
            Scaffold(" "),
            Operative("This is an attributed judgment, never an automatic inference; it ends the report expectation but never refuses or drops a late report.")),
        Fragment(
            S4,
            Operative("- Apply the design-thread escalation filter: keep routine progress / CI-wait / success / closeout / idle internal; surface to the design thread ONLY human-needed decisions, with structured evidence and the exact decision needed."),
            Scaffold(" "),
            Operative("Never hide a failure that needs a human.")),
        Fragment(
            S4,
            Operative("- End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item it reports before sleeping — never leave one for an unscheduled next wake; `awaiting-operator-merge` is informational patient state and is never urged or age-escalated."),
            Scaffold(" "),
            Operative("Escalate explicitly only when another item is genuinely blocked on an operator decision."),
            Scaffold(" "),
            Operative("This includes a `backlog-ready-idle` item (G544, empty WIP + a ready packet + no activity past the idle threshold) — publish and delegate it in THIS wake, the same as any other issue-cut-ready candidate; only announce a following wake will handle it when that wake is actually scheduled.")),
        Fragment(
            S4,
            Operative("- G673 last-net honesty: if stalled-work or heartbeat returns `detection_available=false` / `cause=github-api-quota-exhausted`, do not treat an empty item list as healthy."),
            Scaffold(" "),
            Operative("Record the exhausted resource and `reset_at`, retain and review `partial=true` local findings, and let orchestration decide whether to wait deliberately; no automatic retry, sleep, reset scheduling, or request budgeting is performed."),
            Scaffold(" "),
            Operative("Issue #1442 is the separately attributed remote-herdr measurement; this host's same-day G667 observation is corroboration.")),
        Fragment(
            S4,
            Operative("- **repair** — REPAIR routine off-rail states yourself by messaging the appropriate thread back onto the official intent-cli workflow — e.g. a receiver that stalled, skipped `worker complete`, applied a label by hand, or has not replied."),
            Scaffold(" "),
            Operative("Routine recovery is a repair message, not an escalation."),
            Scaffold(" "),
            Operative("When that recovery or an outcome application means the original report will never arrive, write an explicit `notify dispose` disposition at the cause site with its actor, reason, and applicable superseding task or applied-outcome evidence; never silently close the pending record and never reject a late report.")),
        Fragment(
            S4,
            Operative("- **escalate** — ESCALATE to the operator ONLY for: product/design judgment, credentials or security, a destructive local action, or an unresolved canonical ambiguity (intent-cli/GitHub facts genuinely conflict or are missing)."),
            Scaffold(" "),
            Operative("Do not escalate states you can repair by message.")),
        Fragment(
            S5,
            Operative("Routine next-slice issue publication is an ORCHESTRATOR responsibility, not an operator question."),
            Scaffold(" "),
            Operative("When intent-cli reports a candidate as `issue-cut-ready` and ALL safety gates pass, the orchestrator publishes it itself through canonical intent-cli commands instead of stopping to ask the operator to create the GitHub issue."),
            Scaffold(" "),
            Operative("Publish AT MOST ONE issue per wake, then verify, THEN delegate that same issue to the implementation thread in THE SAME WAKE (G524) — publish and delegate complete together; never defer the delegation to an unscheduled \"next wake\", since no other trigger will ever wake the orchestrator to send it (this was the single largest measured stall class in message-driven orchestration, ~60 hours across G807/G809/G810/G812).")),
        Fragment(S5, Descriptive("- one_per_wake: yes")),
        Fragment(S5, Operative("- Same-domain context (`__DOMAIN__`), or an explicitly routed multi-domain delegation (domain, target repo, destination thread) — never publish a cross-domain candidate without explicit routing.")),
        Fragment(S5, Operative("- The packet contract is complete: no missing required sections (goal, in/out of scope, acceptance criteria, base-branch policy).")),
        Fragment(S5, Operative("- No open clarification or contract ambiguity on the candidate.")),
        Fragment(S5, Operative("- Dependencies are satisfied — every dependency execution unit is completed or already cut; never publish ahead of an uncut dependency.")),
        Fragment(S5, Operative("- Under the WIP cap — no in-progress blocker that should pace the queue first.")),
        Fragment(S5, Operative("- Clean host-sync / preflight: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json` and the publish preflight report no blocker, and the target repo/domain is unambiguous.")),
        Fragment(S5, Operative("- Missing contract sections — hold, do not publish.")),
        Fragment(S5, Operative("- Open clarification / ambiguous contract — hold or escalate one operator decision.")),
        Fragment(S5, Operative("- Dependency mismatch — an uncut or incomplete dependency; hold (publishing ahead would violate the dependency contract).")),
        Fragment(S5, Operative("- WIP cap reached — let the in-progress work drain first.")),
        Fragment(S5, Operative("- Host-sync blocker or failed preflight — fix the sync via intent-cli, do not force the publish.")),
        Fragment(S5, Operative("- Ambiguous target repo or domain (no explicit routing in multi-domain) — escalate rather than guess.")),
        Fragment(S5, Operative("- intent-cli issue publish-flow <execution-unit> --repo __OWNER__/__REPO__ --write --format json")),
        Fragment(S5, Operative("- intent-cli automation issue-publish --write --format json")),
        Fragment(S5, Operative("- Never raw `gh issue create` or `gh ... --add-label`; publication and the `intent-target` label go through the canonical intent-cli surfaces only.")),
        Fragment(S5, Operative("- Confirm via intent-cli / GitHub (not chat) that the issue exists with the expected execution-unit body and the `intent-target` label.")),
        Fragment(S5, Operative("- Confirm the durable workflow state (queue-state / linkage / label) reflects the publish through intent-cli surfaces.")),
        Fragment(
            S5,
            Transport("- Immediately after verification, in THIS SAME WAKE, delegate implementation over agmsg (G524) — do not stop after publishing and wait for a future wake to send the delegation."),
            Scaffold(" "),
            Transport("The implementation receiver still derives its target from `intent-cli worker next-action`, not the agmsg text.")),
        Fragment(
            S6,
            Operative("G524/G578: send workflow notifications only through `intent-cli notify`."),
            Scaffold(" "),
            Operative("It validates the agmsg roster or herdr logical-role mapping before delivery and returns a named failure instead of a silent no-op."),
            Scaffold(" "),
            Operative("Fix the active transport's role registration or mapping before retrying; never guess a role name or bypass notify with a handwritten transport call.")),
        Fragment(S6, Descriptive("- Field-observed loss: 8 dispatches addressed to `review` were silently lost when the registered role was `reviewer` — agmsg neither delivered nor reported the mismatch.")),
        Fragment(
            S7,
            Descriptive("Setup does not stop at role registration."),
            Scaffold(" "),
            Transport("After the agmsg roles are registered and ready, the DESIGN thread starts (or resumes) orchestration by sending ONE message to the orchestrator; the orchestrator then drives the loop autonomously and returns to design only for human decisions.")),
        Fragment(S7, Transport("First message — design → orchestrator (paste into the design thread):")),
        Fragment(S7, Transport("{\"to\":\"orchestrator\",\"type\":\"start\",\"domain\":\"__DOMAIN__\",\"target_repo\":\"__OWNER__/__REPO__\",\"requested_action\":\"<e.g. publish the next ready slice and drive it to a PR>\",\"constraints\":\"one action per wake; escalate to design ONLY for human decisions (product/clarification, release/credentials/security, destructive actions, unresolved blockers)\"}")),
        Fragment(
            S7,
            Operative("- **autonomous publish** — If `intent-cli` reports the next slice `issue-cut-ready` and all publish gates pass (see Next-slice publication), the orchestrator creates/publishes ONE GitHub issue ITSELF via canonical intent-cli commands (`issue publish-flow` / `automation issue-publish`) — it does NOT ask design to do each step."),
            Scaffold(" "),
            Operative("At most one issue per wake; verify after publishing before delegating implementation.")),
        Fragment(
            S7,
            Operative("- **escalation boundary** — Routine delegation (publish, delegate, CI wait, review, closeout) stays orchestrator↔receivers and does NOT go to design."),
            Scaffold(" "),
            Operative("Return to DESIGN only for human decisions — product/design clarification, release/credentials/security, destructive actions, or an unresolved blocker — using the structured escalation message (reason / current_state / evidence / decision_needed).")),
        Fragment(
            S7,
            Transport("- **design inbox workflow** — The design thread is a loopless receiver and reads on demand."),
            Scaffold(" "),
            Transport("To pick up escalations, the human (or the design thread) checks the design inbox with `inbox.sh` — especially when monitor delivery did not appear live or the design session started after the orchestrator sent."),
            Scaffold(" "),
            Descriptive("Read, decide/reply, then the orchestrator continues.")),
        Fragment(
            S8,
            Operative("The design thread acts as a TRAFFIC CONTROLLER, not an implementer."),
            Scaffold(" "),
            Operative("It coordinates through the orchestrator and only surfaces human-needed items — it does not drive implementation/review or mutate workflow state itself.")),
        Fragment(
            S8,
            Descriptive("1."),
            Scaffold(" "),
            Transport("Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.")),
        Fragment(
            S8,
            Descriptive("2."),
            Scaffold(" "),
            Operative("Check intent-cli / GitHub READ-ONLY state (`intent status`, `worker next-action`, PR/issue/labels) to ground any decision — never trust an agmsg message as state.")),
        Fragment(
            S8,
            Descriptive("3."),
            Scaffold(" "),
            Operative("Send the orchestrator a state update or a nudge (start/resume); do not drive implementation/review yourself.")),
        Fragment(
            S8,
            Descriptive("4."),
            Scaffold(" "),
            Operative("Do NOT directly mutate implementation/review work, labels, or host metadata — that is the orchestrator/receivers' job through intent-cli.")),
        Fragment(
            S8,
            Descriptive("5."),
            Scaffold(" "),
            Operative("Summarize ONLY human-needed items to the human; keep routine progress internal.")),
        Fragment(
            S8,
            Descriptive("6."),
            Scaffold(" "),
            Operative("PRIMARY DESIGN DUTY — intent-tree co-evolution: the intent tree moves WITH development, not after it."),
            Scaffold(" "),
            Descriptive("Leaving the tree unupdated while implementation advances is a serious fault in its own right, not a deferred chore: a tree that describes a design the code no longer has is worse than no tree, because every downstream packet, review, and audit is written against it."),
            Scaffold(" "),
            Operative("Reinforce the tree in the same wake that changes the surface it describes.")),
        // G698: keep the shared role-attributed duty typed at sentence
        // granularity; the renderer emits this as one numbered list item.
        Fragment(
            S8,
            Descriptive("7."),
            Scaffold(" "),
            Operative(IntentTreeCoEvolutionDuty.RoleSplit),
            Scaffold(" "),
            Operative("Same-cadence write-back check: perform the packet's declared write-backs and RECORD them in the same closeout wake with `intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role design --write` (or `--role orchestration` when orchestration is recording its own mechanical duty)."),
            Scaffold(" "),
            Operative("Until the selected role is recorded, the unit stays visible as a `knowledge-writeback-pending` item in `automation stalled-work` / `automation heartbeat` — closing the PR does not clear it, and nothing here writes intent content on design's behalf.")),
        Fragment(
            S8,
            Descriptive("8."),
            Scaffold(" "),
            Operative("When progress blocks on a design judgment, record that wait before waiting: open judgment-wait with `--owner design`, query the existing record, and whoever supplies the judgment MUST resolve it with evidence."),
            Scaffold(" "),
            Operative("An answered-but-open record is a lie, not a completed design handoff.")),
        Fragment(
            S8,
            Descriptive("1."),
            Scaffold(" "),
            Operative("Confirm the orchestrator is actually scheduled and on a fresh turn (its `/loop` or Codex automation is running).")),
        Fragment(
            S8,
            Descriptive("2."),
            Scaffold(" "),
            Transport("Confirm it received your last message (`inbox.sh` on the orchestrator) — a pre-monitor send may be queued, not delivered live; resend after an ack.")),
        Fragment(
            S8,
            Descriptive("3."),
            Scaffold(" "),
            Operative("Confirm intent-cli actually reports an actionable item for THIS domain/repo (`worker next-action` / `intent status`) — idle may be correct (nothing to do).")),
        Fragment(
            S8,
            Descriptive("4."),
            Scaffold(" "),
            Operative("Only after these, escalate to the human as a structured decision.")),
        Fragment(
            S8,
            Operative("> **Context-only:** The design thread MAY send context to a receiver thread, but MUST mark it context-only (e.g."),
            Scaffold(" "),
            Operative("`context-only: <text>`) unless the orchestrator delegated the action — receivers act only on orchestrator delegations, not on design context.")),
        Fragment(
            S9,
            Descriptive("Orchestrated work creates temporary worktrees for implementation and review."),
            Scaffold(" "),
            Operative("Allocate them under a managed, allowlisted root inside the workspace and clean them up with `git worktree remove` — NEVER a raw `rm -rf` of an arbitrary `/tmp/intent-review-...` path."),
            Scaffold(" "),
            Descriptive("Safe cleanup design, not disabling approvals, is the right default: a destructive `rm -rf` approval prompt is the symptom of an unmanaged workspace.")),
        Fragment(
            S9,
            Operative("- **managed root** — Allocate temporary worktrees under a repo/workspace-scoped managed root — the `[project] worktree_root` (default `.intent-cli/worktrees/`), git-ignored — not arbitrary `/tmp/intent-review-...` paths."),
            Scaffold(" "),
            Descriptive("A managed root is allowlisted, predictable, and removable with `git worktree remove`.")),
        Fragment(
            S9,
            Operative("- **approval policy** — `approval_policy=never` / `danger-full-access` is NOT a substitute for safe cleanup design."),
            Scaffold(" "),
            Operative("Keep least-privilege approvals as the default; the goal is to never need a destructive `rm -rf` prompt, not to suppress the prompt.")),
        Fragment(S9, Operative("- Create each worktree under the managed root: `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`.")),
        Fragment(S9, Operative("- Keep the managed root git-ignored so it never pollutes the tree.")),
        Fragment(S9, Operative("- One worktree per role/unit; do not reuse a dirty worktree across units.")),
        Fragment(S9, Operative("- Remove a worktree only with `git worktree remove` (it refuses a dirty worktree) — never raw `rm -rf`.")),
        Fragment(S9, Operative("- Validate the target path is INSIDE the allowlisted managed root before removal.")),
        Fragment(S9, Operative("- Confirm the path is a registered git worktree (it appears in `git worktree list`).")),
        Fragment(S9, Operative("- Confirm the worktree state is clean (no uncommitted or untracked user work) before removing.")),
        Fragment(S9, Operative("- Prune stale registrations with `git worktree prune` after removal.")),
        Fragment(S9, Descriptive("- The target is OUTSIDE the allowlisted managed root.")),
        Fragment(S9, Descriptive("- The target is the repo root, `$HOME`, or a system path (`/`, `/tmp` root, etc.).")),
        Fragment(S9, Descriptive("- The path is not a registered git worktree.")),
        Fragment(S9, Operative("- The worktree has uncommitted or untracked user work — STOP and surface it; do not delete user work.")),
        Fragment(
            S10,
            Operative("Review delegation must carry the managed-worktree policy and require design-alignment evidence up front — not leave the reviewer to discover it."),
            Scaffold(" "),
            Descriptive("Dogfooding showed a reviewer allocate a raw `/tmp/...review...` worktree and Codex correctly ask to approve a destructive `rm -rf` — the RIGHT safety behavior for the WRONG workflow."),
            Scaffold(" "),
            Operative("The fix is a managed root, NOT weakening approval settings.")),
        Fragment(
            S10,
            Operative("- **managed worktree root** — Review worktrees use the SAME managed, workspace-local root as the rest of orchestrated work — the `[project] worktree_root` (default `.intent-cli/worktrees/`), e.g."),
            Scaffold(" "),
            Operative("`.intent-cli/worktrees/review-<unit>` — NEVER an arbitrary `/tmp/...review...` path.")),
        Fragment(
            S10,
            Operative("- **prohibited pattern** — PROHIBITED as the normal path: a raw `/tmp/...` review worktree, and a `rm -rf /tmp/... && git worktree add ...` cleanup chain."),
            Scaffold(" "),
            Operative("Reaching for this pattern is the signal to STOP and allocate under the managed root instead — not to ask the operator to approve the `rm -rf`.")),
        Fragment(S10, Operative("- **cleanup rule** — Cleanup is `git worktree remove <managed-path>` for a REGISTERED, CLEAN worktree only — confirmed via `git worktree list` and a clean `git status` first.")),
        Fragment(S10, Transport("- **unsafe/stale path rule** — A stale path that is NOT a registered git worktree, is OUTSIDE the managed root, or is dirty/unsafe is NEVER an operator `rm -rf` approval prompt — it is a STRUCTURED BLOCKER agmsg reply to the orchestrator (`status: blocked`) so the orchestrator can route the repair, not something the reviewer resolves by force-deleting an unmanaged path.")),
        Fragment(S10, Transport("Review delegation example (orchestrator → review):")),
        Fragment(S10, Transport("{\"delegate\":{\"domain\":\"<domain>\",\"execution_unit\":\"<unit>\",\"target_repo\":\"<owner/repo>\",\"pr\":\"<n>\",\"review_cwd\":\"/review/<domain>\",\"managed_worktree_policy\":\"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never /tmp\",\"design_alignment_required\":true,\"destination_thread\":\"review@<domain>\"}}")),
        Fragment(S10, Operative("Design-alignment sources a review reply may cite as checked:")),
        Fragment(S10, Descriptive("- packet — the authored packet content and acceptance criteria.")),
        Fragment(S10, Descriptive("- review-context — the review-context artifact for this PR/unit.")),
        Fragment(S10, Descriptive("- intent tree — the relevant intent-tree entries for the touched domain.")),
        Fragment(S10, Descriptive("- ADR / decision notes — any linked architecture or design-decision records.")),
        Fragment(S10, Descriptive("- relevant docs — user-facing or developer docs the change touches.")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Operative("Confirm you are the ONLY orchestrator for this domain/repo; if a second is detected, STOP and escalate (fail closed).")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Operative("Confirm domain scope: in single-domain mode, treat other-domain items visible in the host repo as OUT OF SCOPE (escalate, never delegate); in multi-domain mode, attach full routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree, base branch policy, destination thread) before each delegation."),
            Scaffold(" "),
            Descriptive("Visibility is not authorization, and an execution-unit prefix mismatch alone is not a wrong-repo signal.")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Transport("Read pending agmsg replies from the implementation/review threads (signals only — do not trust them as state).")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Operative("Ask intent-cli for the real state: `intent-cli intent status --domain __DOMAIN__ --format json` and `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Transport("Verify every GitHub fact an agmsg reply claims (PR merged, CI concluded, labels) before acting on it.")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Operative("The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER, not at-most-one-message overall (G524): a publish this wake must be delegated to implementation in this SAME wake — never defer that delegation to an unscheduled next wake — alongside any repair requests (one per stalled receiver) or one operator escalation.")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Operative("Send workflow notifications only through `intent-cli notify`; it resolves the recorded transport and validates the recipient before delivery, failing closed on an unknown role (G524/G578).")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Operative("Do not launch implement/review recurring timers for this domain/repo while orchestrating.")),
        Fragment(
            S11,
            Descriptive("1."),
            Scaffold(" "),
            Operative("End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item before sleeping — never leave one for an unscheduled next wake; `awaiting-operator-merge` is deliberately informational patient state, not actionable review debt, and receives no urge or age escalation."),
            Scaffold(" "),
            Operative("Escalate explicitly only when a different item is genuinely blocked on an operator decision.")),
        Fragment(S12, Descriptive("- agmsg is a message/progress/completion signal layer only; intent-cli and GitHub are authoritative for all workflow state.")),
        Fragment(S12, Operative("- No raw label mutation (`gh ... --add-label`/`--remove-label`); every label transition goes through intent-cli worker/automation.")),
        Fragment(S12, Operative("- No hand-editing queue-state, runs.jsonl, packets, or any host metadata (`.intent-cli/**`, `intents/**`).")),
        Fragment(S12, Operative("- agmsg never replaces semantic review or authorizes a merge; review/closeout decisions run through intent-cli review surfaces (G480).")),
        Fragment(S12, Operative("- Per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message: a publish's same-wake delegation, repair messages, an escalation, and receiver-report handling may all happen in one wake (G524); never defer a publish's delegation to an unscheduled future wake.")),
        Fragment(S12, Operative("- Use `intent-cli notify` for every workflow send; it validates the active transport's role source and fails closed on unknown or unavailable recipients (G524/G578).")),
        Fragment(S12, Operative("- End every wake with a stalled-work check (`automation stalled-work`, G523) and process any actionable item before sleeping; escalate explicitly rather than deferring silently.")),
        Fragment(
            S12,
            Descriptive("- Domain isolation: a host repo can hold several domains and one repo can serve several domains, so visibility is not authorization."),
            Scaffold(" "),
            Operative("Single-domain orchestrators ignore/escalate other-domain items; multi-domain orchestrators require explicit per-delegation routing."),
            Scaffold(" "),
            Descriptive("An execution-unit prefix mismatch alone is not a wrong-repo signal.")),
        Fragment(S12, Transport("- Fail closed on duplicate orchestrators for the same domain/repo, or when an agmsg reply conflicts with intent-cli/GitHub facts — STOP and escalate, never guess.")),
        Fragment(S12, Operative("- Allocate temporary worktrees under an allowlisted managed root and remove them with `git worktree remove`; never raw `rm -rf` of arbitrary temp paths, and `approval_policy=never`/`danger-full-access` is not a substitute for safe cleanup.")),
        Fragment(S12, Operative("- Never ask intent-cli to launch Claude/Codex/Copilot or any AI provider; intent-cli only emits text the human agent acts on.")),
        Fragment(S0, Descriptive("- **status: `blocked`**")),
        Fragment(
            S0,
            Descriptive("- blocked — existing implementation/review timer loops for this domain/repo would race the orchestrator (mixed-mode)."),
            Scaffold(" "),
            Operative("Stop the existing loops (or re-run with --existing-loop-policy will-stop) before starting orchestrator mode; receivers are never scheduled — orchestrator wakes are message-driven by default, with an explicit fallback/legacy timer as the only case where the orchestrator itself is scheduled.")),
        Fragment(S0, Descriptive("- **status: `setup-ready`**")),
        Fragment(S0, Transport("- setup-ready — register the three roles with the agmsg commands, paste the first prompts, then run the first validation.")),
        Fragment(S0, Transport("agmsg join.sh __TEAM__ orchestrator __OAGENT__ __ORCHPATH__")),
        Fragment(S0, Transport("agmsg delivery.sh set __DELIVERY__ __OAGENT__ __ORCHPATH__")),
        Fragment(S0, Transport("agmsg join.sh __TEAM__ implementation __IAGENT__ __IMPLPATH__")),
        Fragment(S0, Transport("agmsg delivery.sh set __DELIVERY__ __IAGENT__ __IMPLPATH__")),
        Fragment(S0, Transport("agmsg join.sh __TEAM__ review __RAGENT__ __REVIEWPATH__")),
        Fragment(S0, Transport("agmsg delivery.sh set __DELIVERY__ __RAGENT__ __REVIEWPATH__")),
        Fragment(
            S0,
            Transport("You are the ORCHESTRATOR thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__OAGENT__`, running from `__ORCHPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__)."),
            Scaffold(" "),
            Transport("Your steady state is MESSAGE-DRIVEN — implementation/review agmsg replies wake you; an orchestrator timer (Codex automation 5m or Claude `/loop 5m`) is an OPTIONAL fallback/legacy polling mode, not the default."),
            Scaffold(" "),
            Transport("You pace the implementation/review receivers over agmsg and never run their timers."),
            Scaffold(" "),
            Descriptive("See the full orchestrator prompt in the Thread prompts section.")),
        Fragment(
            S0,
            Transport("You are the IMPLEMENTATION thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__IAGENT__`, running from `__IMPLPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__)."),
            Scaffold(" "),
            Operative("You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait."),
            Scaffold(" "),
            Transport("Your worker target comes from `intent-cli worker next-action`, not the agmsg text."),
            Scaffold(" "),
            Descriptive("See the full prompt in the Thread prompts section.")),
        Fragment(
            S0,
            Transport("You are the REVIEW thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__RAGENT__`, running from `__REVIEWPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__)."),
            Scaffold(" "),
            Operative("You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait."),
            Scaffold(" "),
            Transport("Your worker target comes from `intent-cli worker next-action`, not the agmsg text."),
            Scaffold(" "),
            Descriptive("See the full prompt in the Thread prompts section.")),
        Fragment(S0, Operative("- Preflight all three cwds BEFORE mutating: `__ORCHPATH__` (orchestrator), `__IMPLPATH__` (implementation), `__REVIEWPATH__` (review) — clean `git status`, expected git remote/repo, expected branch/base, and no existing timer-loop for this domain/repo (see Preflight).")),
        Fragment(S0, Operative("- Existing-loop conflict check: confirm no implementation/review recurring timer is running for this domain/repo (implementation/review stay loopless whether the orchestrator runs message-driven or on an explicit fallback/legacy timer).")),
        Fragment(S0, Operative("- First read-only wake: run ONE confirm-only orchestrator wake — read state, send nothing.")),
        Fragment(
            S0,
            Transport("- Receiver readiness: ping each receiver and require an ack BEFORE any real delegation — a registered+configured role is not ready until it acks (see the Receiver readiness section)."),
            Scaffold(" "),
            Transport("A session launched before delivery was active may have missed earlier messages; resend or read with `inbox.sh`.")),
        Fragment(
            S3,
            Scaffold("|"),
            Descriptive(" substrate "),
            Scaffold("|"),
            Descriptive(" sharing unit "),
            Scaffold("|"),
            Descriptive(" ownership rule "),
            Scaffold("|")),
        Fragment(
            S3,
            Scaffold("|"),
            Descriptive(" workspace-manager server (e.g. the herdr server) "),
            Scaffold("|"),
            Descriptive(" one server process serving EVERY workspace on the machine "),
            Scaffold("|"),
            Descriptive(" ownership is per WORKSPACE, never the server."),
            Scaffold(" "),
            Operative("Act on your own workspace and its panes; never restart, reconfigure, or kill the shared server — doing so takes down every other team's workspace at once. "),
            Scaffold("|")),
        Fragment(
            S3,
            Scaffold("|"),
            Descriptive(" agmsg run directory (`~/.agents/skills/agmsg/run`) "),
            Scaffold("|"),
            Descriptive(" one directory holding bridge / watcher / app-server state for ALL teams "),
            Scaffold("|"),
            Descriptive(" ownership is per `(team, role)` FILE."),
            Scaffold(" "),
            Operative("Touch only files whose team segment is yours; never clear the directory wholesale to fix your own delivery — that is another team's bridge state you are deleting. "),
            Scaffold("|")),
        Fragment(
            S3,
            Scaffold("|"),
            Descriptive(" codex app-servers "),
            Scaffold("|"),
            Descriptive(" one app-server per FOLDER, and folders belong to teams "),
            Scaffold("|"),
            Descriptive(" ownership follows the folder."),
            Scaffold(" "),
            Operative("Verify the process's cwd before stopping an app-server; a same-named process rooted in another team's folder is theirs. "),
            Scaffold("|")),
        Fragment(
            S3,
            Scaffold("|"),
            Descriptive(" host repo "),
            Scaffold("|"),
            Descriptive(" one repo holding EVERY domain's metadata "),
            Scaffold("|"),
            Descriptive(" ownership is per DOMAIN path."),
            Scaffold(" "),
            Operative("Write only through the canonical commands for your own domain; queue-state is protected against concurrent writers by the no-item-loss invariant and stale-base re-application (G548), which is a safety net, not a licence to hand-edit another domain's state. "),
            Scaffold("|")),
    ];

    /// <summary>
    /// The JSON counterpart, keyed by the mixed PROPERTY a value is rendered
    /// under. The two renderings are formatted differently — markdown decorates
    /// a value with a list marker and a bold label — so each surface declares
    /// its own fragments, and neither can be proved exhaustive by the other.
    /// Types are assigned consistently: the same sentence is routed on both
    /// surfaces or on neither.
    /// </summary>
    public static readonly IReadOnlyList<FragmentDeclaration> JsonDeclarations =
    [
        Fragment(
            "summary",
            Descriptive("PRIMARY four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over the session layer this team runs — herdr-only here."),
            Scaffold(" "),
            Descriptive("The session layer carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout."),
            Scaffold(" "),
            Descriptive("Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation)."),
            Scaffold(" "),
            Descriptive("The concrete herdr-only operating sections below cover provisioning, dispatch, bounded completion detection, the events boundary, recovery, and both switches.")),
        Fragment(
            "setup_intake",
            Descriptive("setup-ready (herdr-only) — concrete provisioning, logical-role→pane mapping, typed launch, and G556 READY procedures are present in the herdr-only operating sections.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 1 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 2 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 3 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 4 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 5 missing field(s) below to get a setup-ready plan.")),
        Fragment(
            "summary",
            Descriptive("PRIMARY four-thread orchestrator model over agmsg + herdr (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over agmsg. agmsg carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout."),
            Scaffold(" "),
            Descriptive("Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation).")),
        Fragment("setup_intake", Descriptive("missing-inputs")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 6 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 7 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 8 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 9 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 10 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Operative("missing-inputs — supply the 11 missing field(s) below to get a setup-ready plan.")),
        Fragment("setup_intake", Descriptive("orchestrator folder")),
        Fragment("setup_intake", Descriptive("implementation folder")),
        Fragment("setup_intake", Descriptive("review folder")),
        Fragment("setup_intake", Transport("agmsg team name")),
        Fragment("setup_intake", Transport("delivery mode")),
        Fragment("setup_intake", Descriptive("existing-loop stop policy")),
        Fragment("setup_intake", Descriptive("target repo")),
        Fragment("setup_intake", Descriptive("orchestrator agent")),
        Fragment("setup_intake", Descriptive("implementer agent")),
        Fragment("setup_intake", Descriptive("reviewer agent")),
        Fragment("setup_intake", Descriptive("__DOMAIN__")),
        Fragment("setup_intake", Descriptive("__OWNER__/__REPO__")),
        Fragment("setup_intake", Descriptive("<orchestrator-path>")),
        Fragment("setup_intake", Descriptive("<implementation-path>")),
        Fragment("setup_intake", Descriptive("<review-path>")),
        Fragment("setup_intake", Descriptive("__AGENT__")),
        Fragment("setup_intake", Descriptive("<team>")),
        Fragment("setup_intake", Descriptive("<delivery-mode>")),
        Fragment("setup_intake", Transport("The implementation and review threads are loopless agmsg receivers — they must NOT run their own `/loop` or recurring timer for the same domain/repo, whether the orchestrator runs message-driven (the default, woken by agmsg replies) or on an explicit fallback/legacy timer (Codex 5m / Claude `/loop 5m`).")),
        Fragment(
            "design_workspace_supervision",
            Descriptive("G666/G689/G690 preview-through-1.x applies the three-layer approval model plus scoped adjudication authority."),
            Scaffold(" "),
            Descriptive("G689's shell-command class extracts command payloads but keeps all shipped shell answer authority orchestration-only; G690 resolves any future capability only through class, scope, risk-floor, audit, and live-CAS checks."),
            Scaffold(" "),
            Descriptive("Under authority the OPERATOR granted it, the design thread drives the team's SESSION LAYER through the workspace manager: it provisions the team (see `Terminal-workspace provisioning`), keeps the sessions alive and correctly held, and supervises for stalls."),
            Scaffold(" "),
            Operative("It records blocking waits and routes them to the canonical adjudication boundary; it never relays keystrokes or bypasses class, scope, risk-floor, audit, or live-CAS checks."),
            Scaffold(" "),
            Descriptive("This adds a session-layer role — it moves NO workflow authority.")),
        Fragment(
            "design_workspace_supervision",
            Descriptive("Two layers, two owners."),
            Scaffold(" "),
            Descriptive("The SESSION layer (panes, processes, holds, blocking dialogs) is what the operator grants the design thread."),
            Scaffold(" "),
            Descriptive("The WORKFLOW layer (labels, queue-state, publication, delegation, closeout) is not granted and never moves — it stays with intent-cli, GitHub, and the orchestrator exactly as before.")),
        Fragment(
            "design_workspace_supervision",
            Operative("the design thread supervises the session layer because the operator asked it to, and the grant's scope is what the operator stated."),
            Scaffold(" "),
            Operative("Outside a grant the design thread observes and reports rather than acts."),
            Scaffold(" "),
            Operative("A grant to supervise sessions is never read as a grant to decide workflow, product, or security questions.")),
        Fragment("design_workspace_supervision", Operative("PROVISIONING — build the team's workspace, folders, panes, launches, and role initialization per `Terminal-workspace provisioning` (G549); supervision references that section rather than repeating it.")),
        Fragment("design_workspace_supervision", Operative("SESSION LIFECYCLE — investigate an unresponsive session and, when it must be replaced, do so through the graceful drop that honors one-holder exclusivity.")),
        Fragment("design_workspace_supervision", Operative("STALL SUPERVISION — run the three supervision layers below so a stall is noticed by a layer that is actually running, not by luck.")),
        Fragment("design_workspace_supervision", Operative("BLOCKING DIALOGS — detect and record the wait, then route it to the canonical adjudication boundary; design never relays keys or bypasses class, scope, risk-floor, audit, or live-CAS checks.")),
        Fragment(
            "design_workspace_supervision",
            Descriptive("workflow state ownership does not move."),
            Scaffold(" "),
            Descriptive("Labels, queue-state, publication, delegation, CI/review gating, and closeout remain with intent-cli, GitHub, and the orchestrator; the design↔orchestrator double-check rule and the orchestrator's ownership of workflow transitions apply exactly as before."),
            Scaffold(" "),
            Operative("Supervising a session never authorizes a workflow transition, and a stuck pane is never a reason to move a label by hand.")),
        Fragment("design_workspace_supervision", Operative("A session that stops responding is a session-layer fault, and the design thread may repair it — but repair means restoring a correctly held, live session, not taking over the role's work or its decisions.")),
        Fragment(
            "design_workspace_supervision",
            Operative("READ the pane first — an \"unresponsive\" session is most often blocked on a dialog, a trust screen, or a prompt waiting for input, not dead."),
            Scaffold(" "),
            Operative("Diagnose from what the pane actually shows.")),
        Fragment("design_workspace_supervision", Transport("Distinguish the layers: a live session that is merely not attached to delivery is a delivery problem (re-check the readiness layers), not a reason to replace the session.")),
        Fragment("design_workspace_supervision", Operative("Confirm the role is still held by that session before concluding anything — a role silently dropped elsewhere looks identical to a dead session from the outside.")),
        Fragment("design_workspace_supervision", Transport("Prefer the least invasive authorized repair that restores liveness: route a residual dialog to orchestration, re-arm delivery, or restart the session — replacement is the last step, not the first.")),
        Fragment(
            "design_workspace_supervision",
            Descriptive("replacing a session never means two sessions holding the same role for even a moment."),
            Scaffold(" "),
            Transport("The successor claims only after the incumbent's hold is released; a refused actas is the exclusivity rule working, not an obstacle to route around.")),
        Fragment(
            "design_workspace_supervision",
            Transport("Replace through the GRACEFUL DROP: the incumbent drops the role (releasing its exclusivity lock and registration), then the successor claims it and re-runs readiness plus the ping test."),
            Scaffold(" "),
            Operative("Never kill a pane and assume the hold cleared, and never force a role away from a live session.")),
        Fragment(
            "design_workspace_supervision",
            Descriptive("The drop's confirmation is OPERATOR-VISIBLE: the handover surfaces to the operator rather than happening silently inside the design thread."),
            Scaffold(" "),
            Operative("The design thread may request and sequence the handover; the decision to retire a live session remains the operator's, and the confirmation is what records it.")),
        Fragment("design_workspace_supervision", Transport("real-time message monitor")),
        Fragment("design_workspace_supervision", Transport("Catch inbound agmsg traffic as it arrives — replies, blockers, and escalations that should wake the design thread immediately.")),
        Fragment("design_workspace_supervision", Transport("continuous / real-time (a live attached inbox stream, not a poll).")),
        Fragment(
            "design_workspace_supervision",
            Transport("This layer is what the message-driven steady state assumes."),
            Scaffold(" "),
            Transport("It sees only what is SENT — it cannot notice a session that went quiet or a pane blocked on a dialog, which is why the other two layers exist.")),
        Fragment("design_workspace_supervision", Descriptive("blocking-UI pane scan")),
        Fragment(
            "design_workspace_supervision",
            Operative("Notice panes that are stuck with nothing to say."),
            Scaffold(" "),
            Descriptive("TWO EQUAL stuck states: a pane blocked on an approval, selection, or trust prompt, AND a pane showing a shell prompt where an agent should be (`agent-absent`, G556)."),
            Scaffold(" "),
            Descriptive("Both produce no message at all — a blocked agent is waiting and a dead one cannot speak — so no message-driven layer can ever detect either.")),
        Fragment("design_workspace_supervision", Operative("sub-minute class (e.g. every few tens of seconds) — a blocking dialog stalls a role for its entire lifetime, and an agent that died seconds after reporting stays dead until someone looks, so this layer is the fast one.")),
        Fragment(
            "design_workspace_supervision",
            Operative("Scanning uses structured process state, and what the scan finds routes by STATE, not by one rule for everything."),
            Scaffold(" "),
            Operative("A blocking dialog is recorded and routed to the canonical adjudication surface under the durable per-team policy; design cannot bypass exact class, scope, risk-floor, audit, or live-CAS checks."),
            Scaffold(" "),
            Operative("An `agent-absent` shell prompt is NOT a dialog and must never be routed through dialog handling: it goes to the shim-safe relaunch recovery (recreating the app-server when that is what died), followed by the COMPLETE verified-liveness re-check — report, settle delay, all three checks."),
            Scaffold(" "),
            Descriptive("See `What the pane scan is looking for` for both recoveries.")),
        Fragment("design_workspace_supervision", Descriptive("periodic state watchdog")),
        Fragment("design_workspace_supervision", Operative("Compare canonical intent-cli/GitHub state against expected progress and nudge the orchestrator when work has gone stale — the existing design-thread watchdog (`intent-cli automation heartbeat --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`).")),
        Fragment("design_workspace_supervision", Operative("tens-of-minutes class (e.g. every 30 minutes) — quiet enough to stay out of the way, frequent enough to bound a stall.")),
        Fragment(
            "design_workspace_supervision",
            Operative("This is the existing watchdog, not a second one: its safety rules apply verbatim (see the watchdog safety-rules reference below)."),
            Scaffold(" "),
            Operative("One canonical nudge per wake, never a batch.")),
        Fragment(
            "design_workspace_supervision",
            Operative("Measured `intent-cli notify supervise` keeps the interval cycle as the safety floor. "),
            Operative("The optional SECOND wake source is enabled only by the concrete `--event-mode` flag: it holds blocking herdr waits for `pane.agent_status_changed` and re-arms after wait death/error. "),
            Operative("It does not add a second supervisor or change finding, recovery, or wake-target semantics.")),
        Fragment(
            "design_workspace_supervision",
            Descriptive("G699: measured supervision keeps the detector authoritative while making repeated observations readable and bounded."),
            Scaffold(" "),
            Descriptive("The first finding is emitted at the full configured cadence; an unchanged same-key observation remains a named active parked record and later findings are emitted no more often than the recorded repeat backoff cadence."),
            Scaffold(" "),
            Descriptive("G704 hardens supervise install with structural bound validation, launchd log paths, bounded first-cycle proof, and duplicate-writer attribution.")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --interval 300 --repeat-backoff-seconds 1800 --debounce-consecutive-observations 3 --once --write --format json")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --once --format markdown")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise install --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --write --format json")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise install --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --write --format json")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --interval 300 --repeat-backoff-seconds 1800 --debounce-consecutive-observations 3 --once --write --format json")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --once --format markdown")),
        Fragment("design_workspace_supervision", Operative("full cadence: `--interval <seconds>`; default when omitted: 30s")),
        Fragment("design_workspace_supervision", Operative("repeat emission backoff: `--repeat-backoff-seconds <seconds>` (alias `--backoff-seconds`); default: 1800s")),
        Fragment("design_workspace_supervision", Operative("pane status debounce: `--debounce-consecutive-observations <count>` (alias `--status-debounce-consecutive`); default: 3 consecutive observations")),
        Fragment("design_workspace_supervision", Operative("write mode records the resolved values at `.intent-cli/supervision/<domain>/<team>/emission-policy.json` and repeats them on every cycle")),
        Fragment("design_workspace_supervision", Operative("G704 bound rule: `--bound` must be >= `--interval`; otherwise `bound-below-interval` names the structural `supervisor-not-running` consequence while the runtime warning remains")),
        Fragment("design_workspace_supervision", Operative("G704 startup proof: `--startup-bound <seconds>` (default 30s) must observe a writer-bearing first cycle before a write reports success")),
        Fragment("design_workspace_supervision", Operative("G704 macOS artifact: WorkingDirectory is the routing root; StandardOutPath and StandardErrorPath are under `.intent-cli/supervision/<domain>/<team>/runtime/`; installed writer identity is recorded beside bound/emission state")),
        Fragment("design_workspace_supervision", Operative("G712 chooses the permitted GUI-session lifetime: supervision may be explicitly bootstrapped into the current GUI domain, but it is never login-auto-loaded and does not survive logout or reboot.")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise install --domain __DOMAIN__ --team __TEAM__ --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --platform macos --write --format json")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise install --domain __DOMAIN__ --team <team> --repo __OWNER__/__REPO__ --owner-role orchestration --bound 900 --interval 300 --startup-bound 30 --platform macos --write --format json")),
        Fragment("design_workspace_supervision", Operative("launchctl bootstrap gui/$(id -u) '<artifact-path>'")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise reconcile --write --format json")),
        Fragment("design_workspace_supervision", Operative("intent-cli notify supervise uninstall --write --format json")),
        Fragment("design_workspace_supervision", Operative("Artifacts remain under `.intent-cli/supervision/<domain>/<team>/install/`; no managed artifact is emitted to `~/Library/LaunchAgents`.")),
        Fragment("design_workspace_supervision", Operative("The generated macOS plist omits `RunAtLoad`; install output names the artifact, lifetime, runtime logs, and legacy artifacts removed.")),
        Fragment("design_workspace_supervision", Operative("Reconcile/uninstall records `loaded_before`, `unloaded`, `removed_artifacts`, `loaded_after`, and `artifacts_after`; use it for the three-loaded-jobs/one-plist drift shape.")),
        Fragment("design_workspace_supervision", Operative("Reconcile removes only `intent-cli.supervise.*` jobs and artifacts, including legacy login-persistent plists; it never kills, replaces, or mutates unrelated jobs.")),
        Fragment(
            "design_workspace_supervision",
            Operative("Install authors and first-cycle-probes only. "),
            Operative("Registration is an explicit operator action; reconcile/uninstall is the bounded current-session unload/removal command and does not grant workflow or recovery authority.")),
        Fragment("design_workspace_supervision", Operative("same-key observations never disappear: the active record names `parked` and exposes `first_seen`, `last_seen`, `repeat_count`, and `emission_cadence_seconds`")),
        Fragment("design_workspace_supervision", Operative("resolution clears the active record; a later reappearance starts a fresh first_seen/repeat_count sequence")),
        Fragment("design_workspace_supervision", Operative("a changed condition fingerprint resets first_seen, last_seen, repeat_count, and emission eligibility immediately")),
        Fragment("design_workspace_supervision", Operative("a genuinely new observation key is emitted immediately even while another key is parked")),
        Fragment("design_workspace_supervision", Operative("a pane status flap below the recorded consecutive threshold is not classified; the threshold-consecutive settled state is classified once with the existing observation-only boundary")),
        Fragment("design_workspace_supervision", Operative("detection predicates and G695 continuation-chain recording remain unchanged; parking suppresses duplicate findings only and never performs a lifecycle transition")),
        Fragment("design_workspace_supervision", Operative("G704 duplicate-supervisor compares G676 writer identities with the installed record and routes the same key through G699 backoff/park")),
        Fragment("design_workspace_supervision", Operative("first-cycle proof failure is named `first-cycle-proof-failed` and names both log paths; a post-install cycle with writer identity is the only success evidence")),
        Fragment("design_workspace_supervision", Operative("do not treat one blocked/idle poll as a settled transition")),
        Fragment("design_workspace_supervision", Operative("do not silently remove a parked key or infer auto-resolution")),
        Fragment("design_workspace_supervision", Operative("do not use a repeated same-key finding to authorize a merge, closeout, label change, or other workflow mutation")),
        Fragment("design_workspace_supervision", Operative("do not accept a bound below the interval, infer first-cycle liveness from a loaded service/PID, or hide startup failure in an unlabeled log")),
        Fragment("design_workspace_supervision", Operative("do not use terminal content to prove duplicate supervision and do not auto-kill, stop, elect, or rank a writer")),
        Fragment(
            "design_workspace_supervision",
            Operative("This is an observation and wake-hygiene policy only."),
            Scaffold(" "),
            Operative("`intent-cli`/GitHub remain authoritative for workflow state; the supervisor may record, wake the owning role, and surface evidence, but never clears work or changes lifecycle state.")),
        Fragment("design_workspace_supervision", Descriptive("G707: before emitting a registration or live-idle finding, supervision corroborates it against contradictory non-terminal observations already collected in the same cycle.")),
        Fragment("design_workspace_supervision", Operative("Consult the recorded seat-state agent_status and interactive_ready observations for the same-cycle workspace/pane before emission; idle or working is evidence that the registration/live-idle conclusion needs corroboration.")),
        Fragment("design_workspace_supervision", Operative("registration-lost-process-present versus same-cycle seat-state agent_status=working or idle")),
        Fragment("design_workspace_supervision", Operative("live-idle-no-report versus same-cycle seat-state agent_status=working")),
        Fragment("design_workspace_supervision", Operative("a same-cycle interactive_ready=true observation also contradicts either conclusion")),
        Fragment("design_workspace_supervision", Operative("observation-conflict")),
        Fragment("design_workspace_supervision", Operative("registration_definition")),
        Fragment("design_workspace_supervision", Operative("registration_lookup")),
        Fragment("design_workspace_supervision", Operative("registration_result")),
        Fragment("design_workspace_supervision", Operative("consulted_observations")),
        Fragment("design_workspace_supervision", Operative("An inconclusive conflict begins with verification and names the consulted producers; it prescribes no automatic action and never authorizes destructive automation.")),
        Fragment("design_workspace_supervision", Operative("When no same-cycle non-terminal seat observation exists, a verified absent seat remains eligible to emit seat-absent or registration-lost-process-present.")),
        Fragment("design_workspace_supervision", Operative("The single observation-conflict per recorded seat is a same-key observation: G699 repeat backoff and park state apply, while a new key remains immediate.")),
        Fragment(
            "design_workspace_supervision",
            Operative("Corroboration changes only the observation classification and evidence."),
            Scaffold(" "),
            Operative("It does not alter canonical workflow state, ownership, or the observation-only boundary.")),
        Fragment("design_workspace_supervision", Descriptive("blocking dialog")),
        Fragment("design_workspace_supervision", Descriptive("an approval, selection, or trust prompt waiting for input.")),
        Fragment("design_workspace_supervision", Operative("record it and route it to the canonical adjudication surface under the durable per-team policy; direct design relay is forbidden and absent policy is escalate-only.")),
        Fragment("design_workspace_supervision", Descriptive("agent-absent")),
        Fragment(
            "design_workspace_supervision",
            Descriptive("a SHELL PROMPT where an agent should be — the pane looks like an ordinary terminal, often with a resume hint left on screen."),
            Scaffold(" "),
            Descriptive("The agent exited; it may have reported startup successfully seconds earlier.")),
        Fragment(
            "design_workspace_supervision",
            Operative("RELAUNCH THROUGH THE SHIM: type the launch into the pane's interactive shell (never spawn the executable), recreating the app-server first when it is the thing that died."),
            Scaffold(" "),
            Operative("Set the permission mode with the LAUNCH FLAG (e.g."),
            Scaffold(" "),
            Operative("`--permission-mode`) rather than trying to switch it afterwards: a workspace manager's synthetic key injection cannot be relied on for mode switching — plain keys are delivered, but modifier chords such as shift+tab are not delivered faithfully (observed across multiple teams)."),
            Scaffold(" "),
            Transport("Then run the FULL verified-liveness sequence again — report, settle delay, all three checks.")),
        Fragment(
            "design_workspace_supervision",
            Transport("supervision schedulers are session-scoped: a `/loop`, an automation, or an attached monitor dies with the design session that hosts it, and nothing announces that it stopped."),
            Scaffold(" "),
            Transport("Every supervision layer must either survive a design-session restart or be RE-ARMED as the first act of the new session — treat re-arming as part of starting the session, not as an optional follow-up."),
            Scaffold(" "),
            Descriptive("Field cost of forgetting: a claim-now lost inside a session-restart window left a published issue stalled for 5.5 HOURS because no supervision layer happened to be running.")),
        Fragment(
            "design_workspace_supervision",
            Operative("the canonical adjudication surface is the only bounded answer path; design may use it only for a declared class/scope capability with no hard-floor tag and matching pane/state-sequence/text-hash CAS."),
            Scaffold(" "),
            Operative("G683 supervision emits exact recipe-registry classes plus observed text; G689 additionally verifies the extracted shell AST and every segment against a scoped policy; unknown text stays escalate-only; unknown syntax and uncovered shell segments stay escalate-only."),
            Scaffold(" "),
            Operative("Only a validated matched pre-approve, declared capability, absent hard-floor tag, durable audit, and live CAS permit a bounded exact-scope answer; no shipped class or scope is design-answerable in this slice.")),
        Fragment("design_workspace_supervision", Descriptive("confirmations of work the design thread itself requested")),
        Fragment("design_workspace_supervision", Operative("answer only through canonical `notify adjudicate` when an exact registry class, validated pre-approve, declared capability, absent hard-floor tag, and live pane/state-sequence/text-hash CAS all match; shipped classes remain orchestration-only.")),
        Fragment("design_workspace_supervision", Descriptive("command approvals verified read-only")),
        Fragment("design_workspace_supervision", Operative("for non-shell attended cases only, use the same exact registry + validated pre-approve + durable-audit path through canonical adjudicate; G689 shell-command is not read-only authority even for project-test, so every AST segment remains orchestration-only; otherwise escalate.")),
        Fragment("design_workspace_supervision", Descriptive("trust screens for hooks the design thread itself installed")),
        Fragment(
            "design_workspace_supervision",
            Operative("eliminate it through agent-side allow configuration recorded in the G636 kind recipe fields."),
            Scaffold(" "),
            Operative("If it remains, only the exact validated/audited orchestration path may answer it; otherwise escalate.")),
        Fragment("design_workspace_supervision", Descriptive("operator-preauthorized mode changes")),
        Fragment(
            "design_workspace_supervision",
            Operative("preauthorization alone is insufficient: require exact registry class, validated rule, durable audit, declared capability, hard-floor check, live CAS, and bounded execution."),
            Scaffold(" "),
            Operative("Unknown text escalates; no shipped class is design-answerable.")),
        Fragment("design_workspace_supervision", Descriptive("unreadable or unverifiable dialogs")),
        Fragment("design_workspace_supervision", Operative("if the pane content cannot be read, or the claim it makes cannot be verified, there is nothing to base an answer on — answering would be guessing on the operator's behalf.")),
        Fragment("design_workspace_supervision", Descriptive("destructive or irreversible approvals")),
        Fragment("design_workspace_supervision", Operative("deletions, force operations, overwrites, and anything else that cannot be undone are the operator's call — the cost of a wrong answer is unbounded and unrecoverable.")),
        Fragment("design_workspace_supervision", Descriptive("choices that embed a product or design decision")),
        Fragment("design_workspace_supervision", Operative("a dialog that picks behavior, scope, or defaults is design content, and design content goes through the operator and the design↔orchestrator double-check — not through whoever happens to be unblocking a pane.")),
        Fragment("design_workspace_supervision", Descriptive("credential, security, and permission waits")),
        Fragment(
            "design_workspace_supervision",
            Operative("these are in G690's hard risk floor and are never answerable by the design thread, with or without prior authorization: they always remain unanswered and escalate to the operator."),
            Scaffold(" "),
            Operative("No grant makes them answerable.")),
        Fragment(
            "design_workspace_supervision",
            Operative("UNSTICKING A SESSION IS NOT DECIDING FOR IT."),
            Scaffold(" "),
            Operative("The design thread's job is to keep the session layer alive so the role can do its own work — not to make the role's choices, and not to make the operator's."),
            Scaffold(" "),
            Operative("It never relays keystrokes or bypasses the canonical adjudication checks."),
            Scaffold(" "),
            Descriptive("This preserves four judgment-bearing threads plus one supervision process."),
            Scaffold(" "),
            Descriptive("Measured 2026-08-11 in workspace wK, Claude app safety blocked the relay and nonexistent `/approvals` advice failed; recipe-first launch plus orchestration-owned policy is the durable remedy.")),
        Fragment("design_workspace_supervision", Transport("Provisioning is NOT repeated here — see `Terminal-workspace provisioning` for role folders, workspace topology, shim-safe launch, actas/readiness, and the exclusivity/handover rules this section supervises.")),
        Fragment(
            "design_workspace_supervision",
            Operative("The watchdog safety rules apply to ALL supervision verbatim: no duplicate delegation, no clearing a permission prompt, no cancelling or resetting in-flight work, no force-closing an issue/PR, and no speculative durable-state surgery (no hand-edited labels, queue-state, or host metadata)."),
            Scaffold(" "),
            Descriptive("See `Design-thread watchdog (recommended safety net)`.")),
        Fragment(
            "design_decision_holds",
            Operative("A hold blocked on a DESIGN DECISION must be visible and bounded."),
            Scaffold(" "),
            Operative("Visible: it is recorded as a clarification artifact through the canonical clarify surface, so `automation stalled-work` and `automation heartbeat` can see it — an agmsg message alone is invisible to every supervision layer."),
            Scaffold(" "),
            Descriptive("Bounded: the operator may pre-delegate enumerated, mechanically fact-checkable decision classes so a correction both threads can verify from repository facts does not wait on design at all."),
            Scaffold(" "),
            Descriptive("Measured cost of getting this wrong: a nine-hour hold on a one-line wording ruling while every technical check was green and `stalled-work` reported `stalled=false` throughout.")),
        Fragment(
            "design_decision_holds",
            Operative("When the orchestrator or the reviewer blocks on a design decision, it RECORDS A CLARIFICATION ARTIFACT through the canonical clarify surface, in addition to whatever agmsg message it sends."),
            Scaffold(" "),
            Descriptive("The artifact is what makes the hold detectable; the message is only a notification.")),
        Fragment("design_decision_holds", Descriptive("domain — the blocked domain (`__DOMAIN__`), so the artifact is scoped to the right pipeline.")),
        Fragment("design_decision_holds", Descriptive("blocking execution unit — the unit that cannot proceed until this is answered.")),
        Fragment("design_decision_holds", Operative("question — what design must decide, stated so someone who was not in the thread can answer it.")),
        Fragment("design_decision_holds", Operative("recommended answer — when the asking thread already believes it knows the answer, state it and cite the facts that support it; design then confirms or overrides rather than starting from scratch.")),
        Fragment(
            "design_decision_holds",
            Operative("An agmsg-only hold is a CONTRACT VIOLATION, not a shortcut."),
            Scaffold(" "),
            Descriptive("A block that exists only as messages is invisible to `stalled-work`, to `heartbeat`, and therefore to every watchdog and every operator glance — which is exactly how a nine-hour hold passed unnoticed with the pipeline reporting healthy."),
            Scaffold(" "),
            Descriptive("If you are waiting on design, the artifact exists; if the artifact does not exist, you are not waiting, you are stalled.")),
        Fragment("design_decision_holds", Operative("record the hold — `intent-cli clarify open` (the canonical clarify surface; never hand-write the artifact)")),
        Fragment("design_decision_holds", Operative("see what is open — `intent-cli clarify list`")),
        Fragment("design_decision_holds", Operative("answer it — `intent-cli clarify answer` (design, or the operator on escalation)")),
        Fragment("design_decision_holds", Operative("confirm it is visible — `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json` reports `design-decision-pending`")),
        Fragment("design_decision_holds", Operative("intent-cli clarify open <execution-unit> \\\n  --question \"<the actual design-blocking question, answerable by someone outside the thread>\" \\\n  --recommended-answer \"<what you believe the answer is, when you believe you know it>\" \\\n  --evidence \"<the repository facts that support the recommendation>\"")),
        Fragment(
            "design_decision_holds",
            Descriptive("The reviewer's hold rule is refined so a green-technical review never becomes an untracked wait."),
            Scaffold(" "),
            Operative("Evaluate what is actually pending before holding.")),
        Fragment(
            "design_decision_holds",
            Operative("Technical checks are GREEN and the only pending item is NON-SEMANTIC and MECHANICALLY FACT-CHECKABLE from repository facts — resolve it under bounded default authority (below), log the resolution with the verifying facts, and proceed."),
            Scaffold(" "),
            Operative("Do not hold a green review on a question whose answer both threads can derive and cite.")),
        Fragment(
            "design_decision_holds",
            Operative("Anything else — a semantic or product question, a fact you cannot verify, or a class the operator has not delegated — becomes a recorded clarification and a VISIBLE pending state."),
            Scaffold(" "),
            Descriptive("The review is still held; the difference is that the hold is now on disk and detectable.")),
        Fragment(
            "design_decision_holds",
            Operative("there is no third option where the reviewer simply waits and says so in a message: either the item is resolved under granted authority with its evidence, or a clarification artifact exists."),
            Scaffold(" "),
            Descriptive("Silence with a message attached is the failure mode this rule exists to remove.")),
        Fragment(
            "design_decision_holds",
            Descriptive("BOUNDED DEFAULT AUTHORITY lets the operator pre-delegate a small, enumerated set of decision classes that can be settled by checking repository facts rather than by judgment."),
            Scaffold(" "),
            Descriptive("It exists so a count correction does not cost nine hours."),
            Scaffold(" "),
            Descriptive("It is bounded in every direction: granted, enumerated, evidence-logged, amendable, and never semantic.")),
        Fragment(
            "design_decision_holds",
            Operative("GRANTED, never assumed."),
            Scaffold(" "),
            Operative("The authority applies only to classes the OPERATOR has explicitly pre-delegated for this domain."),
            Scaffold(" "),
            Operative("Absent a grant, every design decision goes to design as before — the default is unchanged, and no thread may infer a delegation from the fact that an answer seems obvious.")),
        Fragment("design_decision_holds", Descriptive("count and enumeration corrections")),
        Fragment(
            "design_decision_holds",
            Operative("the count is derivable from repository facts both threads can read — e.g. a slice count derived from the merged PR list and the issue's own enumeration."),
            Scaffold(" "),
            Operative("Cite the list and the derivation.")),
        Fragment("design_decision_holds", Descriptive("wording corrections that follow from a cited fact")),
        Fragment(
            "design_decision_holds",
            Operative("the corrected wording is entailed by a fact in the repository (a merged PR title, a label state, a retired unit's own record), and the reviewer and orchestrator AGREE on both the fact and the correction."),
            Scaffold(" "),
            Operative("Disagreement is not fact-checkable — it escalates.")),
        Fragment("design_decision_holds", Descriptive("cross-reference and link corrections")),
        Fragment("design_decision_holds", Operative("the target exists (or does not) in the repository as cited — verifiable by reading the referenced file, heading, issue, or PR.")),
        Fragment("design_decision_holds", Descriptive("identifier and metadata mismatches against a canonical source")),
        Fragment(
            "design_decision_holds",
            Operative("the canonical source is named and read — e.g. a version in `eng/version.json`, a unit id in a packet, a label in the canonical palette."),
            Scaffold(" "),
            Operative("The canonical source wins; the resolution cites it.")),
        Fragment(
            "design_decision_holds",
            Operative("MANDATORY EVIDENCE LOGGING."),
            Scaffold(" "),
            Operative("A resolution taken under this authority is recorded in the durable trail with the facts that verify it — what was decided, which repository facts entail it, and which threads agreed."),
            Scaffold(" "),
            Operative("An unlogged resolution is not a granted-authority resolution; it is an undocumented decision, and it is a violation of this contract.")),
        Fragment(
            "design_decision_holds",
            Descriptive("The sink is the CANONICAL `clarify record` surface: the entry lands under `## Recently Resolved` in the domain's clarification return path (`intents/<domain>/clarifications/open.md`), where `Question` identifies the pending item, `Decision` records the decided value, and `Rationale` records the verified repository facts plus the reviewer/orchestrator agreement."),
            Scaffold(" "),
            Descriptive("The entry is durable and stays readable there, which is exactly what makes design's post-hoc amendment possible — design reads the recorded evidence and amends or reverses from it.")),
        Fragment("design_decision_holds", Operative("# 1. write the decision artifact (## Question / ## Decision / ## Rationale)\ncat > /tmp/authority-decision.md <<'EOF'\n## Question\n<the pending item, identified so design can find it later>\n\n## Decision\n<the decided value>\n\n## Rationale\n<the verified repository facts that entail it, and which threads agreed>\nEOF\n\n# 2. record it in the durable trail (--dry-run first shows the intended update)\nintent-cli clarify record --domain <domain> --from-file /tmp/authority-decision.md")),
        Fragment(
            "design_decision_holds",
            Descriptive("DESIGN MAY AMEND POST HOC."),
            Scaffold(" "),
            Descriptive("A granted-authority resolution is provisional in design's eyes: design can review the logged evidence afterwards and amend or reverse the decision."),
            Scaffold(" "),
            Descriptive("The authority buys latency, not finality — proceeding does not close the question against design.")),
        Fragment(
            "design_decision_holds",
            Operative("SEMANTIC AND PRODUCT DECISIONS ARE EXCLUDED, absolutely."),
            Scaffold(" "),
            Operative("Intent shaping, packet content and acceptance criteria, release scope, prioritization rulings, and anything requiring product or design judgment always go to design through the design↔orchestrator double-check rule, whose scope this contract does not touch."),
            Scaffold(" "),
            Operative("If settling the question requires deciding what SHOULD be true rather than checking what IS true, it is not fact-checkable and this authority does not reach it.")),
        Fragment(
            "design_decision_holds",
            Descriptive("While a clarification stays open, the design thread is reminded on a fixed cadence."),
            Scaffold(" "),
            Descriptive("A recorded hold that nobody re-surfaces is still a slow hold — the artifact makes it detectable, the reminder makes it noticed.")),
        Fragment(
            "design_decision_holds",
            Operative("The ORCHESTRATOR sends the reminder from its long-interval automation — the same wake that already runs the heartbeat check."),
            Scaffold(" "),
            Descriptive("No new scheduler, and the receivers stay loopless.")),
        Fragment(
            "design_decision_holds",
            Operative("30–60 minute class — the same low-frequency band as the heartbeat and the design-thread watchdog."),
            Scaffold(" "),
            Descriptive("Faster polling recreates the churn the message-driven model removes; slower lets a hold sit past the point an operator would want to know.")),
        Fragment(
            "design_decision_holds",
            Operative("AT MOST ONE reminder per interval PER OPEN CLARIFICATION."),
            Scaffold(" "),
            Descriptive("Two open clarifications produce at most two reminders in a wake; one clarification never produces two reminders in the same interval no matter how many wakes fire."),
            Scaffold(" "),
            Descriptive("This is the same one-message discipline the watchdog already follows.")),
        Fragment(
            "design_decision_holds",
            Operative("STOP ON ANSWER."),
            Scaffold(" "),
            Descriptive("Once the clarification is answered (or applied, or cancelled) it is no longer open, `design-decision-pending` clears on its own, and the reminders stop."),
            Scaffold(" "),
            Operative("Never keep reminding against an answered clarification, and never re-open one to keep a thread's attention.")),
        Fragment(
            "design_decision_holds",
            Descriptive("The design thread runs in the OPERATOR APP by preference, which is what makes a reminder land either way: an OPEN design session receives the reminder immediately through its monitor, and a CLOSED one finds it waiting in the inbox on resume."),
            Scaffold(" "),
            Descriptive("Neither case requires design to be resident in the team workspace — there is no workspace-residency requirement here.")),
        Fragment(
            "design_decision_holds",
            Descriptive("Detection is `design-decision-pending` in `automation stalled-work`: it reads the domain's OPEN clarification artifacts and reports each with its age, blocking execution unit, and question summary, and `automation heartbeat` carries it in `message_body` like any other kind."),
            Scaffold(" "),
            Operative("Confirm a hold is visible with `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`; if the hold is real but the kind is absent, the clarification artifact was never recorded — which is the contract violation above, not a detector bug.")),
        Fragment(
            "cross_project_isolation",
            Operative("Assume you are NOT alone on this machine."),
            Scaffold(" "),
            Descriptive("Several project teams run simultaneously, and every substrate below is shared across all of them — the workspace manager's server, the agmsg run directory, the codex app-servers, the host repo."),
            Scaffold(" "),
            Descriptive("`Terminal-workspace provisioning` and `Design-thread workspace supervision` describe how to build and keep ONE team; this section is what keeps that team from damaging another."),
            Scaffold(" "),
            Operative("It narrows the OBJECTS you may act on to your own team's; it does not widen or narrow what you may DO, so the supervision authority boundary applies unchanged."),
            Scaffold(" "),
            Descriptive("Operator incident (2026-07-29): with several teams live, one project's design thread damaged another project's resources and the operator had to intervene by hand.")),
        Fragment(
            "cross_project_isolation",
            Operative("Before you touch anything, establish that it belongs to YOUR team."),
            Scaffold(" "),
            Operative("Attribution is a positive result from the keys below — not the absence of evidence that it belongs to someone else, and not a name that merely looks familiar.")),
        Fragment("cross_project_isolation", Descriptive("injecting keys or text into a pane")),
        Fragment("cross_project_isolation", Descriptive("killing a process")),
        Fragment("cross_project_isolation", Descriptive("closing or restructuring a workspace")),
        Fragment("cross_project_isolation", Descriptive("removing or rewriting a state file")),
        Fragment("cross_project_isolation", Descriptive("workspace label")),
        Fragment(
            "cross_project_isolation",
            Operative("the workspace is labelled with YOUR team/project name."),
            Scaffold(" "),
            Descriptive("A workspace you did not create and cannot name is not yours.")),
        Fragment("cross_project_isolation", Descriptive("pane cwd")),
        Fragment(
            "cross_project_isolation",
            Operative("the pane's working directory is one of YOUR team's dedicated role folders."),
            Scaffold(" "),
            Descriptive("A pane whose cwd you do not recognize belongs to someone.")),
        Fragment("cross_project_isolation", Descriptive("process cwd")),
        Fragment(
            "cross_project_isolation",
            Operative("the process's own working directory — read it per pid before any kill, exactly as the 2026-07-27 migration did when it spared another project's processes."),
            Scaffold(" "),
            Descriptive("A pid list filtered only by process NAME attributes nothing.")),
        Fragment("cross_project_isolation", Descriptive("agmsg `(team, role)` file naming")),
        Fragment("cross_project_isolation", Descriptive("agmsg run-directory state files are named per `(team, role)`; a file whose team segment is not yours is another team's bridge/watcher state, however broken it looks.")),
        Fragment(
            "cross_project_isolation",
            Operative("if you cannot positively establish ownership, the object is READ-ONLY to you: you may look and you may report — you may not mutate."),
            Scaffold(" "),
            Operative("Escalate to the operator instead of guessing: a wrong guess here is another team's outage, and the cost is theirs rather than yours, which is exactly why the default has to be refusal.")),
        Fragment(
            "cross_project_isolation",
            Operative("one workspace per team, labelled with the team/project name."),
            Scaffold(" "),
            Operative("Never reuse, repurpose, or borrow another team's workspace or its panes — not even an idle-looking one."),
            Scaffold(" "),
            Descriptive("A workspace is the unit an operator reads to know whose work is whose; sharing one collapses that.")),
        Fragment(
            "cross_project_isolation",
            Operative("one folder belongs to exactly ONE team."),
            Scaffold(" "),
            Operative("Never launch your agents in another team's folders."),
            Scaffold(" "),
            Transport("This is the same folder-scoping fact that forbids two roles sharing a folder within a team (G521) — agmsg identity and the codex bridge are folder-scoped, so an agent started in another team's folder takes over THEIR identity and delivery, not just its own.")),
        Fragment("cross_project_isolation", Descriptive("workspace-manager server (e.g. the herdr server)")),
        Fragment("cross_project_isolation", Descriptive("one server process serving EVERY workspace on the machine")),
        Fragment(
            "cross_project_isolation",
            Descriptive("ownership is per WORKSPACE, never the server."),
            Scaffold(" "),
            Operative("Act on your own workspace and its panes; never restart, reconfigure, or kill the shared server — doing so takes down every other team's workspace at once.")),
        Fragment("cross_project_isolation", Descriptive("agmsg run directory (`~/.agents/skills/agmsg/run`)")),
        Fragment("cross_project_isolation", Descriptive("one directory holding bridge / watcher / app-server state for ALL teams")),
        Fragment(
            "cross_project_isolation",
            Descriptive("ownership is per `(team, role)` FILE."),
            Scaffold(" "),
            Operative("Touch only files whose team segment is yours; never clear the directory wholesale to fix your own delivery — that is another team's bridge state you are deleting.")),
        Fragment("cross_project_isolation", Descriptive("codex app-servers")),
        Fragment("cross_project_isolation", Descriptive("one app-server per FOLDER, and folders belong to teams")),
        Fragment(
            "cross_project_isolation",
            Descriptive("ownership follows the folder."),
            Scaffold(" "),
            Operative("Verify the process's cwd before stopping an app-server; a same-named process rooted in another team's folder is theirs.")),
        Fragment("cross_project_isolation", Descriptive("host repo")),
        Fragment("cross_project_isolation", Descriptive("one repo holding EVERY domain's metadata")),
        Fragment(
            "cross_project_isolation",
            Descriptive("ownership is per DOMAIN path."),
            Scaffold(" "),
            Operative("Write only through the canonical commands for your own domain; queue-state is protected against concurrent writers by the no-item-loss invariant and stale-base re-application (G548), which is a safety net, not a licence to hand-edit another domain's state.")),
        Fragment(
            "cross_project_isolation",
            Operative("When you find damage — including damage you caused — recovery is NON-DESTRUCTIVE."),
            Scaffold(" "),
            Descriptive("The instinct to tidy up is the failure mode: a broken artifact belonging to another team is still their evidence, and deleting it destroys their ability to diagnose what happened.")),
        Fragment(
            "cross_project_isolation",
            Operative("PRESERVE and SET ASIDE another project's damaged artifacts — rename, move aside, or simply leave them in place and report."),
            Scaffold(" "),
            Operative("Never delete another team's workspace, panes, folders, processes' state, or files, however broken they look."),
            Scaffold(" "),
            Operative("Tell the operator and the affected team's thread what you found and what you set aside.")),
        Fragment(
            "cross_project_isolation",
            Transport("REBUILD YOUR OWN fresh rather than repairing in place: create a new workspace, new panes, new role folders as needed, and re-run provisioning."),
            Scaffold(" "),
            Descriptive("Your own damaged artifacts may also be set aside rather than deleted when they carry evidence worth keeping.")),
        Fragment("cross_project_isolation", Operative("Recovery defaults to RECREATE, NOT CLEANUP.")),
        Fragment(
            "scheduling",
            Descriptive("In orchestrator-message mode the normal steady state is MESSAGE-DRIVEN: implementation/review receivers already send accepted/progress/completed/blocked replies to the orchestrator, and those replies wake the orchestrator path — routine fast polling is NOT required."),
            Scaffold(" "),
            Descriptive("An orchestrator timer (Codex automation every 5m, or Claude same-thread `/loop 5m`) remains SUPPORTED but only as an explicit FALLBACK/LEGACY polling option for an operator who intentionally wants scheduled polling instead of message-driven wakes."),
            Scaffold(" "),
            Descriptive("Either way the implementation and review threads stay long-lived LOOPLESS receivers."),
            Scaffold(" "),
            Descriptive("The RECOMMENDED default safety net for message-driven steady state is a 30-minute-class design-thread watchdog (see Design-thread watchdog), not a fast orchestrator loop.")),
        Fragment("scheduling", Descriptive("orchestrator")),
        Fragment(
            "scheduling",
            Operative("Implementation and review threads are loopless receivers: do NOT start a recurring timer/loop in a receiver thread for this domain/repo."),
            Scaffold(" "),
            Transport("A receiver waits for an agmsg delegation, acts once, replies once, and waits again."),
            Scaffold(" "),
            Descriptive("Receivers are NEVER scheduled; when an explicit fallback/legacy timer is used (message-driven wakes are the default), the orchestrator is the only thread ever scheduled.")),
        Fragment(
            "scheduling",
            Transport("OPTIONAL fallback/legacy polling — Codex automation (run every 5 minutes) for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__AGENT__`: on each run perform exactly ONE orchestrator wake — check design-side progress and agmsg replies, ask intent-cli for state (`intent status`, `worker next-action --github-only`, `automation host-review-preflight`), verify the GitHub facts (CI/approval/merge/closeout), then send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER (implementation, review), NOT at-most-one-message overall, so a publish plus its same-wake delegation, one repair per stalled receiver, and one operator escalation may all go out together — and exit."),
            Scaffold(" "),
            Transport("Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling."),
            Scaffold(" "),
            Operative("Do not run implementation/review loops; they are loopless receivers.")),
        Fragment(
            "scheduling",
            Transport("OPTIONAL fallback/legacy polling — Claude same-thread setup for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__`: in the orchestrator thread run `/loop 5m` with the orchestrator prompt so the same thread re-wakes every 5 minutes."),
            Scaffold(" "),
            Transport("Each wake does exactly one orchestrator pass (read replies, check intent-cli / GitHub state, send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER, NOT at-most-one-message overall)."),
            Scaffold(" "),
            Transport("Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling."),
            Scaffold(" "),
            Transport("Do NOT also launch `/loop` in the implementation or review threads — those are loopless receivers driven only by your delegations.")),
        Fragment("scheduling", Transport("A wake is triggered either by an incoming agmsg reply from implementation/review (the message-driven steady state) or by the optional fallback/legacy timer firing — either trigger runs exactly one orchestrator pass below.")),
        Fragment("scheduling", Operative("Check design-side progress: newly published packets/issues and intent status changes via `intent-cli intent status --domain __DOMAIN__ --format json`.")),
        Fragment("scheduling", Transport("Read pending agmsg replies from the implementation/review receivers (signals only — re-verify against intent-cli / GitHub).")),
        Fragment("scheduling", Operative("Ask intent-cli for worker state: `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.")),
        Fragment("scheduling", Operative("Check host review readiness: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json`.")),
        Fragment("scheduling", Operative("Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge state, and closeout/label state.")),
        Fragment(
            "scheduling",
            Operative("Classify each open PR's CI: pending = wait using the named mode-specific CI re-check producer (no message); green = delegate review/closeout; red = repair or escalate by ownership; stuck = escalate."),
            Scaffold(" "),
            Descriptive("Pending CI is normal progress, not a reason to message the operator.")),
        Fragment("scheduling", Operative("Detect stale blockers and no-reply receivers: a delegation with no accepted/progress reply within the expected window, or a thread stuck off the official workflow.")),
        Fragment("scheduling", Operative("On a no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check: send one non-destructive status-request, check read-only intent-cli/GitHub facts, keep watching if there is progress, treat waiting-permission as an operator notice (never auto-clear), and only after repeated no-reply with no progress send one idempotent re-entry or escalate.")),
        Fragment("scheduling", Operative("If intent-cli reports an `issue-cut-ready` candidate and all gates pass (same-domain or routed, complete contract, no open clarification, dependencies satisfied, under WIP, clean host-sync/preflight), publish ONE issue this wake via canonical publish-flow / issue-publish, verify it, THEN delegate that same issue to implementation in THIS SAME WAKE (G524) — do not ask the operator to create it, and do not stop after publishing to wait for a future wake to send the delegation.")),
        Fragment("scheduling", Operative("If the candidate has unmet dependencies, plan the chain instead of pausing: act on the EARLIEST unmet resolvable dependency (publish or route it), keep the dependent held, and escalate only ambiguous/cycle/cross-domain-unrouted cases.")),
        Fragment("scheduling", Operative("The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message overall (G524): this wake's actions may include a publish plus its same-wake delegation, one repair message per stalled receiver, one operator escalation, and handling any pending receiver reports, all together.")),
        Fragment("scheduling", Operative("Send workflow notifications only through `intent-cli notify`; it resolves the recorded session-layer mode and validates the recipient before delivery, failing closed on an unknown role (G524/G578).")),
        Fragment(
            "scheduling",
            Operative("When an outcome is applied elsewhere, a review round supersedes the predecessor, or a recovery/re-dispatch path makes a report no longer owed, explicitly record the open delegation's disposition with `intent-cli notify dispose --domain <domain> --team <team> --task-id <task-id> --kind applied-elsewhere|superseded --actor <actor> --reason <reason> [--applied-outcome-evidence <evidence>|--superseding-task-id <task-id>] --write --format json`."),
            Scaffold(" "),
            Operative("This is an attributed judgment, never an automatic inference; it ends the report expectation but never refuses or drops a late report.")),
        Fragment(
            "scheduling",
            Operative("REPAIR routine off-rail states yourself by messaging the appropriate thread back onto the official intent-cli workflow — e.g. a receiver that stalled, skipped `worker complete`, applied a label by hand, or has not replied."),
            Scaffold(" "),
            Operative("Routine recovery is a repair message, not an escalation."),
            Scaffold(" "),
            Operative("When that recovery or an outcome application means the original report will never arrive, write an explicit `notify dispose` disposition at the cause site with its actor, reason, and applicable superseding task or applied-outcome evidence; never silently close the pending record and never reject a late report.")),
        Fragment(
            "scheduling",
            Operative("Apply the design-thread escalation filter: keep routine progress / CI-wait / success / closeout / idle internal; surface to the design thread ONLY human-needed decisions, with structured evidence and the exact decision needed."),
            Scaffold(" "),
            Operative("Never hide a failure that needs a human.")),
        Fragment(
            "scheduling",
            Operative("End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item it reports before sleeping — never leave one for an unscheduled next wake; `awaiting-operator-merge` is informational patient state and is never urged or age-escalated."),
            Scaffold(" "),
            Operative("Escalate explicitly only when another item is genuinely blocked on an operator decision."),
            Scaffold(" "),
            Operative("This includes a `backlog-ready-idle` item (G544, empty WIP + a ready packet + no activity past the idle threshold) — publish and delegate it in THIS wake, the same as any other issue-cut-ready candidate; only announce a following wake will handle it when that wake is actually scheduled.")),
        Fragment(
            "scheduling",
            Operative("G673 last-net honesty: if stalled-work or heartbeat returns `detection_available=false` / `cause=github-api-quota-exhausted`, do not treat an empty item list as healthy."),
            Scaffold(" "),
            Operative("Record the exhausted resource and `reset_at`, retain and review `partial=true` local findings, and let orchestration decide whether to wait deliberately; no automatic retry, sleep, reset scheduling, or request budgeting is performed."),
            Scaffold(" "),
            Operative("Issue #1442 is the separately attributed remote-herdr measurement; this host's same-day G667 observation is corroboration.")),
        Fragment(
            "scheduling",
            Operative("ESCALATE to the operator ONLY for: product/design judgment, credentials or security, a destructive local action, or an unresolved canonical ambiguity (intent-cli/GitHub facts genuinely conflict or are missing)."),
            Scaffold(" "),
            Operative("Do not escalate states you can repair by message.")),
        Fragment(
            "dispatch_verification",
            Operative("G524/G578: send workflow notifications only through `intent-cli notify`."),
            Scaffold(" "),
            Operative("It validates the agmsg roster or herdr logical-role mapping before delivery and returns a named failure instead of a silent no-op."),
            Scaffold(" "),
            Operative("Fix the active transport's role registration or mapping before retrying; never guess a role name or bypass notify with a handwritten transport call.")),
        Fragment("dispatch_verification", Descriptive("Field-observed loss: 8 dispatches addressed to `review` were silently lost when the registered role was `reviewer` — agmsg neither delivered nor reported the mismatch.")),
        Fragment(
            "design_handoff",
            Descriptive("Setup does not stop at role registration."),
            Scaffold(" "),
            Transport("After the agmsg roles are registered and ready, the DESIGN thread starts (or resumes) orchestration by sending ONE message to the orchestrator; the orchestrator then drives the loop autonomously and returns to design only for human decisions.")),
        Fragment("design_handoff", Transport("{\"to\":\"orchestrator\",\"type\":\"start\",\"domain\":\"__DOMAIN__\",\"target_repo\":\"__OWNER__/__REPO__\",\"requested_action\":\"<e.g. publish the next ready slice and drive it to a PR>\",\"constraints\":\"one action per wake; escalate to design ONLY for human decisions (product/clarification, release/credentials/security, destructive actions, unresolved blockers)\"}")),
        Fragment(
            "design_handoff",
            Operative("If `intent-cli` reports the next slice `issue-cut-ready` and all publish gates pass (see Next-slice publication), the orchestrator creates/publishes ONE GitHub issue ITSELF via canonical intent-cli commands (`issue publish-flow` / `automation issue-publish`) — it does NOT ask design to do each step."),
            Scaffold(" "),
            Operative("At most one issue per wake; verify after publishing before delegating implementation.")),
        Fragment(
            "design_handoff",
            Operative("Routine delegation (publish, delegate, CI wait, review, closeout) stays orchestrator↔receivers and does NOT go to design."),
            Scaffold(" "),
            Operative("Return to DESIGN only for human decisions — product/design clarification, release/credentials/security, destructive actions, or an unresolved blocker — using the structured escalation message (reason / current_state / evidence / decision_needed).")),
        Fragment(
            "design_handoff",
            Transport("The design thread is a loopless receiver and reads on demand."),
            Scaffold(" "),
            Transport("To pick up escalations, the human (or the design thread) checks the design inbox with `inbox.sh` — especially when monitor delivery did not appear live or the design session started after the orchestrator sent."),
            Scaffold(" "),
            Descriptive("Read, decide/reply, then the orchestrator continues.")),
        Fragment(
            "design_traffic_controller",
            Operative("The design thread acts as a TRAFFIC CONTROLLER, not an implementer."),
            Scaffold(" "),
            Operative("It coordinates through the orchestrator and only surfaces human-needed items — it does not drive implementation/review or mutate workflow state itself.")),
        Fragment("design_traffic_controller", Transport("Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.")),
        Fragment("design_traffic_controller", Operative("Check intent-cli / GitHub READ-ONLY state (`intent status`, `worker next-action`, PR/issue/labels) to ground any decision — never trust an agmsg message as state.")),
        Fragment("design_traffic_controller", Operative("Send the orchestrator a state update or a nudge (start/resume); do not drive implementation/review yourself.")),
        Fragment("design_traffic_controller", Operative("Do NOT directly mutate implementation/review work, labels, or host metadata — that is the orchestrator/receivers' job through intent-cli.")),
        Fragment("design_traffic_controller", Operative("Summarize ONLY human-needed items to the human; keep routine progress internal.")),
        Fragment(
            "design_traffic_controller",
            Operative("PRIMARY DESIGN DUTY — intent-tree co-evolution: the intent tree moves WITH development, not after it."),
            Scaffold(" "),
            Descriptive("Leaving the tree unupdated while implementation advances is a serious fault in its own right, not a deferred chore: a tree that describes a design the code no longer has is worse than no tree, because every downstream packet, review, and audit is written against it."),
            Scaffold(" "),
            Operative("Reinforce the tree in the same wake that changes the surface it describes.")),
        // G698 JSON counterpart of the role-attributed closeout item above.
        Fragment(
            "design_traffic_controller",
            Operative(IntentTreeCoEvolutionDuty.RoleSplit),
            Scaffold(" "),
            Operative("Same-cadence write-back check: perform the packet's declared write-backs and RECORD them in the same closeout wake with `intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role design --write` (or `--role orchestration` when orchestration is recording its own mechanical duty)."),
            Scaffold(" "),
            Operative("Until the selected role is recorded, the unit stays visible as a `knowledge-writeback-pending` item in `automation stalled-work` / `automation heartbeat` — closing the PR does not clear it, and nothing here writes intent content on design's behalf.")),
        Fragment(
            "design_traffic_controller",
            Operative("When progress blocks on a design judgment, record that wait before waiting: open judgment-wait with `--owner design`, query the existing record, and whoever supplies the judgment MUST resolve it with evidence."),
            Scaffold(" "),
            Operative("An answered-but-open record is a lie, not a completed design handoff.")),
        Fragment("design_traffic_controller", Operative("Confirm the orchestrator is actually scheduled and on a fresh turn (its `/loop` or Codex automation is running).")),
        Fragment("design_traffic_controller", Transport("Confirm it received your last message (`inbox.sh` on the orchestrator) — a pre-monitor send may be queued, not delivered live; resend after an ack.")),
        Fragment("design_traffic_controller", Operative("Confirm intent-cli actually reports an actionable item for THIS domain/repo (`worker next-action` / `intent status`) — idle may be correct (nothing to do).")),
        Fragment("design_traffic_controller", Operative("Only after these, escalate to the human as a structured decision.")),
        Fragment(
            "design_traffic_controller",
            Operative("The design thread MAY send context to a receiver thread, but MUST mark it context-only (e.g."),
            Scaffold(" "),
            Operative("`context-only: <text>`) unless the orchestrator delegated the action — receivers act only on orchestrator delegations, not on design context.")),
        Fragment(
            "worktree_management",
            Descriptive("Orchestrated work creates temporary worktrees for implementation and review."),
            Scaffold(" "),
            Operative("Allocate them under a managed, allowlisted root inside the workspace and clean them up with `git worktree remove` — NEVER a raw `rm -rf` of an arbitrary `/tmp/intent-review-...` path."),
            Scaffold(" "),
            Descriptive("Safe cleanup design, not disabling approvals, is the right default: a destructive `rm -rf` approval prompt is the symptom of an unmanaged workspace.")),
        Fragment(
            "worktree_management",
            Operative("Allocate temporary worktrees under a repo/workspace-scoped managed root — the `[project] worktree_root` (default `.intent-cli/worktrees/`), git-ignored — not arbitrary `/tmp/intent-review-...` paths."),
            Scaffold(" "),
            Descriptive("A managed root is allowlisted, predictable, and removable with `git worktree remove`.")),
        Fragment("worktree_management", Operative("Create each worktree under the managed root: `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`.")),
        Fragment("worktree_management", Operative("Keep the managed root git-ignored so it never pollutes the tree.")),
        Fragment("worktree_management", Operative("One worktree per role/unit; do not reuse a dirty worktree across units.")),
        Fragment("worktree_management", Operative("Remove a worktree only with `git worktree remove` (it refuses a dirty worktree) — never raw `rm -rf`.")),
        Fragment("worktree_management", Operative("Validate the target path is INSIDE the allowlisted managed root before removal.")),
        Fragment("worktree_management", Operative("Confirm the path is a registered git worktree (it appears in `git worktree list`).")),
        Fragment("worktree_management", Operative("Confirm the worktree state is clean (no uncommitted or untracked user work) before removing.")),
        Fragment("worktree_management", Operative("Prune stale registrations with `git worktree prune` after removal.")),
        Fragment("worktree_management", Descriptive("The target is OUTSIDE the allowlisted managed root.")),
        Fragment("worktree_management", Descriptive("The target is the repo root, `$HOME`, or a system path (`/`, `/tmp` root, etc.).")),
        Fragment("worktree_management", Descriptive("The path is not a registered git worktree.")),
        Fragment("worktree_management", Operative("The worktree has uncommitted or untracked user work — STOP and surface it; do not delete user work.")),
        Fragment(
            "worktree_management",
            Operative("`approval_policy=never` / `danger-full-access` is NOT a substitute for safe cleanup design."),
            Scaffold(" "),
            Operative("Keep least-privilege approvals as the default; the goal is to never need a destructive `rm -rf` prompt, not to suppress the prompt.")),
        Fragment(
            "review_delegation_contract",
            Operative("Review delegation must carry the managed-worktree policy and require design-alignment evidence up front — not leave the reviewer to discover it."),
            Scaffold(" "),
            Descriptive("Dogfooding showed a reviewer allocate a raw `/tmp/...review...` worktree and Codex correctly ask to approve a destructive `rm -rf` — the RIGHT safety behavior for the WRONG workflow."),
            Scaffold(" "),
            Operative("The fix is a managed root, NOT weakening approval settings.")),
        Fragment(
            "review_delegation_contract",
            Operative("Review worktrees use the SAME managed, workspace-local root as the rest of orchestrated work — the `[project] worktree_root` (default `.intent-cli/worktrees/`), e.g."),
            Scaffold(" "),
            Operative("`.intent-cli/worktrees/review-<unit>` — NEVER an arbitrary `/tmp/...review...` path.")),
        Fragment(
            "review_delegation_contract",
            Operative("PROHIBITED as the normal path: a raw `/tmp/...` review worktree, and a `rm -rf /tmp/... && git worktree add ...` cleanup chain."),
            Scaffold(" "),
            Operative("Reaching for this pattern is the signal to STOP and allocate under the managed root instead — not to ask the operator to approve the `rm -rf`.")),
        Fragment("review_delegation_contract", Operative("Cleanup is `git worktree remove <managed-path>` for a REGISTERED, CLEAN worktree only — confirmed via `git worktree list` and a clean `git status` first.")),
        Fragment("review_delegation_contract", Transport("A stale path that is NOT a registered git worktree, is OUTSIDE the managed root, or is dirty/unsafe is NEVER an operator `rm -rf` approval prompt — it is a STRUCTURED BLOCKER agmsg reply to the orchestrator (`status: blocked`) so the orchestrator can route the repair, not something the reviewer resolves by force-deleting an unmanaged path.")),
        Fragment("review_delegation_contract", Transport("{\"delegate\":{\"domain\":\"<domain>\",\"execution_unit\":\"<unit>\",\"target_repo\":\"<owner/repo>\",\"pr\":\"<n>\",\"review_cwd\":\"/review/<domain>\",\"managed_worktree_policy\":\"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never /tmp\",\"design_alignment_required\":true,\"destination_thread\":\"review@<domain>\"}}")),
        Fragment("review_delegation_contract", Descriptive("packet — the authored packet content and acceptance criteria.")),
        Fragment("review_delegation_contract", Descriptive("review-context — the review-context artifact for this PR/unit.")),
        Fragment("review_delegation_contract", Descriptive("intent tree — the relevant intent-tree entries for the touched domain.")),
        Fragment("review_delegation_contract", Descriptive("ADR / decision notes — any linked architecture or design-decision records.")),
        Fragment("review_delegation_contract", Descriptive("relevant docs — user-facing or developer docs the change touches.")),
        Fragment("orchestrator_first_wake", Operative("Confirm you are the ONLY orchestrator for this domain/repo; if a second is detected, STOP and escalate (fail closed).")),
        Fragment(
            "orchestrator_first_wake",
            Operative("Confirm domain scope: in single-domain mode, treat other-domain items visible in the host repo as OUT OF SCOPE (escalate, never delegate); in multi-domain mode, attach full routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree, base branch policy, destination thread) before each delegation."),
            Scaffold(" "),
            Descriptive("Visibility is not authorization, and an execution-unit prefix mismatch alone is not a wrong-repo signal.")),
        Fragment("orchestrator_first_wake", Transport("Read pending agmsg replies from the implementation/review threads (signals only — do not trust them as state).")),
        Fragment("orchestrator_first_wake", Operative("Ask intent-cli for the real state: `intent-cli intent status --domain __DOMAIN__ --format json` and `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.")),
        Fragment("orchestrator_first_wake", Transport("Verify every GitHub fact an agmsg reply claims (PR merged, CI concluded, labels) before acting on it.")),
        Fragment("orchestrator_first_wake", Operative("The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER, not at-most-one-message overall (G524): a publish this wake must be delegated to implementation in this SAME wake — never defer that delegation to an unscheduled next wake — alongside any repair requests (one per stalled receiver) or one operator escalation.")),
        Fragment("orchestrator_first_wake", Operative("Send workflow notifications only through `intent-cli notify`; it resolves the recorded transport and validates the recipient before delivery, failing closed on an unknown role (G524/G578).")),
        Fragment("orchestrator_first_wake", Operative("Do not launch implement/review recurring timers for this domain/repo while orchestrating.")),
        Fragment(
            "orchestrator_first_wake",
            Operative("End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item before sleeping — never leave one for an unscheduled next wake; `awaiting-operator-merge` is deliberately informational patient state, not actionable review debt, and receives no urge or age escalation."),
            Scaffold(" "),
            Operative("Escalate explicitly only when a different item is genuinely blocked on an operator decision.")),
        Fragment("safety_boundaries", Descriptive("agmsg is a message/progress/completion signal layer only; intent-cli and GitHub are authoritative for all workflow state.")),
        Fragment("safety_boundaries", Operative("No raw label mutation (`gh ... --add-label`/`--remove-label`); every label transition goes through intent-cli worker/automation.")),
        Fragment("safety_boundaries", Operative("No hand-editing queue-state, runs.jsonl, packets, or any host metadata (`.intent-cli/**`, `intents/**`).")),
        Fragment("safety_boundaries", Operative("agmsg never replaces semantic review or authorizes a merge; review/closeout decisions run through intent-cli review surfaces (G480).")),
        Fragment("safety_boundaries", Operative("Per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message: a publish's same-wake delegation, repair messages, an escalation, and receiver-report handling may all happen in one wake (G524); never defer a publish's delegation to an unscheduled future wake.")),
        Fragment("safety_boundaries", Operative("Use `intent-cli notify` for every workflow send; it validates the active transport's role source and fails closed on unknown or unavailable recipients (G524/G578).")),
        Fragment("safety_boundaries", Operative("End every wake with a stalled-work check (`automation stalled-work`, G523) and process any actionable item before sleeping; escalate explicitly rather than deferring silently.")),
        Fragment(
            "safety_boundaries",
            Descriptive("Domain isolation: a host repo can hold several domains and one repo can serve several domains, so visibility is not authorization."),
            Scaffold(" "),
            Operative("Single-domain orchestrators ignore/escalate other-domain items; multi-domain orchestrators require explicit per-delegation routing."),
            Scaffold(" "),
            Descriptive("An execution-unit prefix mismatch alone is not a wrong-repo signal.")),
        Fragment("safety_boundaries", Transport("Fail closed on duplicate orchestrators for the same domain/repo, or when an agmsg reply conflicts with intent-cli/GitHub facts — STOP and escalate, never guess.")),
        Fragment("safety_boundaries", Operative("Allocate temporary worktrees under an allowlisted managed root and remove them with `git worktree remove`; never raw `rm -rf` of arbitrary temp paths, and `approval_policy=never`/`danger-full-access` is not a substitute for safe cleanup.")),
        Fragment("safety_boundaries", Operative("Never ask intent-cli to launch Claude/Codex/Copilot or any AI provider; intent-cli only emits text the human agent acts on.")),
        Fragment(
            "next_slice_publication",
            Operative("Routine next-slice issue publication is an ORCHESTRATOR responsibility, not an operator question."),
            Scaffold(" "),
            Operative("When intent-cli reports a candidate as `issue-cut-ready` and ALL safety gates pass, the orchestrator publishes it itself through canonical intent-cli commands instead of stopping to ask the operator to create the GitHub issue."),
            Scaffold(" "),
            Operative("Publish AT MOST ONE issue per wake, then verify, THEN delegate that same issue to the implementation thread in THE SAME WAKE (G524) — publish and delegate complete together; never defer the delegation to an unscheduled \"next wake\", since no other trigger will ever wake the orchestrator to send it (this was the single largest measured stall class in message-driven orchestration, ~60 hours across G807/G809/G810/G812).")),
        Fragment("next_slice_publication", Operative("Same-domain context (`__DOMAIN__`), or an explicitly routed multi-domain delegation (domain, target repo, destination thread) — never publish a cross-domain candidate without explicit routing.")),
        Fragment("next_slice_publication", Operative("The packet contract is complete: no missing required sections (goal, in/out of scope, acceptance criteria, base-branch policy).")),
        Fragment("next_slice_publication", Operative("No open clarification or contract ambiguity on the candidate.")),
        Fragment("next_slice_publication", Operative("Dependencies are satisfied — every dependency execution unit is completed or already cut; never publish ahead of an uncut dependency.")),
        Fragment("next_slice_publication", Operative("Under the WIP cap — no in-progress blocker that should pace the queue first.")),
        Fragment("next_slice_publication", Operative("Clean host-sync / preflight: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json` and the publish preflight report no blocker, and the target repo/domain is unambiguous.")),
        Fragment("next_slice_publication", Operative("Missing contract sections — hold, do not publish.")),
        Fragment("next_slice_publication", Operative("Open clarification / ambiguous contract — hold or escalate one operator decision.")),
        Fragment("next_slice_publication", Operative("Dependency mismatch — an uncut or incomplete dependency; hold (publishing ahead would violate the dependency contract).")),
        Fragment("next_slice_publication", Operative("WIP cap reached — let the in-progress work drain first.")),
        Fragment("next_slice_publication", Operative("Host-sync blocker or failed preflight — fix the sync via intent-cli, do not force the publish.")),
        Fragment("next_slice_publication", Operative("Ambiguous target repo or domain (no explicit routing in multi-domain) — escalate rather than guess.")),
        Fragment("next_slice_publication", Operative("intent-cli issue publish-flow <execution-unit> --repo __OWNER__/__REPO__ --write --format json")),
        Fragment("next_slice_publication", Operative("intent-cli automation issue-publish --write --format json")),
        Fragment("next_slice_publication", Operative("Never raw `gh issue create` or `gh ... --add-label`; publication and the `intent-target` label go through the canonical intent-cli surfaces only.")),
        Fragment("next_slice_publication", Operative("Confirm via intent-cli / GitHub (not chat) that the issue exists with the expected execution-unit body and the `intent-target` label.")),
        Fragment("next_slice_publication", Operative("Confirm the durable workflow state (queue-state / linkage / label) reflects the publish through intent-cli surfaces.")),
        Fragment(
            "next_slice_publication",
            Transport("Immediately after verification, in THIS SAME WAKE, delegate implementation over agmsg (G524) — do not stop after publishing and wait for a future wake to send the delegation."),
            Scaffold(" "),
            Transport("The implementation receiver still derives its target from `intent-cli worker next-action`, not the agmsg text.")),
        Fragment("setup_intake", Descriptive("blocked")),
        Fragment(
            "setup_intake",
            Operative("blocked — shared session-layer preflight did not pass."),
            Scaffold(" "),
            Operative("Record and validate the intended mode before declaring READY or notifying.")),
        Fragment(
            "setup_intake",
            Descriptive("blocked — existing implementation/review timer loops for this domain/repo would race the orchestrator (mixed-mode)."),
            Scaffold(" "),
            Operative("Stop the existing loops (or re-run with --existing-loop-policy will-stop) before starting orchestrator mode; receivers are never scheduled — orchestrator wakes are message-driven by default, with an explicit fallback/legacy timer as the only case where the orchestrator itself is scheduled.")),
        Fragment("setup_intake", Descriptive("__ORCHPATH__")),
        Fragment("setup_intake", Descriptive("__IMPLPATH__")),
        Fragment("setup_intake", Descriptive("__REVIEWPATH__")),
        Fragment("setup_intake", Descriptive("__OAGENT__")),
        Fragment("setup_intake", Descriptive("__IAGENT__")),
        Fragment("setup_intake", Descriptive("__RAGENT__")),
        Fragment("setup_intake", Descriptive("__TEAM__")),
        Fragment("setup_intake", Descriptive("__DELIVERY__")),
        Fragment("setup_intake", Descriptive("keep")),
        Fragment("setup_intake", Descriptive("setup-ready")),
        Fragment("setup_intake", Transport("setup-ready — register the three roles with the agmsg commands, paste the first prompts, then run the first validation.")),
        Fragment("setup_intake", Descriptive("none")),
        Fragment("setup_intake", Transport("agmsg join.sh __TEAM__ orchestrator __OAGENT__ __ORCHPATH__")),
        Fragment("setup_intake", Transport("agmsg delivery.sh set __DELIVERY__ __OAGENT__ __ORCHPATH__")),
        Fragment("setup_intake", Transport("agmsg join.sh __TEAM__ implementation __IAGENT__ __IMPLPATH__")),
        Fragment("setup_intake", Transport("agmsg delivery.sh set __DELIVERY__ __IAGENT__ __IMPLPATH__")),
        Fragment("setup_intake", Transport("agmsg join.sh __TEAM__ review __RAGENT__ __REVIEWPATH__")),
        Fragment("setup_intake", Transport("agmsg delivery.sh set __DELIVERY__ __RAGENT__ __REVIEWPATH__")),
        Fragment("setup_intake", Descriptive("orchestrator")),
        Fragment("setup_intake", Transport("First prompt — paste into the scheduled orchestrator thread.")),
        Fragment(
            "setup_intake",
            Transport("You are the ORCHESTRATOR thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__OAGENT__`, running from `__ORCHPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__)."),
            Scaffold(" "),
            Transport("Your steady state is MESSAGE-DRIVEN — implementation/review agmsg replies wake you; an orchestrator timer (Codex automation 5m or Claude `/loop 5m`) is an OPTIONAL fallback/legacy polling mode, not the default."),
            Scaffold(" "),
            Transport("You pace the implementation/review receivers over agmsg and never run their timers."),
            Scaffold(" "),
            Descriptive("See the full orchestrator prompt in the Thread prompts section.")),
        Fragment("setup_intake", Descriptive("implementation")),
        Fragment("setup_intake", Transport("First prompt — paste into the loopless implementation receiver.")),
        Fragment(
            "setup_intake",
            Transport("You are the IMPLEMENTATION thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__IAGENT__`, running from `__IMPLPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__)."),
            Scaffold(" "),
            Operative("You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait."),
            Scaffold(" "),
            Transport("Your worker target comes from `intent-cli worker next-action`, not the agmsg text."),
            Scaffold(" "),
            Descriptive("See the full prompt in the Thread prompts section.")),
        Fragment("setup_intake", Descriptive("review")),
        Fragment("setup_intake", Transport("First prompt — paste into the loopless review receiver.")),
        Fragment(
            "setup_intake",
            Transport("You are the REVIEW thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__RAGENT__`, running from `__REVIEWPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__)."),
            Scaffold(" "),
            Operative("You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait."),
            Scaffold(" "),
            Transport("Your worker target comes from `intent-cli worker next-action`, not the agmsg text."),
            Scaffold(" "),
            Descriptive("See the full prompt in the Thread prompts section.")),
        Fragment("setup_intake", Operative("Preflight all three cwds BEFORE mutating: `__ORCHPATH__` (orchestrator), `__IMPLPATH__` (implementation), `__REVIEWPATH__` (review) — clean `git status`, expected git remote/repo, expected branch/base, and no existing timer-loop for this domain/repo (see Preflight).")),
        Fragment("setup_intake", Operative("Existing-loop conflict check: confirm no implementation/review recurring timer is running for this domain/repo (implementation/review stay loopless whether the orchestrator runs message-driven or on an explicit fallback/legacy timer).")),
        Fragment("setup_intake", Operative("First read-only wake: run ONE confirm-only orchestrator wake — read state, send nothing.")),
        Fragment(
            "setup_intake",
            Transport("Receiver readiness: ping each receiver and require an ack BEFORE any real delegation — a registered+configured role is not ready until it acks (see the Receiver readiness section)."),
            Scaffold(" "),
            Transport("A session launched before delivery was active may have missed earlier messages; resend or read with `inbox.sh`.")),
        Fragment("setup_intake", Descriptive("will-stop")),
    ];

    /// <summary>
    /// Role-agent placeholders fall back to the general <c>--agent</c> value
    /// when the caller does not set them, exactly as the renderer does. Without
    /// mirroring that fallback, every declaration naming a role agent would
    /// miss on a bare invocation.
    /// </summary>
    /// <summary>
    /// Declaration text stores the caller's inputs as COLLISION-PROOF sentinels
    /// rather than as angle-bracket placeholders, because the guide itself
    /// contains literal <c>&lt;domain&gt;</c> and <c>&lt;owner/repo&gt;</c> —
    /// they are part of command templates a reader is meant to fill in. Keying
    /// on angle brackets made those literals indistinguishable from
    /// interpolation points, and expansion corrupted them. A sentinel appears
    /// nowhere in the document, so expansion touches only what it should.
    /// </summary>
    private static readonly IReadOnlyList<(string Sentinel, string ValueKey)> Interpolations =
    [
        ("__OWNER__/__REPO__", "<owner/repo>"),
        ("__ORCHPATH__", "<orchestrator-path>"),
        ("__IMPLPATH__", "<implementation-path>"),
        ("__REVIEWPATH__", "<review-path>"),
        ("__DELIVERY__", "<delivery-mode>"),
        ("__DOMAIN__", "<domain>"),
        ("__OAGENT__", "<orchestrator-agent>"),
        ("__IAGENT__", "<implementer-agent>"),
        ("__RAGENT__", "<reviewer-agent>"),
        ("__AGENT__", "<agent>"),
        ("__TEAM__", "<team>"),
    ];

    /// <summary>
    /// Role-agent sentinels fall back to the general <c>--agent</c> value when
    /// the caller does not set them, exactly as the renderer does. Without
    /// mirroring that fallback, every declaration naming a role agent would miss
    /// on an invocation that supplied only <c>--agent</c>.
    /// </summary>
    private static readonly IReadOnlyList<string> RoleAgentSentinels =
    [
        "__OAGENT__",
        "__IAGENT__",
        "__RAGENT__",
    ];

    /// <summary>
    /// Renders a declaration's sentinel text with the caller's values, so one
    /// declaration serves every invocation. Expansion is forward-only and
    /// deliberate: an earlier attempt canonicalised the other way, substituting
    /// rendered values back to placeholders, and a short value ("monitor" for
    /// <c>--delivery-mode</c>) rewrote unrelated prose. Expanding a declaration
    /// cannot corrupt the document, because the document is never rewritten.
    /// </summary>
    public static string Expand(IReadOnlyDictionary<string, string> values, string text)
    {
        var expanded = text;
        values.TryGetValue("<agent>", out var agent);

        foreach (var (sentinel, key) in Interpolations)
        {
            values.TryGetValue(key, out var value);
            if (string.IsNullOrWhiteSpace(value) && RoleAgentSentinels.Contains(sentinel))
            {
                value = agent;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                expanded = expanded.Replace(sentinel, value, StringComparison.Ordinal);
            }
        }

        return expanded;
    }

    /// <summary>
    /// Sections whose fragments are declared individually — the mixed sections.
    /// A section outside this set is wholly agmsg-only or wholly
    /// mode-independent and is decided at section granularity by
    /// <see cref="SessionLayerSections"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> DeclaredSections =
        Declarations.Select(d => d.Section).Distinct(StringComparer.Ordinal).ToArray();

    public static bool IsDeclaredSection(string section) =>
        DeclaredSections.Contains(section, StringComparer.Ordinal);

    /// <summary>
    /// True when a line carries no semantics — blank, heading, fence marker, or
    /// table scaffolding. Recognised mechanically because it is a syntactic
    /// fact about markdown, not a judgement about meaning.
    /// </summary>
    public static bool IsStructural(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0
            || trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith("```", StringComparison.Ordinal)
            || IsTableScaffolding(trimmed);
    }

    private static bool IsTableScaffolding(string trimmed) =>
        trimmed.StartsWith("|", StringComparison.Ordinal)
        && trimmed.Replace("|", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Trim().Length == 0;

    /// <summary>
    /// Expands every declaration in <paramref name="declarations"/> for this
    /// invocation and indexes it. Built per render rather than cached, because
    /// the expansion depends on the caller's values.
    /// </summary>
    private static Dictionary<(string, string), SessionLayerSections.FragmentType> BuildIndex(
        IReadOnlyList<FragmentDeclaration> declarations,
        IReadOnlyDictionary<string, string> values)
    {
        var index = new Dictionary<(string, string), SessionLayerSections.FragmentType>();
        foreach (var declaration in declarations)
        {
            var key = (declaration.Section, Expand(values, declaration.Text));
            if (index.TryGetValue(key, out var existing) && existing != declaration.Type)
            {
                throw new InvalidOperationException(
                    $"Conflicting fragment declarations in '{declaration.Section}': the same rendered text is "
                    + $"declared both {existing} and {declaration.Type}. Fragment: {key.Item2}");
            }

            index[key] = declaration.Type;
        }

        return index;
    }

    /// <summary>
    /// The declared type of one rendered markdown fragment. FAILS CLOSED: an
    /// undeclared, non-structural fragment throws instead of defaulting, so a
    /// newly added sentence cannot reach herdr-only output untyped.
    /// </summary>
    public static SessionLayerSections.FragmentType TypeOf(
        IReadOnlyDictionary<string, string> values,
        string section,
        string line)
    {
        if (IsStructural(line))
        {
            return SessionLayerSections.FragmentType.Structural;
        }

        if (BuildIndex(Declarations, values).TryGetValue((section, line.Trim()), out var type))
        {
            return type;
        }

        throw new InvalidOperationException(
            $"Undeclared session-layer fragment in section '{section}'. Every rendered fragment must be typed in "
            + "SessionLayerFragments.Declarations before it can be rendered under a session layer, because an "
            + $"untyped fragment cannot be routed or retained on purpose. Fragment: {line.Trim()}");
    }

    /// <summary>
    /// The declared clauses of one rendered fragment, in order. This is what the
    /// renderers consume: routing and labelling happen per CLAUSE, so a fragment
    /// that mixes mechanism with a binding duty keeps the mechanism and points
    /// away only the transport step, and the descriptive label attaches to the
    /// descriptive clause alone.
    /// </summary>
    public static IReadOnlyList<FragmentClause> ClausesOf(
        IReadOnlyDictionary<string, string> values,
        string section,
        string line)
    {
        if (IsStructural(line))
        {
            return [new FragmentClause(line, SessionLayerSections.FragmentType.Structural)];
        }

        var trimmed = line.Trim();
        foreach (var declaration in Declarations)
        {
            if (declaration.Section != section)
            {
                continue;
            }

            if (Expand(values, declaration.Text) == trimmed)
            {
                return declaration.Clauses!
                    .Select(c => c with { Text = Expand(values, c.Text) })
                    .ToArray();
            }
        }

        throw new InvalidOperationException(
            $"Undeclared session-layer fragment in section '{section}'. Every rendered fragment must be typed at "
            + "sentence granularity in SessionLayerFragments.Declarations before it can be rendered under a session "
            + $"layer. Fragment: {trimmed}");
    }

    /// <summary>JSON counterpart of <see cref="ClausesOf"/>.</summary>
    public static IReadOnlyList<FragmentClause> JsonClausesOf(
        IReadOnlyDictionary<string, string> values,
        string property,
        string value)
    {
        foreach (var declaration in JsonDeclarations)
        {
            if (declaration.Section == property && Expand(values, declaration.Text) == value)
            {
                return declaration.Clauses!
                    .Select(c => c with { Text = Expand(values, c.Text) })
                    .ToArray();
            }
        }

        throw new InvalidOperationException(
            $"Undeclared session-layer JSON fragment under '{property}'. Value: {value}");
    }

    /// <summary>
    /// True when a CLAUSE is descriptive agmsg illustration — the only thing the
    /// agmsg-example label is about. An operative clause is never eligible, and
    /// a descriptive clause naming no transport carries nothing to disclaim.
    /// </summary>
    public static bool IsAgmsgIllustration(FragmentClause clause) =>
        clause.Type == SessionLayerSections.FragmentType.CanonDescriptive
        && clause.Text.Contains("agmsg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a DESCRIPTIVE fragment actually illustrates the model using
    /// agmsg mechanics — the only case the agmsg-example label is about. A
    /// descriptive fragment that names no transport carries nothing for the
    /// label to disclaim, and labelling it would be the same over-reach at
    /// fragment granularity that the section-wide banner was at section
    /// granularity.
    /// </summary>
    public static bool CarriesAgmsgIllustration(
        IReadOnlyDictionary<string, string> values,
        string section,
        string line)
    {
        if (TypeOf(values, section, line) != SessionLayerSections.FragmentType.CanonDescriptive)
        {
            return false;
        }

        return line.Contains("agmsg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The JSON counterpart of <see cref="CarriesAgmsgIllustration"/>. Returns
    /// false for a value that routing already replaced, and for anything that is
    /// not descriptive agmsg illustration.
    /// </summary>
    public static bool JsonCarriesAgmsgIllustration(
        IReadOnlyDictionary<string, string> values,
        string property,
        string value)
    {
        if (value.Contains(SessionLayerSections.MechanicPointer, StringComparison.Ordinal))
        {
            return false;
        }

        return JsonTypeOf(values, property, value) == SessionLayerSections.FragmentType.CanonDescriptive
            && value.Contains("agmsg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The declared type of one rendered JSON value. Fails closed exactly as the
    /// markdown lookup does.
    /// </summary>
    public static SessionLayerSections.FragmentType JsonTypeOf(
        IReadOnlyDictionary<string, string> values,
        string property,
        string value)
    {
        if (BuildIndex(JsonDeclarations, values).TryGetValue((property, value), out var type))
        {
            return type;
        }

        throw new InvalidOperationException(
            $"Undeclared session-layer JSON fragment under '{property}'. Declare it in "
            + "SessionLayerFragments.JsonDeclarations with an explicit type before it can be rendered under a "
            + $"session layer. Value: {value}");
    }
}
