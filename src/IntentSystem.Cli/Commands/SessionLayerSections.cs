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
    internal sealed record SectionDeclaration(string Heading, string? JsonProperty, Applicability Applies);

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
        new("## Design-thread workspace supervision (G550)", "design_workspace_supervision", Applicability.ModeIndependentWithTransportMechanics),
        new("## Cross-project isolation on a shared machine (G555)", "cross_project_isolation", Applicability.ModeIndependentWithTransportMechanics),
        new("## Design-decision holds and bounded authority (G552)", "design_decision_holds", Applicability.ModeIndependentWithTransportMechanics),
        new("## Scheduled orchestrator cadence", "scheduling", Applicability.ModeIndependentWithTransportMechanics),
        new("## Dispatch verification (G524)", "dispatch_verification", Applicability.ModeIndependentWithTransportMechanics),
        new("## End-of-wake check (G523/G524)", "end_of_wake_check", Applicability.ModeIndependentWithTransportMechanics),
        new("## Stale-thread health check", "stale_thread_health_check", Applicability.ModeIndependentWithTransportMechanics),
        new("## Design-thread escalation filter", "design_thread_escalation", Applicability.ModeIndependentWithTransportMechanics),
        new("## Design handoff (start / resume)", "design_handoff", Applicability.ModeIndependentWithTransportMechanics),
        new("## Design traffic-controller playbook", "design_traffic_controller", Applicability.ModeIndependentWithTransportMechanics),
        new("## Managed worktree cleanup", "worktree_management", Applicability.ModeIndependentWithTransportMechanics),
        new("## Review delegation — managed worktrees and design alignment", "review_delegation_contract", Applicability.ModeIndependentWithTransportMechanics),
        new("## Orchestrator first wake", "orchestrator_first_wake", Applicability.ModeIndependentWithTransportMechanics),
        new("## Safety boundaries", "safety_boundaries", Applicability.ModeIndependentWithTransportMechanics),
        new("## Next-slice publication", "next_slice_publication", Applicability.ModeIndependentWithTransportMechanics),

        // mode-independent — unchanged in both.
        new("## Session layer", "session_layer", Applicability.ModeIndependent),
        new("## Mode separation", "mode_separation", Applicability.ModeIndependent),
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

    public static readonly IReadOnlyList<string> MixedHeadings =
        Declarations.Where(d => d.Applies == Applicability.ModeIndependentWithTransportMechanics)
            .Select(d => d.Heading).Append(ReplacementHeadingValue).ToArray();

    public static readonly IReadOnlyList<string> MixedJsonProperties =
        Declarations.Where(d => d.Applies == Applicability.ModeIndependentWithTransportMechanics && d.JsonProperty is not null)
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
    [
        "## Mode separation",
    ];

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
    public const string MechanicPointer =
        "(herdr-only: the session-layer step described here is agmsg-specific and does not apply; its herdr-only "
        + "counterpart ships in G571. The rule stated by this section still binds.)";

    private const string ReplacementHeadingValue = "## Session-layer switch checklist (herdr-only)";

    public const string ReplacementHeading = ReplacementHeadingValue;

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
