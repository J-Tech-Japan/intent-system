namespace IntentSystem.Cli.Commands;

/// <summary>
/// G570 rereview repair: which session layer each orchestrator-thread section
/// applies to, declared once.
///
/// The first two attempts routed by replacing STRINGS that carried agmsg
/// mechanics. Design ruled that out, and the reason is worth keeping next to
/// the fix: a substring rule is simultaneously too weak and too strong. Too
/// weak, because operative prose carries no mechanic token — "wait for an agmsg
/// delegation" instructs the reader to use a transport this team is not running
/// while matching no script name. Too strong, because a mode-independent field
/// gets destroyed the moment it happens to mention one — an earlier draft
/// deleted the timer-loop separation canon exactly that way.
///
/// Applicability is therefore a property of the SECTION, declared here, and the
/// renderer selects or replaces whole sections. A section is either about the
/// transport or it is not; that is a semantic question about content, and it is
/// answered once, in one table, where it can be reviewed.
/// </summary>
internal static class SessionLayerSections
{
    /// <summary>G570 third repair: the four applicability values, named.</summary>
    internal enum Applicability
    {
        /// <summary>Exists only to operate agmsg; replaced whole under herdr-only.</summary>
        AgmsgOnly,

        /// <summary>Applies unchanged in both modes.</summary>
        ModeIndependent,

        /// <summary>
        /// Canon that binds in both modes but is expressed through an agmsg
        /// mechanic: kept, with mechanic-bearing sentences pointed away.
        /// </summary>
        ModeIndependentWithTransportMechanics,

        /// <summary>
        /// G570 fourth repair: the ruling's fourth value, previously missing.
        /// Content that exists only under herdr-only — today the synthetic
        /// switch-checklist section and its JSON replacement metadata, which
        /// have no agmsg counterpart and are produced by the routing itself.
        /// </summary>
        HerdrOnly,
    }

    /// <summary>
    /// One declaration per section, carrying BOTH renderings' identity.
    ///
    /// G570 third repair: markdown and JSON classifications used to live in
    /// separate lists, and they drifted — end-of-wake was mixed in JSON but
    /// mode-independent in markdown, and codex_bridge_guidance was dropped in
    /// JSON with no markdown counterpart. A reader and a field consumer
    /// disagreeing about what applies is the same defect as leaking an
    /// instruction, so the two renderings are now derived from ONE row and
    /// cannot diverge.
    /// </summary>
    /// <summary>
    /// G570 fifth repair: <paramref name="Descriptive"/> is a property of the
    /// ROW, not a parallel list. A descriptive section's agmsg content is
    /// mechanism/history — a shared substrate identity such as "agmsg run
    /// directory" — so it stays BYTE-IDENTICAL inside its explicit example
    /// label; only non-descriptive mixed sections have operative sentences
    /// pointed away. Keeping the two facts on one row is what stops them
    /// disagreeing, which is how the isolation table's descriptive identity got
    /// over-stripped.
    /// </summary>
    internal sealed record SectionDeclaration(
        string Heading,
        string? JsonProperty,
        Applicability Applies,
        bool Descriptive = false);

