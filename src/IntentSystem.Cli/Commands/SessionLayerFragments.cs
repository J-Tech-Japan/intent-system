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
    /// Declares a fragment as an ordered list of independently typed clauses.
    /// The row's own type is the strongest routing consequence of its clauses,
    /// so existing lookups keep working; the clause list is what the descriptive
    /// labelling and the guards read.
    /// </summary>
    private static FragmentDeclaration Mixed(string section, params FragmentClause[] clauses)
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
        new(S0, "- session layer: Recorded session layer for this setup: agmsg (PRIMARY) (recorded). Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team <team> --mode agmsg|herdr-only --write`. A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- session layer: Recorded session layer for this setup: agmsg (PRIMARY) (recorded). Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team __TEAM__ --mode agmsg|herdr-only --write`. A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- session layer: Recorded session layer for this setup: agmsg (PRIMARY) (default — nothing recorded yet). Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team <team> --mode agmsg|herdr-only --write`. A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- session layer: Recorded session layer for this setup: agmsg (PRIMARY) (default — nothing recorded yet). Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team __TEAM__ --mode agmsg|herdr-only --write`. A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- session layer: Recorded session layer for this setup: herdr-only (PREVIEW — session transport only) (recorded). Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team <team> --mode agmsg|herdr-only --write`. A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- session layer: Recorded session layer for this setup: herdr-only (PREVIEW — session transport only) (recorded). Record or change it with `intent-cli session-layer set --domain __DOMAIN__ --team __TEAM__ --mode agmsg|herdr-only --write`. A herdr-only request made at first setup is honoured from then on; the choice is reversible in both directions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- setup-ready (herdr-only) — the registration, delivery-configuration and role-prompt steps of this intake are agmsg-only and do not apply. Their herdr-only counterparts ship in G571.", SessionLayerSections.FragmentType.CanonDescriptive),
        // The herdr-only summary. Mode-specific by construction — it is
        // written in the herdr-only voice rather than routed.
        new(S0, "PRIMARY four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over the session layer this team runs — herdr-only here. The session layer carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout. Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation). The herdr-only operating steps ship in G571.", SessionLayerSections.FragmentType.CanonDescriptive),
        // The missing-input count varies with which inputs the caller supplied.
        // Only the counts the intake actually reports are declared: a new count
        // is a new fragment, and failing closed on it is the point.
        new(S0, "- missing-inputs — supply the 4 missing field(s) below to get a setup-ready plan.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        // The recorded-session-layer intake line, in every rendering it has:
        // each mode description x recorded/default x team supplied/missing.
        // It is an intent-cli instruction, so it binds in both modes.
        new(S0, "- **status: `missing-inputs`**", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "- missing-inputs — supply the 6 missing field(s) below to get a setup-ready plan.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- The implementation and review threads are loopless agmsg receivers — they must NOT run their own `/loop` or recurring timer for the same domain/repo, whether the orchestrator runs message-driven (the default, woken by agmsg replies) or on an explicit fallback/legacy timer (Codex 5m / Claude `/loop 5m`).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "- orchestrator folder", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "- implementation folder", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "- review folder", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "- agmsg team name", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "- delivery mode", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "- existing-loop stop policy", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "PRIMARY agmsg-backed four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over agmsg. agmsg carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout. Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation).", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new(S1, "Under authority the OPERATOR granted it, the design thread drives the team's SESSION LAYER through the workspace manager: it provisions the team (see `Terminal-workspace provisioning`), keeps the sessions alive and correctly held, and supervises for stalls. It answers a blocking dialog only inside an explicit boundary and only after READING that dialog from the pane; everything outside the boundary escalates to the operator. This adds a session-layer role — it moves NO workflow authority.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S1, "Two layers, two owners. The SESSION layer (panes, processes, holds, blocking dialogs) is what the operator grants the design thread. The WORKFLOW layer (labels, queue-state, publication, delegation, closeout) is not granted and never moves — it stays with intent-cli, GitHub, and the orchestrator exactly as before.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S1, "- **authority is granted, not assumed** — the design thread supervises the session layer because the operator asked it to, and the grant's scope is what the operator stated. Outside a grant the design thread observes and reports rather than acts. A grant to supervise sessions is never read as a grant to decide workflow, product, or security questions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "The design thread operates the session layer:", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S1, "- PROVISIONING — build the team's workspace, folders, panes, launches, and role initialization per `Terminal-workspace provisioning` (G549); supervision references that section rather than repeating it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- SESSION LIFECYCLE — investigate an unresponsive session and, when it must be replaced, do so through the graceful drop that honors one-holder exclusivity.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- STALL SUPERVISION — run the three supervision layers below so a stall is noticed by a layer that is actually running, not by luck.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- BLOCKING DIALOGS — answer only what the MAY list allows, only after the verified read; escalate everything else.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "> **Workflow state ownership:** workflow state ownership does not move. Labels, queue-state, publication, delegation, CI/review gating, and closeout remain with intent-cli, GitHub, and the orchestrator; the design↔orchestrator double-check rule and the orchestrator's ownership of workflow transitions apply exactly as before. Supervising a session never authorizes a workflow transition, and a stuck pane is never a reason to move a label by hand.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S1, "A session that stops responding is a session-layer fault, and the design thread may repair it — but repair means restoring a correctly held, live session, not taking over the role's work or its decisions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- READ the pane first — an \"unresponsive\" session is most often blocked on a dialog, a trust screen, or a prompt waiting for input, not dead. Diagnose from what the pane actually shows.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- Distinguish the layers: a live session that is merely not attached to delivery is a delivery problem (re-check the readiness layers), not a reason to replace the session.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- Confirm the role is still held by that session before concluding anything — a role silently dropped elsewhere looks identical to a dead session from the outside.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- Prefer the least invasive repair that restores liveness: answer an in-boundary dialog, re-arm delivery, or restart the session — replacement is the last step, not the first.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- **one holder per role** — replacing a session never means two sessions holding the same role for even a moment. The successor claims only after the incumbent's hold is released; a refused actas is the exclusivity rule working, not an obstacle to route around.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- **graceful drop** — Replace through the GRACEFUL DROP: the incumbent drops the role (releasing its exclusivity lock and registration), then the successor claims it and re-runs readiness plus the ping test. Never kill a pane and assume the hold cleared, and never force a role away from a live session.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- **operator-visible confirmation** — The drop's confirmation is OPERATOR-VISIBLE: the handover surfaces to the operator rather than happening silently inside the design thread. The design thread may request and sequence the handover; the decision to retire a live session remains the operator's, and the confirmation is what records it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **real-time message monitor**", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- purpose — Catch inbound agmsg traffic as it arrives — replies, blockers, and escalations that should wake the design thread immediately.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- cadence — continuous / real-time (a live attached inbox stream, not a poll).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- note — This layer is what the message-driven steady state assumes. It sees only what is SENT — it cannot notice a session that went quiet or a pane blocked on a dialog, which is why the other two layers exist.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- **blocking-UI pane scan**", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S1, "- purpose — Notice panes that are stuck with nothing to say. TWO EQUAL stuck states: a pane blocked on an approval, selection, or trust prompt, AND a pane showing a shell prompt where an agent should be (`agent-absent`, G556). Both produce no message at all — a blocked agent is waiting and a dead one cannot speak — so no message-driven layer can ever detect either.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- cadence — sub-minute class (e.g. every few tens of seconds) — a blocking dialog stalls a role for its entire lifetime, and an agent that died seconds after reporting stays dead until someone looks, so this layer is the fast one.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- note — Scanning is READING, and what the scan finds routes by STATE, not by one rule for everything. A blocking dialog goes to the dialog rules below — answer only what the MAY list covers after the verified read, and escalate the rest. An `agent-absent` shell prompt is NOT a dialog and must never be routed through dialog handling: it goes to the shim-safe relaunch recovery (recreating the app-server when that is what died), followed by the COMPLETE verified-liveness re-check — report, settle delay, all three checks. See `What the pane scan is looking for` for both recoveries.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **periodic state watchdog**", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S1, "- purpose — Compare canonical intent-cli/GitHub state against expected progress and nudge the orchestrator when work has gone stale — the existing design-thread watchdog (`intent-cli automation heartbeat --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- cadence — tens-of-minutes class (e.g. every 30 minutes) — quiet enough to stay out of the way, frequent enough to bound a stall.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- note — This is the existing watchdog, not a second one: its safety rules apply verbatim (see the watchdog safety-rules reference below). One canonical nudge per wake, never a batch.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **blocking dialog** — the scan sees: an approval, selection, or trust prompt waiting for input. Recovery: handle it under the dialog rules — answer only what the MAY list covers after the verified read, escalate the rest.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **agent-absent** — the scan sees: a SHELL PROMPT where an agent should be — the pane looks like an ordinary terminal, often with a resume hint left on screen. The agent exited; it may have reported startup successfully seconds earlier. Recovery: RELAUNCH THROUGH THE SHIM: type the launch into the pane's interactive shell (never spawn the executable), recreating the app-server first when it is the thing that died. Set the permission mode with the LAUNCH FLAG (e.g. `--permission-mode`) rather than trying to switch it afterwards: a workspace manager's synthetic key injection cannot be relied on for mode switching — plain keys are delivered, but modifier chords such as shift+tab are not delivered faithfully (observed across multiple teams). Then run the FULL verified-liveness sequence again — report, settle delay, all three checks.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "> **Re-arm across restarts:** supervision schedulers are session-scoped: a `/loop`, an automation, or an attached monitor dies with the design session that hosts it, and nothing announces that it stopped. Every supervision layer must either survive a design-session restart or be RE-ARMED as the first act of the new session — treat re-arming as part of starting the session, not as an optional follow-up. Field cost of forgetting: a claim-now lost inside a session-restart window left a published issue stalled for 5.5 HOURS because no supervision layer happened to be running.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "> **Verified read before answer:** the design thread may answer a dialog ONLY after it has actually read that dialog's content from the pane and can state what it is approving. A blind keystroke into a dialog it has not rendered is prohibited, no matter how routine the prompt looks or how confident it is about which key clears it. If the content cannot be read or cannot be verified, the dialog is an escalation, not an answer.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **confirmations of work the design thread itself requested** — verify: the read pane's prompt must match an action THIS design thread just initiated — same target, same operation. A confirmation it cannot trace to its own request is not its to answer.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **command approvals verified read-only** — verify: the exact command shown in the pane must be read and verified to be READ-ONLY. Anything that writes, deletes, installs, publishes, or mutates state fails this check and escalates — \"probably read-only\" is not verified.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **trust screens for hooks the design thread itself installed** — verify: the trust screen must name a hook THIS design thread installed as part of this provisioning (its own hook-trust case). A trust screen for anything it did not install is not its to accept.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **operator-preauthorized mode changes** — verify: the operator must have PREAUTHORIZED this specific mode change, and the read pane must show that same change. Preauthorization is specific and prior — it is never inferred from a general grant to supervise sessions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **unreadable or unverifiable dialogs** — if the pane content cannot be read, or the claim it makes cannot be verified, there is nothing to base an answer on — answering would be guessing on the operator's behalf.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **destructive or irreversible approvals** — deletions, force operations, overwrites, and anything else that cannot be undone are the operator's call — the cost of a wrong answer is unbounded and unrecoverable.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **choices that embed a product or design decision** — a dialog that picks behavior, scope, or defaults is design content, and design content goes through the operator and the design↔orchestrator double-check — not through whoever happens to be unblocking a pane.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **credential, security, and permission waits** — these are NEVER answerable by the design thread, with or without prior authorization: they always remain unanswered and always escalate to the operator. No grant makes them answerable.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "> **Boundary:** UNSTICKING A SESSION IS NOT DECIDING FOR IT. The design thread's job is to keep the session layer alive so the role can do its own work — not to make the role's choices, and not to make the operator's.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S1, "- **provisioning** — Provisioning is NOT repeated here — see `Terminal-workspace provisioning` for role folders, workspace topology, shim-safe launch, actas/readiness, and the exclusivity/handover rules this section supervises.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S1, "- **watchdog safety rules** — The watchdog safety rules apply to ALL supervision verbatim: no duplicate delegation, no clearing a permission prompt, no cancelling or resetting in-flight work, no force-closing an issue/PR, and no speculative durable-state surgery (no hand-edited labels, queue-state, or host metadata). See `Design-thread watchdog (recommended safety net)`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "A hold blocked on a DESIGN DECISION must be visible and bounded. Visible: it is recorded as a clarification artifact through the canonical clarify surface, so `automation stalled-work` and `automation heartbeat` can see it — an agmsg message alone is invisible to every supervision layer. Bounded: the operator may pre-delegate enumerated, mechanically fact-checkable decision classes so a correction both threads can verify from repository facts does not wait on design at all. Measured cost of getting this wrong: a nine-hour hold on a one-line wording ruling while every technical check was green and `stalled-work` reported `stalled=false` throughout.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "When the orchestrator or the reviewer blocks on a design decision, it RECORDS A CLARIFICATION ARTIFACT through the canonical clarify surface, in addition to whatever agmsg message it sends. The artifact is what makes the hold detectable; the message is only a notification.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "Record these fields:", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- domain — the blocked domain (`__DOMAIN__`), so the artifact is scoped to the right pipeline.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S2, "- blocking execution unit — the unit that cannot proceed until this is answered.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S2, "- question — what design must decide, stated so someone who was not in the thread can answer it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- recommended answer — when the asking thread already believes it knows the answer, state it and cite the facts that support it; design then confirms or overrides rather than starting from scratch.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "> **Contract violation:** An agmsg-only hold is a CONTRACT VIOLATION, not a shortcut. A block that exists only as messages is invisible to `stalled-work`, to `heartbeat`, and therefore to every watchdog and every operator glance — which is exactly how a nine-hour hold passed unnoticed with the pipeline reporting healthy. If you are waiting on design, the artifact exists; if the artifact does not exist, you are not waiting, you are stalled.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- record the hold — `intent-cli clarify open` (the canonical clarify surface; never hand-write the artifact)", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- see what is open — `intent-cli clarify list`", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- answer it — `intent-cli clarify answer` (design, or the operator on escalation)", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- confirm it is visible — `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json` reports `design-decision-pending`", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "Paste-ready — the OPEN artifact carries the real content, not a packet-derived synthesis:", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S2, "intent-cli clarify open <execution-unit> \\", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "--question \"<the actual design-blocking question, answerable by someone outside the thread>\" \\", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "--recommended-answer \"<what you believe the answer is, when you believe you know it>\" \\", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "--evidence \"<the repository facts that support the recommendation>\"", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "The reviewer's hold rule is refined so a green-technical review never becomes an untracked wait. Evaluate what is actually pending before holding.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **resolve under granted authority when** — Technical checks are GREEN and the only pending item is NON-SEMANTIC and MECHANICALLY FACT-CHECKABLE from repository facts — resolve it under bounded default authority (below), log the resolution with the verifying facts, and proceed. Do not hold a green review on a question whose answer both threads can derive and cite.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **record a clarification otherwise** — Anything else — a semantic or product question, a fact you cannot verify, or a class the operator has not delegated — becomes a recorded clarification and a VISIBLE pending state. The review is still held; the difference is that the hold is now on disk and detectable.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "> **Never an untracked wait:** there is no third option where the reviewer simply waits and says so in a message: either the item is resolved under granted authority with its evidence, or a clarification artifact exists. Silence with a message attached is the failure mode this rule exists to remove.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "BOUNDED DEFAULT AUTHORITY lets the operator pre-delegate a small, enumerated set of decision classes that can be settled by checking repository facts rather than by judgment. It exists so a count correction does not cost nine hours. It is bounded in every direction: granted, enumerated, evidence-logged, amendable, and never semantic.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S2, "- **operator grant required** — GRANTED, never assumed. The authority applies only to classes the OPERATOR has explicitly pre-delegated for this domain. Absent a grant, every design decision goes to design as before — the default is unchanged, and no thread may infer a delegation from the fact that an answer seems obvious.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **count and enumeration corrections** — verify: the count is derivable from repository facts both threads can read — e.g. a slice count derived from the merged PR list and the issue's own enumeration. Cite the list and the derivation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **wording corrections that follow from a cited fact** — verify: the corrected wording is entailed by a fact in the repository (a merged PR title, a label state, a retired unit's own record), and the reviewer and orchestrator AGREE on both the fact and the correction. Disagreement is not fact-checkable — it escalates.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **cross-reference and link corrections** — verify: the target exists (or does not) in the repository as cited — verifiable by reading the referenced file, heading, issue, or PR.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **identifier and metadata mismatches against a canonical source** — verify: the canonical source is named and read — e.g. a version in `eng/version.json`, a unit id in a packet, a label in the canonical palette. The canonical source wins; the resolution cites it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **evidence logging** — MANDATORY EVIDENCE LOGGING. A resolution taken under this authority is recorded in the durable trail with the facts that verify it — what was decided, which repository facts entail it, and which threads agreed. An unlogged resolution is not a granted-authority resolution; it is an undocumented decision, and it is a violation of this contract.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **evidence sink** — The sink is the CANONICAL `clarify record` surface: the entry lands under `## Recently Resolved` in the domain's clarification return path (`intents/<domain>/clarifications/open.md`), where `Question` identifies the pending item, `Decision` records the decided value, and `Rationale` records the verified repository facts plus the reviewer/orchestrator agreement. The entry is durable and stays readable there, which is exactly what makes design's post-hoc amendment possible — design reads the recorded evidence and amends or reverses from it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **post-hoc amendment** — DESIGN MAY AMEND POST HOC. A granted-authority resolution is provisional in design's eyes: design can review the logged evidence afterwards and amend or reverse the decision. The authority buys latency, not finality — proceeding does not close the question against design.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S2, "Paste-ready evidence operation:", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S2, "cat > /tmp/authority-decision.md <<'EOF'", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "<the pending item, identified so design can find it later>", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "<the decided value>", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "<the verified repository facts that entail it, and which threads agreed>", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "EOF", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "intent-cli clarify record --domain <domain> --from-file /tmp/authority-decision.md", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "> **Semantic exclusion:** SEMANTIC AND PRODUCT DECISIONS ARE EXCLUDED, absolutely. Intent shaping, packet content and acceptance criteria, release scope, prioritization rulings, and anything requiring product or design judgment always go to design through the design↔orchestrator double-check rule, whose scope this contract does not touch. If settling the question requires deciding what SHOULD be true rather than checking what IS true, it is not fact-checkable and this authority does not reach it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "While a clarification stays open, the design thread is reminded on a fixed cadence. A recorded hold that nobody re-surfaces is still a slow hold — the artifact makes it detectable, the reminder makes it noticed.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S2, "- **sender** — The ORCHESTRATOR sends the reminder from its long-interval automation — the same wake that already runs the heartbeat check. No new scheduler, and the receivers stay loopless.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **interval** — 30–60 minute class — the same low-frequency band as the heartbeat and the design-thread watchdog. Faster polling recreates the churn the message-driven model removes; slower lets a hold sit past the point an operator would want to know.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **one per interval per clarification** — AT MOST ONE reminder per interval PER OPEN CLARIFICATION. Two open clarifications produce at most two reminders in a wake; one clarification never produces two reminders in the same interval no matter how many wakes fire. This is the same one-message discipline the watchdog already follows.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **stop on answer** — STOP ON ANSWER. Once the clarification is answered (or applied, or cancelled) it is no longer open, `design-decision-pending` clears on its own, and the reminders stop. Never keep reminding against an answered clarification, and never re-open one to keep a thread's attention.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S2, "- **operator app** — The design thread runs in the OPERATOR APP by preference, which is what makes a reminder land either way: an OPEN design session receives the reminder immediately through its monitor, and a CLOSED one finds it waiting in the inbox on resume. Neither case requires design to be resident in the team workspace — there is no workspace-residency requirement here.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new(S2, "- **detection** — Detection is `design-decision-pending` in `automation stalled-work`: it reads the domain's OPEN clarification artifacts and reports each with its age, blocking execution unit, and question summary, and `automation heartbeat` carries it in `message_body` like any other kind. Confirm a hold is visible with `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`; if the hold is real but the kind is absent, the clarification artifact was never recorded — which is the contract violation above, not a detector bug.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "Assume you are NOT alone on this machine. Several project teams run simultaneously, and every substrate below is shared across all of them — the workspace manager's server, the agmsg run directory, the codex app-servers, the host repo. `Terminal-workspace provisioning` and `Design-thread workspace supervision` describe how to build and keep ONE team; this section is what keeps that team from damaging another. It narrows the OBJECTS you may act on to your own team's; it does not widen or narrow what you may DO, so the supervision authority boundary applies unchanged. Operator incident (2026-07-29): with several teams live, one project's design thread damaged another project's resources and the operator had to intervene by hand.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "Before you touch anything, establish that it belongs to YOUR team. Attribution is a positive result from the keys below — not the absence of evidence that it belongs to someone else, and not a name that merely looks familiar.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "Attribution is required before any of these:", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- injecting keys or text into a pane", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S3, "- killing a process", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S3, "- closing or restructuring a workspace", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S3, "- removing or rewriting a state file", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S3, "Verify ownership with all four keys:", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **workspace label** — the workspace is labelled with YOUR team/project name. A workspace you did not create and cannot name is not yours.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **pane cwd** — the pane's working directory is one of YOUR team's dedicated role folders. A pane whose cwd you do not recognize belongs to someone.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **process cwd** — the process's own working directory — read it per pid before any kill, exactly as the 2026-07-27 migration did when it spared another project's processes. A pid list filtered only by process NAME attributes nothing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **agmsg `(team, role)` file naming** — agmsg run-directory state files are named per `(team, role)`; a file whose team segment is not yours is another team's bridge/watcher state, however broken it looks.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new(S3, "> **Unverifiable = read-only:** if you cannot positively establish ownership, the object is READ-ONLY to you: you may look and you may report — you may not mutate. Escalate to the operator instead of guessing: a wrong guess here is another team's outage, and the cost is theirs rather than yours, which is exactly why the default has to be refusal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **one workspace per team** — one workspace per team, labelled with the team/project name. Never reuse, repurpose, or borrow another team's workspace or its panes — not even an idle-looking one. A workspace is the unit an operator reads to know whose work is whose; sharing one collapses that.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **team-exclusive role folders** — one folder belongs to exactly ONE team. Never launch your agents in another team's folders. This is the same folder-scoping fact that forbids two roles sharing a folder within a team (G521) — agmsg identity and the codex bridge are folder-scoped, so an agent started in another team's folder takes over THEIR identity and delivery, not just its own.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        // The table header — column labels, not content.
        Mixed(
            S3,
            Scaffold("|"),
            Descriptive(" substrate "),
            Scaffold("|"),
            Descriptive(" sharing unit "),
            Scaffold("|"),
            Descriptive(" ownership rule "),
            Scaffold("|")),
        // Mixed row: descriptive substrate identity + a binding ownership rule.
        Mixed(
            S3,
            Scaffold("|"),
            Descriptive(" workspace-manager server (e.g. the herdr server) "),
            Scaffold("|"),
            Descriptive(" one server process serving EVERY workspace on the machine "),
            Scaffold("|"),
            Operative(" ownership is per WORKSPACE, never the server. Act on your own workspace and its panes; never restart, reconfigure, or kill the shared server — doing so takes down every other team's workspace at once. "),
            Scaffold("|")),
        // Mixed row: descriptive substrate identity + a binding ownership rule.
        Mixed(
            S3,
            Scaffold("|"),
            Descriptive(" agmsg run directory (`~/.agents/skills/agmsg/run`) "),
            Scaffold("|"),
            Descriptive(" one directory holding bridge / watcher / app-server state for ALL teams "),
            Scaffold("|"),
            Operative(" ownership is per `(team, role)` FILE. Touch only files whose team segment is yours; never clear the directory wholesale to fix your own delivery — that is another team's bridge state you are deleting. "),
            Scaffold("|")),
        // Mixed row: descriptive substrate identity + a binding ownership rule.
        Mixed(
            S3,
            Scaffold("|"),
            Descriptive(" codex app-servers "),
            Scaffold("|"),
            Descriptive(" one app-server per FOLDER, and folders belong to teams "),
            Scaffold("|"),
            Operative(" ownership follows the folder. Verify the process's cwd before stopping an app-server; a same-named process rooted in another team's folder is theirs. "),
            Scaffold("|")),
        // Mixed row: descriptive substrate identity + a binding ownership rule.
        Mixed(
            S3,
            Scaffold("|"),
            Descriptive(" host repo "),
            Scaffold("|"),
            Descriptive(" one repo holding EVERY domain's metadata "),
            Scaffold("|"),
            Operative(" ownership is per DOMAIN path. Write only through the canonical commands for your own domain; queue-state is protected against concurrent writers by the no-item-loss invariant and stale-base re-application (G548), which is a safety net, not a licence to hand-edit another domain's state. "),
            Scaffold("|")),
        new(S3, "When you find damage — including damage you caused — recovery is NON-DESTRUCTIVE. The instinct to tidy up is the failure mode: a broken artifact belonging to another team is still their evidence, and deleting it destroys their ability to diagnose what happened.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **preserve theirs** — PRESERVE and SET ASIDE another project's damaged artifacts — rename, move aside, or simply leave them in place and report. Never delete another team's workspace, panes, folders, processes' state, or files, however broken they look. Tell the operator and the affected team's thread what you found and what you set aside.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S3, "- **rebuild yours** — REBUILD YOUR OWN fresh rather than repairing in place: create a new workspace, new panes, new role folders as needed, and re-run provisioning. Your own damaged artifacts may also be set aside rather than deleted when they carry evidence worth keeping.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S3, "> **Recovery defaults to RECREATE, NOT CLEANUP.**", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "In orchestrator-message mode the normal steady state is MESSAGE-DRIVEN: implementation/review receivers already send accepted/progress/completed/blocked replies to the orchestrator, and those replies wake the orchestrator path — routine fast polling is NOT required. An orchestrator timer (Codex automation every 5m, or Claude same-thread `/loop 5m`) remains SUPPORTED but only as an explicit FALLBACK/LEGACY polling option for an operator who intentionally wants scheduled polling instead of message-driven wakes. Either way the implementation and review threads stay long-lived LOOPLESS receivers. The RECOMMENDED default safety net for message-driven steady state is a 30-minute-class design-thread watchdog (see Design-thread watchdog), not a fast orchestrator loop.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S4, "- scheduled thread when an explicit timer is used: `orchestrator` (the only thread ever scheduled)", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S4, "- **receivers are loopless** — Implementation and review threads are loopless receivers: do NOT start a recurring timer/loop in a receiver thread for this domain/repo. A receiver waits for an agmsg delegation, acts once, replies once, and waits again. Receivers are NEVER scheduled; when an explicit fallback/legacy timer is used (message-driven wakes are the default), the orchestrator is the only thread ever scheduled.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S4, "OPTIONAL fallback/legacy polling — Codex automation (run every 5 minutes) for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__AGENT__`: on each run perform exactly ONE orchestrator wake — check design-side progress and agmsg replies, ask intent-cli for state (`intent status`, `worker next-action --github-only`, `automation host-review-preflight`), verify the GitHub facts (CI/approval/merge/closeout), then send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER (implementation, review), NOT at-most-one-message overall, so a publish plus its same-wake delegation, one repair per stalled receiver, and one operator escalation may all go out together — and exit. Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling. Do not run implementation/review loops; they are loopless receivers.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S4, "OPTIONAL fallback/legacy polling — Claude same-thread setup for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__`: in the orchestrator thread run `/loop 5m` with the orchestrator prompt so the same thread re-wakes every 5 minutes. Each wake does exactly one orchestrator pass (read replies, check intent-cli / GitHub state, send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER, NOT at-most-one-message overall). Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling. Do NOT also launch `/loop` in the implementation or review threads — those are loopless receivers driven only by your delegations.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S4, "- A wake is triggered either by an incoming agmsg reply from implementation/review (the message-driven steady state) or by the optional fallback/legacy timer firing — either trigger runs exactly one orchestrator pass below.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S4, "- Check design-side progress: newly published packets/issues and intent status changes via `intent-cli intent status --domain __DOMAIN__ --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- Read pending agmsg replies from the implementation/review receivers (signals only — re-verify against intent-cli / GitHub).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S4, "- Ask intent-cli for worker state: `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- Check host review readiness: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge state, and closeout/label state.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- Classify each open PR's CI: pending = wait-and-recheck next wake (no message); green = delegate review/closeout; red = repair or escalate by ownership; stuck = escalate. Pending CI is normal progress, not a reason to message the operator.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- Detect stale blockers and no-reply receivers: a delegation with no accepted/progress reply within the expected window, or a thread stuck off the official workflow.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- On a no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check: send one non-destructive status-request, check read-only intent-cli/GitHub facts, keep watching if there is progress, treat waiting-permission as an operator notice (never auto-clear), and only after repeated no-reply with no progress send one idempotent re-entry or escalate.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- If intent-cli reports an `issue-cut-ready` candidate and all gates pass (same-domain or routed, complete contract, no open clarification, dependencies satisfied, under WIP, clean host-sync/preflight), publish ONE issue this wake via canonical publish-flow / issue-publish, verify it, THEN delegate that same issue to implementation in THIS SAME WAKE (G524) — do not ask the operator to create it, and do not stop after publishing to wait for a future wake to send the delegation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- If the candidate has unmet dependencies, plan the chain instead of pausing: act on the EARLIEST unmet resolvable dependency (publish or route it), keep the dependent held, and escalate only ambiguous/cycle/cross-domain-unrouted cases.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message overall (G524): this wake's actions may include a publish plus its same-wake delegation, one repair message per stalled receiver, one operator escalation, and handling any pending receiver reports, all together.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- Before sending any agmsg message this wake, verify the recipient id against the team roster (`agmsg team.sh`) — treat an id not on the roster as an error, never a guess (G524).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S4, "- Apply the design-thread escalation filter: keep routine progress / CI-wait / success / closeout / idle internal; surface to the design thread ONLY human-needed decisions, with structured evidence and the exact decision needed. Never hide a failure that needs a human.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item it reports before sleeping — never leave one for an unscheduled next wake; escalate explicitly if it is genuinely blocked on an operator decision. This includes a `backlog-ready-idle` item (G544, empty WIP + a ready packet + no activity past the idle threshold) — publish and delegate it in THIS wake, the same as any other issue-cut-ready candidate; only announce a following wake will handle it when that wake is actually scheduled.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- **repair** — REPAIR routine off-rail states yourself by messaging the appropriate thread back onto the official intent-cli workflow — e.g. a receiver that stalled, skipped `worker complete`, applied a label by hand, or has not replied. Routine recovery is a repair message, not an escalation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S4, "- **escalate** — ESCALATE to the operator ONLY for: product/design judgment, credentials or security, a destructive local action, or an unresolved canonical ambiguity (intent-cli/GitHub facts genuinely conflict or are missing). Do not escalate states you can repair by message.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "Routine next-slice issue publication is an ORCHESTRATOR responsibility, not an operator question. When intent-cli reports a candidate as `issue-cut-ready` and ALL safety gates pass, the orchestrator publishes it itself through canonical intent-cli commands instead of stopping to ask the operator to create the GitHub issue. Publish AT MOST ONE issue per wake, then verify, THEN delegate that same issue to the implementation thread in THE SAME WAKE (G524) — publish and delegate complete together; never defer the delegation to an unscheduled \"next wake\", since no other trigger will ever wake the orchestrator to send it (this was the single largest measured stall class in message-driven orchestration, ~60 hours across G807/G809/G810/G812).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- one_per_wake: yes", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S5, "- Same-domain context (`__DOMAIN__`), or an explicitly routed multi-domain delegation (domain, target repo, destination thread) — never publish a cross-domain candidate without explicit routing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- The packet contract is complete: no missing required sections (goal, in/out of scope, acceptance criteria, base-branch policy).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- No open clarification or contract ambiguity on the candidate.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Dependencies are satisfied — every dependency execution unit is completed or already cut; never publish ahead of an uncut dependency.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Under the WIP cap — no in-progress blocker that should pace the queue first.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Clean host-sync / preflight: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json` and the publish preflight report no blocker, and the target repo/domain is unambiguous.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Missing contract sections — hold, do not publish.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Open clarification / ambiguous contract — hold or escalate one operator decision.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Dependency mismatch — an uncut or incomplete dependency; hold (publishing ahead would violate the dependency contract).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- WIP cap reached — let the in-progress work drain first.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Host-sync blocker or failed preflight — fix the sync via intent-cli, do not force the publish.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Ambiguous target repo or domain (no explicit routing in multi-domain) — escalate rather than guess.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- intent-cli issue publish-flow <execution-unit> --repo __OWNER__/__REPO__ --write --format json", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- intent-cli automation issue-publish --write --format json", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Never raw `gh issue create` or `gh ... --add-label`; publication and the `intent-target` label go through the canonical intent-cli surfaces only.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Confirm via intent-cli / GitHub (not chat) that the issue exists with the expected execution-unit body and the `intent-target` label.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Confirm the durable workflow state (queue-state / linkage / label) reflects the publish through intent-cli surfaces.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S5, "- Immediately after verification, in THIS SAME WAKE, delegate implementation over agmsg (G524) — do not stop after publishing and wait for a future wake to send the delegation. The implementation receiver still derives its target from `intent-cli worker next-action`, not the agmsg text.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S6, "G524: before sending ANY agmsg message, verify the recipient id is present in the team roster (agmsg `team.sh`). agmsg accepts an unknown recipient silently — there is no delivery error to notice. Treat a recipient id that is not on the roster as an error: fix the id or the roster registration before sending; never guess or approximate a role name.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S6, "- Field-observed loss: 8 dispatches addressed to `review` were silently lost when the registered role was `reviewer` — agmsg neither delivered nor reported the mismatch.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new(S7, "Setup does not stop at role registration. After the agmsg roles are registered and ready, the DESIGN thread starts (or resumes) orchestration by sending ONE message to the orchestrator; the orchestrator then drives the loop autonomously and returns to design only for human decisions.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S7, "First message — design → orchestrator (paste into the design thread):", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S7, "{\"to\":\"orchestrator\",\"type\":\"start\",\"domain\":\"__DOMAIN__\",\"target_repo\":\"__OWNER__/__REPO__\",\"requested_action\":\"<e.g. publish the next ready slice and drive it to a PR>\",\"constraints\":\"one action per wake; escalate to design ONLY for human decisions (product/clarification, release/credentials/security, destructive actions, unresolved blockers)\"}", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S7, "- **autonomous publish** — If `intent-cli` reports the next slice `issue-cut-ready` and all publish gates pass (see Next-slice publication), the orchestrator creates/publishes ONE GitHub issue ITSELF via canonical intent-cli commands (`issue publish-flow` / `automation issue-publish`) — it does NOT ask design to do each step. At most one issue per wake; verify after publishing before delegating implementation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S7, "- **escalation boundary** — Routine delegation (publish, delegate, CI wait, review, closeout) stays orchestrator↔receivers and does NOT go to design. Return to DESIGN only for human decisions — product/design clarification, release/credentials/security, destructive actions, or an unresolved blocker — using the structured escalation message (reason / current_state / evidence / decision_needed).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S7, "- **design inbox workflow** — The design thread is a loopless receiver and reads on demand. To pick up escalations, the human (or the design thread) checks the design inbox with `inbox.sh` — especially when monitor delivery did not appear live or the design session started after the orchestrator sent. Read, decide/reply, then the orchestrator continues.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S8, "The design thread acts as a TRAFFIC CONTROLLER, not an implementer. It coordinates through the orchestrator and only surfaces human-needed items — it does not drive implementation/review or mutate workflow state itself.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "1. Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S8, "2. Check intent-cli / GitHub READ-ONLY state (`intent status`, `worker next-action`, PR/issue/labels) to ground any decision — never trust an agmsg message as state.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "3. Send the orchestrator a state update or a nudge (start/resume); do not drive implementation/review yourself.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "4. Do NOT directly mutate implementation/review work, labels, or host metadata — that is the orchestrator/receivers' job through intent-cli.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "5. Summarize ONLY human-needed items to the human; keep routine progress internal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "6. PRIMARY DESIGN DUTY — intent-tree co-evolution: the intent tree moves WITH development, not after it. Leaving the tree unupdated while implementation advances is a serious fault in its own right, not a deferred chore: a tree that describes a design the code no longer has is worse than no tree, because every downstream packet, review, and audit is written against it. Reinforce the tree in the same wake that changes the surface it describes.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "7. Same-cadence write-back check: perform the packet's declared write-backs and RECORD them in the same closeout wake, with `intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --write`. Until it is recorded, the unit stays visible as a `knowledge-writeback-pending` item in `automation stalled-work` / `automation heartbeat` — closing the PR does not clear it, and nothing here writes intent content on design's behalf.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "1. Confirm the orchestrator is actually scheduled and on a fresh turn (its `/loop` or Codex automation is running).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "2. Confirm it received your last message (`inbox.sh` on the orchestrator) — a pre-monitor send may be queued, not delivered live; resend after an ack.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S8, "3. Confirm intent-cli actually reports an actionable item for THIS domain/repo (`worker next-action` / `intent status`) — idle may be correct (nothing to do).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "4. Only after these, escalate to the human as a structured decision.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S8, "> **Context-only:** The design thread MAY send context to a receiver thread, but MUST mark it context-only (e.g. `context-only: <text>`) unless the orchestrator delegated the action — receivers act only on orchestrator delegations, not on design context.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "Orchestrated work creates temporary worktrees for implementation and review. Allocate them under a managed, allowlisted root inside the workspace and clean them up with `git worktree remove` — NEVER a raw `rm -rf` of an arbitrary `/tmp/intent-review-...` path. Safe cleanup design, not disabling approvals, is the right default: a destructive `rm -rf` approval prompt is the symptom of an unmanaged workspace.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- **managed root** — Allocate temporary worktrees under a repo/workspace-scoped managed root — the `[project] worktree_root` (default `.intent-cli/worktrees/`), git-ignored — not arbitrary `/tmp/intent-review-...` paths. A managed root is allowlisted, predictable, and removable with `git worktree remove`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- **approval policy** — `approval_policy=never` / `danger-full-access` is NOT a substitute for safe cleanup design. Keep least-privilege approvals as the default; the goal is to never need a destructive `rm -rf` prompt, not to suppress the prompt.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- Create each worktree under the managed root: `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- Keep the managed root git-ignored so it never pollutes the tree.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- One worktree per role/unit; do not reuse a dirty worktree across units.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- Remove a worktree only with `git worktree remove` (it refuses a dirty worktree) — never raw `rm -rf`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- Validate the target path is INSIDE the allowlisted managed root before removal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- Confirm the path is a registered git worktree (it appears in `git worktree list`).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- Confirm the worktree state is clean (no uncommitted or untracked user work) before removing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- Prune stale registrations with `git worktree prune` after removal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S9, "- The target is OUTSIDE the allowlisted managed root.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S9, "- The target is the repo root, `$HOME`, or a system path (`/`, `/tmp` root, etc.).", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S9, "- The path is not a registered git worktree.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S9, "- The worktree has uncommitted or untracked user work — STOP and surface it; do not delete user work.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S10, "Review delegation must carry the managed-worktree policy and require design-alignment evidence up front — not leave the reviewer to discover it. Dogfooding showed a reviewer allocate a raw `/tmp/...review...` worktree and Codex correctly ask to approve a destructive `rm -rf` — the RIGHT safety behavior for the WRONG workflow. The fix is a managed root, NOT weakening approval settings.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S10, "- **managed worktree root** — Review worktrees use the SAME managed, workspace-local root as the rest of orchestrated work — the `[project] worktree_root` (default `.intent-cli/worktrees/`), e.g. `.intent-cli/worktrees/review-<unit>` — NEVER an arbitrary `/tmp/...review...` path.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S10, "- **prohibited pattern** — PROHIBITED as the normal path: a raw `/tmp/...` review worktree, and a `rm -rf /tmp/... && git worktree add ...` cleanup chain. Reaching for this pattern is the signal to STOP and allocate under the managed root instead — not to ask the operator to approve the `rm -rf`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S10, "- **cleanup rule** — Cleanup is `git worktree remove <managed-path>` for a REGISTERED, CLEAN worktree only — confirmed via `git worktree list` and a clean `git status` first.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S10, "- **unsafe/stale path rule** — A stale path that is NOT a registered git worktree, is OUTSIDE the managed root, or is dirty/unsafe is NEVER an operator `rm -rf` approval prompt — it is a STRUCTURED BLOCKER agmsg reply to the orchestrator (`status: blocked`) so the orchestrator can route the repair, not something the reviewer resolves by force-deleting an unmanaged path.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S10, "Review delegation example (orchestrator → review):", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S10, "{\"delegate\":{\"domain\":\"<domain>\",\"execution_unit\":\"<unit>\",\"target_repo\":\"<owner/repo>\",\"pr\":\"<n>\",\"review_cwd\":\"/review/<domain>\",\"managed_worktree_policy\":\"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never /tmp\",\"design_alignment_required\":true,\"destination_thread\":\"review@<domain>\"}}", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S10, "Design-alignment sources a review reply may cite as checked:", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S10, "- packet — the authored packet content and acceptance criteria.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S10, "- review-context — the review-context artifact for this PR/unit.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S10, "- intent tree — the relevant intent-tree entries for the touched domain.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S10, "- ADR / decision notes — any linked architecture or design-decision records.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S10, "- relevant docs — user-facing or developer docs the change touches.", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S11, "1. Confirm you are the ONLY orchestrator for this domain/repo; if a second is detected, STOP and escalate (fail closed).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S11, "1. Confirm domain scope: in single-domain mode, treat other-domain items visible in the host repo as OUT OF SCOPE (escalate, never delegate); in multi-domain mode, attach full routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree, base branch policy, destination thread) before each delegation. Visibility is not authorization, and an execution-unit prefix mismatch alone is not a wrong-repo signal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S11, "1. Read pending agmsg replies from the implementation/review threads (signals only — do not trust them as state).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S11, "1. Ask intent-cli for the real state: `intent-cli intent status --domain __DOMAIN__ --format json` and `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S11, "1. Verify every GitHub fact an agmsg reply claims (PR merged, CI concluded, labels) before acting on it.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S11, "1. The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER, not at-most-one-message overall (G524): a publish this wake must be delegated to implementation in this SAME wake — never defer that delegation to an unscheduled next wake — alongside any repair requests (one per stalled receiver) or one operator escalation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S11, "1. Before sending any agmsg message, verify the recipient id against the team roster (`agmsg team.sh`); treat an id not on the roster as an error, never a guess (G524).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S11, "1. Do not launch implement/review recurring timers for this domain/repo while orchestrating.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S11, "1. End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item before sleeping — never leave one for an unscheduled next wake; escalate explicitly if it is genuinely blocked on an operator decision.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S12, "- agmsg is a message/progress/completion signal layer only; intent-cli and GitHub are authoritative for all workflow state.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new(S12, "- No raw label mutation (`gh ... --add-label`/`--remove-label`); every label transition goes through intent-cli worker/automation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S12, "- No hand-editing queue-state, runs.jsonl, packets, or any host metadata (`.intent-cli/**`, `intents/**`).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S12, "- agmsg never replaces semantic review or authorizes a merge; review/closeout decisions run through intent-cli review surfaces (G480).", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new(S12, "- Per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message: a publish's same-wake delegation, repair messages, an escalation, and receiver-report handling may all happen in one wake (G524); never defer a publish's delegation to an unscheduled future wake.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S12, "- Verify the recipient id against the team roster (`agmsg team.sh`) before every send; an id not on the roster is an error, not a guess (G524).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S12, "- End every wake with a stalled-work check (`automation stalled-work`, G523) and process any actionable item before sleeping; escalate explicitly rather than deferring silently.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S12, "- Domain isolation: a host repo can hold several domains and one repo can serve several domains, so visibility is not authorization. Single-domain orchestrators ignore/escalate other-domain items; multi-domain orchestrators require explicit per-delegation routing. An execution-unit prefix mismatch alone is not a wrong-repo signal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S12, "- Fail closed on duplicate orchestrators for the same domain/repo, or when an agmsg reply conflicts with intent-cli/GitHub facts — STOP and escalate, never guess.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S12, "- Allocate temporary worktrees under an allowlisted managed root and remove them with `git worktree remove`; never raw `rm -rf` of arbitrary temp paths, and `approval_policy=never`/`danger-full-access` is not a substitute for safe cleanup.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S12, "- Never ask intent-cli to launch Claude/Codex/Copilot or any AI provider; intent-cli only emits text the human agent acts on.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- **status: `blocked`**", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "- blocked — existing implementation/review timer loops for this domain/repo would race the orchestrator (mixed-mode). Stop the existing loops (or re-run with --existing-loop-policy will-stop) before starting orchestrator mode; receivers are never scheduled — orchestrator wakes are message-driven by default, with an explicit fallback/legacy timer as the only case where the orchestrator itself is scheduled.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- **status: `setup-ready`**", SessionLayerSections.FragmentType.CanonDescriptive),
        new(S0, "- setup-ready — register the three roles with the agmsg commands, paste the first prompts, then run the first validation.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "agmsg join.sh __TEAM__ orchestrator __OAGENT__ __ORCHPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "agmsg delivery.sh set __DELIVERY__ __OAGENT__ __ORCHPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "agmsg join.sh __TEAM__ implementation __IAGENT__ __IMPLPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "agmsg delivery.sh set __DELIVERY__ __IAGENT__ __IMPLPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "agmsg join.sh __TEAM__ review __RAGENT__ __REVIEWPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "agmsg delivery.sh set __DELIVERY__ __RAGENT__ __REVIEWPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "You are the ORCHESTRATOR thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__OAGENT__`, running from `__ORCHPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__). Your steady state is MESSAGE-DRIVEN — implementation/review agmsg replies wake you; an orchestrator timer (Codex automation 5m or Claude `/loop 5m`) is an OPTIONAL fallback/legacy polling mode, not the default. You pace the implementation/review receivers over agmsg and never run their timers. See the full orchestrator prompt in the Thread prompts section.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "You are the IMPLEMENTATION thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__IAGENT__`, running from `__IMPLPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__). You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait. Your worker target comes from `intent-cli worker next-action`, not the agmsg text. See the full prompt in the Thread prompts section.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "You are the REVIEW thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__RAGENT__`, running from `__REVIEWPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__). You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait. Your worker target comes from `intent-cli worker next-action`, not the agmsg text. See the full prompt in the Thread prompts section.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new(S0, "- Preflight all three cwds BEFORE mutating: `__ORCHPATH__` (orchestrator), `__IMPLPATH__` (implementation), `__REVIEWPATH__` (review) — clean `git status`, expected git remote/repo, expected branch/base, and no existing timer-loop for this domain/repo (see Preflight).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- Existing-loop conflict check: confirm no implementation/review recurring timer is running for this domain/repo (implementation/review stay loopless whether the orchestrator runs message-driven or on an explicit fallback/legacy timer).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- First read-only wake: run ONE confirm-only orchestrator wake — read state, send nothing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new(S0, "- Receiver readiness: ping each receiver and require an ack BEFORE any real delegation — a registered+configured role is not ready until it acks (see the Receiver readiness section). A session launched before delivery was active may have missed earlier messages; resend or read with `inbox.sh`.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
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
        // Mode-specific and count-varying intake text, declared for every
        // rendering the command can produce.
        new("summary", "PRIMARY four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over the session layer this team runs — herdr-only here. The session layer carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout. Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation). The herdr-only operating steps ship in G571.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "setup-ready (herdr-only) — the registration, delivery-configuration and role-prompt steps of this intake are agmsg-only and do not apply. Their herdr-only counterparts ship in G571.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "missing-inputs — supply the 4 missing field(s) below to get a setup-ready plan.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("summary", "PRIMARY agmsg-backed four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / implementation / review coordinate over agmsg. agmsg carries natural-language delegation / progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, and closeout. Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread (see Mode separation).", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("setup_intake", "missing-inputs", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "missing-inputs — supply the 6 missing field(s) below to get a setup-ready plan.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("setup_intake", "orchestrator folder", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "implementation folder", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "review folder", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "agmsg team name", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "delivery mode", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "existing-loop stop policy", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__DOMAIN__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__OWNER__/__REPO__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "<orchestrator-path>", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "<implementation-path>", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "<review-path>", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__AGENT__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "<team>", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "<delivery-mode>", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "The implementation and review threads are loopless agmsg receivers — they must NOT run their own `/loop` or recurring timer for the same domain/repo, whether the orchestrator runs message-driven (the default, woken by agmsg replies) or on an explicit fallback/legacy timer (Codex 5m / Claude `/loop 5m`).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "Under authority the OPERATOR granted it, the design thread drives the team's SESSION LAYER through the workspace manager: it provisions the team (see `Terminal-workspace provisioning`), keeps the sessions alive and correctly held, and supervises for stalls. It answers a blocking dialog only inside an explicit boundary and only after READING that dialog from the pane; everything outside the boundary escalates to the operator. This adds a session-layer role — it moves NO workflow authority.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "Two layers, two owners. The SESSION layer (panes, processes, holds, blocking dialogs) is what the operator grants the design thread. The WORKFLOW layer (labels, queue-state, publication, delegation, closeout) is not granted and never moves — it stays with intent-cli, GitHub, and the orchestrator exactly as before.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "the design thread supervises the session layer because the operator asked it to, and the grant's scope is what the operator stated. Outside a grant the design thread observes and reports rather than acts. A grant to supervise sessions is never read as a grant to decide workflow, product, or security questions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "PROVISIONING — build the team's workspace, folders, panes, launches, and role initialization per `Terminal-workspace provisioning` (G549); supervision references that section rather than repeating it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "SESSION LIFECYCLE — investigate an unresponsive session and, when it must be replaced, do so through the graceful drop that honors one-holder exclusivity.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "STALL SUPERVISION — run the three supervision layers below so a stall is noticed by a layer that is actually running, not by luck.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "BLOCKING DIALOGS — answer only what the MAY list allows, only after the verified read; escalate everything else.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "workflow state ownership does not move. Labels, queue-state, publication, delegation, CI/review gating, and closeout remain with intent-cli, GitHub, and the orchestrator; the design↔orchestrator double-check rule and the orchestrator's ownership of workflow transitions apply exactly as before. Supervising a session never authorizes a workflow transition, and a stuck pane is never a reason to move a label by hand.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "A session that stops responding is a session-layer fault, and the design thread may repair it — but repair means restoring a correctly held, live session, not taking over the role's work or its decisions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "READ the pane first — an \"unresponsive\" session is most often blocked on a dialog, a trust screen, or a prompt waiting for input, not dead. Diagnose from what the pane actually shows.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "Distinguish the layers: a live session that is merely not attached to delivery is a delivery problem (re-check the readiness layers), not a reason to replace the session.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "Confirm the role is still held by that session before concluding anything — a role silently dropped elsewhere looks identical to a dead session from the outside.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "Prefer the least invasive repair that restores liveness: answer an in-boundary dialog, re-arm delivery, or restart the session — replacement is the last step, not the first.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "replacing a session never means two sessions holding the same role for even a moment. The successor claims only after the incumbent's hold is released; a refused actas is the exclusivity rule working, not an obstacle to route around.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "Replace through the GRACEFUL DROP: the incumbent drops the role (releasing its exclusivity lock and registration), then the successor claims it and re-runs readiness plus the ping test. Never kill a pane and assume the hold cleared, and never force a role away from a live session.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "The drop's confirmation is OPERATOR-VISIBLE: the handover surfaces to the operator rather than happening silently inside the design thread. The design thread may request and sequence the handover; the decision to retire a live session remains the operator's, and the confirmation is what records it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "real-time message monitor", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "Catch inbound agmsg traffic as it arrives — replies, blockers, and escalations that should wake the design thread immediately.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "continuous / real-time (a live attached inbox stream, not a poll).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "This layer is what the message-driven steady state assumes. It sees only what is SENT — it cannot notice a session that went quiet or a pane blocked on a dialog, which is why the other two layers exist.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "blocking-UI pane scan", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "Notice panes that are stuck with nothing to say. TWO EQUAL stuck states: a pane blocked on an approval, selection, or trust prompt, AND a pane showing a shell prompt where an agent should be (`agent-absent`, G556). Both produce no message at all — a blocked agent is waiting and a dead one cannot speak — so no message-driven layer can ever detect either.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "sub-minute class (e.g. every few tens of seconds) — a blocking dialog stalls a role for its entire lifetime, and an agent that died seconds after reporting stays dead until someone looks, so this layer is the fast one.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "Scanning is READING, and what the scan finds routes by STATE, not by one rule for everything. A blocking dialog goes to the dialog rules below — answer only what the MAY list covers after the verified read, and escalate the rest. An `agent-absent` shell prompt is NOT a dialog and must never be routed through dialog handling: it goes to the shim-safe relaunch recovery (recreating the app-server when that is what died), followed by the COMPLETE verified-liveness re-check — report, settle delay, all three checks. See `What the pane scan is looking for` for both recoveries.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "periodic state watchdog", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "Compare canonical intent-cli/GitHub state against expected progress and nudge the orchestrator when work has gone stale — the existing design-thread watchdog (`intent-cli automation heartbeat --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "tens-of-minutes class (e.g. every 30 minutes) — quiet enough to stay out of the way, frequent enough to bound a stall.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "This is the existing watchdog, not a second one: its safety rules apply verbatim (see the watchdog safety-rules reference below). One canonical nudge per wake, never a batch.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "blocking dialog", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "an approval, selection, or trust prompt waiting for input.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "handle it under the dialog rules — answer only what the MAY list covers after the verified read, escalate the rest.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "agent-absent", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "a SHELL PROMPT where an agent should be — the pane looks like an ordinary terminal, often with a resume hint left on screen. The agent exited; it may have reported startup successfully seconds earlier.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "RELAUNCH THROUGH THE SHIM: type the launch into the pane's interactive shell (never spawn the executable), recreating the app-server first when it is the thing that died. Set the permission mode with the LAUNCH FLAG (e.g. `--permission-mode`) rather than trying to switch it afterwards: a workspace manager's synthetic key injection cannot be relied on for mode switching — plain keys are delivered, but modifier chords such as shift+tab are not delivered faithfully (observed across multiple teams). Then run the FULL verified-liveness sequence again — report, settle delay, all three checks.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "supervision schedulers are session-scoped: a `/loop`, an automation, or an attached monitor dies with the design session that hosts it, and nothing announces that it stopped. Every supervision layer must either survive a design-session restart or be RE-ARMED as the first act of the new session — treat re-arming as part of starting the session, not as an optional follow-up. Field cost of forgetting: a claim-now lost inside a session-restart window left a published issue stalled for 5.5 HOURS because no supervision layer happened to be running.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "the design thread may answer a dialog ONLY after it has actually read that dialog's content from the pane and can state what it is approving. A blind keystroke into a dialog it has not rendered is prohibited, no matter how routine the prompt looks or how confident it is about which key clears it. If the content cannot be read or cannot be verified, the dialog is an escalation, not an answer.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "confirmations of work the design thread itself requested", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "the read pane's prompt must match an action THIS design thread just initiated — same target, same operation. A confirmation it cannot trace to its own request is not its to answer.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "command approvals verified read-only", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "the exact command shown in the pane must be read and verified to be READ-ONLY. Anything that writes, deletes, installs, publishes, or mutates state fails this check and escalates — \"probably read-only\" is not verified.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "trust screens for hooks the design thread itself installed", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "the trust screen must name a hook THIS design thread installed as part of this provisioning (its own hook-trust case). A trust screen for anything it did not install is not its to accept.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "operator-preauthorized mode changes", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "the operator must have PREAUTHORIZED this specific mode change, and the read pane must show that same change. Preauthorization is specific and prior — it is never inferred from a general grant to supervise sessions.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "unreadable or unverifiable dialogs", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "if the pane content cannot be read, or the claim it makes cannot be verified, there is nothing to base an answer on — answering would be guessing on the operator's behalf.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "destructive or irreversible approvals", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "deletions, force operations, overwrites, and anything else that cannot be undone are the operator's call — the cost of a wrong answer is unbounded and unrecoverable.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "choices that embed a product or design decision", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "a dialog that picks behavior, scope, or defaults is design content, and design content goes through the operator and the design↔orchestrator double-check — not through whoever happens to be unblocking a pane.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "credential, security, and permission waits", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_workspace_supervision", "these are NEVER answerable by the design thread, with or without prior authorization: they always remain unanswered and always escalate to the operator. No grant makes them answerable.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "UNSTICKING A SESSION IS NOT DECIDING FOR IT. The design thread's job is to keep the session layer alive so the role can do its own work — not to make the role's choices, and not to make the operator's.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_workspace_supervision", "Provisioning is NOT repeated here — see `Terminal-workspace provisioning` for role folders, workspace topology, shim-safe launch, actas/readiness, and the exclusivity/handover rules this section supervises.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_workspace_supervision", "The watchdog safety rules apply to ALL supervision verbatim: no duplicate delegation, no clearing a permission prompt, no cancelling or resetting in-flight work, no force-closing an issue/PR, and no speculative durable-state surgery (no hand-edited labels, queue-state, or host metadata). See `Design-thread watchdog (recommended safety net)`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "A hold blocked on a DESIGN DECISION must be visible and bounded. Visible: it is recorded as a clarification artifact through the canonical clarify surface, so `automation stalled-work` and `automation heartbeat` can see it — an agmsg message alone is invisible to every supervision layer. Bounded: the operator may pre-delegate enumerated, mechanically fact-checkable decision classes so a correction both threads can verify from repository facts does not wait on design at all. Measured cost of getting this wrong: a nine-hour hold on a one-line wording ruling while every technical check was green and `stalled-work` reported `stalled=false` throughout.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "When the orchestrator or the reviewer blocks on a design decision, it RECORDS A CLARIFICATION ARTIFACT through the canonical clarify surface, in addition to whatever agmsg message it sends. The artifact is what makes the hold detectable; the message is only a notification.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "domain — the blocked domain (`__DOMAIN__`), so the artifact is scoped to the right pipeline.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "blocking execution unit — the unit that cannot proceed until this is answered.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "question — what design must decide, stated so someone who was not in the thread can answer it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "recommended answer — when the asking thread already believes it knows the answer, state it and cite the facts that support it; design then confirms or overrides rather than starting from scratch.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "An agmsg-only hold is a CONTRACT VIOLATION, not a shortcut. A block that exists only as messages is invisible to `stalled-work`, to `heartbeat`, and therefore to every watchdog and every operator glance — which is exactly how a nine-hour hold passed unnoticed with the pipeline reporting healthy. If you are waiting on design, the artifact exists; if the artifact does not exist, you are not waiting, you are stalled.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "record the hold — `intent-cli clarify open` (the canonical clarify surface; never hand-write the artifact)", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "see what is open — `intent-cli clarify list`", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "answer it — `intent-cli clarify answer` (design, or the operator on escalation)", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "confirm it is visible — `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json` reports `design-decision-pending`", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "intent-cli clarify open <execution-unit> \\\n  --question \"<the actual design-blocking question, answerable by someone outside the thread>\" \\\n  --recommended-answer \"<what you believe the answer is, when you believe you know it>\" \\\n  --evidence \"<the repository facts that support the recommendation>\"", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "The reviewer's hold rule is refined so a green-technical review never becomes an untracked wait. Evaluate what is actually pending before holding.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "Technical checks are GREEN and the only pending item is NON-SEMANTIC and MECHANICALLY FACT-CHECKABLE from repository facts — resolve it under bounded default authority (below), log the resolution with the verifying facts, and proceed. Do not hold a green review on a question whose answer both threads can derive and cite.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "Anything else — a semantic or product question, a fact you cannot verify, or a class the operator has not delegated — becomes a recorded clarification and a VISIBLE pending state. The review is still held; the difference is that the hold is now on disk and detectable.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "there is no third option where the reviewer simply waits and says so in a message: either the item is resolved under granted authority with its evidence, or a clarification artifact exists. Silence with a message attached is the failure mode this rule exists to remove.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "BOUNDED DEFAULT AUTHORITY lets the operator pre-delegate a small, enumerated set of decision classes that can be settled by checking repository facts rather than by judgment. It exists so a count correction does not cost nine hours. It is bounded in every direction: granted, enumerated, evidence-logged, amendable, and never semantic.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "GRANTED, never assumed. The authority applies only to classes the OPERATOR has explicitly pre-delegated for this domain. Absent a grant, every design decision goes to design as before — the default is unchanged, and no thread may infer a delegation from the fact that an answer seems obvious.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "count and enumeration corrections", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "the count is derivable from repository facts both threads can read — e.g. a slice count derived from the merged PR list and the issue's own enumeration. Cite the list and the derivation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "wording corrections that follow from a cited fact", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "the corrected wording is entailed by a fact in the repository (a merged PR title, a label state, a retired unit's own record), and the reviewer and orchestrator AGREE on both the fact and the correction. Disagreement is not fact-checkable — it escalates.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "cross-reference and link corrections", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "the target exists (or does not) in the repository as cited — verifiable by reading the referenced file, heading, issue, or PR.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "identifier and metadata mismatches against a canonical source", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "the canonical source is named and read — e.g. a version in `eng/version.json`, a unit id in a packet, a label in the canonical palette. The canonical source wins; the resolution cites it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "MANDATORY EVIDENCE LOGGING. A resolution taken under this authority is recorded in the durable trail with the facts that verify it — what was decided, which repository facts entail it, and which threads agreed. An unlogged resolution is not a granted-authority resolution; it is an undocumented decision, and it is a violation of this contract.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "The sink is the CANONICAL `clarify record` surface: the entry lands under `## Recently Resolved` in the domain's clarification return path (`intents/<domain>/clarifications/open.md`), where `Question` identifies the pending item, `Decision` records the decided value, and `Rationale` records the verified repository facts plus the reviewer/orchestrator agreement. The entry is durable and stays readable there, which is exactly what makes design's post-hoc amendment possible — design reads the recorded evidence and amends or reverses from it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "# 1. write the decision artifact (## Question / ## Decision / ## Rationale)\ncat > /tmp/authority-decision.md <<'EOF'\n## Question\n<the pending item, identified so design can find it later>\n\n## Decision\n<the decided value>\n\n## Rationale\n<the verified repository facts that entail it, and which threads agreed>\nEOF\n\n# 2. record it in the durable trail (--dry-run first shows the intended update)\nintent-cli clarify record --domain <domain> --from-file /tmp/authority-decision.md", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "DESIGN MAY AMEND POST HOC. A granted-authority resolution is provisional in design's eyes: design can review the logged evidence afterwards and amend or reverse the decision. The authority buys latency, not finality — proceeding does not close the question against design.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "SEMANTIC AND PRODUCT DECISIONS ARE EXCLUDED, absolutely. Intent shaping, packet content and acceptance criteria, release scope, prioritization rulings, and anything requiring product or design judgment always go to design through the design↔orchestrator double-check rule, whose scope this contract does not touch. If settling the question requires deciding what SHOULD be true rather than checking what IS true, it is not fact-checkable and this authority does not reach it.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "While a clarification stays open, the design thread is reminded on a fixed cadence. A recorded hold that nobody re-surfaces is still a slow hold — the artifact makes it detectable, the reminder makes it noticed.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("design_decision_holds", "The ORCHESTRATOR sends the reminder from its long-interval automation — the same wake that already runs the heartbeat check. No new scheduler, and the receivers stay loopless.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "30–60 minute class — the same low-frequency band as the heartbeat and the design-thread watchdog. Faster polling recreates the churn the message-driven model removes; slower lets a hold sit past the point an operator would want to know.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "AT MOST ONE reminder per interval PER OPEN CLARIFICATION. Two open clarifications produce at most two reminders in a wake; one clarification never produces two reminders in the same interval no matter how many wakes fire. This is the same one-message discipline the watchdog already follows.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "STOP ON ANSWER. Once the clarification is answered (or applied, or cancelled) it is no longer open, `design-decision-pending` clears on its own, and the reminders stop. Never keep reminding against an answered clarification, and never re-open one to keep a thread's attention.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_decision_holds", "The design thread runs in the OPERATOR APP by preference, which is what makes a reminder land either way: an OPEN design session receives the reminder immediately through its monitor, and a CLOSED one finds it waiting in the inbox on resume. Neither case requires design to be resident in the team workspace — there is no workspace-residency requirement here.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("design_decision_holds", "Detection is `design-decision-pending` in `automation stalled-work`: it reads the domain's OPEN clarification artifacts and reports each with its age, blocking execution unit, and question summary, and `automation heartbeat` carries it in `message_body` like any other kind. Confirm a hold is visible with `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`; if the hold is real but the kind is absent, the clarification artifact was never recorded — which is the contract violation above, not a detector bug.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "Assume you are NOT alone on this machine. Several project teams run simultaneously, and every substrate below is shared across all of them — the workspace manager's server, the agmsg run directory, the codex app-servers, the host repo. `Terminal-workspace provisioning` and `Design-thread workspace supervision` describe how to build and keep ONE team; this section is what keeps that team from damaging another. It narrows the OBJECTS you may act on to your own team's; it does not widen or narrow what you may DO, so the supervision authority boundary applies unchanged. Operator incident (2026-07-29): with several teams live, one project's design thread damaged another project's resources and the operator had to intervene by hand.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "Before you touch anything, establish that it belongs to YOUR team. Attribution is a positive result from the keys below — not the absence of evidence that it belongs to someone else, and not a name that merely looks familiar.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "injecting keys or text into a pane", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "killing a process", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "closing or restructuring a workspace", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "removing or rewriting a state file", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "workspace label", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "the workspace is labelled with YOUR team/project name. A workspace you did not create and cannot name is not yours.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "pane cwd", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "the pane's working directory is one of YOUR team's dedicated role folders. A pane whose cwd you do not recognize belongs to someone.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "process cwd", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "the process's own working directory — read it per pid before any kill, exactly as the 2026-07-27 migration did when it spared another project's processes. A pid list filtered only by process NAME attributes nothing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "agmsg `(team, role)` file naming", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("cross_project_isolation", "agmsg run-directory state files are named per `(team, role)`; a file whose team segment is not yours is another team's bridge/watcher state, however broken it looks.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("cross_project_isolation", "if you cannot positively establish ownership, the object is READ-ONLY to you: you may look and you may report — you may not mutate. Escalate to the operator instead of guessing: a wrong guess here is another team's outage, and the cost is theirs rather than yours, which is exactly why the default has to be refusal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "one workspace per team, labelled with the team/project name. Never reuse, repurpose, or borrow another team's workspace or its panes — not even an idle-looking one. A workspace is the unit an operator reads to know whose work is whose; sharing one collapses that.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "one folder belongs to exactly ONE team. Never launch your agents in another team's folders. This is the same folder-scoping fact that forbids two roles sharing a folder within a team (G521) — agmsg identity and the codex bridge are folder-scoped, so an agent started in another team's folder takes over THEIR identity and delivery, not just its own.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("cross_project_isolation", "workspace-manager server (e.g. the herdr server)", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "one server process serving EVERY workspace on the machine", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "ownership is per WORKSPACE, never the server. Act on your own workspace and its panes; never restart, reconfigure, or kill the shared server — doing so takes down every other team's workspace at once.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "agmsg run directory (`~/.agents/skills/agmsg/run`)", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("cross_project_isolation", "one directory holding bridge / watcher / app-server state for ALL teams", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "ownership is per `(team, role)` FILE. Touch only files whose team segment is yours; never clear the directory wholesale to fix your own delivery — that is another team's bridge state you are deleting.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "codex app-servers", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "one app-server per FOLDER, and folders belong to teams", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "ownership follows the folder. Verify the process's cwd before stopping an app-server; a same-named process rooted in another team's folder is theirs.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "host repo", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "one repo holding EVERY domain's metadata", SessionLayerSections.FragmentType.CanonDescriptive),
        new("cross_project_isolation", "ownership is per DOMAIN path. Write only through the canonical commands for your own domain; queue-state is protected against concurrent writers by the no-item-loss invariant and stale-base re-application (G548), which is a safety net, not a licence to hand-edit another domain's state.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "When you find damage — including damage you caused — recovery is NON-DESTRUCTIVE. The instinct to tidy up is the failure mode: a broken artifact belonging to another team is still their evidence, and deleting it destroys their ability to diagnose what happened.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "PRESERVE and SET ASIDE another project's damaged artifacts — rename, move aside, or simply leave them in place and report. Never delete another team's workspace, panes, folders, processes' state, or files, however broken they look. Tell the operator and the affected team's thread what you found and what you set aside.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("cross_project_isolation", "REBUILD YOUR OWN fresh rather than repairing in place: create a new workspace, new panes, new role folders as needed, and re-run provisioning. Your own damaged artifacts may also be set aside rather than deleted when they carry evidence worth keeping.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("cross_project_isolation", "Recovery defaults to RECREATE, NOT CLEANUP.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "In orchestrator-message mode the normal steady state is MESSAGE-DRIVEN: implementation/review receivers already send accepted/progress/completed/blocked replies to the orchestrator, and those replies wake the orchestrator path — routine fast polling is NOT required. An orchestrator timer (Codex automation every 5m, or Claude same-thread `/loop 5m`) remains SUPPORTED but only as an explicit FALLBACK/LEGACY polling option for an operator who intentionally wants scheduled polling instead of message-driven wakes. Either way the implementation and review threads stay long-lived LOOPLESS receivers. The RECOMMENDED default safety net for message-driven steady state is a 30-minute-class design-thread watchdog (see Design-thread watchdog), not a fast orchestrator loop.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("scheduling", "orchestrator", SessionLayerSections.FragmentType.CanonDescriptive),
        new("scheduling", "Implementation and review threads are loopless receivers: do NOT start a recurring timer/loop in a receiver thread for this domain/repo. A receiver waits for an agmsg delegation, acts once, replies once, and waits again. Receivers are NEVER scheduled; when an explicit fallback/legacy timer is used (message-driven wakes are the default), the orchestrator is the only thread ever scheduled.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("scheduling", "OPTIONAL fallback/legacy polling — Codex automation (run every 5 minutes) for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__AGENT__`: on each run perform exactly ONE orchestrator wake — check design-side progress and agmsg replies, ask intent-cli for state (`intent status`, `worker next-action --github-only`, `automation host-review-preflight`), verify the GitHub facts (CI/approval/merge/closeout), then send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER (implementation, review), NOT at-most-one-message overall, so a publish plus its same-wake delegation, one repair per stalled receiver, and one operator escalation may all go out together — and exit. Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling. Do not run implementation/review loops; they are loopless receivers.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("scheduling", "OPTIONAL fallback/legacy polling — Claude same-thread setup for the ORCHESTRATOR thread, domain `__DOMAIN__` against `__OWNER__/__REPO__`: in the orchestrator thread run `/loop 5m` with the orchestrator prompt so the same thread re-wakes every 5 minutes. Each wake does exactly one orchestrator pass (read replies, check intent-cli / GitHub state, send this wake's messages under the G524 cap — AT MOST ONE DELEGATION PER RECEIVER, NOT at-most-one-message overall). Prefer the message-driven steady state (implementation/review agmsg replies already wake the orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy polling. Do NOT also launch `/loop` in the implementation or review threads — those are loopless receivers driven only by your delegations.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("scheduling", "A wake is triggered either by an incoming agmsg reply from implementation/review (the message-driven steady state) or by the optional fallback/legacy timer firing — either trigger runs exactly one orchestrator pass below.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("scheduling", "Check design-side progress: newly published packets/issues and intent status changes via `intent-cli intent status --domain __DOMAIN__ --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "Read pending agmsg replies from the implementation/review receivers (signals only — re-verify against intent-cli / GitHub).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("scheduling", "Ask intent-cli for worker state: `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "Check host review readiness: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge state, and closeout/label state.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "Classify each open PR's CI: pending = wait-and-recheck next wake (no message); green = delegate review/closeout; red = repair or escalate by ownership; stuck = escalate. Pending CI is normal progress, not a reason to message the operator.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "Detect stale blockers and no-reply receivers: a delegation with no accepted/progress reply within the expected window, or a thread stuck off the official workflow.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "On a no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check: send one non-destructive status-request, check read-only intent-cli/GitHub facts, keep watching if there is progress, treat waiting-permission as an operator notice (never auto-clear), and only after repeated no-reply with no progress send one idempotent re-entry or escalate.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "If intent-cli reports an `issue-cut-ready` candidate and all gates pass (same-domain or routed, complete contract, no open clarification, dependencies satisfied, under WIP, clean host-sync/preflight), publish ONE issue this wake via canonical publish-flow / issue-publish, verify it, THEN delegate that same issue to implementation in THIS SAME WAKE (G524) — do not ask the operator to create it, and do not stop after publishing to wait for a future wake to send the delegation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "If the candidate has unmet dependencies, plan the chain instead of pausing: act on the EARLIEST unmet resolvable dependency (publish or route it), keep the dependent held, and escalate only ambiguous/cycle/cross-domain-unrouted cases.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message overall (G524): this wake's actions may include a publish plus its same-wake delegation, one repair message per stalled receiver, one operator escalation, and handling any pending receiver reports, all together.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "Before sending any agmsg message this wake, verify the recipient id against the team roster (`agmsg team.sh`) — treat an id not on the roster as an error, never a guess (G524).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("scheduling", "Apply the design-thread escalation filter: keep routine progress / CI-wait / success / closeout / idle internal; surface to the design thread ONLY human-needed decisions, with structured evidence and the exact decision needed. Never hide a failure that needs a human.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item it reports before sleeping — never leave one for an unscheduled next wake; escalate explicitly if it is genuinely blocked on an operator decision. This includes a `backlog-ready-idle` item (G544, empty WIP + a ready packet + no activity past the idle threshold) — publish and delegate it in THIS wake, the same as any other issue-cut-ready candidate; only announce a following wake will handle it when that wake is actually scheduled.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "REPAIR routine off-rail states yourself by messaging the appropriate thread back onto the official intent-cli workflow — e.g. a receiver that stalled, skipped `worker complete`, applied a label by hand, or has not replied. Routine recovery is a repair message, not an escalation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("scheduling", "ESCALATE to the operator ONLY for: product/design judgment, credentials or security, a destructive local action, or an unresolved canonical ambiguity (intent-cli/GitHub facts genuinely conflict or are missing). Do not escalate states you can repair by message.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("dispatch_verification", "G524: before sending ANY agmsg message, verify the recipient id is present in the team roster (agmsg `team.sh`). agmsg accepts an unknown recipient silently — there is no delivery error to notice. Treat a recipient id that is not on the roster as an error: fix the id or the roster registration before sending; never guess or approximate a role name.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("dispatch_verification", "Field-observed loss: 8 dispatches addressed to `review` were silently lost when the registered role was `reviewer` — agmsg neither delivered nor reported the mismatch.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("design_handoff", "Setup does not stop at role registration. After the agmsg roles are registered and ready, the DESIGN thread starts (or resumes) orchestration by sending ONE message to the orchestrator; the orchestrator then drives the loop autonomously and returns to design only for human decisions.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_handoff", "{\"to\":\"orchestrator\",\"type\":\"start\",\"domain\":\"__DOMAIN__\",\"target_repo\":\"__OWNER__/__REPO__\",\"requested_action\":\"<e.g. publish the next ready slice and drive it to a PR>\",\"constraints\":\"one action per wake; escalate to design ONLY for human decisions (product/clarification, release/credentials/security, destructive actions, unresolved blockers)\"}", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_handoff", "If `intent-cli` reports the next slice `issue-cut-ready` and all publish gates pass (see Next-slice publication), the orchestrator creates/publishes ONE GitHub issue ITSELF via canonical intent-cli commands (`issue publish-flow` / `automation issue-publish`) — it does NOT ask design to do each step. At most one issue per wake; verify after publishing before delegating implementation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_handoff", "Routine delegation (publish, delegate, CI wait, review, closeout) stays orchestrator↔receivers and does NOT go to design. Return to DESIGN only for human decisions — product/design clarification, release/credentials/security, destructive actions, or an unresolved blocker — using the structured escalation message (reason / current_state / evidence / decision_needed).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_handoff", "The design thread is a loopless receiver and reads on demand. To pick up escalations, the human (or the design thread) checks the design inbox with `inbox.sh` — especially when monitor delivery did not appear live or the design session started after the orchestrator sent. Read, decide/reply, then the orchestrator continues.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_traffic_controller", "The design thread acts as a TRAFFIC CONTROLLER, not an implementer. It coordinates through the orchestrator and only surfaces human-needed items — it does not drive implementation/review or mutate workflow state itself.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_traffic_controller", "Check intent-cli / GitHub READ-ONLY state (`intent status`, `worker next-action`, PR/issue/labels) to ground any decision — never trust an agmsg message as state.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Send the orchestrator a state update or a nudge (start/resume); do not drive implementation/review yourself.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Do NOT directly mutate implementation/review work, labels, or host metadata — that is the orchestrator/receivers' job through intent-cli.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Summarize ONLY human-needed items to the human; keep routine progress internal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "PRIMARY DESIGN DUTY — intent-tree co-evolution: the intent tree moves WITH development, not after it. Leaving the tree unupdated while implementation advances is a serious fault in its own right, not a deferred chore: a tree that describes a design the code no longer has is worse than no tree, because every downstream packet, review, and audit is written against it. Reinforce the tree in the same wake that changes the surface it describes.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Same-cadence write-back check: perform the packet's declared write-backs and RECORD them in the same closeout wake, with `intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --write`. Until it is recorded, the unit stays visible as a `knowledge-writeback-pending` item in `automation stalled-work` / `automation heartbeat` — closing the PR does not clear it, and nothing here writes intent content on design's behalf.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Confirm the orchestrator is actually scheduled and on a fresh turn (its `/loop` or Codex automation is running).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Confirm it received your last message (`inbox.sh` on the orchestrator) — a pre-monitor send may be queued, not delivered live; resend after an ack.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("design_traffic_controller", "Confirm intent-cli actually reports an actionable item for THIS domain/repo (`worker next-action` / `intent status`) — idle may be correct (nothing to do).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "Only after these, escalate to the human as a structured decision.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("design_traffic_controller", "The design thread MAY send context to a receiver thread, but MUST mark it context-only (e.g. `context-only: <text>`) unless the orchestrator delegated the action — receivers act only on orchestrator delegations, not on design context.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Orchestrated work creates temporary worktrees for implementation and review. Allocate them under a managed, allowlisted root inside the workspace and clean them up with `git worktree remove` — NEVER a raw `rm -rf` of an arbitrary `/tmp/intent-review-...` path. Safe cleanup design, not disabling approvals, is the right default: a destructive `rm -rf` approval prompt is the symptom of an unmanaged workspace.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Allocate temporary worktrees under a repo/workspace-scoped managed root — the `[project] worktree_root` (default `.intent-cli/worktrees/`), git-ignored — not arbitrary `/tmp/intent-review-...` paths. A managed root is allowlisted, predictable, and removable with `git worktree remove`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Create each worktree under the managed root: `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Keep the managed root git-ignored so it never pollutes the tree.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "One worktree per role/unit; do not reuse a dirty worktree across units.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Remove a worktree only with `git worktree remove` (it refuses a dirty worktree) — never raw `rm -rf`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Validate the target path is INSIDE the allowlisted managed root before removal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Confirm the path is a registered git worktree (it appears in `git worktree list`).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Confirm the worktree state is clean (no uncommitted or untracked user work) before removing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "Prune stale registrations with `git worktree prune` after removal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "The target is OUTSIDE the allowlisted managed root.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("worktree_management", "The target is the repo root, `$HOME`, or a system path (`/`, `/tmp` root, etc.).", SessionLayerSections.FragmentType.CanonDescriptive),
        new("worktree_management", "The path is not a registered git worktree.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("worktree_management", "The worktree has uncommitted or untracked user work — STOP and surface it; do not delete user work.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("worktree_management", "`approval_policy=never` / `danger-full-access` is NOT a substitute for safe cleanup design. Keep least-privilege approvals as the default; the goal is to never need a destructive `rm -rf` prompt, not to suppress the prompt.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("review_delegation_contract", "Review delegation must carry the managed-worktree policy and require design-alignment evidence up front — not leave the reviewer to discover it. Dogfooding showed a reviewer allocate a raw `/tmp/...review...` worktree and Codex correctly ask to approve a destructive `rm -rf` — the RIGHT safety behavior for the WRONG workflow. The fix is a managed root, NOT weakening approval settings.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("review_delegation_contract", "Review worktrees use the SAME managed, workspace-local root as the rest of orchestrated work — the `[project] worktree_root` (default `.intent-cli/worktrees/`), e.g. `.intent-cli/worktrees/review-<unit>` — NEVER an arbitrary `/tmp/...review...` path.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("review_delegation_contract", "PROHIBITED as the normal path: a raw `/tmp/...` review worktree, and a `rm -rf /tmp/... && git worktree add ...` cleanup chain. Reaching for this pattern is the signal to STOP and allocate under the managed root instead — not to ask the operator to approve the `rm -rf`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("review_delegation_contract", "Cleanup is `git worktree remove <managed-path>` for a REGISTERED, CLEAN worktree only — confirmed via `git worktree list` and a clean `git status` first.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("review_delegation_contract", "A stale path that is NOT a registered git worktree, is OUTSIDE the managed root, or is dirty/unsafe is NEVER an operator `rm -rf` approval prompt — it is a STRUCTURED BLOCKER agmsg reply to the orchestrator (`status: blocked`) so the orchestrator can route the repair, not something the reviewer resolves by force-deleting an unmanaged path.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("review_delegation_contract", "{\"delegate\":{\"domain\":\"<domain>\",\"execution_unit\":\"<unit>\",\"target_repo\":\"<owner/repo>\",\"pr\":\"<n>\",\"review_cwd\":\"/review/<domain>\",\"managed_worktree_policy\":\"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never /tmp\",\"design_alignment_required\":true,\"destination_thread\":\"review@<domain>\"}}", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("review_delegation_contract", "packet — the authored packet content and acceptance criteria.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("review_delegation_contract", "review-context — the review-context artifact for this PR/unit.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("review_delegation_contract", "intent tree — the relevant intent-tree entries for the touched domain.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("review_delegation_contract", "ADR / decision notes — any linked architecture or design-decision records.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("review_delegation_contract", "relevant docs — user-facing or developer docs the change touches.", SessionLayerSections.FragmentType.CanonDescriptive),
        new("orchestrator_first_wake", "Confirm you are the ONLY orchestrator for this domain/repo; if a second is detected, STOP and escalate (fail closed).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("orchestrator_first_wake", "Confirm domain scope: in single-domain mode, treat other-domain items visible in the host repo as OUT OF SCOPE (escalate, never delegate); in multi-domain mode, attach full routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree, base branch policy, destination thread) before each delegation. Visibility is not authorization, and an execution-unit prefix mismatch alone is not a wrong-repo signal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("orchestrator_first_wake", "Read pending agmsg replies from the implementation/review threads (signals only — do not trust them as state).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("orchestrator_first_wake", "Ask intent-cli for the real state: `intent-cli intent status --domain __DOMAIN__ --format json` and `intent-cli worker next-action --repo __OWNER__/__REPO__ --github-only --format json`.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("orchestrator_first_wake", "Verify every GitHub fact an agmsg reply claims (PR merged, CI concluded, labels) before acting on it.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("orchestrator_first_wake", "The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER, not at-most-one-message overall (G524): a publish this wake must be delegated to implementation in this SAME wake — never defer that delegation to an unscheduled next wake — alongside any repair requests (one per stalled receiver) or one operator escalation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("orchestrator_first_wake", "Before sending any agmsg message, verify the recipient id against the team roster (`agmsg team.sh`); treat an id not on the roster as an error, never a guess (G524).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("orchestrator_first_wake", "Do not launch implement/review recurring timers for this domain/repo while orchestrating.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("orchestrator_first_wake", "End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain __DOMAIN__ --repo __OWNER__/__REPO__ --format json`, and process every actionable item before sleeping — never leave one for an unscheduled next wake; escalate explicitly if it is genuinely blocked on an operator decision.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("safety_boundaries", "agmsg is a message/progress/completion signal layer only; intent-cli and GitHub are authoritative for all workflow state.", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("safety_boundaries", "No raw label mutation (`gh ... --add-label`/`--remove-label`); every label transition goes through intent-cli worker/automation.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("safety_boundaries", "No hand-editing queue-state, runs.jsonl, packets, or any host metadata (`.intent-cli/**`, `intents/**`).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("safety_boundaries", "agmsg never replaces semantic review or authorizes a merge; review/closeout decisions run through intent-cli review surfaces (G480).", SessionLayerSections.FragmentType.CanonDescriptive), // hand-typed
        new("safety_boundaries", "Per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message: a publish's same-wake delegation, repair messages, an escalation, and receiver-report handling may all happen in one wake (G524); never defer a publish's delegation to an unscheduled future wake.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("safety_boundaries", "Verify the recipient id against the team roster (`agmsg team.sh`) before every send; an id not on the roster is an error, not a guess (G524).", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("safety_boundaries", "End every wake with a stalled-work check (`automation stalled-work`, G523) and process any actionable item before sleeping; escalate explicitly rather than deferring silently.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("safety_boundaries", "Domain isolation: a host repo can hold several domains and one repo can serve several domains, so visibility is not authorization. Single-domain orchestrators ignore/escalate other-domain items; multi-domain orchestrators require explicit per-delegation routing. An execution-unit prefix mismatch alone is not a wrong-repo signal.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("safety_boundaries", "Fail closed on duplicate orchestrators for the same domain/repo, or when an agmsg reply conflicts with intent-cli/GitHub facts — STOP and escalate, never guess.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("safety_boundaries", "Allocate temporary worktrees under an allowlisted managed root and remove them with `git worktree remove`; never raw `rm -rf` of arbitrary temp paths, and `approval_policy=never`/`danger-full-access` is not a substitute for safe cleanup.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("safety_boundaries", "Never ask intent-cli to launch Claude/Codex/Copilot or any AI provider; intent-cli only emits text the human agent acts on.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Routine next-slice issue publication is an ORCHESTRATOR responsibility, not an operator question. When intent-cli reports a candidate as `issue-cut-ready` and ALL safety gates pass, the orchestrator publishes it itself through canonical intent-cli commands instead of stopping to ask the operator to create the GitHub issue. Publish AT MOST ONE issue per wake, then verify, THEN delegate that same issue to the implementation thread in THE SAME WAKE (G524) — publish and delegate complete together; never defer the delegation to an unscheduled \"next wake\", since no other trigger will ever wake the orchestrator to send it (this was the single largest measured stall class in message-driven orchestration, ~60 hours across G807/G809/G810/G812).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Same-domain context (`__DOMAIN__`), or an explicitly routed multi-domain delegation (domain, target repo, destination thread) — never publish a cross-domain candidate without explicit routing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "The packet contract is complete: no missing required sections (goal, in/out of scope, acceptance criteria, base-branch policy).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "No open clarification or contract ambiguity on the candidate.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Dependencies are satisfied — every dependency execution unit is completed or already cut; never publish ahead of an uncut dependency.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Under the WIP cap — no in-progress blocker that should pace the queue first.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Clean host-sync / preflight: `intent-cli automation host-review-preflight --repo __OWNER__/__REPO__ --format json` and the publish preflight report no blocker, and the target repo/domain is unambiguous.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Missing contract sections — hold, do not publish.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Open clarification / ambiguous contract — hold or escalate one operator decision.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Dependency mismatch — an uncut or incomplete dependency; hold (publishing ahead would violate the dependency contract).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "WIP cap reached — let the in-progress work drain first.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Host-sync blocker or failed preflight — fix the sync via intent-cli, do not force the publish.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Ambiguous target repo or domain (no explicit routing in multi-domain) — escalate rather than guess.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "intent-cli issue publish-flow <execution-unit> --repo __OWNER__/__REPO__ --write --format json", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "intent-cli automation issue-publish --write --format json", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Never raw `gh issue create` or `gh ... --add-label`; publication and the `intent-target` label go through the canonical intent-cli surfaces only.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Confirm via intent-cli / GitHub (not chat) that the issue exists with the expected execution-unit body and the `intent-target` label.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Confirm the durable workflow state (queue-state / linkage / label) reflects the publish through intent-cli surfaces.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("next_slice_publication", "Immediately after verification, in THIS SAME WAKE, delegate implementation over agmsg (G524) — do not stop after publishing and wait for a future wake to send the delegation. The implementation receiver still derives its target from `intent-cli worker next-action`, not the agmsg text.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "blocked", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "blocked — existing implementation/review timer loops for this domain/repo would race the orchestrator (mixed-mode). Stop the existing loops (or re-run with --existing-loop-policy will-stop) before starting orchestrator mode; receivers are never scheduled — orchestrator wakes are message-driven by default, with an explicit fallback/legacy timer as the only case where the orchestrator itself is scheduled.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("setup_intake", "__ORCHPATH__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__IMPLPATH__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__REVIEWPATH__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__OAGENT__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__IAGENT__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__RAGENT__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__TEAM__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "__DELIVERY__", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "keep", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "setup-ready", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "setup-ready — register the three roles with the agmsg commands, paste the first prompts, then run the first validation.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "none", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "agmsg join.sh __TEAM__ orchestrator __OAGENT__ __ORCHPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "agmsg delivery.sh set __DELIVERY__ __OAGENT__ __ORCHPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "agmsg join.sh __TEAM__ implementation __IAGENT__ __IMPLPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "agmsg delivery.sh set __DELIVERY__ __IAGENT__ __IMPLPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "agmsg join.sh __TEAM__ review __RAGENT__ __REVIEWPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "agmsg delivery.sh set __DELIVERY__ __RAGENT__ __REVIEWPATH__", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "orchestrator", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "First prompt — paste into the scheduled orchestrator thread.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "You are the ORCHESTRATOR thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__OAGENT__`, running from `__ORCHPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__). Your steady state is MESSAGE-DRIVEN — implementation/review agmsg replies wake you; an orchestrator timer (Codex automation 5m or Claude `/loop 5m`) is an OPTIONAL fallback/legacy polling mode, not the default. You pace the implementation/review receivers over agmsg and never run their timers. See the full orchestrator prompt in the Thread prompts section.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "implementation", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "First prompt — paste into the loopless implementation receiver.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "You are the IMPLEMENTATION thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__IAGENT__`, running from `__IMPLPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__). You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait. Your worker target comes from `intent-cli worker next-action`, not the agmsg text. See the full prompt in the Thread prompts section.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "review", SessionLayerSections.FragmentType.CanonDescriptive),
        new("setup_intake", "First prompt — paste into the loopless review receiver.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "You are the REVIEW thread for domain `__DOMAIN__` against `__OWNER__/__REPO__` using `__RAGENT__`, running from `__REVIEWPATH__` as part of agmsg team `__TEAM__` (delivery: __DELIVERY__). You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator delegation, act once, reply once, then wait. Your worker target comes from `intent-cli worker next-action`, not the agmsg text. See the full prompt in the Thread prompts section.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "Preflight all three cwds BEFORE mutating: `__ORCHPATH__` (orchestrator), `__IMPLPATH__` (implementation), `__REVIEWPATH__` (review) — clean `git status`, expected git remote/repo, expected branch/base, and no existing timer-loop for this domain/repo (see Preflight).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("setup_intake", "Existing-loop conflict check: confirm no implementation/review recurring timer is running for this domain/repo (implementation/review stay loopless whether the orchestrator runs message-driven or on an explicit fallback/legacy timer).", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("setup_intake", "First read-only wake: run ONE confirm-only orchestrator wake — read state, send nothing.", SessionLayerSections.FragmentType.ModeIndependentOperative), // hand-typed
        new("setup_intake", "Receiver readiness: ping each receiver and require an ack BEFORE any real delegation — a registered+configured role is not ready until it acks (see the Receiver readiness section). A session launched before delivery was active may have missed earlier messages; resend or read with `inbox.sh`.", SessionLayerSections.FragmentType.TransportOperative), // hand-typed
        new("setup_intake", "will-stop", SessionLayerSections.FragmentType.CanonDescriptive),
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
