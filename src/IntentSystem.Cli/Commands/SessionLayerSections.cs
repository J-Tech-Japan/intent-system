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
    /// <summary>
    /// Sections that exist only to operate agmsg. Under herdr-only they are
    /// REPLACED — not annotated — by a pointer to the G571 herdr-only operating
    /// sections. Every one of these is an instruction set for a transport the
    /// team is not running.
    /// </summary>
    public static readonly IReadOnlyList<string> AgmsgOnlyHeadings =
    [
        "## Terminal-workspace provisioning (G549)",
        "## Setup (starting orchestrator mode)",
        "## Setup intake form",
        "## Preflight (all three cwds)",
        "## Receiver readiness",
        "## Troubleshooting",
        "## Monitor recovery",
        "## Monitor tool vs delivery-mode (G511)",
        "## Codex monitor (beta) failure modes (G521)",
        "## Design / human receiver (optional)",
        "## Design-thread watchdog (recommended safety net)",
        "## Orchestrator-side long-interval automation (alternative safety net)",
        "## Thread prompts",
        "## agmsg reply contract",
    ];

    /// <summary>
    /// The JSON properties those sections serialize to. Removed wholesale under
    /// herdr-only, so a consumer reading fields sees the same selection a reader
    /// of the prose does — the two renderings cannot disagree about what
    /// applies.
    /// </summary>
    public static readonly IReadOnlyList<string> AgmsgOnlyJsonProperties =
    [
        "terminal_workspace_provisioning",
        "setup",
        "intake_form",
        "preflight",
        "receiver_readiness",
        "troubleshooting",
        "monitor_recovery",
        "monitor_tool_distinction",
        "codex_monitor_beta",
        "design_receiver",
        "design_watchdog",
        "orchestrator_automation_alternative",
        "threads",
        "agmsg_reply_contract",
        "codex_bridge_guidance",
    ];

    /// <summary>
    /// Mode-independent sections, declared explicitly rather than left as
    /// "everything else". Naming them is what makes the OVER-stripping failure
    /// testable: a guard can assert each one survives under herdr-only, and it
    /// does so without consulting any list the production selection uses to
    /// decide what to remove.
    /// </summary>
    public static readonly IReadOnlyList<string> ModeIndependentHeadings =
    [
        "## Session layer",
        "## Mode separation",
        "## Role boundary (design authors; orchestrator coordinates)",
        "## Design-thread workspace supervision (G550)",
        "## Cross-project isolation on a shared machine (G555)",
        "## Design-decision holds and bounded authority (G552)",
        "## Domain routing — single-domain vs multi-domain",
        "## Scheduled orchestrator cadence",
        "## CI wait state",
        "## Draft PR reviewability",
        "## Next-slice publication",
        "## End-of-wake check (G523/G524)",
        "## Dispatch verification (G524)",
        "## Dependency planning",
        "## Stale-thread health check",
        "## Design-thread escalation filter",
        "## Design handoff (start / resume)",
        "## Design traffic-controller playbook",
        "## Managed worktree cleanup",
        "## Review delegation — managed worktrees and design alignment",
        "## Orchestrator first wake",
        "## Safety boundaries",
        "## Detailed guide commands",
        "## Setup intake",
    ];

    /// <summary>
    /// The fourth applicability value from design's ruling (host main
    /// `fb1913c8`): MODE-INDEPENDENT-WITH-TRANSPORT-MECHANICS. These sections
    /// carry canon that binds in both modes — supervision, isolation, wake
    /// cadence, dispatch verification, safety boundaries — expressed through an
    /// agmsg mechanic. Dropping the section would delete canon; keeping it
    /// verbatim would hand a herdr-only reader an agmsg instruction.
    ///
    /// So under herdr-only the section is KEPT and only its
    /// mechanic-bearing sentences become POINTER-ONLY text. Per the ruling,
    /// pointer-only text is G570 routing metadata; naming a concrete herdr
    /// procedure would be G571 content and is forbidden here — which is exactly
    /// why the replacement says what does not apply and where the counterpart
    /// ships, and never what to run instead.
    /// </summary>
    public static readonly IReadOnlyList<string> MixedHeadings =
    [
        "## Setup intake",
        "## Design-thread workspace supervision (G550)",
        "## Cross-project isolation on a shared machine (G555)",
        "## Scheduled orchestrator cadence",
        "## Dispatch verification (G524)",
        "## Design handoff (start / resume)",
        "## Design traffic-controller playbook",
        "## Review delegation — managed worktrees and design alignment",
        "## Orchestrator first wake",
        "## Safety boundaries",
        "## Session-layer switch checklist (herdr-only)",
    ];

    /// <summary>
    /// The same sections as JSON properties, for the field rendering.
    /// </summary>
    public static readonly IReadOnlyList<string> MixedJsonProperties =
    [
        "setup_intake",
        "design_workspace_supervision",
        "cross_project_isolation",
        "scheduling",
        "dispatch_verification",
        "design_handoff",
        "design_traffic_controller",
        "review_delegation_contract",
        "orchestrator_first_wake",
        "worktree_management",
        "design_decision_holds",
        "design_thread_escalation",
        "stale_thread_health_check",
        "end_of_wake_check",
        "safety_boundaries",
    ];

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
    ];

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

    public const string ReplacementHeading = "## Session-layer switch checklist (herdr-only)";

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