    public static readonly IReadOnlyList<SectionDeclaration> Declarations =
    [
        // agmsg-only — replaced whole.
        new("## Terminal-workspace provisioning (G549)", "terminal_workspace_provisioning", Applicability.AgmsgOnly),
        new("## Setup (starting orchestrator mode)", "setup", Applicability.AgmsgOnly),
        new("## Setup intake form", "intake_form", Applicability.AgmsgOnly),
        new("## Preflight (all three cwds)", "preflight", Applicability.AgmsgOnly),
        new("## Receiver readiness", "receiver_readiness", Applicability.AgmsgOnly),
        new("## Troubleshooting", "troubleshooting", Applicability.AgmsgOnly),
        new("## Monitor recovery", "monitor_recovery", Applicability.AgmsgOnly),
        new("## Monitor tool vs delivery-mode (G511)", "monitor_tool_distinction", Applicability.AgmsgOnly),
        new("## Codex monitor (beta) failure modes (G521)", "codex_bridge_guidance", Applicability.AgmsgOnly),
        new("## Design / human receiver (optional)", "design_receiver", Applicability.AgmsgOnly),
        new("## Design-thread watchdog (recommended safety net)", "design_watchdog", Applicability.AgmsgOnly),
        new("## Orchestrator-side long-interval automation (alternative safety net)", "orchestrator_automation_alternative", Applicability.AgmsgOnly),
        new("## Thread prompts", "threads", Applicability.AgmsgOnly),
        new("## agmsg reply contract", "agmsg_reply_contract", Applicability.AgmsgOnly),

        // mode-independent with transport mechanics — kept, sentences pointed away.
        new("## Setup intake", "setup_intake", Applicability.ModeIndependentWithTransportMechanics),
        new("## Design-thread workspace supervision (G550)", "design_workspace_supervision", Applicability.ModeIndependentWithTransportMechanics, Descriptive: true),
        new("## Cross-project isolation on a shared machine (G555)", "cross_project_isolation", Applicability.ModeIndependentWithTransportMechanics, Descriptive: true),
        new("## Design-decision holds and bounded authority (G552)", "design_decision_holds", Applicability.ModeIndependentWithTransportMechanics, Descriptive: true),
        new("## Scheduled orchestrator cadence", "scheduling", Applicability.ModeIndependentWithTransportMechanics),
        new("## Dispatch verification (G524)", "dispatch_verification", Applicability.ModeIndependentWithTransportMechanics, Descriptive: true),
        new("## End-of-wake check (G523/G524)", "end_of_wake_check", Applicability.ModeIndependent),
        new("## Stale-thread health check", "stale_thread_health_check", Applicability.ModeIndependent),
        new("## Design-thread escalation filter", "design_thread_escalation", Applicability.ModeIndependent),
        new("## Design handoff (start / resume)", "design_handoff", Applicability.ModeIndependentWithTransportMechanics),
        new("## Design traffic-controller playbook", "design_traffic_controller", Applicability.ModeIndependentWithTransportMechanics),
        new("## Managed worktree cleanup", "worktree_management", Applicability.ModeIndependentWithTransportMechanics),
        new("## Review delegation — managed worktrees and design alignment", "review_delegation_contract", Applicability.ModeIndependentWithTransportMechanics),
        new("## Orchestrator first wake", "orchestrator_first_wake", Applicability.ModeIndependentWithTransportMechanics),
        new("## Safety boundaries", "safety_boundaries", Applicability.ModeIndependentWithTransportMechanics, Descriptive: true),
        new("## Next-slice publication", "next_slice_publication", Applicability.ModeIndependentWithTransportMechanics),

        // G570 fourth repair: surfaces the renderers produce OUTSIDE the
        // section builder were undeclared, so "one row drives every surface"
        // was not true of them. The document title and summary are declared
        // here, as is the synthetic herdr-only metadata.
        // G570 sixth repair: a title is a per-mode RENDERING of one identity,
        // and the old single row matched neither actual title — invisible while
        // the surface guard enumerated only `##`.
        new("# Guide — agmsg-backed orchestrator thread (G487)", null, Applicability.ModeIndependentWithTransportMechanics, Descriptive: true),
        new("# Guide — orchestrator thread (G487), herdr-only session layer", null, Applicability.HerdrOnly),
        new("(json) guide summary", "summary", Applicability.ModeIndependentWithTransportMechanics, Descriptive: true),
        new(ReplacementHeadingValue, "herdr_only_replaced_sections", Applicability.HerdrOnly),
        new("(json) herdr-only replacement note", "herdr_only_replacement_note", Applicability.HerdrOnly),
        new("(json) herdr-only descriptive context", "herdr_only_descriptive_agmsg_context", Applicability.HerdrOnly),

        // mode-independent — unchanged in both.
        new("## Session layer", "session_layer", Applicability.ModeIndependent),
        new("## Mode separation", "mode_separation", Applicability.ModeIndependent, Descriptive: true),
        new("## Role boundary (design authors; orchestrator coordinates)", "role_boundary", Applicability.ModeIndependent),
        new("## Domain routing — single-domain vs multi-domain", "domain_routing", Applicability.ModeIndependent),
        new("## CI wait state", "ci_wait_state", Applicability.ModeIndependent),
        new("## Draft PR reviewability", "draft_pr_reviewability", Applicability.ModeIndependent),
        new("## Dependency planning", "dependency_planning", Applicability.ModeIndependent),
        new("## Detailed guide commands", "detailed_guide_commands", Applicability.ModeIndependent),
    ];

    public static readonly IReadOnlyList<string> AgmsgOnlyHeadings =
        Declarations.Where(d => d.Applies == Applicability.AgmsgOnly).Select(d => d.Heading).ToArray();

    public static readonly IReadOnlyList<string> AgmsgOnlyJsonProperties =
        Declarations.Where(d => d.Applies == Applicability.AgmsgOnly && d.JsonProperty is not null)
            .Select(d => d.JsonProperty!).ToArray();

    /// <summary>
    /// G570 sixth repair: EVERY mixed section is fragment-typed, including the
    /// labelled ones. `Descriptive` no longer excludes a section from routing —
    /// it only marks that the section carries the explicit agmsg-example label,
    /// because it holds mechanism/history worth labelling. Excluding whole
    /// sections is what let imperative steps survive behind a label.
    /// </summary>
    public static readonly IReadOnlyList<string> MixedHeadings =
        Declarations.Where(d => d.Applies == Applicability.ModeIndependentWithTransportMechanics)
            .Select(d => d.Heading).ToArray();

    public static readonly IReadOnlyList<string> MixedJsonProperties =
        Declarations.Where(d => d.Applies == Applicability.ModeIndependentWithTransportMechanics
                && d.JsonProperty is not null)
            .Select(d => d.JsonProperty!).ToArray();

    public static readonly IReadOnlyList<string> ModeIndependentHeadings =
        Declarations.Where(d => d.Applies != Applicability.AgmsgOnly).Select(d => d.Heading).ToArray();

    /// <summary>
    /// Transport mechanics, used ONLY to find mechanic-bearing sentences INSIDE
    /// a declared mixed section. This is deliberately not the global
    /// correctness mechanism — that approach was ruled out, because a substring
    /// rule cannot tell a section's subject. Applicability is decided by the
    /// declarations above; this list only locates the sentences to point away
    /// from, within sections already judged mixed.
    /// </summary>
    public static readonly IReadOnlyList<string> TransportMechanics =
    [
        "join.sh",
        "delivery.sh",
        "team.sh",
        "inbox.sh",
        "send.sh",
        "watch.sh",
        "spawn.sh",
        "despawn.sh",
        "history.sh",
        "actas",
        "/agmsg",
        "$agmsg",
        "agmsg delegation",
        "agmsg delegations",
        "agmsg repl",
        "agmsg message",
        "agmsg team",
        "agmsg inbox",
        "agmsg role",
        "Codex bridge",
        "codex bridge",
        "ping/ack",
        "ping-ack",
        "delivery mode",
        "delivery-mode",
        "AGMSG-DIRECTIVE",
        // G570 third repair: imperative forms carry no script name but are
        // instructions all the same — "delegate implementation over agmsg" told
        // a herdr-only reader to use a transport the team does not run.
        "over agmsg",
        "via agmsg",
        "through agmsg",
        "using agmsg",
    ];

    /// <summary>
    /// G570 third repair: sections whose agmsg content is DESCRIPTIVE — the
    /// model's history and mechanism, byte-identical in both modes — rather
    /// than instructions. Design's ruling requires them to be preceded by an
    /// explicit agmsg-example label under herdr-only, so a reader can tell
    /// description from instruction at the point of reading rather than by
    /// inference.
    /// </summary>
    public static readonly IReadOnlyList<string> DescriptiveAgmsgContextHeadings =
        Declarations.Where(d => d.Descriptive).Select(d => d.Heading).ToArray();

    /// <summary>
    /// The JSON properties those sections serialize to, so the field rendering
    /// carries the same explicit context the prose does. G570 fourth repair:
    /// the label existed only in markdown, so a field consumer had no way to
    /// tell retained description from instruction.
    /// </summary>
    public static readonly IReadOnlyList<string> DescriptiveAgmsgContextJsonProperties =
        Declarations.Where(d => d.Descriptive && d.JsonProperty is not null).Select(d => d.JsonProperty!).ToArray();

    public const string DescriptiveAgmsgContextLabel =
        "> **agmsg example — descriptive, not an instruction.** This section explains the model using the agmsg "
        + "transport because that is the practiced one. In herdr-only the rule it states still binds; the agmsg "
        + "mechanics named here are illustration, and the herdr-only operating steps ship in G571.";

    public static bool CarriesTransportMechanic(string value) =>
        TransportMechanics.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pointer-only replacement for one mechanic-bearing sentence inside a
    /// mixed section. Says what does not apply and where its counterpart ships;
    /// deliberately says nothing about what to run instead, because that would
    /// be G571 operating content.
    /// </summary>
    /// <summary>
    /// G570 sixth repair (design ruling, clarification G570 applied): inside a
    /// mixed section, applicability is a property of the FRAGMENT, not of the
    /// section. A whole-row descriptive flag could not express a section that
    /// holds both — it either leaked imperative steps or over-stripped canon,
    /// and the tests only passed because I excluded descriptive sections
    /// wholesale from the guards.
    /// </summary>
    public enum FragmentType
    {
        /// <summary>Document structure — headings, table scaffolding, fences, blanks. Never routed.</summary>
        Structural,

        /// <summary>Mechanism, history, substrate identity. Byte-identical in both modes.</summary>
        CanonDescriptive,

        /// <summary>An instruction to drive the transport. Pointed away under herdr-only.</summary>
        TransportOperative,
    }

    /// <summary>
    /// Instructional cues. A fragment is TRANSPORT-OPERATIVE when it both names
    /// a transport mechanic AND tells the reader to do something with it —
    /// "verify the recipient id against the team roster (agmsg team.sh)" is an
    /// instruction; "| agmsg run directory |" is an identity in a table.
    /// </summary>
    private static readonly IReadOnlyList<string> ImperativeCues =
    [
        "verify", "run ", "register", "set ", "check", "confirm", "start", "restart", "stop",
        "send", "sending", "paste", "join", "attach", "launch", "configure", "resend", "re-send",
        "must ", "do not", "do NOT", "never ", "always ", "before ", "then ", "ensure",
        "use `", "invoke", "call ", "wait for", "reply", "ack", "delegate", "escalate to",
    ];

    /// <summary>
    /// Types one rendered fragment (a line). Structural lines are recognised
    /// first so table scaffolding and fences are never mistaken for content.
    /// </summary>
    public static FragmentType ClassifyFragment(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length == 0
            || trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith("```", StringComparison.Ordinal)
            || IsTableScaffolding(trimmed))
        {
            return FragmentType.Structural;
        }

        if (!CarriesTransportMechanic(line))
        {
            return FragmentType.CanonDescriptive;
        }

        return ImperativeCues.Any(cue => line.Contains(cue, StringComparison.OrdinalIgnoreCase))
            ? FragmentType.TransportOperative
            : FragmentType.CanonDescriptive;
    }

    private static bool IsTableScaffolding(string trimmed) =>
        trimmed.StartsWith("|", StringComparison.Ordinal)
        && trimmed.Replace("|", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Trim().Length == 0;

    public const string MechanicPointer =
        "(herdr-only: the session-layer step described here is agmsg-specific and does not apply; its herdr-only "
        + "counterpart ships in G571. The rule stated by this section still binds.)";

    private const string ReplacementHeadingValue = "## Session-layer switch checklist (herdr-only)";

    public const string ReplacementHeading = ReplacementHeadingValue;

    /// <summary>
    /// G570 fifth repair: the renderer reads these names from the table, so the
    /// HerdrOnly rows are CONSUMED rather than decorative. A row and the
    /// surface it names cannot drift apart if the surface is emitted under the
    /// name the row carries.
    /// </summary>
    public static string ReplacedSectionsProperty =>
        Declarations.Single(d => d.Heading == ReplacementHeadingValue).JsonProperty!;

    public static string ReplacementNoteProperty =>
        Declarations.Single(d => d.Heading == "(json) herdr-only replacement note").JsonProperty!;

    public static string DescriptiveContextProperty =>
        Declarations.Single(d => d.Heading == "(json) herdr-only descriptive context").JsonProperty!;

    public static string ReplacementSection(IReadOnlyList<string> replacedHeadings)
    {
        var lines = new List<string>
        {
            ReplacementHeading,
            string.Empty,
            "This team runs the **herdr-only** session layer, so the agmsg-only sections of this guide do not apply "
            + "and are not rendered. Their herdr-only counterparts — provisioning, dispatch and task format, the "
            + "events channel, and the switch checklists — ship in **G571**.",
            string.Empty,
            "Replaced here (agmsg-only):",
            string.Empty,
        };

        foreach (var heading in replacedHeadings)
        {
            lines.Add($"- `{heading.TrimStart('#', ' ')}`");
        }

        lines.Add(string.Empty);
        lines.Add(
            "Everything else in this document is mode-independent and applies unchanged: supervision, isolation, "
            + "liveness, the wake contract, publish authority, the design↔orchestrator double-check rule, dependency "
            + "planning, and escalation are properties of the four-thread model, and the model does not change with "
            + "the transport.");
        lines.Add(string.Empty);

        return string.Join('\n', lines);
    }
}
