using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G487: read-only guide surface for the PRIMARY agmsg-backed four-thread
/// orchestrator model (design / orchestrator / implementation / review over
/// agmsg; ADR-012 / spec-26; G540 repositions this as primary, superseding
/// the earlier preview/opt-in framing). Renders paste-ready prompts for an
/// orchestrator thread plus the implementation/review threads it delegates
/// to, and pins the operating contract: agmsg is a message/progress/
/// completion signal layer ONLY; <c>intent-cli</c> and GitHub remain
/// authoritative for domain status, queue-state, issue/PR facts, labels, CI,
/// and closeout. Timer-loop mode remains the fully supported, simpler
/// ALTERNATIVE for setups without an orchestrator thread; orchestrator-message
/// mode MUST NOT also launch implement/review recurring timer loops for the
/// same domain/repo (no mixed-mode timer races). Host-state-free; never
/// launches an AI provider; never sends agmsg messages itself.
///
/// G489: a host repo can legitimately hold several intent domains (e.g.
/// <c>sekiban-as-a-service</c>, <c>sekiban-wasm-runtime</c>, <c>intent-cli</c>),
/// and more than one domain may target the same GitHub repository. The guide
/// therefore distinguishes a SINGLE-DOMAIN orchestrator (only one domain in
/// scope even though other-domain metadata is visible) from a MULTI-DOMAIN
/// orchestrator (intentionally coordinates several domains and must carry
/// explicit per-delegation routing). <c>--mode single-domain|multi-domain</c>
/// selects which contract the generated prompts emphasize. An execution-unit
/// ID prefix mismatch alone is NOT a wrong-repo signal — packet/domain metadata
/// and routing context decide.
/// </summary>
internal static class GuideOrchestratorThreadCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeSingleDomain = "single-domain";
    private const string ModeMultiDomain = "multi-domain";

    // G500: setup-intake outcomes and the existing-loop stop policy values.
    private const string IntakeMissingInputs = "missing-inputs";
    private const string IntakeSetupReady = "setup-ready";
    private const string IntakeBlocked = "blocked";

    private const string ExistingLoopNone = "none";
    private const string ExistingLoopWillStop = "will-stop";
    private const string ExistingLoopKeep = "keep";

    private const string UsageLine =
        "Usage: intent-cli guide orchestrator-thread [--domain <name>] [--target-repo <owner/repo>] [--agent <agent>] "
        + "[--mode single-domain|multi-domain] [--orchestrator-path <p>] [--implementation-path <p>] [--review-path <p>] "
        + "[--orchestrator-agent <a>] [--implementer-agent <a>] [--reviewer-agent <a>] [--team <name>] "
        + "[--delivery-mode <mode>] [--existing-loop-policy none|will-stop|keep] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var format, out var values, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        // G570: the recorded session layer selects which operating sections
        // this guide renders. Under `agmsg` the projection below is the
        // identity, so agmsg output is byte-identical to before this slice.
        //
        // G570 review repair: this used to fall back to agmsg when the record
        // could not be read, so a corrupted or hand-edited record silently
        // routed the whole guide through the wrong transport — the reader
        // would follow agmsg instructions in a herdr-only team and never know
        // the record was the reason. An invalid PRESENT record is not absence:
        // it fails closed here, with no guidance rendered at all.
        SessionLayerModeResolution sessionLayer;
        try
        {
            sessionLayer = SessionLayerModeStore.Resolve(
                context.RepoRoot,
                values["<domain>"],
                string.IsNullOrWhiteSpace(values["<team>"]) ? null : values["<team>"]);
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine($"session-layer-mode-unreadable: {exception.Message}");
            writer.WriteLine(
                "Refusing to render orchestrator-thread guidance: which sections are operative depends on the "
                + "session layer, and rendering the default would hand you instructions for a transport this team "
                + "may not be running. Repair the record with `intent-cli session-layer set --domain "
                + $"{values["<domain>"]} --mode agmsg|herdr-only --write`, or remove it to return to the default.");
            return 1;
        }

        var guide = ApplySessionLayer(BuildGuide(values, sessionLayer.IsHerdrOnly), sessionLayer, values);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            var json = JsonSerializer.Serialize(guide, JsonOptions);
            writer.Write(sessionLayer.IsHerdrOnly ? SelectJsonSections(json, values) : json);
            writer.WriteLine();
            return 0;
        }

        // G570 rereview repair: routing is SECTION-LEVEL and structural. Whole
        // agmsg-only sections are replaced by one pointer section; every
        // mode-independent section renders unchanged. See SessionLayerSections
        // for why substring projection was the wrong mechanism.
        if (sessionLayer.IsHerdrOnly)
        {
            using var buffer = new StringWriter();
            WriteMarkdown(buffer, guide);
            writer.Write(SelectMarkdownSections(buffer.ToString(), values));
            return 0;
        }

        WriteMarkdown(writer, guide);
        return 0;
    }

    /// <summary>
    /// G570: attaches the session-layer block and the intake's mode note to the
    /// built guide. It no longer TRANSFORMS any content — after the rereview,
    /// which content applies is decided by declared section applicability at
    /// the rendering boundaries (<see cref="SelectMarkdownSections"/> /
    /// <see cref="SelectJsonSections"/>), not by rewriting strings here.
    /// </summary>
    internal static OrchestratorThreadGuide ApplySessionLayer(
        OrchestratorThreadGuide guide,
        SessionLayerModeResolution sessionLayer,
        IReadOnlyDictionary<string, string> values)
    {
        var block = new OrchestratorSessionLayer
        {
            Mode = sessionLayer.Mode,
            Source = sessionLayer.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
            Summary = sessionLayer.Source == SessionLayerModeSource.Recorded
                ? $"Session layer: {SessionLayerMode.Describe(sessionLayer.Mode)} — recorded for this domain/team."
                : $"Session layer: {SessionLayerMode.Describe(SessionLayerMode.Default)} — no selection recorded, so the default is in force.",
            Exclusivity = SessionLayerMode.ExclusivitySentence,
            PreviewScoping = SessionLayerMode.PreviewScopingSentence,
            Selection =
                $"`intent-cli session-layer show --domain {values["<domain>"]}` reports it; "
                + $"`intent-cli session-layer set --domain {values["<domain>"]} --mode agmsg|herdr-only --write` changes it, "
                + "reversibly, in both directions.",
            ResidualAgmsgMechanics = sessionLayer.IsHerdrOnly
                ? "HERDR-ONLY: the agmsg-only sections of this guide are REPLACED, whole, by the switch-checklist "
                    + "section below — they are not rendered and not annotated. Every section that remains is "
                    + "mode-independent and applies unchanged."
                : null,
        };

        var intakeNote =
            $"Recorded session layer for this setup: {SessionLayerMode.Describe(sessionLayer.Mode)} "
            + $"({(sessionLayer.Source == SessionLayerModeSource.Recorded ? "recorded" : "default — nothing recorded yet")}). "
            + $"Record or change it with `intent-cli session-layer set --domain {values["<domain>"]} "
            + (string.IsNullOrWhiteSpace(values["<team>"]) ? string.Empty : $"--team {values["<team>"]} ")
            + "--mode agmsg|herdr-only --write`. A herdr-only request made at first setup is honoured from then on; "
            + "the choice is reversible in both directions.";

        return guide with
        {
            SessionLayer = block,
            SetupIntake = guide.SetupIntake with
            {
                SessionLayerMode = sessionLayer.Mode,
                SessionLayerNote = intakeNote,
            },
        };
    }

    /// <summary>
    /// G570 rereview repair: keeps whole sections whose declared applicability
    /// includes herdr-only, and replaces the run of agmsg-only sections with a
    /// single pointer section naming what was replaced.
    ///
    /// Section-level, not line-level: a section is either about the transport or
    /// it is not, and that judgement lives in <see cref="SessionLayerSections"/>
    /// where it can be reviewed — rather than being re-derived per line from a
    /// substring rule that is both too weak (operative prose carries no
    /// mechanic token) and too strong (canon that merely mentions agmsg gets
    /// destroyed).
    /// </summary>
    internal static string SelectMarkdownSections(string markdown, IReadOnlyDictionary<string, string> values)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(lines.Length);
        var replaced = new List<string>();
        var dropping = false;
        string? currentMixedHeading = null;

        var inFencedBlock = false;

        foreach (var line in lines)
        {
            // G570 fourth repair: a `## …` INSIDE a fenced block is quoted
            // content — an artifact template the guide shows — not a section of
            // this document. Treating it as one silently re-scoped routing from
            // that point on, which the rendered-surface guard caught.
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFencedBlock = !inFencedBlock;
            }

            if (!inFencedBlock && line.StartsWith("## ", StringComparison.Ordinal))
            {
                dropping = SessionLayerSections.AgmsgOnlyHeadings.Contains(line, StringComparer.Ordinal);
                currentMixedHeading = SessionLayerSections.MixedHeadings.Contains(line, StringComparer.Ordinal)
                    ? line
                    : null;
                if (dropping)
                {
                    replaced.Add(line);
                    continue;
                }
            }

            if (dropping)
            {
                continue;
            }

            // Descriptive agmsg content is labelled as such, in place, so a
            // reader distinguishes illustration from instruction at the point
            // of reading rather than by inference.
            if (!inFencedBlock
                && line.StartsWith("## ", StringComparison.Ordinal)
                && SessionLayerSections.DescriptiveAgmsgContextHeadings.Contains(line, StringComparer.Ordinal))
            {
                kept.Add(line);
                kept.Add(string.Empty);
                kept.Add(SessionLayerSections.DescriptiveAgmsgContextLabel);
                continue;
            }

            // Four-valued applicability (design ruling, host main fb1913c8):
            // inside a section declared MODE-INDEPENDENT-WITH-TRANSPORT-
            // MECHANICS, the canon stays and only the mechanic-bearing
            // sentences become pointer-only text. The section's rule still
            // binds; only the agmsg way of carrying it out is pointed away.
            // G570 sixth repair: fenced CONTENT is still content — a
            // paste-ready prompt is the most operative thing in the document.
            // The fence only stops `##` inside it becoming a section boundary;
            // it does not exempt the lines from routing.
            //
            // G570 seventh repair: the type comes from the hand-authored
            // declaration table, not from a cue heuristic, and an undeclared
            // fragment throws rather than defaulting to "keep".
            if (currentMixedHeading is not null
                && SessionLayerFragments.TypeOf(values, currentMixedHeading, line)
                    == SessionLayerSections.FragmentType.TransportOperative)
            {
                var pointerLine = PointerFor(line);
                if (kept.Count == 0 || !string.Equals(kept[^1], pointerLine, StringComparison.Ordinal))
                {
                    kept.Add(pointerLine);
                }

                continue;
            }

            kept.Add(line);
        }

        if (replaced.Count == 0)
        {
            return string.Join('\n', kept);
        }

        // The replacement section goes where the reader will meet it before any
        // operating instruction: immediately after the session-layer section.
        var anchor = kept.FindIndex(line => line.StartsWith("## Session layer", StringComparison.Ordinal));
        var insertAt = anchor < 0
            ? kept.Count
            : kept.FindIndex(anchor + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        if (insertAt < 0)
        {
            insertAt = kept.Count;
        }

        kept.InsertRange(insertAt, SessionLayerSections.ReplacementSection(replaced).Split('\n'));
        return string.Join('\n', kept);
    }

    /// <summary>
    /// Keeps the line's leading list/quote marker so a replaced bullet stays a
    /// bullet — the surrounding canon is still a readable list.
    /// </summary>
    private static string PointerFor(string line)
    {
        var trimmed = line.TrimStart();
        var indent = line[..(line.Length - trimmed.Length)];

        // G570 fourth repair: a markdown TABLE row replaced by a bare pointer
        // line breaks the table for every row after it. Keep the row, and
        // point away only inside its cells.
        if (trimmed.StartsWith("|", StringComparison.Ordinal) && trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            // G570 seventh repair: typing is per ROW, so a routed row points
            // away as a whole. The cell count is preserved so the table stays a
            // table for every row after it.
            var cellCount = trimmed.Trim('|').Split('|').Length;
            var pointed = new string[cellCount];
            pointed[0] = " " + SessionLayerSections.MechanicPointer + " ";
            for (var i = 1; i < cellCount; i++)
            {
                pointed[i] = " ";
            }

            return indent + "|" + string.Join('|', pointed) + "|";
        }

        foreach (var marker in new[] { "- ", "* ", "> " })
        {
            if (trimmed.StartsWith(marker, StringComparison.Ordinal))
            {
                return indent + marker + SessionLayerSections.MechanicPointer;
            }
        }

        // G570 third repair: ordered lists keep their OWN number. Replacing
        // "4. …" with an unnumbered pointer left playbooks reading 1, 2, 3, 5 —
        // a reader cannot tell whether a step is missing or merely
        // inapplicable.
        var ordered = System.Text.RegularExpressions.Regex.Match(trimmed, @"^(\d+\.\s+)");
        if (ordered.Success)
        {
            return indent + ordered.Groups[1].Value + SessionLayerSections.MechanicPointer;
        }

        return indent + SessionLayerSections.MechanicPointer;
    }

    /// <summary>
    /// The same selection over the JSON rendering, so a consumer reading fields
    /// sees exactly what a reader of the prose sees. The two renderings cannot
    /// disagree about what applies.
    /// </summary>
    internal static string SelectJsonSections(string json, IReadOnlyDictionary<string, string> values)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
        if (node is null)
        {
            return json;
        }

        var replaced = new List<string>();
        foreach (var property in SessionLayerSections.AgmsgOnlyJsonProperties)
        {
            if (node.Remove(property))
            {
                replaced.Add(property);
            }
        }

        foreach (var property in SessionLayerSections.MixedJsonProperties)
        {
            if (node.TryGetPropertyValue(property, out var value) && value is not null)
            {
                node[property] = PointMechanics(values, property, value);
            }
        }

        // G570 fourth repair: the explicit descriptive-agmsg context exists in
        // BOTH renderers now. A field consumer previously had no way to tell a
        // retained description from an instruction.
        var descriptiveContext = new System.Text.Json.Nodes.JsonObject();
        foreach (var property in SessionLayerSections.DescriptiveAgmsgContextJsonProperties)
        {
            if (node.ContainsKey(property))
            {
                descriptiveContext[property] = SessionLayerSections.DescriptiveAgmsgContextLabel;
            }
        }

        node[SessionLayerSections.DescriptiveContextProperty] = descriptiveContext;
        node[SessionLayerSections.ReplacedSectionsProperty] = new System.Text.Json.Nodes.JsonArray(
            replaced.Select(name => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(name)).ToArray());
        node[SessionLayerSections.ReplacementNoteProperty] =
            "Removed because this team runs the herdr-only session layer: these sections operate agmsg. Their "
            + "herdr-only counterparts ship in G571. Every remaining field is mode-independent and applies unchanged.";

        return node.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// JSON counterpart of the mixed-section rule: inside a declared mixed
    /// property, every string VALUE carrying a transport mechanic becomes
    /// pointer-only text, and everything else is untouched.
    /// </summary>
    private static System.Text.Json.Nodes.JsonNode? PointMechanics(
        IReadOnlyDictionary<string, string> values,
        string property,
        System.Text.Json.Nodes.JsonNode? node)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject mapping:
                {
                    var result = new System.Text.Json.Nodes.JsonObject();
                    foreach (var entry in mapping.ToArray())
                    {
                        mapping.Remove(entry.Key);
                        result[entry.Key] = entry.Key.StartsWith("session_layer", StringComparison.Ordinal)
                            ? entry.Value
                            : PointMechanics(values, property, entry.Value);
                    }

                    return result;
                }

            case System.Text.Json.Nodes.JsonArray array:
                {
                    var result = new System.Text.Json.Nodes.JsonArray();
                    foreach (var item in array.ToArray())
                    {
                        array.Remove(item);
                        result.Add(PointMechanics(values, property, item));
                    }

                    return result;
                }

            case System.Text.Json.Nodes.JsonValue value
                when value.TryGetValue<string>(out var text)
                    && SessionLayerFragments.JsonTypeOf(values, property, text)
                        == SessionLayerSections.FragmentType.TransportOperative:
                return System.Text.Json.Nodes.JsonValue.Create(SessionLayerSections.MechanicPointer);

            default:
                return node;
        }
    }

    private static OrchestratorThreadGuide BuildGuide(IReadOnlyDictionary<string, string> values, bool herdrOnly)
    {
        var domain = values["<domain>"];
        var repo = values["<owner/repo>"];
        var agent = values["<agent>"];
        var mode = values["<mode>"];
        var multiDomain = string.Equals(mode, ModeMultiDomain, StringComparison.Ordinal);

        string Apply(string template) => template
            .Replace("<domain>", domain, StringComparison.Ordinal)
            .Replace("<owner/repo>", repo, StringComparison.Ordinal)
            .Replace("<agent>", agent, StringComparison.Ordinal);

        // G489: the orchestrator prompt carries a mode-specific routing clause —
        // single-domain orchestrators stay scoped to one domain; multi-domain
        // orchestrators must attach explicit routing metadata to each delegation.
        var routingClause = multiDomain
            ? Apply(
                " You are in MULTI-DOMAIN mode: you intentionally coordinate several domains, and a single host repo "
                + "can hold several domains while one target repo (`<owner/repo>`) may receive work from more than one "
                + "domain. Before EACH delegation you MUST attach explicit routing metadata — domain, execution unit, "
                + "target repo, implementation cwd/worktree, review cwd/worktree, base branch policy, and destination "
                + "thread — and send each execution unit only to the thread that owns that domain's checkout. Never "
                + "delegate without complete routing. An execution-unit ID prefix that differs from the domain name is "
                + "NOT by itself a wrong-repo signal — compare packet/domain metadata and the routing context, not the "
                + "prefix.")
            : Apply(
                " You are in SINGLE-DOMAIN mode: only domain `<domain>` is in scope. A host checkout can expose other "
                + "domains' metadata in the same repo; those other-domain items are OUT OF SCOPE — do NOT delegate, "
                + "publish, or repair them, even if they target `<owner/repo>`. Escalate to the operator to switch "
                + "domain/mode instead of treating a visible other-domain item as delegable.");

        return new OrchestratorThreadGuide
        {
            SetupIntake = BuildSetupIntake(values, herdrOnly),
            // G570 third repair: the summary is CANON about authority, and it
            // must survive in both modes — but its agmsg phrasing is an
            // instruction in the practiced mode and a description in the other.
            // So it is stated mode-specifically rather than token-replaced,
            // which previously destroyed the authority sentence outright.
            Summary = herdrOnly
                ? "PRIMARY four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / "
                    + "implementation / review coordinate over the session layer this team runs — herdr-only here. "
                    + "The session layer carries natural-language delegation / progress / completion / blocker "
                    + "signals between threads; it is NOT workflow state. intent-cli and GitHub remain authoritative "
                    + "for domain status, queue-state, issue/PR facts, labels, CI, and closeout. Timer-loop mode "
                    + "remains fully supported as the simpler ALTERNATIVE for setups without an orchestrator thread "
                    + "(see Mode separation). The herdr-only operating steps ship in G571."
                : "PRIMARY agmsg-backed four-thread orchestrator model (ADR-012 / spec-26): design / orchestrator / "
                    + "implementation / review coordinate over agmsg. agmsg carries natural-language delegation / "
                    + "progress / completion / blocker signals between threads; it is NOT workflow state. intent-cli "
                    + "and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, CI, "
                    + "and closeout. Timer-loop mode remains fully supported as the simpler ALTERNATIVE for setups "
                    + "without an orchestrator thread (see Mode separation).",
            ModeSeparation = new OrchestratorModeSeparation
            {
                TimerLoopMode =
                    "ALTERNATIVE — fully supported, simpler setup for a domain/repo that does not run an orchestrator "
                    + "thread: implementation and review threads run on recurring timers and use intent-cli `worker "
                    + "next-action` / host review-next-slice as their source of truth. Use `intent-cli guide "
                    + "prompt-matrix` / `guide prompt-template` to set these up. Trade-off vs the primary model: "
                    + "simpler to start (no agmsg team, no orchestrator thread), but each receiver polls "
                    + "independently rather than being paced by a coordinating orchestrator, and there is no design"
                    + "↔orchestrator double-check on packet readiness before a receiver picks it up.",
                OrchestratorMessageMode =
                    "PRIMARY model: a fourth orchestrator thread delegates to implementation/review threads over "
                    + "agmsg instead of relying on independent timers, coordinating alongside the design thread. "
                    + "This is the practiced, maintained model (G520–G539: wake contract, stalled-work, "
                    + "heartbeat, issue-retire, priority override, publish reliability). It is still being hardened "
                    + "in places, but that is a factual maturity note, not a caveat that it is optional or "
                    + "secondary. Choose ONE mode per domain/repo.",
                MixedModeWarning =
                    "Do NOT run both modes for the same domain/repo. In orchestrator-message mode, do NOT launch the "
                    + "implementation/review recurring timer loops for that domain/repo — two drivers (a timer AND the "
                    + "orchestrator) would race on the same GitHub state. The orchestrator paces those threads; they do "
                    + "not also self-schedule.",
            },
            RoleBoundary = new OrchestratorRoleBoundary
            {
                Summary =
                    "ROLE BOUNDARY: DESIGN creates packets; the ORCHESTRATOR moves READY packets through the workflow. "
                    + "The orchestrator must NOT silently become the product/release/design author. When a needed packet "
                    + "is absent, incomplete, or would require product/release/design judgment, ask design to create or "
                    + "update it — do NOT draft it yourself.",
                DesignOwns = new[]
                {
                    "Intent shaping and clarifications.",
                    "ADRs and design decisions.",
                    "Release scope and version selection.",
                    "Packet content and acceptance criteria (the durable packet files).",
                },
                OrchestratorOwns = new[]
                {
                    "Inspect canonical intent-cli / GitHub state.",
                    "Publish exactly ONE already-authored, `issue-cut-ready` packet per wake (via canonical publish surfaces — see Next-slice publication).",
                    "Delegate implementation/review to the loopless receivers.",
                    "Wait for CI / review and track review state.",
                    "Close out approved PRs through the canonical review surfaces.",
                    "Report blockers and missing packets back to design.",
                },
                MissingPacketResponse =
                    "When a needed packet is absent or incomplete, or a vague goal would require authoring intent / "
                    + "acceptance criteria / release scope, the orchestrator does NOT invent the packet. It sends a "
                    + "structured request to DESIGN describing what is needed and WAITS for design to author/update the "
                    + "packet (or give an explicit instruction). The orchestrator only publishes/coordinates a packet "
                    + "that already exists and is `issue-cut-ready`.",
                MissingPacketMessageTemplate = Apply(
                    "{\"to\":\"design\",\"type\":\"packet-needed\",\"domain\":\"<domain>\",\"need\":\"<what is needed: "
                    + "e.g. a release-prep packet for vX.Y.Z, or acceptance criteria for <topic>>\",\"reason\":\"<why the "
                    + "orchestrator cannot proceed: requires product/release/design judgment, or the packet is absent/"
                    + "incomplete>\",\"blocking\":\"<the work that is waiting on it>\"}"),
                ReleasePrepRule =
                    "Release-prep is design-owned: DESIGN decides the release version and scope and AUTHORS the "
                    + "release-prep packet. The orchestrator may publish and coordinate that release-prep packet ONLY "
                    + "after it exists and is `issue-cut-ready` — it must not pick the version, decide scope, or author "
                    + "the release notes/packet itself from a vague \"prepare a release\" instruction.",
                DoubleCheckRule =
                    "DESIGN↔ORCHESTRATOR DOUBLE-CHECK (G540): neither thread decides design content alone. Four "
                    + "categories of design decision are always consulted between design and orchestrator before "
                    + "they take effect: (1) intent shaping and clarifications, (2) packet content and acceptance "
                    + "criteria, (3) release scope and version selection, and (4) prioritization rulings (e.g. "
                    + "`queue reprioritize`). The orchestrator NEVER authors design content unilaterally — it "
                    + "inspects, publishes, and delegates already-authored, `issue-cut-ready` packets, and escalates "
                    + "to design (packet-needed) rather than inventing intent, acceptance criteria, release scope, "
                    + "or priority. DESIGN NEVER bypasses the orchestrator for workflow transitions — publish/"
                    + "delegate/review/closeout label and state transitions stay the orchestrator's canonical "
                    + "responsibility, even when design authored the underlying packet. This formalizes the "
                    + "de-facto practice already in effect: the orchestrator escalates missing/incomplete packets "
                    + "to design; design rules on decomposition and prioritization; the orchestrator refuses to "
                    + "author packets.",
            },
            DomainRouting = new OrchestratorDomainRouting
            {
                Mode = mode,
                SingleDomainRule = Apply(
                    "Single-domain orchestrator: only domain `<domain>` is in scope. A host repo can hold several "
                    + "domains, so other-domain queue items may be VISIBLE in the same checkout — they are OUT OF SCOPE "
                    + "unless the operator switches domain/mode. Do not publish, delegate, or repair another domain's "
                    + "item just because it is visible or targets the same repo; escalate instead."),
                MultiDomainRule = Apply(
                    "Multi-domain orchestrator: intentionally coordinates several domains. One target repo can receive "
                    + "work from more than one domain, so visibility is not authorization. Require explicit routing "
                    + "metadata for EACH delegation before publishing, delegating, reviewing, or repairing, and route "
                    + "each execution unit to the thread that owns that domain's checkout."),
                RoutingMetadataFields = new[]
                {
                    "domain",
                    "execution unit",
                    "target repo",
                    "implementation cwd/worktree",
                    "review cwd/worktree",
                    "base branch policy",
                    "destination thread",
                },
                DelegationExample =
                    "{\"delegate\":{\"domain\":\"sekiban-as-a-service\",\"execution_unit\":\"G491\","
                    + "\"target_repo\":\"J-Tech-Japan/intent-system\",\"impl_cwd\":\"/work/sekiban-saas\","
                    + "\"review_cwd\":\"/review/sekiban-saas\",\"base_branch_policy\":\"direct-main\","
                    + "\"destination_thread\":\"implementation@sekiban-as-a-service\"}}",
                PrefixMismatchNote =
                    "Do NOT treat an execution-unit ID prefix that differs from the domain name as a wrong-repo signal "
                    + "on its own (a host repo can hold several domains, and one repo can serve several domains). Compare "
                    + "the packet/domain metadata and the routing context to decide ownership, not the prefix string.",
            },
            Scheduling = new OrchestratorScheduling
            {
                Summary =
                    "In orchestrator-message mode the normal steady state is MESSAGE-DRIVEN: implementation/review "
                    + "receivers already send accepted/progress/completed/blocked replies to the orchestrator, and those "
                    + "replies wake the orchestrator path — routine fast polling is NOT required. An orchestrator timer "
                    + "(Codex automation every 5m, or Claude same-thread `/loop 5m`) remains SUPPORTED but only as an "
                    + "explicit FALLBACK/LEGACY polling option for an operator who intentionally wants scheduled "
                    + "polling instead of message-driven wakes. Either way the implementation and review threads stay "
                    + "long-lived LOOPLESS receivers. The RECOMMENDED default safety net for message-driven steady "
                    + "state is a 30-minute-class design-thread watchdog (see Design-thread watchdog), not a fast "
                    + "orchestrator loop.",
                ScheduledThread = "orchestrator",
                ReceiverNote =
                    "Implementation and review threads are loopless receivers: do NOT start a recurring timer/loop in a "
                    + "receiver thread for this domain/repo. A receiver waits for an agmsg delegation, acts once, replies "
                    + "once, and waits again. Receivers are NEVER scheduled; when an explicit fallback/legacy timer is "
                    + "used (message-driven wakes are the default), the orchestrator is the only thread ever scheduled.",
                CodexSetupPrompt = Apply(
                    "OPTIONAL fallback/legacy polling — Codex automation (run every 5 minutes) for the ORCHESTRATOR "
                    + "thread, domain `<domain>` against `<owner/repo>` using `<agent>`: on each run perform exactly "
                    + "ONE orchestrator wake — check design-side progress and agmsg replies, ask intent-cli for state "
                    + "(`intent status`, `worker next-action --github-only`, `automation host-review-preflight`), "
                    + "verify the GitHub facts (CI/approval/merge/closeout), then send this wake's messages under the "
                    + "G524 cap — AT MOST ONE DELEGATION PER RECEIVER (implementation, review), NOT at-most-one-message "
                    + "overall, so a publish plus its same-wake delegation, one repair per stalled receiver, and one "
                    + "operator escalation may all go out together — and exit. Prefer the message-driven steady state "
                    + "(implementation/review agmsg replies already wake the orchestrator); use this timer only when "
                    + "the operator explicitly wants scheduled fallback/legacy polling. Do not run implementation/"
                    + "review loops; they are loopless receivers."),
                ClaudeLoopSetupPrompt = Apply(
                    "OPTIONAL fallback/legacy polling — Claude same-thread setup for the ORCHESTRATOR thread, domain "
                    + "`<domain>` against `<owner/repo>`: in the orchestrator thread run `/loop 5m` with the "
                    + "orchestrator prompt so the same thread re-wakes every 5 minutes. Each wake does exactly one "
                    + "orchestrator pass (read replies, check intent-cli / GitHub state, send this wake's messages under "
                    + "the G524 cap — AT MOST ONE DELEGATION PER RECEIVER, NOT at-most-one-message overall). "
                    + "Prefer the message-driven steady state (implementation/review agmsg replies already wake the "
                    + "orchestrator); use this timer only when the operator explicitly wants scheduled fallback/legacy "
                    + "polling. Do NOT also launch `/loop` in the implementation or review threads — those are "
                    + "loopless receivers driven only by your delegations."),
                WakeResponsibilities = new[]
                {
                    "A wake is triggered either by an incoming agmsg reply from implementation/review (the message-driven steady state) or by the optional fallback/legacy timer firing — either trigger runs exactly one orchestrator pass below.",
                    Apply("Check design-side progress: newly published packets/issues and intent status changes via `intent-cli intent status --domain <domain> --format json`."),
                    "Read pending agmsg replies from the implementation/review receivers (signals only — re-verify against intent-cli / GitHub).",
                    Apply("Ask intent-cli for worker state: `intent-cli worker next-action --repo <owner/repo> --github-only --format json`."),
                    Apply("Check host review readiness: `intent-cli automation host-review-preflight --repo <owner/repo> --format json`."),
                    "Verify GitHub facts directly: open PRs, CI conclusion, approvals, merge state, and closeout/label state.",
                    "Classify each open PR's CI: pending = wait-and-recheck next wake (no message); green = delegate review/closeout; red = repair or escalate by ownership; stuck = escalate. Pending CI is normal progress, not a reason to message the operator.",
                    "Detect stale blockers and no-reply receivers: a delegation with no accepted/progress reply within the expected window, or a thread stuck off the official workflow.",
                    "On a no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check: send one non-destructive status-request, check read-only intent-cli/GitHub facts, keep watching if there is progress, treat waiting-permission as an operator notice (never auto-clear), and only after repeated no-reply with no progress send one idempotent re-entry or escalate.",
                    "If intent-cli reports an `issue-cut-ready` candidate and all gates pass (same-domain or routed, complete contract, no open clarification, dependencies satisfied, under WIP, clean host-sync/preflight), publish ONE issue this wake via canonical publish-flow / issue-publish, verify it, THEN delegate that same issue to implementation in THIS SAME WAKE (G524) — do not ask the operator to create it, and do not stop after publishing to wait for a future wake to send the delegation.",
                    "If the candidate has unmet dependencies, plan the chain instead of pausing: act on the EARLIEST unmet resolvable dependency (publish or route it), keep the dependent held, and escalate only ambiguous/cycle/cross-domain-unrouted cases.",
                    "The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message overall (G524): this wake's actions may include a publish plus its same-wake delegation, one repair message per stalled receiver, one operator escalation, and handling any pending receiver reports, all together.",
                    "Before sending any agmsg message this wake, verify the recipient id against the team roster (`agmsg team.sh`) — treat an id not on the roster as an error, never a guess (G524).",
                    "Apply the design-thread escalation filter: keep routine progress / CI-wait / success / closeout / idle internal; surface to the design thread ONLY human-needed decisions, with structured evidence and the exact decision needed. Never hide a failure that needs a human.",
                    Apply("End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --format json`, and process every actionable item it reports before sleeping — never leave one for an unscheduled next wake; escalate explicitly if it is genuinely blocked on an operator decision. This includes a `backlog-ready-idle` item (G544, empty WIP + a ready packet + no activity past the idle threshold) — publish and delegate it in THIS wake, the same as any other issue-cut-ready candidate; only announce a following wake will handle it when that wake is actually scheduled."),
                },
                RepairVsEscalate = new OrchestratorRepairEscalate
                {
                    Repair =
                        "REPAIR routine off-rail states yourself by messaging the appropriate thread back onto the "
                        + "official intent-cli workflow — e.g. a receiver that stalled, skipped `worker complete`, "
                        + "applied a label by hand, or has not replied. Routine recovery is a repair message, not an "
                        + "escalation.",
                    Escalate =
                        "ESCALATE to the operator ONLY for: product/design judgment, credentials or security, a "
                        + "destructive local action, or an unresolved canonical ambiguity (intent-cli/GitHub facts "
                        + "genuinely conflict or are missing). Do not escalate states you can repair by message.",
                },
            },
            CiWaitState = new OrchestratorCiWaitState
            {
                Summary =
                    "A PR with pending/running CI is an ACTIVE WAIT STATE, not a blocker. GitHub checks are "
                    + "authoritative for CI state. Re-check the required checks on each scheduled wake; pending CI is "
                    + "normal progress and by itself NEVER triggers a request-update label, a repair message, or an "
                    + "operator question. Always re-verify the required checks immediately before delegating review, "
                    + "merge, or closeout — a green status read on an earlier wake can go stale.",
                States = new[]
                {
                    new OrchestratorCiState
                    {
                        State = "pending",
                        Routing =
                            "PENDING / RUNNING — wait and re-check on the next wake. Do not send a message, do not apply "
                            + "request-update, and do not ask the operator. Track the PR as in-flight and move on; the "
                            + "scheduled cadence re-evaluates it.",
                    },
                    new OrchestratorCiState
                    {
                        State = "green",
                        Routing =
                            "GREEN — all required checks passed. Route to review/closeout: delegate the PR to the review "
                            + "thread (or orchestrate merge/closeout of an already-approved PR) through intent-cli review "
                            + "surfaces. Re-verify the checks are still green at delegation time.",
                    },
                    new OrchestratorCiState
                    {
                        State = "red",
                        Routing =
                            "RED — a required check failed. Route by ownership: if the implementation thread can fix it "
                            + "(test/build/lint failure on the PR branch), send ONE repair message to that thread; if it "
                            + "needs product/design or canonical judgment, escalate. Never delegate merge/closeout while "
                            + "a required check is red.",
                    },
                    new OrchestratorCiState
                    {
                        State = "stuck",
                        Routing =
                            "STUCK / AMBIGUOUS — checks never started, hung well past a reasonable window, or report a "
                            + "conflicting/unknown status that intent-cli and GitHub cannot resolve. Escalate one operator "
                            + "decision (fail closed); do not guess green or force a merge.",
                    },
                },
            },
            DraftPrReviewability =
                "A DRAFT PR may still be reviewable depending on domain guidance — a reviewer MAY perform review "
                + "feedback on a draft when the domain's review policy allows it. But the reviewer must use the canonical "
                + "intent-cli review surfaces (`review closeout-plan`, `guide review`, `automation pr-transition`, "
                + "`closeout pr`); merge/approval stays gated by those surfaces. A draft is never approved/merged by hand "
                + "or by raw label edits, and never via host-metadata editing.",
            NextSlicePublication = new OrchestratorNextSlicePublication
            {
                Summary =
                    "Routine next-slice issue publication is an ORCHESTRATOR responsibility, not an operator question. "
                    + "When intent-cli reports a candidate as `issue-cut-ready` and ALL safety gates pass, the "
                    + "orchestrator publishes it itself through canonical intent-cli commands instead of stopping to ask "
                    + "the operator to create the GitHub issue. Publish AT MOST ONE issue per wake, then verify, THEN "
                    + "delegate that same issue to the implementation thread in THE SAME WAKE (G524) — publish and "
                    + "delegate complete together; never defer the delegation to an unscheduled \"next wake\", since no "
                    + "other trigger will ever wake the orchestrator to send it (this was the single largest measured "
                    + "stall class in message-driven orchestration, ~60 hours across G807/G809/G810/G812).",
                OnePerWake = true,
                Preconditions = new[]
                {
                    Apply("Same-domain context (`<domain>`), or an explicitly routed multi-domain delegation (domain, target repo, destination thread) — never publish a cross-domain candidate without explicit routing."),
                    "The packet contract is complete: no missing required sections (goal, in/out of scope, acceptance criteria, base-branch policy).",
                    "No open clarification or contract ambiguity on the candidate.",
                    "Dependencies are satisfied — every dependency execution unit is completed or already cut; never publish ahead of an uncut dependency.",
                    "Under the WIP cap — no in-progress blocker that should pace the queue first.",
                    Apply("Clean host-sync / preflight: `intent-cli automation host-review-preflight --repo <owner/repo> --format json` and the publish preflight report no blocker, and the target repo/domain is unambiguous."),
                },
                Blockers = new[]
                {
                    "Missing contract sections — hold, do not publish.",
                    "Open clarification / ambiguous contract — hold or escalate one operator decision.",
                    "Dependency mismatch — an uncut or incomplete dependency; hold (publishing ahead would violate the dependency contract).",
                    "WIP cap reached — let the in-progress work drain first.",
                    "Host-sync blocker or failed preflight — fix the sync via intent-cli, do not force the publish.",
                    "Ambiguous target repo or domain (no explicit routing in multi-domain) — escalate rather than guess.",
                },
                CanonicalCommands = new[]
                {
                    Apply("intent-cli issue publish-flow <execution-unit> --repo <owner/repo> --write --format json"),
                    "intent-cli automation issue-publish --write --format json",
                    "Never raw `gh issue create` or `gh ... --add-label`; publication and the `intent-target` label go through the canonical intent-cli surfaces only.",
                },
                PostPublishVerification = new[]
                {
                    "Confirm via intent-cli / GitHub (not chat) that the issue exists with the expected execution-unit body and the `intent-target` label.",
                    "Confirm the durable workflow state (queue-state / linkage / label) reflects the publish through intent-cli surfaces.",
                    "Immediately after verification, in THIS SAME WAKE, delegate implementation over agmsg (G524) — do not stop after publishing and wait for a future wake to send the delegation. The implementation receiver still derives its target from `intent-cli worker next-action`, not the agmsg text.",
                },
            },
            EndOfWakeCheck = new OrchestratorEndOfWakeCheck
            {
                Summary =
                    "G524: every orchestrator wake ends with a read-only stalled-work check (G523) — a wake must never "
                    + "end leaving an actionable pending transition for an unscheduled \"next wake\" that nothing would "
                    + "trigger. This closes the measured publish-then-sleep and silent-completion stall classes without "
                    + "adding any timer.",
                Command = Apply("intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --format json"),
                NeverDeferRule =
                    "Process every actionable item the check reports in THIS wake — delegate, repair, or route to "
                    + "closeout — before sleeping. This explicitly includes a `backlog-ready-idle` item (G544): WIP "
                    + "is empty, a packet is ready to publish, and nothing has moved for the idle threshold — "
                    + "publish it and delegate in THIS wake exactly like an `issue-cut-ready` candidate found any "
                    + "other way, never left for later. Do not announce work for a future wake unless an explicit "
                    + "fallback/legacy timer is actually scheduled to run it; message-driven wakes have no other "
                    + "trigger to pick the deferred work back up.",
                EscalateInsteadOfDeferRule =
                    "If an actionable item is genuinely blocked on an external/operator decision, escalate it "
                    + "explicitly to the design thread now via the design-thread escalation filter — do not silently "
                    + "defer it and do not leave it unprocessed.",
            },
            DispatchVerification = new OrchestratorDispatchVerification
            {
                Rule =
                    "G524: before sending ANY agmsg message, verify the recipient id is present in the team roster "
                    + "(agmsg `team.sh`). agmsg accepts an unknown recipient silently — there is no delivery error to "
                    + "notice. Treat a recipient id that is not on the roster as an error: fix the id or the roster "
                    + "registration before sending; never guess or approximate a role name.",
                DeadAddressExample =
                    "Field-observed loss: 8 dispatches addressed to `review` were silently lost when the registered "
                    + "role was `reviewer` — agmsg neither delivered nor reported the mismatch.",
            },
            DependencyPlanning = new OrchestratorDependencyPlanning
            {
                Summary =
                    "Unmet dependencies are NORMAL orchestration work when explicit and resolvable — not an operator "
                    + "stop. When a candidate depends on work that is not yet complete, do NOT pause for operator "
                    + "judgment; plan the dependency chain deterministically: hold the dependent candidate and route this "
                    + "wake's action to the earliest unmet dependency.",
                SelectionRule =
                    "Select the EARLIEST unmet same-domain dependency first (or an explicitly routed multi-domain "
                    + "dependency). Act on that dependency this wake — publish or route it — rather than on the dependent "
                    + "candidate. Walk the chain from its root, not from the visible leaf candidate.",
                Statuses = new[]
                {
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-publish-ready",
                        Action =
                            "The earliest unmet dependency is `issue-cut-ready` and has no GitHub issue — publish it this "
                            + "wake under the next-slice publication gates (one issue per wake), then keep the dependent "
                            + "candidate held.",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-actionable",
                        Action =
                            "The dependency already has an issue or PR that can move forward — route it (delegate "
                            + "implementation, review, closeout, or repair) using intent-cli / GitHub facts, not the "
                            + "dependent candidate.",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-waiting",
                        Action =
                            "The dependency is published and in flight (e.g. PR CI pending) — wait and re-check on the "
                            + "next wake; do not ask the operator. Keep the dependent candidate held.",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-ambiguous",
                        Action =
                            "The dependency cannot be resolved deterministically (missing dependency packet, conflicting "
                            + "GitHub linkage, or a cross-domain dependency with no route mapping) — escalate one operator "
                            + "decision (fail closed).",
                    },
                    new OrchestratorDependencyStatus
                    {
                        Status = "dependency-cycle",
                        Action =
                            "The dependencies form a cycle — escalate (fail closed); never pick a node arbitrarily to "
                            + "break the cycle.",
                    },
                },
                DependentHold =
                    "Keep the dependent candidate held — do not publish or delegate it — until every dependency is "
                    + "completed/cut. Re-evaluate the chain on each wake.",
                EscalationCases = new[]
                {
                    "A dependency packet is missing (referenced but no packet exists).",
                    "The dependencies form a cycle.",
                    "A cross-domain dependency has no explicit route mapping.",
                    "Conflicting GitHub linkage (two issues/PRs claim the same dependency, or linkage disagrees with the packet).",
                    "Destructive recovery would be required to proceed.",
                    "Credentials / security are involved.",
                    "A human product/design judgment is required.",
                },
            },
            StaleThreadHealthCheck = new OrchestratorStaleThreadHealthCheck
            {
                Summary =
                    "The implementation/review receivers are loopless, so silence is ambiguous — a receiver may be "
                    + "working, waiting for CI, waiting for a permission prompt, blocked, completed-without-reply, or "
                    + "truly stale. When a receiver has had no reply past the threshold, run a SAFE liveness check: ask "
                    + "before acting, verify authoritative facts, and never auto-cancel work, auto-clear a permission "
                    + "prompt, or duplicate a task.",
                NoReplyThreshold =
                    "Default 30 minutes since the receiver's last reply (configurable). Below the threshold, treat "
                    + "silence as normal in-progress work — do not poke.",
                Procedure = new[]
                {
                    "On no reply for >= the threshold, send ONE non-destructive status-request (see status_request_template) — ask, do not retry, cancel, or reset yet.",
                    "Check read-only intent-cli / GitHub facts: `worker next-action`, the issue/PR state, CI conclusion, and labels. Silence plus visible progress means the thread is working.",
                    "If authoritative facts show progress (new commits, PR opened/updated, CI running), keep watching — do not re-send the work.",
                    "If the receiver replies `waiting-permission`, treat it as an OPERATOR NOTICE — surface it; never auto-clear the permission/credential prompt.",
                    "Only after repeated no-reply AND no observable progress, send AT MOST ONE idempotent re-entry prompt that references the same issue/PR so it cannot duplicate work.",
                    "Escalate to the operator after repeated silence with no progress, or any unsafe case (would require cancel/reset, destructive git, or credentials).",
                },
                StatusRequestTemplate =
                    "{\"type\":\"status-request\",\"to\":\"<thread>\",\"ref\":\"issue#<n>|pr#<n>\",\"ask\":"
                    + "\"non-destructive liveness check: reply with one of working / waiting-ci / waiting-permission / "
                    + "blocked / completed / idle; do not start new work\"}",
                ReceiverStatuses = new[]
                {
                    new OrchestratorReceiverStatus { Status = "working", Meaning = "actively implementing/reviewing — keep watching, take no action." },
                    new OrchestratorReceiverStatus { Status = "waiting-ci", Meaning = "waiting on CI to conclude — wait and re-check; pending CI is normal progress." },
                    new OrchestratorReceiverStatus { Status = "waiting-permission", Meaning = "blocked on a permission/credential prompt — OPERATOR NOTICE; never auto-clear, surface it to the operator." },
                    new OrchestratorReceiverStatus { Status = "blocked", Meaning = "blocked on a clarification or external dependency — read the structured blocker and route repair or escalate." },
                    new OrchestratorReceiverStatus { Status = "completed", Meaning = "work finished — verify against intent-cli / GitHub; a completed-without-reply thread may just need its reply confirmed." },
                    new OrchestratorReceiverStatus { Status = "idle", Meaning = "no current task — safe to delegate the next item." },
                },
                Safety = new[]
                {
                    "Never auto-clear a permission/credential prompt — `waiting-permission` is an operator notice only.",
                    "Never auto-cancel or reset a receiver's work as part of a health check.",
                    "No destructive git operations and no raw label mutation in a health check.",
                    "Re-entry is idempotent: reference the existing issue/PR so a re-sent prompt cannot duplicate work.",
                    "Ask (status-request) and verify authoritative facts before any retry or escalation.",
                },
            },
            DesignThreadEscalation = new OrchestratorDesignThreadEscalation
            {
                Summary =
                    "The design thread is the PRIMARY human communication surface. Humans mainly talk to the design "
                    + "thread; implementation and review run through the orchestrator, and only human-needed decisions "
                    + "return to the design thread. Keep routine orchestration internal — this is a NOISE filter, not a "
                    + "failure filter: never hide a failure that needs a human. A design escalation carries a concise "
                    + "reason, the current AUTHORITATIVE state read from intent-cli/GitHub, the supporting evidence, "
                    + "options only when useful, and the exact decision needed — so the human can decide without "
                    + "re-deriving the state.",
                KeptInternal = new[]
                {
                    "Normal progress, accepted, and in-flight delegations.",
                    "CI waiting — pending checks are an active wait state, not a design-thread event.",
                    "Successful implementation (PR opened, CI green).",
                    "Successful review / approval.",
                    "Closeout of an already-approved PR.",
                    "Idle wakes with no actionable change.",
                },
                EscalateWhen = new[]
                {
                    "Clarification required — the issue/packet contract is ambiguous.",
                    "Product intent ambiguity or a design decision the orchestrator cannot make.",
                    "Permission / credentials / security.",
                    "A destructive action would be required to proceed.",
                    "Repeated no-reply / no-progress after the safe stale-thread health check.",
                    "Unresolved canonical state — intent-cli / GitHub facts conflict or are missing.",
                    "Release / public publish decision.",
                    "An explicit policy decision the operator owns.",
                },
                EscalationMessageTemplate =
                    "{\"to\":\"design\",\"type\":\"escalation\",\"ref\":\"issue#<n>|pr#<n>\",\"reason\":\"<clarification|"
                    + "product-ambiguity|permission|destructive|no-progress|canonical-conflict|release|policy>\","
                    + "\"current_state\":\"<the current AUTHORITATIVE state read from intent-cli/GitHub: labels, PR/CI/"
                    + "review/merge state, queue position>\",\"evidence\":\"<the intent-cli/GitHub facts that establish "
                    + "that state>\",\"options\":\"<OPTIONAL: candidate choices, only when useful>\",\"decision_needed\":"
                    + "\"<the exact decision or action requested from the human>\"}",
                MessageFields = new[]
                {
                    "reason — which human-needed category triggered the escalation.",
                    "current_state — the current AUTHORITATIVE state, read from intent-cli / GitHub (labels, PR/CI/review/merge state, queue position). REQUIRED: the receiver must not have to re-derive it.",
                    "evidence — the intent-cli / GitHub facts that establish the current state (do not pass generic wording as a substitute for the explicit state).",
                    "options — OPTIONAL candidate choices, included only when they help the human decide.",
                    "decision_needed — the exact decision or action requested from the human.",
                },
            },
            DesignReceiver = new OrchestratorDesignReceiver
            {
                Summary =
                    "When human-needed escalations should be deliverable over agmsg, add a FOURTH logical role: a "
                    + "DESIGN / human-facing receiver. Routine progress stays internal to orchestrator / implementation "
                    + "/ review; only human-needed decisions go to the design thread (see the design-thread escalation "
                    + "filter). The design receiver is OPTIONAL for routine operation but RECOMMENDED so escalations "
                    + "reach the human reliably — and it may receive manually by checking its inbox.",
                Optional = true,
                Roles = new[]
                {
                    "orchestrator — paces the other roles over agmsg; message-driven by default, with an explicit timer only as a fallback/legacy option.",
                    "implementation receiver — LOOPLESS; acts on delegations only, never starts its own timer.",
                    "review receiver — LOOPLESS; acts on delegations only, never starts its own timer.",
                    "design / human receiver — OPTIONAL; receives ONLY human-needed escalations and is also loopless (the human reads on demand, e.g. via `inbox.sh`).",
                },
                Setup = new[]
                {
                    "Register the design role in the SAME agmsg team — `agmsg join.sh <team> design <agent> <design-folder>` — or simply address escalation messages to the existing design thread.",
                    "Optional streamed delivery: `agmsg delivery.sh set <mode> <agent> <design-folder>`; otherwise the design thread reads on demand with `inbox.sh`.",
                    "The design receiver does NOT need a recurring loop — like implementation/review it is loopless; the human reads when prompted.",
                },
                ManualInboxTriggerPrompt =
                    "agmsg の inbox を確認してください。あなたは `<team>` の design です。 "
                    + "(Check your agmsg inbox — you are the `design` role of team `<team>`. Read pending escalations "
                    + "with `inbox.sh`; routine progress is intentionally not sent here.)",
                PreStartNote =
                    "Messages sent BEFORE the design receiver's monitor started may be in agmsg history but not visibly "
                    + "delivered — the design thread should read its inbox with `inbox.sh` to catch earlier escalations, "
                    + "exactly like the other receivers (see Receiver readiness / startup order).",
            },
            DesignHandoff = new OrchestratorDesignHandoff
            {
                Summary =
                    "Setup does not stop at role registration. After the agmsg roles are registered and ready, the "
                    + "DESIGN thread starts (or resumes) orchestration by sending ONE message to the orchestrator; the "
                    + "orchestrator then drives the loop autonomously and returns to design only for human decisions.",
                FirstMessageTemplate = Apply(
                    "{\"to\":\"orchestrator\",\"type\":\"start\",\"domain\":\"<domain>\",\"target_repo\":"
                    + "\"<owner/repo>\",\"requested_action\":\"<e.g. publish the next ready slice and drive it to a PR>\","
                    + "\"constraints\":\"one action per wake; escalate to design ONLY for human decisions (product/"
                    + "clarification, release/credentials/security, destructive actions, unresolved blockers)\"}"),
                AutonomousPublishRule =
                    "If `intent-cli` reports the next slice `issue-cut-ready` and all publish gates pass (see Next-slice "
                    + "publication), the orchestrator creates/publishes ONE GitHub issue ITSELF via canonical intent-cli "
                    + "commands (`issue publish-flow` / `automation issue-publish`) — it does NOT ask design to do each "
                    + "step. At most one issue per wake; verify after publishing before delegating implementation.",
                EscalationBoundary =
                    "Routine delegation (publish, delegate, CI wait, review, closeout) stays orchestrator↔receivers and "
                    + "does NOT go to design. Return to DESIGN only for human decisions — product/design clarification, "
                    + "release/credentials/security, destructive actions, or an unresolved blocker — using the structured "
                    + "escalation message (reason / current_state / evidence / decision_needed).",
                DesignInboxWorkflow =
                    "The design thread is a loopless receiver and reads on demand. To pick up escalations, the human (or "
                    + "the design thread) checks the design inbox with `inbox.sh` — especially when monitor delivery did "
                    + "not appear live or the design session started after the orchestrator sent. Read, decide/reply, "
                    + "then the orchestrator continues.",
            },
            DesignWatchdog = new OrchestratorDesignWatchdog
            {
                Summary =
                    "In the message-driven steady state, implementation/review replies already wake the orchestrator, "
                    + "so a fast orchestrator loop is redundant — but something must still notice a stall the "
                    + "message-driven path itself cannot self-report. The RECOMMENDED DEFAULT safety net (G539, "
                    + "superseding G526's external-scheduler recommendation) is a watchdog loop run from the DESIGN "
                    + "thread at a 30-minute-class interval: it calls `intent-cli automation heartbeat` and, when "
                    + "`stale=true`, sends AT MOST ONE canonical nudge to the orchestrator using the returned "
                    + "`message_body` — completely silent otherwise. It runs INSIDE a live, human-monitored agent "
                    + "session rather than an invisible external process, needs no separate credential/keychain "
                    + "setup (it authenticates the same way the rest of the session does), and is visible on the "
                    + "operator's screen the moment it breaks.",
                Optional = true,
                Frequency =
                    "30-minute class (e.g. every 30 minutes) — the RECOMMENDED default: quiet enough to stay out "
                    + "of the way, frequent enough to bound a stall far below what the field trial measured. A "
                    + "faster watchdog loop recreates the same churn the message-driven model removes.",
                LoopSetupPrompt = Apply(
                    "RECOMMENDED — design-thread watchdog loop for domain `<domain>` against `<owner/repo>`: in "
                    + "the DESIGN thread, run `/loop 30m` (Claude same-thread) or a Codex automation firing every "
                    + "30 minutes, with a prompt that on each wake runs `intent-cli automation heartbeat --domain "
                    + "<domain> --repo <owner/repo> --format json`; when the result's `stale` field is `true`, send "
                    + "its `message_body` verbatim to the orchestrator via the agmsg send script (exactly ONE "
                    + "message); when `stale` is `false`, send nothing and exit quietly — silence is reserved for "
                    + "this healthy case ONLY. A heartbeat command execution failure or malformed/non-object output "
                    + "is NEVER silent: state the failure explicitly in this wake's own turn output, visible to the "
                    + "operator watching this live session — the exact advantage an in-session watchdog has over "
                    + "the retired invisible external scheduler (see the fallback timer / retired-cron notes) — "
                    + "while still never fabricating or sending an agmsg nudge from broken input; only a genuine "
                    + "`stale=true` heartbeat result ever produces a sent message."),
                FailureVisibilityRule =
                    "Silence is reserved for a healthy `stale=false` heartbeat result ONLY. A heartbeat command "
                    + "execution failure or malformed/non-object output must be surfaced VISIBLY in the watchdog's "
                    + "own turn output this wake — never silently swallowed or silently retried, since silent "
                    + "failure is exactly the defect this slice retires the external OS scheduler for — while "
                    + "still never fabricating or sending an agmsg nudge from broken input; only a genuine "
                    + "`stale=true` result ever produces a sent message.",
                HeartbeatCommandExample =
                    "intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json",
                Checks = new[]
                {
                    "Check the design/HITL inbox for unread human-facing escalations or unanswered questions (`inbox.sh` on the design role).",
                    Apply("Check orchestrator staleness: read-only intent-cli / GitHub facts (`worker next-action --repo <owner/repo> --github-only --format json`, open PR/CI/label state) compared against the last known orchestrator activity."),
                    Apply("Run `intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json` — the RECOMMENDED primary check; it wraps `automation stalled-work` (G523) and returns a ready-to-send `message_body` naming every stale item and its canonical next command."),
                },
                Action =
                    "When staleness, an unanswered HITL message, or a heartbeat `stale=true` result is detected, "
                    + "send AT MOST ONE canonical repair/status request or heartbeat nudge to the orchestrator (the "
                    + "same non-destructive status-request shape as the stale-thread health check, or the "
                    + "heartbeat's own `message_body`) — never more than one per watchdog wake, and never a batch "
                    + "of repairs.",
                RepairStatusRequestTemplate =
                    "{\"type\":\"status-request\",\"to\":\"orchestrator\",\"from\":\"design-watchdog\",\"ask\":"
                    + "\"non-destructive liveness check: reply with current state and next action, or confirm idle\"}",
                StopCondition =
                    "Stop or archive the watchdog once both the backlog and the human-decision (HITL) queues are "
                    + "drained — an idle orchestrator with nothing queued and no pending human decisions needs no "
                    + "further watchdog wakes until new work appears.",
                SafetyRules = new[]
                {
                    "PROHIBITED: duplicate delegation — the watchdog never re-sends or re-creates a delegation itself; only the orchestrator delegates.",
                    "PROHIBITED: clearing a permission prompt — `waiting-permission` stays an operator notice; the watchdog never auto-clears it.",
                    "PROHIBITED: cancelling or resetting in-flight work.",
                    "PROHIBITED: force-closing an issue/PR or any other terminal action.",
                    "PROHIBITED: speculative durable-state surgery — no hand-editing labels, queue-state, or any host metadata; the watchdog only sends a message and reads read-only facts.",
                },
                FallbackTimerNote =
                    "An explicit orchestrator timer (Codex automation every 5m, or Claude same-thread `/loop 5m`) "
                    + "remains SUPPORTED as fallback/legacy polling when an operator intentionally wants scheduled "
                    + "polling instead of the message-driven steady state — measured weakness: this is fast polling "
                    + "the operator explicitly does not want in steady state, which is exactly why the design-thread "
                    + "watchdog is the recommended default instead. The design-thread watchdog (recommended), the "
                    + "orchestrator-side long-interval automation (alternative), and the 5-minute orchestrator "
                    + "fallback timer (legacy/discouraged) are alternative safety nets, not all required together.",
                MeasuredWeakness =
                    "Field trial (2026-06-28..07-14): the design session — where this watchdog runs — died 8-9 "
                    + "times in 16 days, its monitor dead until manually restored each time; several stalls were "
                    + "only discovered when that session happened to restart on its own. This remains a known "
                    + "limitation to weigh, but G539's field evidence (2026-07-15..07-20) showed the alternative — "
                    + "a session-independent external OS scheduler — is strictly worse: it failed SILENTLY on "
                    + "EVERY run for five continuous days (credential-store access; see Retired below), versus a "
                    + "session that dies visibly and gets restarted by the operator. A watchdog that occasionally "
                    + "restarts but is visible when broken is a stronger guarantee than one that runs invisibly "
                    + "until an operator happens to check its logs.",
            },
            OrchestratorAutomation = new OrchestratorAutomationAlternative
            {
                Summary =
                    "The SELECTABLE ALTERNATIVE to the design-thread watchdog: the same `intent-cli automation "
                    + "heartbeat` call, run directly from a long-interval automation IN THE ORCHESTRATOR'S OWN "
                    + "THREAD (Codex automation or Claude same-thread `/loop`) rather than from the design thread. "
                    + "On each wake it calls `automation heartbeat` itself and, when stale, acts on the returned "
                    + "state in the SAME wake — there is no design-to-orchestrator message hop, because the "
                    + "orchestrator is the one running the check.",
                Frequency =
                    "30-60 minute class — the same low-frequency band as the recommended design-thread watchdog, "
                    + "never the fast 5-minute fallback timer (see the design-thread watchdog above for why fast "
                    + "polling is discouraged in steady state).",
                TradeOff =
                    "Design-side (recommended) keeps the orchestrator strictly loopless — it only ever wakes from "
                    + "an inbound agmsg message, matching its normal message-driven model, at the cost of one extra "
                    + "hop (design watchdog to orchestrator). Orchestrator-side automation removes that hop (the "
                    + "orchestrator wakes and acts on its own heartbeat check directly) but requires the "
                    + "orchestrator itself to run a recurring loop — exactly the pattern orchestrator-message mode "
                    + "is designed to avoid in steady state. Choose orchestrator-side only when an operator has a "
                    + "specific reason to prefer one fewer hop over keeping the orchestrator loopless.",
                CommandExample =
                    "intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json",
                SetupPrompt = Apply(
                    "ALTERNATIVE — orchestrator-side long-interval automation for domain `<domain>` against "
                    + "`<owner/repo>`: run a Codex automation or Claude same-thread `/loop` firing every 30-60 "
                    + "minutes IN THE ORCHESTRATOR THREAD; each wake runs `intent-cli automation heartbeat --domain "
                    + "<domain> --repo <owner/repo> --format json` in addition to the normal orchestrator wake "
                    + "checks, and when `stale` is `true` treats the returned `message_body` as this wake's "
                    + "repair/escalation signal (still under the G524 wake contract's AT MOST ONE DELEGATION PER "
                    + "RECEIVER cap, not an at-most-one-message cap)."),
                RetiredCronNote =
                    "RETIRED (G539): the external cron/launchd OS-scheduler recommendation added by G526 is "
                    + "retired. Reasons: (1) credential-store access — the wrapper's `gh`/agmsg auth commonly "
                    + "lives in a login keychain a cron job cannot reach, so it fails at the credential step, not "
                    + "the logic; (2) invisible failure — a failed cron run writes to an OS log nobody watches, so "
                    + "it is not actually a safety net; (3) outside the agmsg model — intent-cli coordinates "
                    + "through agmsg and holds no thread of its own, and an OS scheduler sits entirely outside "
                    + "that model. Field evidence: every run failed silently from installation (2026-07-15) "
                    + "through 2026-07-20 — five continuous days — and a 105-minute stall on 2026-07-20 (G538 / PR "
                    + "#1179) went unrecovered even though `automation stalled-work` correctly detected it "
                    + "(`pr-created-not-reviewing, age=105m`); only a human ping surfaced it. `intent-cli "
                    + "automation heartbeat` itself is UNCHANGED and remains scheduler-agnostic — any scheduler, "
                    + "including cron, can still call it — the guide simply no longer RECOMMENDS an external OS "
                    + "scheduler as the mechanism.",
            },
            MonitorRecovery = new[]
            {
                new OrchestratorTroubleshooting
                {
                    Symptom = "Monitor did not start",
                    Action = "Restart the receiver session so the monitor/watch hook attaches on a fresh turn; verify with `delivery.sh status` and a ping/ack. Until then, read with `inbox.sh`.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Message not visible",
                    Action = "It may be queued but not delivered live — read the role's queue with `inbox.sh`; if still missing, re-confirm registration (`team.sh`) and delivery, then resend after an ack.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Receiver started after the message was sent",
                    Action = "Earlier messages are in history but not delivered live to the new session — read them with `inbox.sh`, or have the sender resend after the receiver acks.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Orchestrator idle despite a packet existing",
                    Action = "Confirm the orchestrator received the design start/resume message (`inbox.sh` on the orchestrator) and that `worker next-action` / `intent status` report an actionable item for THIS domain/repo (not another domain visible in the host repo). If issue-cut-ready and safe, the orchestrator should publish one issue itself rather than wait.",
                },
            },
            MonitorToolDistinction = new OrchestratorMonitorDistinction
            {
                Summary =
                    "Claude Code's `Monitor` is a generic Claude Code tool — the real mechanism that streams the agmsg "
                    + "inbox into a receiver. agmsg attaches it by launching `watch.sh` from the Claude Code SessionStart "
                    + "directive; the running Monitor task is what turns incoming agmsg lines into live transcript events. "
                    + "The word \"monitor\" is overloaded — do NOT confuse this Claude Code `Monitor` tool with agmsg's "
                    + "delivery-mode config or with unrelated `Azure Monitor` / other MCP `monitor` tools.",
                DeliveryModeNote =
                    "agmsg `delivery.sh status` `mode=monitor` is configuration only and is NOT proof that a Monitor tool "
                    + "is attached and streaming — a receiver can report `mode=monitor` while no Claude Code `Monitor` is "
                    + "running and nothing is delivered live. Confirm live attachment with the success markers below, not "
                    + "with the delivery mode alone.",
                SuccessMarkers = new[]
                {
                    "`ToolSearch select:Monitor` resolves Monitor in the receiver session (the tool is available).",
                    "the transcript shows `Monitor(agmsg inbox stream)` — the Monitor tool attached to the inbox stream.",
                    "the Claude Code footer shows `1 monitor` (a live Monitor task is attached).",
                    "the transcript shows `Monitor event` lines as inbox messages arrive (the stream is live).",
                },
                FailureMarkers = new[]
                {
                    "delivery falls back to a plain `Bash` / background `watch.sh` task instead of an attached Monitor — no live stream.",
                    "the footer shows `1 shell` instead of `1 monitor` (a background shell is running, not a Monitor).",
                    "confusion with `Azure Monitor` / other MCP `monitor` tools — those are unrelated to agmsg inbox streaming and never prove attachment.",
                },
                TrustRepair = new[]
                {
                    "Root cause: the exact-cwd project key in `~/.claude.json` with `hasTrustDialogAccepted=false` suppresses the SessionStart directive that launches Monitor, so no Monitor attaches and the inbox never streams (the receiver still reports `mode=monitor`).",
                    "Repair (operator action only): repair Claude project trust for that exact cwd, restart the receiver session, then re-verify the success markers above. intent-cli never auto-detects or edits `~/.claude.json`.",
                },
                WindowsGuidance = new[]
                {
                    "On Windows, start the monitor-mode Claude Code receiver from **Git Bash**. Dogfooding showed PowerShell / native-Windows startup may not attach the agmsg Monitor reliably (the SessionStart `watch.sh` directive assumes a bash environment), so the receiver can report `mode=monitor` yet never stream.",
                    "If Git Bash is unavailable or the Monitor still does not attach on Windows, fall back to `turn` delivery or manual `inbox.sh` polling (see the fallback ladder) — do NOT report the receiver ready on `mode=monitor` alone.",
                },
                FallbackLadder = new[]
                {
                    "Realtime Monitor delivery is NOT required for orchestrator mode — it is a convenience. When the success markers are missing, work the bounded ladder below and then keep going with an explicit fallback; do not silently claim a live monitor.",
                    "Restart the receiver Claude Code session so the SessionStart directive re-launches `watch.sh`/Monitor on a fresh turn, then re-check the success markers.",
                    "Verify project trust / session: the exact-cwd `~/.claude.json` project key must have trust accepted (see the trust-repair runbook); confirm `ToolSearch select:Monitor` resolves the generic Monitor tool in that session.",
                    "On Windows, relaunch the receiver from Git Bash (see Windows guidance) rather than PowerShell / a native shell.",
                    "Compare against a known-good receiver project (one already showing `1 monitor` / `Monitor event`) to isolate whether the break is this cwd's config or the environment.",
                    "If it still will not attach, fall back to `turn` delivery or manual `inbox.sh` polling and say so explicitly, or escalate to the operator. A Bash/background `watch.sh` (`1 shell`) is diagnostic/fallback only — never a substitute for the Claude Code Monitor, and never a reason to report the receiver as live-monitored.",
                    "See the agmsg monitor-delivery docs (https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md) for backend-specific delivery/watch details; intent-cli does not own or modify agmsg internals or Claude Code tool availability.",
                },
                ProjectSettingsDiagnosis = new[]
                {
                    "When `ToolSearch select:Monitor` finds NO generic `Monitor` tool at all (not `1 shell` vs `1 monitor` — the tool is simply absent), treat it as a Claude Code TOOL-SURFACE problem FIRST, before debugging agmsg delivery — regardless of what `delivery.sh status` `mode=monitor` reports. agmsg cannot stream through a Monitor tool that Claude Code is not exposing.",
                    "Known-good comparison checklist — diff this project's Claude Code config against a folder where `1 monitor` already works: `.claude/settings.json`, `.claude/settings.local.json`, `~/.claude.json` project trust/onboarding flags, the enabled/disabled MCP server lists, and project-level `env` settings.",
                    "Suspect project-level `env` overrides observed in dogfooding (in `.claude/settings.json` `env`) that can suppress the tool surface: `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true`, `CLAUDE_CODE_ENABLE_TELEMETRY=false`, `DISABLE_ERROR_REPORTING=true`, `DISABLE_TELEMETRY=true`. Removing/isolating these project `env` overrides (agmsg hooks preserved) restored `ToolSearch select:Monitor` in the affected folders.",
                    "Safe remediation (operator action, does NOT touch agmsg): close the Claude Code sessions → remove or isolate the suspect project-level `env` settings while PRESERVING the agmsg SessionStart hooks → reopen Claude Code → run `ToolSearch select:Monitor` → verify `Monitor(agmsg inbox stream)`, the footer `1 monitor`, and `Monitor event` as inbox messages arrive.",
                    "This is a Claude Code project-config repair, not an agmsg change — intent-cli never edits `.claude/settings.json`, `~/.claude.json`, or agmsg internals. Preserve the G516 distinction throughout: `1 monitor` = live success, `1 shell` = diagnostic/fallback only, never success.",
                },
            },
            CodexBridgeGuidance = new OrchestratorCodexBridgeGuidance
            {
                ObservedVersions =
                    "Observed at agmsg 1.1.6 / Codex v0.144.1 (macOS, `codex()` shim launch) — the setup preflight, "
                    + "healthy-state markers, and troubleshooting entries below are observations from that tested "
                    + "environment, not a permanent bridge contract. Re-verify against the installed agmsg/Codex "
                    + "versions after an upgrade before trusting the exact mechanics (e.g. retry interval, thread "
                    + "attachment order) described here.",
                SetupPreflight =
                    "Before launching a Codex receiver, verify the (project, codex) pair resolves to exactly ONE "
                    + "identity — `whoami.sh <project> codex` should print a single `agent=` line. Clean up any stale "
                    + "registration first (e.g. a leftover `actas` registering another role into the project); more "
                    + "than one identity blocks the bridge launcher silently.",
                HealthyStateMarkers = new[]
                {
                    "`delivery.sh status` shows `Codex bridge: <team>/<role> alive (pid N)`.",
                    "The bridge arms on the FIRST turn sent to the session, not at Codex startup — do not expect delivery before that first turn.",
                    "An already-running Codex session stays unmonitored until it is restarted after the bridge is enabled.",
                },
                Troubleshooting = new[]
                {
                    new OrchestratorTroubleshooting
                    {
                        Symptom = "mode: monitor but the Codex bridge never starts",
                        Action = "The (project, codex) pair resolves to more than one identity — `codex-bridge-launcher.sh` proceeds only when there is exactly ONE, and otherwise retries silently every 0.3s forever (e.g. a stale `actas` registration for another role). Check the identity count with `whoami.sh <project> codex`, remove the stale registration, then relaunch.",
                    },
                    new OrchestratorTroubleshooting
                    {
                        Symptom = "bridge alive (pid shown) but the Codex TUI never moves / never reacts to messages",
                        Action = "The shared Codex app-server accumulates loaded threads across sessions, and `codex-bridge.js` attaches to the FIRST (oldest) entry of `thread/loaded/list` — turns are injected into an old background thread while the visible TUI never reacts. Recover by: quit the TUI, stop the app-server/bridge/launcher processes, remove the recorded app-server/bridge state files (`codex-app-server.*.{pid,port,version}` and the bridge `{pid,appserver,meta}` files), relaunch codex, then send one turn to re-arm.",
                    },
                    new OrchestratorTroubleshooting
                    {
                        Symptom = "responses to one message appear twice across a restart window",
                        Action = "Suspect a doubled bridge — verify only one bridge pid exists (`delivery.sh status`) before relaunching; kill any duplicate bridge process first.",
                    },
                },
                ReferenceLink =
                    "See the agmsg codex-monitor-beta doc (https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md) "
                    + "for internals — intent-cli does not own or modify agmsg's Codex bridge implementation.",
            },
            IntakeForm = new OrchestratorIntakeForm
            {
                Summary =
                    "When the user asks only \"I want to use orchestrator mode\", elicit or infer the setup facts, then "
                    + "produce the concrete commands/messages (see Setup intake / Setup). Ask for what is missing; apply "
                    + "the recommended defaults for the rest.",
                Questions = new[]
                {
                    "domain and target repo",
                    "orchestrator cwd + agent type",
                    "implementation receiver cwd + agent type",
                    "review receiver cwd + agent type",
                    "design cwd + agent type, and whether design is manual-inbox or monitored",
                    "delivery mode per role (monitored / streamed inbox watch, or manual inbox)",
                },
                Defaults = new[]
                {
                    "orchestrator = Claude",
                    "implementer = Claude",
                    "reviewer = Codex",
                    "design = manual-inbox Codex",
                    "runtime / implementation / review receivers = monitor (when supported)",
                },
                DesignDeliveryNote =
                    "Design may be a manual-inbox receiver (reads with `inbox.sh` on demand) or a monitored receiver; "
                    + "either way it receives ONLY human-decision escalations or explicit summaries, never routine "
                    + "progress.",
                RoleStartupMessages = new[]
                {
                    new OrchestratorRoleStartup
                    {
                        AgentType = "claude",
                        ActasInvocation = "/agmsg actas <role>",
                        Note = "In a Claude session, paste the slash-command form to assume the agmsg role, then paste that role's prompt from the Thread prompts section.",
                    },
                    new OrchestratorRoleStartup
                    {
                        AgentType = "codex",
                        ActasInvocation = "$agmsg actas <role>",
                        Note = "In a Codex session, paste the `$agmsg actas <role>` form to assume the agmsg role, then paste that role's prompt from the Thread prompts section.",
                    },
                },
            },
            TerminalWorkspaceProvisioning = BuildTerminalWorkspaceProvisioning(repo, values["<team>"]),
            DesignWorkspaceSupervision = BuildDesignWorkspaceSupervision(domain, repo),
            DesignDecisionHolds = BuildDesignDecisionHolds(domain, repo),
            CrossProjectIsolation = BuildCrossProjectIsolation(),
            DesignTrafficController = new OrchestratorDesignTrafficController
            {
                Summary =
                    "The design thread acts as a TRAFFIC CONTROLLER, not an implementer. It coordinates through the "
                    + "orchestrator and only surfaces human-needed items — it does not drive implementation/review or "
                    + "mutate workflow state itself.",
                Playbook = new[]
                {
                    "Check the design inbox (`inbox.sh`) for orchestrator escalations / summaries.",
                    "Check intent-cli / GitHub READ-ONLY state (`intent status`, `worker next-action`, PR/issue/labels) to ground any decision — never trust an agmsg message as state.",
                    "Send the orchestrator a state update or a nudge (start/resume); do not drive implementation/review yourself.",
                    "Do NOT directly mutate implementation/review work, labels, or host metadata — that is the orchestrator/receivers' job through intent-cli.",
                    "Summarize ONLY human-needed items to the human; keep routine progress internal.",
                    // G564: the one design duty that is NOT delegable to the
                    // orchestrator — the tree is written by design, and only
                    // design can discharge it.
                    IntentTreeCoEvolutionDuty.Duty,
                    IntentTreeCoEvolutionDuty.CloseoutCheck,
                },
                IdleDiagnostic = new[]
                {
                    "Confirm the orchestrator is actually scheduled and on a fresh turn (its `/loop` or Codex automation is running).",
                    "Confirm it received your last message (`inbox.sh` on the orchestrator) — a pre-monitor send may be queued, not delivered live; resend after an ack.",
                    "Confirm intent-cli actually reports an actionable item for THIS domain/repo (`worker next-action` / `intent status`) — idle may be correct (nothing to do).",
                    "Only after these, escalate to the human as a structured decision.",
                },
                ContextOnlyRule =
                    "The design thread MAY send context to a receiver thread, but MUST mark it context-only (e.g. "
                    + "`context-only: <text>`) unless the orchestrator delegated the action — receivers act only on "
                    + "orchestrator delegations, not on design context.",
            },
            WorktreeManagement = new OrchestratorWorktreeManagement
            {
                Summary =
                    "Orchestrated work creates temporary worktrees for implementation and review. Allocate them under a "
                    + "managed, allowlisted root inside the workspace and clean them up with `git worktree remove` — "
                    + "NEVER a raw `rm -rf` of an arbitrary `/tmp/intent-review-...` path. Safe cleanup design, not "
                    + "disabling approvals, is the right default: a destructive `rm -rf` approval prompt is the symptom "
                    + "of an unmanaged workspace.",
                ManagedRoot =
                    "Allocate temporary worktrees under a repo/workspace-scoped managed root — the `[project] "
                    + "worktree_root` (default `.intent-cli/worktrees/`), git-ignored — not arbitrary `/tmp/"
                    + "intent-review-...` paths. A managed root is allowlisted, predictable, and removable with `git "
                    + "worktree remove`.",
                Allocation = new[]
                {
                    "Create each worktree under the managed root: `git worktree add .intent-cli/worktrees/<role>-<unit> <branch>`.",
                    "Keep the managed root git-ignored so it never pollutes the tree.",
                    "One worktree per role/unit; do not reuse a dirty worktree across units.",
                },
                SafeCleanup = new[]
                {
                    "Remove a worktree only with `git worktree remove` (it refuses a dirty worktree) — never raw `rm -rf`.",
                    "Validate the target path is INSIDE the allowlisted managed root before removal.",
                    "Confirm the path is a registered git worktree (it appears in `git worktree list`).",
                    "Confirm the worktree state is clean (no uncommitted or untracked user work) before removing.",
                    "Prune stale registrations with `git worktree prune` after removal.",
                },
                RefuseWhen = new[]
                {
                    "The target is OUTSIDE the allowlisted managed root.",
                    "The target is the repo root, `$HOME`, or a system path (`/`, `/tmp` root, etc.).",
                    "The path is not a registered git worktree.",
                    "The worktree has uncommitted or untracked user work — STOP and surface it; do not delete user work.",
                },
                ApprovalPolicyNote =
                    "`approval_policy=never` / `danger-full-access` is NOT a substitute for safe cleanup design. Keep "
                    + "least-privilege approvals as the default; the goal is to never need a destructive `rm -rf` prompt, "
                    + "not to suppress the prompt.",
            },
            ReviewDelegationContract = new OrchestratorReviewDelegationContract
            {
                Summary =
                    "Review delegation must carry the managed-worktree policy and require design-alignment evidence up "
                    + "front — not leave the reviewer to discover it. Dogfooding showed a reviewer allocate a raw "
                    + "`/tmp/...review...` worktree and Codex correctly ask to approve a destructive `rm -rf` — the "
                    + "RIGHT safety behavior for the WRONG workflow. The fix is a managed root, NOT weakening approval "
                    + "settings.",
                ManagedWorktreeRoot =
                    "Review worktrees use the SAME managed, workspace-local root as the rest of orchestrated work — the "
                    + "`[project] worktree_root` (default `.intent-cli/worktrees/`), e.g. "
                    + "`.intent-cli/worktrees/review-<unit>` — NEVER an arbitrary `/tmp/...review...` path.",
                ProhibitedPattern =
                    "PROHIBITED as the normal path: a raw `/tmp/...` review worktree, and a `rm -rf /tmp/... && git "
                    + "worktree add ...` cleanup chain. Reaching for this pattern is the signal to STOP and allocate "
                    + "under the managed root instead — not to ask the operator to approve the `rm -rf`.",
                CleanupRule =
                    "Cleanup is `git worktree remove <managed-path>` for a REGISTERED, CLEAN worktree only — confirmed "
                    + "via `git worktree list` and a clean `git status` first.",
                UnsafeStalePathRule =
                    "A stale path that is NOT a registered git worktree, is OUTSIDE the managed root, or is dirty/"
                    + "unsafe is NEVER an operator `rm -rf` approval prompt — it is a STRUCTURED BLOCKER agmsg reply to "
                    + "the orchestrator (`status: blocked`) so the orchestrator can route the repair, not something the "
                    + "reviewer resolves by force-deleting an unmanaged path.",
                DelegationExample =
                    "{\"delegate\":{\"domain\":\"<domain>\",\"execution_unit\":\"<unit>\",\"target_repo\":"
                    + "\"<owner/repo>\",\"pr\":\"<n>\",\"review_cwd\":\"/review/<domain>\",\"managed_worktree_policy\":"
                    + "\"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never "
                    + "/tmp\",\"design_alignment_required\":true,\"destination_thread\":\"review@<domain>\"}}",
                DesignAlignmentSources = new[]
                {
                    "packet — the authored packet content and acceptance criteria.",
                    "review-context — the review-context artifact for this PR/unit.",
                    "intent tree — the relevant intent-tree entries for the touched domain.",
                    "ADR / decision notes — any linked architecture or design-decision records.",
                    "relevant docs — user-facing or developer docs the change touches.",
                },
            },
            Setup = new OrchestratorSetup
            {
                Summary =
                    "Design-thread setup for starting orchestrator-message mode. The steady state is MESSAGE-DRIVEN: "
                    + "implementation/review receivers reply over agmsg and those replies wake the orchestrator, so no "
                    + "fast recurring driver is required by default; the implementation and review threads are always "
                    + "loopless receivers. Decide the inputs, register the agmsg roles under one team, paste the role "
                    + "prompts, run one read-only first wake, ping-test the inbox, then either rely on message-driven "
                    + "wakes or schedule the orchestrator only as an explicit fallback/legacy timer (see Design-thread "
                    + "watchdog for the RECOMMENDED default safety net). agmsg is a signal layer only; intent-cli "
                    + "and GitHub stay authoritative.",
                Decisions = new[]
                {
                    Apply("domain (`<domain>`) and target repo (`<owner/repo>`)"),
                    "host / orchestrator / implementation / review paths — each role runs from its own folder, clone, or worktree",
                    "base branch policy (e.g. direct-main)",
                    Apply("per-role agents (e.g. orchestrator=`<agent>`, implementation=claude, review=codex)"),
                    "agmsg team name",
                    "delivery mode — how each role receives messages (e.g. a streamed inbox watch per role)",
                },
                Checklist = new[]
                {
                    "Record the decisions above (domain, repo, paths, base branch policy, agents, team, delivery mode).",
                    "Register each role with agmsg under ONE team: orchestrator, implementation, review (agmsg `join.sh`).",
                    "Set the delivery mode so each role receives messages — e.g. a streamed inbox watch per role (agmsg `delivery.sh` / `watch.sh`).",
                    "Paste the role prompts from the `Thread prompts` section into the matching thread (orchestrator / implementation / review).",
                    "Run ONE read-only first wake in the orchestrator (see `Orchestrator first wake`) — confirm only, send nothing.",
                    "Ping-test the inbox before real delegation (see ping_test).",
                    "Message-driven steady state is the default — implementation/review replies wake the orchestrator; schedule an orchestrator timer (Codex automation 5m, or Claude same-thread `/loop 5m`) only as an explicit fallback/legacy option, and leave the receivers loopless either way.",
                    "On teardown, clean up the agmsg roles through the agmsg scripts (see cleanup).",
                },
                AgmsgCommands = new[]
                {
                    "register a role / join the team — agmsg `join.sh`",
                    "set the delivery mode — agmsg `delivery.sh`",
                    "watch a role's inbox (receiver delivery) — agmsg `watch.sh`",
                    "send a message / ping — agmsg `send.sh`",
                    "list the team / identities — agmsg `team.sh`",
                    "leave / clean up a role — agmsg `leave.sh` / `despawn.sh`",
                },
                PingTest =
                    "Send one agmsg message from the orchestrator to each receiver and confirm it appears in that "
                    + "receiver's inbox/stream before any real delegation. If the ping does not arrive, fix "
                    + "delivery/registration first — do not start delegating against a broken channel.",
                Cleanup = new[]
                {
                    "Leave the team / despawn each role through the agmsg scripts (`leave.sh` / `despawn.sh`).",
                    "Stop any inbox watchers started for the roles.",
                },
                Warning =
                    "Never edit the agmsg database or team files directly — register, message, and clean up ONLY through "
                    + "the agmsg scripts. Hand-editing agmsg state corrupts delivery.",
            },
            Preflight = new OrchestratorPreflight
            {
                Summary =
                    "Before mutating anything, preflight ALL THREE checkouts (the orchestrator, implementation, and "
                    + "review cwds). A receiver acting in the wrong repo, on the wrong branch, or over dirty user work is "
                    + "the most common orchestration failure — catch it before the first delegation.",
                Checks = new[]
                {
                    "For each cwd (orchestrator / implementation / review), confirm `git status` is clean — no uncommitted or untracked work that a checkout/branch switch would clobber.",
                    "Confirm each cwd's git remote is the EXPECTED repo for its role — the implementation/review receivers must point at the delegated target repo, not a sibling clone.",
                    "Confirm each cwd is on the expected branch/base (e.g. the base-branch policy's base, or a fresh work branch) — a receiver on a stale branch implements against the wrong base.",
                    "Confirm the host repo / domain context: if a checkout exposes multiple domains, the orchestrator MUST filter by the requested domain/target repo before publishing or delegating (visibility is not authorization).",
                    "Existing-loop conflict check: confirm no timer-loop (implement/review recurring timer) is already running for this domain/repo — orchestrator-message mode and timer-loop mode must not run together for the same route.",
                },
            },
            Troubleshooting = new[]
            {
                new OrchestratorTroubleshooting
                {
                    Symptom = "Message not received by a receiver",
                    Action = "Confirm the role is registered (`team.sh`) and delivery is set (`delivery.sh status`). The receiver may have missed it (monitor not yet active) — have it read its queue with `inbox.sh`, or resend after a ping/ack.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Monitor/delivery configured AFTER the session started",
                    Action = "A session started before its monitor/watch path was active will not pick up earlier messages live — restart the receiver session (or read with `inbox.sh`) so the monitor hook attaches, then re-confirm with a ping/ack before delegating.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Codex Desktop app thread is the receiver",
                    Action = "Codex Desktop app threads are NOT agmsg monitor receivers by default — they receive manually only. Use a CLI session as the receiver, or have the Desktop thread read its queue with `inbox.sh`.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Receiver cwd sees a different repo/domain than delegated",
                    Action = "STOP — do not claim. The receiver's cwd/worktree, git remote, and delegated domain must match the routing; reply blocked and re-route. An execution-unit ID prefix mismatch alone is NOT the signal — compare packet/domain metadata and the routing context.",
                },
                new OrchestratorTroubleshooting
                {
                    Symptom = "Codex asks to approve `rm -rf /tmp/...review...`",
                    Action = "This is the RIGHT safety behavior for the WRONG workflow — the review worktree was allocated at an unmanaged `/tmp` path instead of the managed root. The fix is the managed root (`.intent-cli/worktrees/review-<unit>`), NOT weakening approval settings: re-allocate under the managed root (see Review delegation — managed worktrees and design alignment), and do NOT approve the `rm -rf` for the stale `/tmp` path — reply blocked to the orchestrator so it can route the cleanup as a repair instead.",
                },
            },
            ReceiverReadiness = new OrchestratorReceiverReadiness
            {
                Summary =
                    "Monitor configuration is NOT enough. A registered team plus a configured delivery mode does NOT mean "
                    + "a receiver will see your message — a newly launched or restarted session may not pick up messages "
                    + "sent before its monitor/watch path was active. Confirm each receiver is READY with a ping/ack "
                    + "before sending real work.",
                StartupOrder = new[]
                {
                    "Join the three roles to the team (`join.sh`).",
                    "Set the delivery mode for each role (`delivery.sh set`).",
                    "Launch or restart the receiver CLI sessions (implementation, review, and the orchestrator).",
                    "Wait for the monitor/bridge to attach in each receiver session before sending anything.",
                    "Send a ping to each receiver only AFTER its session is active.",
                    "Require an ack from each receiver — or confirm receipt manually with `inbox.sh` — before proceeding.",
                    "Only then send the first real delegation.",
                },
                SendBeforeReadyWarning =
                    "Messages sent BEFORE a receiver is ready may be stored in agmsg history but NOT visibly delivered "
                    + "to a freshly launched/restarted session. A send is not a delivery: an unacked message is "
                    + "receiver-NOT-READY, not a successful delegation. Recover by resending after the ack, or have the "
                    + "receiver read its queue with `inbox.sh`.",
                RecoveryMessageTemplate =
                    "Heads up: your session started AFTER I sent earlier messages, so they may be in agmsg history but "
                    + "not visibly delivered to you. Read your queue now with `inbox.sh` to catch anything you missed. "
                    + "Any prior unacked message is receiver-not-ready (NOT a delegation you must act on) — reply `ack` "
                    + "to this ping and I will (re)send the current delegation.",
                States = new[]
                {
                    new OrchestratorReadinessState { State = "registered", Meaning = "the role joined the team (it appears in `team.sh`)." },
                    new OrchestratorReadinessState { State = "delivery-configured", Meaning = "the delivery mode is set for the role (`delivery.sh status`)." },
                    new OrchestratorReadinessState { State = "watcher-alive", Meaning = "the monitor / watch process is running for the role." },
                    new OrchestratorReadinessState { State = "receiver-session-active", Meaning = "a launched/restarted receiver session is actually attached to the monitor path — a session started before delivery was active may not receive earlier messages." },
                    new OrchestratorReadinessState { State = "ping-acknowledged", Meaning = "the receiver replied to a ping — the only proof the channel works end to end." },
                },
                PingAckRequired =
                    "Before ANY real delegation, send a ping to the orchestrator, implementer, and reviewer and require "
                    + "an ack from each. Treat a missing ack as NOT-READY and do not send real work until every receiver "
                    + "has acked. Re-do the ping/ack after any receiver launch or restart.",
                NotReadyRecovery = new[]
                {
                    "Messages sent before readiness may not have been received — resend them after the ack.",
                    "Read what is already queued for a role with `inbox.sh` (a session can pull messages it missed).",
                    "Re-confirm registration (`team.sh`) and delivery (`delivery.sh status`) before resending.",
                },
                WatchNote =
                    "Manual `watch.sh` streams a role's inbox live but OCCUPIES a terminal — it is a debug / fallback "
                    + "streaming option, not the default setup requirement. The normal path is the monitor delivery hook; "
                    + "use `watch.sh` to diagnose, not as the standing receiver.",
                CodexDesktopNote =
                    "Codex Desktop app threads are NOT agmsg monitor receivers by default — they are a different "
                    + "execution surface from a CLI session. Do not assume a Desktop-app thread will receive agmsg "
                    + "messages; use a CLI session as the receiver (or read with `inbox.sh`).",
                DiagnosticCommands = new[]
                {
                    "agmsg team.sh — confirm the role is registered in the team.",
                    "agmsg delivery.sh status — confirm the delivery mode is configured/active for the role.",
                    "agmsg inbox.sh — read what is queued for a role (catch messages sent before readiness).",
                    "agmsg send.sh — send a ping; the receiver's ack proves the channel end to end.",
                },
            },
            Threads = new[]
            {
                new OrchestratorThreadPrompt
                {
                    Role = "orchestrator",
                    Purpose =
                        "Coordinate implementation/review threads for domain `" + domain + "` via agmsg; never mutate "
                        + "workflow state directly.",
                    Prompt = Apply(
                        "You are the ORCHESTRATOR thread for domain `<domain>` against `<owner/repo>` using `<agent>`. "
                        + "You coordinate the implementation and review threads over agmsg; you do NOT implement code, "
                        + "perform semantic review, or mutate GitHub/intent-cli workflow state yourself. agmsg is a "
                        + "signal layer only — intent-cli and GitHub are authoritative. Per wake: read pending agmsg "
                        + "replies, ask intent-cli for the real state (`intent-cli intent status --domain <domain> "
                        + "--format json`, `intent-cli worker next-action --repo <owner/repo> --github-only --format "
                        + "json`, `intent-cli automation host-review-preflight --repo <owner/repo> --format json`), "
                        + "verify the GitHub facts that an agmsg reply claims (merged PR, CI, labels). Treat pending/"
                        + "running CI as an active wait state — re-check it on a later wake rather than asking the "
                        + "operator; delegate review/closeout only after required checks are green, route red checks to "
                        + "repair or escalation by ownership, and escalate only stuck/ambiguous CI. If you publish a "
                        + "ready next-slice issue this wake (when intent-cli reports it `issue-cut-ready` and all gates "
                        + "pass — via canonical `intent-cli issue publish-flow` / `automation issue-publish`), verify it "
                        + "exists, THEN delegate that same issue to implementation in THIS SAME WAKE (G524) — never stop "
                        + "after publishing to wait for an unscheduled future wake to send the delegation; no other "
                        + "trigger will ever pick it back up. The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER "
                        + "(implementation, review), NOT at-most-one-message overall: alongside a publish+delegation you "
                        + "may also send one repair request per stalled receiver (pointing it back to the official "
                        + "intent-cli workflow), escalate one operator decision, and handle any pending receiver "
                        + "reports. Before sending ANY agmsg message, verify the recipient id is present in the team "
                        + "roster (agmsg `team.sh`) — agmsg accepts an unknown recipient silently, so treat an "
                        + "off-roster id as an error, never a guess (a legacy `review` vs the registered `reviewer` has "
                        + "silently lost messages in the field). Unmet dependencies are normal work, not a stop: if the "
                        + "next candidate depends on incomplete work, act on the EARLIEST unmet resolvable dependency "
                        + "(publish or route it) and keep the dependent held, rather than pausing for the operator. For a "
                        + "no-reply receiver past the threshold (default 30m), run the SAFE stale-thread health check — "
                        + "send one non-destructive status-request and verify read-only intent-cli/GitHub facts before any "
                        + "retry; never auto-clear a permission prompt, auto-cancel work, or duplicate a task. Report to "
                        + "the human-facing DESIGN thread ONLY human-needed decisions (clarification, product ambiguity, "
                        + "permission/credentials, destructive action, repeated no-progress, unresolved canonical state, "
                        + "release/publish, explicit policy); keep routine progress / CI-wait / success / closeout / idle "
                        + "internal — but never hide a failure that needs a human. Do "
                        + "NOT "
                        + "launch recurring implement/review timers for this domain/repo while orchestrating. End every "
                        + "wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain "
                        + "<domain> --repo <owner/repo> --format json`, and process every actionable item it reports "
                        + "before sleeping — a wake must never end leaving an actionable transition for an unscheduled "
                        + "next wake; escalate explicitly if an item is genuinely blocked on an operator decision. Fail "
                        + "closed: if you detect a second orchestrator for this domain/repo, or agmsg replies conflict "
                        + "with GitHub/intent-cli facts, STOP and escalate rather than guessing. Your normal steady "
                        + "state is MESSAGE-DRIVEN: implementation/review agmsg replies wake you, so you do NOT need a "
                        + "fast recurring timer. An orchestrator timer (Codex automation every 5m, or Claude "
                        + "same-thread `/loop 5m`) remains SUPPORTED only as an explicit FALLBACK/LEGACY polling "
                        + "option; the implementation/review receivers stay loopless and act only on your delegations "
                        + "either way."
                        + routingClause),
                },
                new OrchestratorThreadPrompt
                {
                    Role = "implementation",
                    Purpose =
                        "Implement exactly one delegated item, then report a structured agmsg reply.",
                    Prompt = Apply(
                        "You are the IMPLEMENTATION thread for domain `<domain>` against `<owner/repo>` using `<agent>`, "
                        + "driven by orchestrator agmsg delegations. You are a LOOPLESS receiver: do NOT start your own "
                        + "recurring timer/loop for this domain/repo — wait for a delegation, act once, reply once, then "
                        + "wait again (receivers are never scheduled; the orchestrator is message-driven by default, with an explicit fallback/legacy timer as the only case where it is scheduled). When delegated an item, run "
                        + "the normal child implementation workflow: the issue/PR number comes from `intent-cli worker "
                        + "next-action --repo <owner/repo> --github-only`, NOT from the agmsg text. Before claiming, "
                        + "verify your local checkout context matches the delegation: your cwd/worktree, the git remote "
                        + "repo, and the delegated domain must line up with the routing you were handed. If the checkout "
                        + "does not match the delegated repo/domain, STOP and reply blocked instead of claiming. An "
                        + "execution-unit ID prefix that differs from the domain name is NOT by itself a wrong-repo "
                        + "signal — confirm via packet/domain metadata and the routing context, not the prefix. Then "
                        + "claim, implement, open the PR with a `Closes #<issue>` reference, and `worker complete` — all "
                        + "label transitions through intent-cli worker/automation only. intent-cli and GitHub remain "
                        + "authoritative; agmsg is only how you receive the delegation and send back your reply. "
                        + "Reporting completion or blocked status to the orchestrator is a REQUIRED FINAL STEP of "
                        + "EVERY delegation (G524) — it is not optional and the orchestrator cannot discover a silent "
                        + "completion on its own (a PR opened with no report reaching the orchestrator is LOST WORK "
                        + "from the orchestrator's perspective, observed in the field for 88 minutes before a manual "
                        + "check found it). When done or blocked, send ONE structured agmsg reply (accepted / progress "
                        + "/ completed / blocked) in the exact shape "
                        + "`{\"status\":\"completed\",\"thread\":\"implementation\",\"ref\":\"pr#<n>\",\"note\":\"PR "
                        + "opened, Closes #<n>, CI green\"}` (or the `blocked` shape naming one operator action), "
                        + "citing the GitHub facts (PR number, CI) — do not consider the delegation finished until this "
                        + "reply is sent. Do NOT read host metadata (`.intent-cli/**`, "
                        + "`intents/**`)."),
                },
                new OrchestratorThreadPrompt
                {
                    Role = "review",
                    Purpose =
                        "Review/closeout exactly one delegated PR through intent-cli, then report a structured agmsg reply.",
                    Prompt = Apply(
                        "You are the REVIEW thread for domain `<domain>` against `<owner/repo>` using `<agent>`, driven "
                        + "by orchestrator agmsg delegations. You are a LOOPLESS receiver: do NOT start your own "
                        + "recurring timer/loop for this domain/repo — wait for a delegation, act once, reply once, then "
                        + "wait again (receivers are never scheduled; the orchestrator is message-driven by default, with an explicit fallback/legacy timer as the only case where it is scheduled). When delegated a PR, run the "
                        + "official host review/closeout through intent-cli surfaces (`review closeout-plan`, `guide "
                        + "review`, `automation pr-transition`, `closeout pr`) — agmsg never replaces semantic review or "
                        + "authorizes a merge. Perform semantic review only when you are the packet `review_role` or "
                        + "explicitly assigned (G480); otherwise orchestrate the merge/closeout of an already-approved "
                        + "PR. If you need a review worktree, allocate it under the MANAGED root "
                        + "(`.intent-cli/worktrees/review-<unit>`) — NEVER a raw `/tmp/...review...` path, and NEVER "
                        + "`rm -rf /tmp/... && git worktree add ...`; remove it only with `git worktree remove` once it "
                        + "is a registered, clean worktree. A non-registered, dirty, or otherwise unsafe stale path is a "
                        + "STRUCTURED BLOCKER reply to the orchestrator, not an operator `rm -rf` approval prompt (see "
                        + "Review delegation — managed worktrees and design alignment). Your review must be grounded in "
                        + "design intent, not only diff/CI: check the packet, review-context, intent tree, ADR/decision "
                        + "notes, and relevant docs, and your reply must set `design_alignment_checked: true` plus which "
                        + "of those sources you checked — the orchestrator treats a reply missing that evidence as an "
                        + "INCOMPLETE review unless an authoritative prior approval state already proves equivalent "
                        + "review. Reporting completion or blocked status to the orchestrator is a REQUIRED FINAL STEP "
                        + "of EVERY delegation (G524) — report ONE structured agmsg reply (accepted / progress / "
                        + "completed / blocked) in the exact shape "
                        + "`{\"status\":\"completed\",\"thread\":\"review\",\"ref\":\"pr#<n>\",\"note\":\"approved; "
                        + "closeout done\",\"design_alignment_checked\":true,\"design_alignment_sources_checked\":"
                        + "[\"packet\",\"review-context\",\"intent-tree\",\"adr-decision-notes\",\"relevant-docs\"]}` "
                        + "(or the `blocked` shape naming one operator action), citing the intent-cli/GitHub facts — do "
                        + "not consider the delegation finished until this reply is sent. intent-cli and GitHub stay "
                        + "authoritative."),
                },
            },
            AgmsgReplyContract = new OrchestratorReplyContract
            {
                Description =
                    "Implementation/review threads reply to a delegation with exactly one structured agmsg message. "
                    + "The reply is a SIGNAL; the orchestrator re-verifies every claim against intent-cli / GitHub "
                    + "before acting on it.",
                Accepted = "{\"status\":\"accepted\",\"thread\":\"implementation\",\"ref\":\"issue#<n>\",\"note\":\"claimed; starting\"}",
                Progress = "{\"status\":\"progress\",\"thread\":\"implementation\",\"ref\":\"issue#<n>\",\"note\":\"branch pushed; CI running\"}",
                Completed = "{\"status\":\"completed\",\"thread\":\"implementation\",\"ref\":\"pr#<n>\",\"note\":\"PR opened, Closes #<n>, CI green\"}",
                Blocked = "{\"status\":\"blocked\",\"thread\":\"review\",\"ref\":\"pr#<n>\",\"classification\":\"clarification-required\",\"note\":\"one operator action: <text>\"}",
                ReviewCompletedExample =
                    "{\"status\":\"completed\",\"thread\":\"review\",\"ref\":\"pr#<n>\",\"note\":\"approved; closeout done\","
                    + "\"design_alignment_checked\":true,\"design_alignment_sources_checked\":[\"packet\","
                    + "\"review-context\",\"intent-tree\",\"adr-decision-notes\",\"relevant-docs\"],"
                    + "\"managed_worktree_policy\":\"compliant — .intent-cli/worktrees/review-<unit>, removed after review\"}",
                CloseoutKnowledgeWriteBackRule =
                    "G564 — closeout reports NAME the packet's declared knowledge write-backs: for every "
                    + "`knowledge_updates.*.required: true` facet and `closeout_learning.write_back_required: true`, "
                    + "the report to the design thread lists the facet, its declared target paths, and whether it is "
                    + "`recorded` (with the host commit) or `pending`. A closed-out unit whose declared write-back has "
                    + "no record is an aging `knowledge-writeback-pending` item in `automation stalled-work` / "
                    + "`automation heartbeat`, cleared only by `intent-cli automation knowledge-writeback-record "
                    + "--execution-unit <unit> --commit <host-sha> --write`. This is read-only propagation of packet "
                    + "metadata: the orchestrator reports the obligation, design performs the write-back, and no thread "
                    + "here mutates host intent content.",
                ReviewIncompleteRule =
                    "A review `completed` reply that omits `design_alignment_checked: true` and the checked-source list "
                    + "is INCOMPLETE — the orchestrator does not route merge/closeout on that reply alone. The only "
                    + "exception is when an authoritative PRIOR approval state already proves equivalent design-alignment "
                    + "review (the orchestrator must point to that specific prior evidence, not assume equivalence).",
            },
            OrchestratorFirstWake = new[]
            {
                "Confirm you are the ONLY orchestrator for this domain/repo; if a second is detected, STOP and escalate (fail closed).",
                Apply("Confirm domain scope: in single-domain mode, treat other-domain items visible in the host repo as OUT OF SCOPE (escalate, never delegate); in multi-domain mode, attach full routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree, base branch policy, destination thread) before each delegation. Visibility is not authorization, and an execution-unit prefix mismatch alone is not a wrong-repo signal."),
                "Read pending agmsg replies from the implementation/review threads (signals only — do not trust them as state).",
                Apply("Ask intent-cli for the real state: `intent-cli intent status --domain <domain> --format json` and `intent-cli worker next-action --repo <owner/repo> --github-only --format json`."),
                "Verify every GitHub fact an agmsg reply claims (PR merged, CI concluded, labels) before acting on it.",
                "The per-wake cap is AT MOST ONE DELEGATION PER RECEIVER, not at-most-one-message overall (G524): a publish this wake must be delegated to implementation in this SAME wake — never defer that delegation to an unscheduled next wake — alongside any repair requests (one per stalled receiver) or one operator escalation.",
                "Before sending any agmsg message, verify the recipient id against the team roster (`agmsg team.sh`); treat an id not on the roster as an error, never a guess (G524).",
                "Do not launch implement/review recurring timers for this domain/repo while orchestrating.",
                Apply("End this wake with the stalled-work check (G523): `intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --format json`, and process every actionable item before sleeping — never leave one for an unscheduled next wake; escalate explicitly if it is genuinely blocked on an operator decision."),
            },
            SafetyBoundaries = new[]
            {
                "agmsg is a message/progress/completion signal layer only; intent-cli and GitHub are authoritative for all workflow state.",
                "No raw label mutation (`gh ... --add-label`/`--remove-label`); every label transition goes through intent-cli worker/automation.",
                "No hand-editing queue-state, runs.jsonl, packets, or any host metadata (`.intent-cli/**`, `intents/**`).",
                "agmsg never replaces semantic review or authorizes a merge; review/closeout decisions run through intent-cli review surfaces (G480).",
                "Per-wake cap is AT MOST ONE DELEGATION PER RECEIVER (implementation, review) — NOT at-most-one-message: a publish's same-wake delegation, repair messages, an escalation, and receiver-report handling may all happen in one wake (G524); never defer a publish's delegation to an unscheduled future wake.",
                "Verify the recipient id against the team roster (`agmsg team.sh`) before every send; an id not on the roster is an error, not a guess (G524).",
                "End every wake with a stalled-work check (`automation stalled-work`, G523) and process any actionable item before sleeping; escalate explicitly rather than deferring silently.",
                "Domain isolation: a host repo can hold several domains and one repo can serve several domains, so visibility is not authorization. Single-domain orchestrators ignore/escalate other-domain items; multi-domain orchestrators require explicit per-delegation routing. An execution-unit prefix mismatch alone is not a wrong-repo signal.",
                "Fail closed on duplicate orchestrators for the same domain/repo, or when an agmsg reply conflicts with intent-cli/GitHub facts — STOP and escalate, never guess.",
                "Allocate temporary worktrees under an allowlisted managed root and remove them with `git worktree remove`; never raw `rm -rf` of arbitrary temp paths, and `approval_policy=never`/`danger-full-access` is not a substitute for safe cleanup.",
                "Never ask intent-cli to launch Claude/Codex/Copilot or any AI provider; intent-cli only emits text the human agent acts on.",
            },
            DetailedGuideCommands = new[]
            {
                Apply("intent-cli guide prompt-matrix --mode child-loop --target-repo <owner/repo> --agent <agent> --format markdown"),
                Apply("intent-cli guide prompt-matrix --mode host-loop --domain <domain> --target-repo <owner/repo> --agent <agent> --format markdown"),
                Apply("intent-cli automation summary --domain <domain> --format json"),
            },
        };
    }

    // G549: the provisioning section a design thread executes BEFORE the setup
    // checklist — it creates what the rest of the guide already assumes exists.
    // Only the target repo and the agmsg team come from CLI inputs; the project
    // name and the host metadata repo stay as placeholders, because a design
    // thread asked to "set this team up" knows them from its own context and
    // intent-cli never needs them for any mutation.
    private static OrchestratorTerminalWorkspaceProvisioning BuildTerminalWorkspaceProvisioning(string targetRepo, string team)
    {
        string Fill(string template) => template
            .Replace("<owner/repo>", targetRepo, StringComparison.Ordinal)
            .Replace("<team>", team, StringComparison.Ordinal);

        return new OrchestratorTerminalWorkspaceProvisioning
        {
            Summary = Fill(
                "Provision the whole team on a terminal-workspace manager BEFORE running the setup checklist. The rest "
                + "of this guide assumes each role already has its own folder and its own live terminal session; this "
                + "section creates both. Work through it top to bottom — role folders (create them when absent), "
                + "workspace topology, launch rules, role initialization, exclusivity/handover — then continue at "
                + "`Setup (starting orchestrator mode)`. Every step below is executable with the placeholders listed "
                + "next; nothing here requires knowledge outside this page."),
            Placeholders = new[]
            {
                new OrchestratorProvisioningPlaceholder { Token = "<Project>", Meaning = "the project/product name used as the folder-name prefix (e.g. `Estivo` → `EstivoOrchestrator`)." },
                new OrchestratorProvisioningPlaceholder { Token = "<owner/host-repo>", Meaning = "the HOST metadata repo that owns `.intent-cli/` and `intents/<domain>/` — cloned for the host-side roles (orchestrator, review)." },
                new OrchestratorProvisioningPlaceholder { Token = Fill("<owner/repo>"), Meaning = "the TARGET repo the work lands in — cloned for the implementation role." },
                new OrchestratorProvisioningPlaceholder { Token = Fill("<team>"), Meaning = "the agmsg team name; also the workspace tab name." },
                new OrchestratorProvisioningPlaceholder { Token = "<workspace-root>", Meaning = "the parent directory the role folders are created under (e.g. `~/dev`)." },
            },
            FolderProvisioning = new OrchestratorProvisioningFolders
            {
                Summary = Fill(
                    "Each role runs from its OWN dedicated folder. Host-side roles (orchestrator, review) run from a "
                    + "clone of the host metadata repo `<owner/host-repo>`; the implementation role runs from a clone of "
                    + "the target repo `<owner/repo>`. Create any folder that does not exist yet — do not reuse an "
                    + "existing checkout that another role already occupies, and do not point two roles at one folder."),
                NeverShareRule =
                    "NEVER share a folder between two roles. agmsg identity and the codex monitor bridge are "
                    + "(project, type)-scoped (G521): two roles of the same agent type in one folder resolve to the SAME "
                    + "identity, so actas exclusivity, delivery, and the bridge collide and one role silently stops "
                    + "receiving. One role = one folder, always.",
                Roles = new[]
                {
                    new OrchestratorProvisioningRoleFolder
                    {
                        Role = "orchestrator",
                        Folder = "<workspace-root>/<Project>Orchestrator",
                        CloneSource = "host metadata repo `<owner/host-repo>` (owns `.intent-cli/` and `intents/<domain>/`)",
                        CreateCommand = "git clone https://github.com/<owner/host-repo>.git <workspace-root>/<Project>Orchestrator",
                    },
                    new OrchestratorProvisioningRoleFolder
                    {
                        Role = "review",
                        Folder = "<workspace-root>/<Project>Review",
                        CloneSource = "host metadata repo `<owner/host-repo>` — a SECOND, separate clone (not the orchestrator's)",
                        CreateCommand = "git clone https://github.com/<owner/host-repo>.git <workspace-root>/<Project>Review",
                    },
                    new OrchestratorProvisioningRoleFolder
                    {
                        Role = "implementation",
                        Folder = "<workspace-root>/<Project>Implementation",
                        CloneSource = Fill("target repo `<owner/repo>` — implementation is GitHub-contract-only and never reads host `.intent-cli/` state"),
                        CreateCommand = Fill("git clone https://github.com/<owner/repo>.git <workspace-root>/<Project>Implementation"),
                    },
                },
                AbsentFolderRule =
                    "When a folder is absent, CREATE it with the clone command for that role before launching anything "
                    + "in that pane — a pane opened at a missing cwd falls back to the shell's default directory, which "
                    + "silently gives the role the wrong (or a shared) identity.",
                Verification = new[]
                {
                    "Each of the three folders exists and is a distinct path — no two roles share one.",
                    "`git -C <folder> remote get-url origin` matches the intended source for that role (host metadata repo for orchestrator/review, target repo for implementation).",
                    "`git -C <folder> status` is clean in every folder before any role is launched.",
                },
            },
            Topology = new OrchestratorProvisioningTopology
            {
                Summary = Fill(
                    "One workspace per team, one tab named after the team, one pane per role, each pane opened with that "
                    + "role's own folder as its cwd. The topology is what makes the folder rule hold in practice: the "
                    + "pane carries the cwd, and the cwd carries the identity."),
                Rules = new[]
                {
                    Fill("One WORKSPACE per agmsg team — the team `<team>` gets its own workspace, not a pane inside someone else's."),
                    Fill("One TAB named after the team (`<team>`) so the team is identifiable at a glance."),
                    "One PANE per role — orchestrator, implementation, review (plus design only if design is a monitored receiver in this team).",
                    "Each pane's cwd is that role's dedicated folder — set the cwd at pane creation, do not `cd` after launching the agent.",
                    "Panes are long-lived: a role's session lives for the whole run, so do not recycle a pane for a second role.",
                },
                DesignThreadPosition =
                    "The DESIGN thread stays OUTSIDE the workspace. It is the thread doing the provisioning — it drives "
                    + "the workspace manager from where it already runs and never claims a pane inside the team "
                    + "workspace it is constructing.",
            },
            LaunchRules = new OrchestratorProvisioningLaunchRules
            {
                Summary =
                    "How an agent is launched decides whether its message bridge arms and whether its first run stalls "
                    + "on an unattended prompt. Launch every agent by TYPING into the pane's interactive shell, then "
                    + "attend the first-run screens.",
                CodexShimRule =
                    "codex MUST be launched by typing into the pane's interactive shell (send-text + enter), never by "
                    + "spawning the executable. The `codex()` shell shim is what wraps the launch and arms the agmsg "
                    + "monitor bridge (G521); typing goes through the shell, so the shim applies.",
                CodexDirectSpawnWarning =
                    "a workspace manager that exec's the canonical `codex` executable directly BYPASSES the "
                    + "shim, so the bridge never arms. The session looks healthy and messages are simply never "
                    + "delivered. If a manager offers a \"run this command\" pane option, do NOT use it for codex; use "
                    + "the send-text-into-the-shell path.",
                ClaudePermissionModeRule =
                    "claude is launched with the permission mode the OPERATOR chose for this team — the design thread "
                    + "does not silently pick a broader mode. Type the launch into the pane's shell the same way, so "
                    + "the session inherits the folder as cwd.",
                AttendedFirstRunRule =
                    "ATTEND the first run of every pane. First-run trust screens (codex hooks-trust) and permission "
                    + "prompts block the session until answered, and an unattended pane looks \"launched\" while it is "
                    + "actually waiting. Where the design thread is authorized to answer (see the authority boundary "
                    + "below), answer so the result is a DURABLE allowlist/trust record — a per-invocation approval "
                    + "re-prompts on the next wake and stalls the role again.",
                AuthorityBoundary =
                    "attending a pane is not authority to decide for the operator. The design "
                    + "thread may act ONLY on pane contents it has actually READ (never on a blind keystroke into a "
                    + "dialog it has not rendered). After that verified read, authorization extends ONLY to the four "
                    + "MAY-answer classes the supervision section grants — " + SupervisionMayAnswerClasses.InlineList
                    + " — and to nothing else. "
                    + "CREDENTIAL, SECURITY, and PERMISSION prompts are NEVER answerable by the design thread: they "
                    + "ALWAYS remain unanswered and are ALWAYS ESCALATED to the operator, with or without prior "
                    + "authorization — no authorization makes them answerable. Unsticking a pane is not deciding for "
                    + "the operator: if answering the dialog would grant access, widen a permission mode, or accept a "
                    + "security warning, it is the operator's call, not the design thread's.",
            },
            RoleInitialization = new OrchestratorProvisioningRoleInitialization
            {
                Summary = Fill(
                    "Once a pane's agent is running, give it its agmsg role, wait for readiness, and only then ping-test "
                    + "it. Use the actas form that matches the CLI in that pane; `<role>` is `orchestrator`, "
                    + "`implementation`, or `review`, joined under team `<team>`."),
                ActasForms = new[]
                {
                    new OrchestratorRoleStartup
                    {
                        AgentType = "claude",
                        ActasInvocation = "/agmsg actas <role>",
                        Note = "Type the slash-command form into the claude pane; it claims the role's exclusivity lock and re-points that session's inbox subscription at the role.",
                    },
                    new OrchestratorRoleStartup
                    {
                        AgentType = "codex",
                        ActasInvocation = "$agmsg actas <role>",
                        Note = "Type the `$agmsg actas <role>` form into the codex pane; the monitor bridge only attaches if the session was launched through the shim.",
                    },
                },
                ReadinessWait =
                    "WAIT for the role to report ready before sending anything — actas is submitted, not completed, at "
                    + "the moment you type it. Readiness has THREE separate layers and they must not be collapsed: "
                    + "delivery CONFIGURATION, LIVE ATTACHMENT, and END-TO-END delivery. A pane still sitting on a trust "
                    + "screen is NOT live-attached and NOT session-active — and that says nothing about its delivery "
                    + "configuration, which is set with `delivery.sh` before launch and stays configured regardless of "
                    + "what the launch UI is showing. Launch-UI state never erases configuration, and configuration "
                    + "never implies attachment.",
                ConfigurationProof =
                    "CONFIGURATION only — `delivery.sh status` reporting a delivery mode (e.g. `mode=monitor`) proves "
                    + "the role is registered and how it is CONFIGURED to receive. It does NOT prove a watcher is alive, "
                    + "and it does NOT prove any session is attached: a receiver can report `mode=monitor` while nothing "
                    + "is streaming. Never treat a delivery mode as readiness.",
                LiveAttachmentEvidence = new[]
                {
                    new OrchestratorProvisioningLiveEvidence
                    {
                        AgentType = "claude",
                        Evidence =
                            "Live attachment is proven by the Claude Code MONITOR markers in that receiver's own "
                            + "session, not by `delivery.sh status`: the transcript shows `Monitor(agmsg inbox stream)`, "
                            + "the footer shows `1 monitor` (NOT `1 shell` — a background `watch.sh` shell is "
                            + "diagnostic/fallback only and never counts as attached), and `Monitor event` lines appear "
                            + "as messages arrive. See `Monitor tool vs delivery-mode` for the full marker list and the "
                            + "trust/fallback ladder when they are missing.",
                    },
                    new OrchestratorProvisioningLiveEvidence
                    {
                        AgentType = "codex",
                        Evidence =
                            "Live attachment is proven by the BRIDGE-ALIVE marker where the codex bridge applies: "
                            + "`delivery.sh status` shows `Codex bridge: <team>/<role> alive (pid N)`. Note the bridge "
                            + "arms on the FIRST turn sent to the session, not at codex startup, and an already-running "
                            + "session stays unmonitored until it is restarted — so absence of the marker right after "
                            + "launch is expected, and its presence is what you wait for. See `Codex monitor (beta) "
                            + "failure modes` for the caveats.",
                    },
                },
                EndToEndProof =
                    "PING/ACK is the ONLY end-to-end proof. Configuration and live-attachment evidence are necessary "
                    + "preconditions, never a substitute: a role counts as ready only after it has ACKED a ping. If the "
                    + "live markers are unavailable for an agent surface, do not infer readiness — fall back explicitly "
                    + "(e.g. `turn` delivery or manual `inbox.sh`), say so, and still require the ack.",
                PingTestReference =
                    "After readiness, run the existing ping test before ANY delegation: send one agmsg message to each "
                    + "role and require the ack (see `Receiver readiness` / `ping_test`). Readiness is a precondition "
                    + "for the ping test, not a replacement for it.",
                VerifiedLiveness = new OrchestratorVerifiedLiveness
                {
                    Summary =
                        "Provisioning concludes on VERIFIED LIVENESS, not on a message. A role is provisioned when its "
                        + "startup report has arrived AND, after a settle delay, all three checks below still pass. "
                        + "Until then the pane is a candidate, not a receiver.",
                    ReportIsNotReadiness =
                        "the report says the agent reached the point of sending a "
                        + "message; it says nothing about whether the agent is still running now. Field incident "
                        + "(2026-07-29): two codex agents reported startup-complete and died SECONDS later when their "
                        + "shared app-server was lost — and the supervising thread went on \"waiting for startup "
                        + "reports\" while every agent was already dead. Never conclude provisioning on the report "
                        + "alone.",
                    SettleDelay =
                        "wait after the report before verifying — long enough for an early death to "
                        + "have happened, since the failure this catches occurs seconds after the report, not at the "
                        + "moment of it. Verifying instantly re-observes the same moment the report described and "
                        + "proves nothing new.",
                    PostReportChecks = new[]
                    {
                        new OrchestratorLivenessCheck
                        {
                            Check = "the pane still hosts the agent TUI",
                            HowToVerify =
                                "READ the pane. The agent's own interface must be there — a shell prompt means the "
                                + "agent exited, however recently it reported. The pane is ground truth; a message is "
                                + "a claim about the past.",
                        },
                        new OrchestratorLivenessCheck
                        {
                            Check = "an agmsg ping-pong round trip succeeds",
                            HowToVerify =
                                "send a ping NOW and require the pong NOW. A round trip completed after the settle "
                                + "delay is the only evidence that the receiver is alive at THIS moment; the earlier "
                                + "readiness ack proves only that it was alive then.",
                        },
                        new OrchestratorLivenessCheck
                        {
                            Check = "codex: the bridge is armed and the app-server attachment is stable",
                            HowToVerify =
                                "confirm the bridge-alive marker AND that the app-server attachment has not dropped "
                                + "since the report — a codex TUI attaches to a per-folder app-server over a "
                                + "`--remote` websocket, so the attachment is a separate thing that can die while the "
                                + "pane and the bridge both looked fine a moment ago.",
                        },
                    },
                    EarlyDeathIsNormal = new OrchestratorEarlyDeathMode
                    {
                        Summary =
                            "an agent can die within "
                            + "seconds of reporting, and the provisioning flow is expected to detect it rather than "
                            + "assume it away — this is a normal mode, not an anomaly to be surprised by.",
                        TransportResetSignature =
                            "Signature: the TUI EXITS TO A SHELL PROMPT, typically leaving a resume hint on screen, "
                            + "after a websocket TRANSPORT RESET dropped its app-server connection. The pane looks "
                            + "like an ordinary terminal — which is exactly why a scan that only looks for dialogs "
                            + "misses it.",
                        RecheckObligation =
                            "re-check; do not wait for another report. A dead agent sends nothing, so waiting for a "
                            + "further message is waiting forever. When a check fails, treat the role as not "
                            + "provisioned and recover (see the `agent-absent` stuck state) — then run the full "
                            + "verified-liveness sequence again from the start.",
                    },
                    SharedAppServerDeathMode = new OrchestratorSharedAppServerDeath
                    {
                        Summary =
                            "Codex TUIs attach to PER-FOLDER app-servers over `--remote` websockets, and an "
                            + "app-server is shared by every TUI attached to it.",
                        BlastRadius =
                            "KILLING AN APP-SERVER TAKES DOWN EVERY ATTACHED TUI at once — including agents that "
                            + "belong to other teams and had nothing to do with whatever prompted the kill. The "
                            + "2026-07-29 double death was exactly this: a lost app-server, two dead agents, neither "
                            + "of which was the intended target of anything.",
                        PreventionReference =
                            "Prevention is the cross-project attribution rule: verify the process's own cwd before "
                            + "stopping any app-server, and never act on a process you cannot attribute (see "
                            + "`Cross-project isolation on a shared machine`). This death mode is the second-order "
                            + "cost of an attribution violation — the victim is not the process you killed, it is "
                            + "every agent that was attached to it.",
                    },
                },
            },
            ExclusivityHandover = new OrchestratorProvisioningHandover
            {
                OneHolderRule =
                    "Exactly ONE live session may hold a role at a time. A second session trying to actas the same role "
                    + "is refused (the role is reported held by the owning session) — that refusal is correct behavior, "
                    + "not a bug to work around by joining under a near-miss name.",
                GracefulDropRule =
                    "Replacing a role's session goes through the GRACEFUL DROP first: the current holder drops the role "
                    + "(releasing the exclusivity lock and its registration), and only then does the successor actas it. "
                    + "Never kill the holder's pane and hope the lock clears.",
                OperatorConfirmationRule =
                    "The graceful drop carries an OPERATOR CONFIRMATION — the human running the outgoing session "
                    + "confirms the handover. The design thread requests the drop and waits for that confirmation; it "
                    + "does not force a role away from a live session.",
                SuccessorClaimRule =
                    "The successor claims the role only AFTER the drop is confirmed, then repeats readiness + ping test "
                    + "for that role. A handover is not complete until the new holder has acked a ping.",
            },
            ReferenceManager = new OrchestratorProvisioningReferenceManager
            {
                Name = "herdr",
                Summary =
                    "herdr is the REFERENCE workspace manager for this flow — the surfaces below are the ones a design "
                    + "thread drives programmatically to build the team. intent-cli does not own, ship, or wrap herdr; "
                    + "it states the operational steps and the success criteria only.",
                Surfaces = new[]
                {
                    new OrchestratorProvisioningSurface { Surface = "`workspace create`", UsedFor = "create the team's workspace and its team-named tab." },
                    new OrchestratorProvisioningSurface { Surface = "`pane split`", UsedFor = "add one pane per role, each opened with that role's dedicated folder as cwd." },
                    new OrchestratorProvisioningSurface { Surface = "`pane send-text` / `send-keys`", UsedFor = "type the agent launch and the actas prompt into the pane's interactive shell — this is the shim-safe path." },
                    new OrchestratorProvisioningSurface { Surface = "`agent prompt`", UsedFor = "deliver a prompt to an agent already running in a pane." },
                    new OrchestratorProvisioningSurface { Surface = "`agent wait`", UsedFor = "block until the pane's agent is idle/ready before the next step." },
                },
                InternalsLinkOut =
                    "For herdr internals — installation, socket/API details, exact flags — consult herdr's own "
                    + "documentation. This guide deliberately links out rather than restating them, exactly as it does "
                    + "for agmsg internals.",
                SubstitutionRule =
                    "ANY equivalent workspace manager may be substituted, provided the same rules hold: one dedicated "
                    + "folder per role as the pane cwd, launch typed into an interactive shell (shim-safe), attended "
                    + "first-run trust/permission prompts, actas + readiness before the ping test, and one holder per "
                    + "role with a graceful drop on handover.",
            },
            Checklist = new[]
            {
                Fill("Collect the placeholders: `<Project>`, host metadata repo `<owner/host-repo>`, target repo `<owner/repo>`, agmsg team `<team>`, `<workspace-root>`."),
                "For each role, check whether its dedicated folder exists; CREATE any missing one with that role's clone command (host metadata repo for orchestrator/review, target repo for implementation).",
                "Verify the three folders are distinct, have the expected `origin`, and are clean.",
                Fill("Create one workspace for team `<team>` with a tab named after the team; keep the design thread outside it."),
                "Split one pane per role, each opened with that role's folder as cwd.",
                "Launch each pane's agent by TYPING into its interactive shell — codex through the shim, claude with the operator's chosen permission mode; never spawn the executable directly.",
                "Attend the first run of each pane: answer ONLY the read-pane trust/allowlist dialogs the operator explicitly authorized (durably, not per-invocation). ALWAYS leave every credential, security, and permission prompt unanswered and escalate it to the operator — no authorization makes those answerable by the design thread.",
                "Type the actas form into each pane (`/agmsg actas <role>` for claude, `$agmsg actas <role>` for codex), then confirm readiness LAYER BY LAYER: delivery configuration (`delivery.sh status`), then the agent-specific live-attachment marker (claude: `Monitor(agmsg inbox stream)` / footer `1 monitor`; codex: `Codex bridge: <team>/<role> alive (pid N)`).",
                "Ping-test every role and require an ack before the first real delegation — the ack is the ONLY end-to-end proof; configuration and live markers are preconditions, not readiness.",
                "Continue with `Setup (starting orchestrator mode)` — the delivery mode, role prompts, read-only first wake, and the rest of the setup checklist.",
                "On a role handover, drop the role from the current holder (with its operator confirmation) BEFORE the successor claims it, then redo readiness + ping test.",
            },
        };
    }

    // G550: the design thread's supervision half. G549 builds the team; this
    // keeps it moving. Every rule here is session-layer only — the workflow
    // transitions stay with the orchestrator and the canonical commands, and
    // the dialog lists are deliberately closed sets rather than judgment calls.
    private static OrchestratorDesignWorkspaceSupervision BuildDesignWorkspaceSupervision(string domain, string targetRepo)
    {
        string Fill(string template) => template
            .Replace("<domain>", domain, StringComparison.Ordinal)
            .Replace("<owner/repo>", targetRepo, StringComparison.Ordinal);

        return new OrchestratorDesignWorkspaceSupervision
        {
            Summary = Fill(
                "Under authority the OPERATOR granted it, the design thread drives the team's SESSION LAYER through "
                + "the workspace manager: it provisions the team (see `Terminal-workspace provisioning`), keeps the "
                + "sessions alive and correctly held, and supervises for stalls. It answers a blocking dialog only "
                + "inside an explicit boundary and only after READING that dialog from the pane; everything outside "
                + "the boundary escalates to the operator. This adds a session-layer role — it moves NO workflow "
                + "authority."),
            GrantedAuthority = new OrchestratorSupervisionAuthority
            {
                Summary =
                    "Two layers, two owners. The SESSION layer (panes, processes, holds, blocking dialogs) is what "
                    + "the operator grants the design thread. The WORKFLOW layer (labels, queue-state, publication, "
                    + "delegation, closeout) is not granted and never moves — it stays with intent-cli, GitHub, and "
                    + "the orchestrator exactly as before.",
                OperatorGrantRule =
                    "the design thread supervises the session layer because the operator asked it to, and the grant's scope is what the operator stated. Outside a grant "
                    + "the design thread observes and reports rather than acts. A grant to supervise sessions is "
                    + "never read as a grant to decide workflow, product, or security questions.",
                DesignOperatesSessionLayer = new[]
                {
                    "PROVISIONING — build the team's workspace, folders, panes, launches, and role initialization per `Terminal-workspace provisioning` (G549); supervision references that section rather than repeating it.",
                    "SESSION LIFECYCLE — investigate an unresponsive session and, when it must be replaced, do so through the graceful drop that honors one-holder exclusivity.",
                    "STALL SUPERVISION — run the three supervision layers below so a stall is noticed by a layer that is actually running, not by luck.",
                    "BLOCKING DIALOGS — answer only what the MAY list allows, only after the verified read; escalate everything else.",
                },
                WorkflowStateOwnershipUnchanged =
                    "workflow state ownership does not move. Labels, queue-state, publication, delegation, CI/review gating, and closeout remain with intent-cli, GitHub, and the "
                    + "orchestrator; the design↔orchestrator double-check rule and the orchestrator's ownership of "
                    + "workflow transitions apply exactly as before. Supervising a session never authorizes a "
                    + "workflow transition, and a stuck pane is never a reason to move a label by hand.",
            },
            SessionLifecycle = new OrchestratorSupervisionSessionLifecycle
            {
                Summary =
                    "A session that stops responding is a session-layer fault, and the design thread may repair it — "
                    + "but repair means restoring a correctly held, live session, not taking over the role's work or "
                    + "its decisions.",
                UnresponsiveSessionInvestigation = new[]
                {
                    "READ the pane first — an \"unresponsive\" session is most often blocked on a dialog, a trust screen, or a prompt waiting for input, not dead. Diagnose from what the pane actually shows.",
                    "Distinguish the layers: a live session that is merely not attached to delivery is a delivery problem (re-check the readiness layers), not a reason to replace the session.",
                    "Confirm the role is still held by that session before concluding anything — a role silently dropped elsewhere looks identical to a dead session from the outside.",
                    "Prefer the least invasive repair that restores liveness: answer an in-boundary dialog, re-arm delivery, or restart the session — replacement is the last step, not the first.",
                },
                ExclusivityRule =
                    "replacing a session never means two sessions holding the same role for even a moment. The successor claims only after the incumbent's hold is released; a refused "
                    + "actas is the exclusivity rule working, not an obstacle to route around.",
                GracefulDropRule =
                    "Replace through the GRACEFUL DROP: the incumbent drops the role (releasing its exclusivity lock "
                    + "and registration), then the successor claims it and re-runs readiness plus the ping test. "
                    + "Never kill a pane and assume the hold cleared, and never force a role away from a live session.",
                OperatorVisibleConfirmation =
                    "The drop's confirmation is OPERATOR-VISIBLE: the handover surfaces to the operator rather than "
                    + "happening silently inside the design thread. The design thread may request and sequence the "
                    + "handover; the decision to retire a live session remains the operator's, and the confirmation "
                    + "is what records it.",
            },
            SupervisionLayers = new[]
            {
                new OrchestratorSupervisionLayer
                {
                    Layer = "real-time message monitor",
                    Purpose =
                        "Catch inbound agmsg traffic as it arrives — replies, blockers, and escalations that should "
                        + "wake the design thread immediately.",
                    Cadence = "continuous / real-time (a live attached inbox stream, not a poll).",
                    Note =
                        "This layer is what the message-driven steady state assumes. It sees only what is SENT — it "
                        + "cannot notice a session that went quiet or a pane blocked on a dialog, which is why the "
                        + "other two layers exist.",
                },
                new OrchestratorSupervisionLayer
                {
                    Layer = "blocking-UI pane scan",
                    Purpose =
                        "Notice panes that are stuck with nothing to say. TWO EQUAL stuck states: a pane blocked on "
                        + "an approval, selection, or trust prompt, AND a pane showing a shell prompt where an agent "
                        + "should be (`agent-absent`, G556). Both produce no message at all — a blocked agent is "
                        + "waiting and a dead one cannot speak — so no message-driven layer can ever detect either.",
                    Cadence =
                        "sub-minute class (e.g. every few tens of seconds) — a blocking dialog stalls a role for its "
                        + "entire lifetime, and an agent that died seconds after reporting stays dead until someone "
                        + "looks, so this layer is the fast one.",
                    Note =
                        "Scanning is READING, and what the scan finds routes by STATE, not by one rule for "
                        + "everything. A blocking dialog goes to the dialog rules below — answer only what the MAY "
                        + "list covers after the verified read, and escalate the rest. An `agent-absent` shell "
                        + "prompt is NOT a dialog and must never be routed through dialog handling: it goes to the "
                        + "shim-safe relaunch recovery (recreating the app-server when that is what died), followed "
                        + "by the COMPLETE verified-liveness re-check — report, settle delay, all three checks. See "
                        + "`What the pane scan is looking for` for both recoveries.",
                },
                new OrchestratorSupervisionLayer
                {
                    Layer = "periodic state watchdog",
                    Purpose = Fill(
                        "Compare canonical intent-cli/GitHub state against expected progress and nudge the "
                        + "orchestrator when work has gone stale — the existing design-thread watchdog "
                        + "(`intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json`)."),
                    Cadence =
                        "tens-of-minutes class (e.g. every 30 minutes) — quiet enough to stay out of the way, "
                        + "frequent enough to bound a stall.",
                    Note =
                        "This is the existing watchdog, not a second one: its safety rules apply verbatim (see the "
                        + "watchdog safety-rules reference below). One canonical nudge per wake, never a batch.",
                },
            },
            PaneScanStuckStates = new[]
            {
                new OrchestratorPaneStuckState
                {
                    State = "blocking dialog",
                    WhatTheScanSees = "an approval, selection, or trust prompt waiting for input.",
                    Recovery =
                        "handle it under the dialog rules — answer only what the MAY list covers after the verified "
                        + "read, escalate the rest.",
                },
                new OrchestratorPaneStuckState
                {
                    State = "agent-absent",
                    WhatTheScanSees =
                        "a SHELL PROMPT where an agent should be — the pane looks like an ordinary terminal, often "
                        + "with a resume hint left on screen. The agent exited; it may have reported startup "
                        + "successfully seconds earlier.",
                    Recovery =
                        "RELAUNCH THROUGH THE SHIM: type the launch into the pane's interactive shell (never spawn "
                        + "the executable), recreating the app-server first when it is the thing that died. Set the "
                        + "permission mode with the LAUNCH FLAG (e.g. `--permission-mode`) rather than trying to "
                        + "switch it afterwards: a workspace manager's synthetic key injection cannot be relied on "
                        + "for mode switching — plain keys are delivered, but modifier chords such as shift+tab are "
                        + "not delivered faithfully (observed across multiple teams). Then run the FULL "
                        + "verified-liveness sequence again — report, settle delay, all three checks.",
                },
            },
            RearmRule =
                "supervision schedulers are session-scoped: a `/loop`, an automation, or an "
                + "attached monitor dies with the design session that hosts it, and nothing announces that it stopped. "
                + "Every supervision layer must either survive a design-session restart or be RE-ARMED as the first "
                + "act of the new session — treat re-arming as part of starting the session, not as an optional "
                + "follow-up. Field cost of forgetting: a claim-now lost inside a session-restart window left a "
                + "published issue stalled for 5.5 HOURS because no supervision layer happened to be running.",
            VerifiedReadRule =
                "the design thread may answer a dialog ONLY after it has actually read "
                + "that dialog's content from the pane and can state what it is approving. A blind keystroke into a "
                + "dialog it has not rendered is prohibited, no matter how routine the prompt looks or how confident "
                + "it is about which key clears it. If the content cannot be read or cannot be verified, the dialog "
                + "is an escalation, not an answer.",
            MayAnswer = new[]
            {
                new OrchestratorSupervisionMayAnswer
                {
                    Dialog = SupervisionMayAnswerClasses.RequestedConfirmations,
                    Verification =
                        "the read pane's prompt must match an action THIS design thread just initiated — same target, "
                        + "same operation. A confirmation it cannot trace to its own request is not its to answer.",
                },
                new OrchestratorSupervisionMayAnswer
                {
                    Dialog = SupervisionMayAnswerClasses.VerifiedReadOnlyCommandApprovals,
                    Verification =
                        "the exact command shown in the pane must be read and verified to be READ-ONLY. Anything that "
                        + "writes, deletes, installs, publishes, or mutates state fails this check and escalates — "
                        + "\"probably read-only\" is not verified.",
                },
                new OrchestratorSupervisionMayAnswer
                {
                    Dialog = SupervisionMayAnswerClasses.OwnHookTrustScreens,
                    Verification =
                        "the trust screen must name a hook THIS design thread installed as part of this provisioning "
                        + "(its own hook-trust case). A trust screen for anything it did not install is not its to "
                        + "accept.",
                },
                new OrchestratorSupervisionMayAnswer
                {
                    Dialog = SupervisionMayAnswerClasses.PreauthorizedModeChanges,
                    Verification =
                        "the operator must have PREAUTHORIZED this specific mode change, and the read pane must show "
                        + "that same change. Preauthorization is specific and prior — it is never inferred from a "
                        + "general grant to supervise sessions.",
                },
            },
            MustEscalate = new[]
            {
                new OrchestratorSupervisionMustEscalate
                {
                    Category = "unreadable or unverifiable dialogs",
                    Reason =
                        "if the pane content cannot be read, or the claim it makes cannot be verified, there is "
                        + "nothing to base an answer on — answering would be guessing on the operator's behalf.",
                },
                new OrchestratorSupervisionMustEscalate
                {
                    Category = "destructive or irreversible approvals",
                    Reason =
                        "deletions, force operations, overwrites, and anything else that cannot be undone are the "
                        + "operator's call — the cost of a wrong answer is unbounded and unrecoverable.",
                },
                new OrchestratorSupervisionMustEscalate
                {
                    Category = "choices that embed a product or design decision",
                    Reason =
                        "a dialog that picks behavior, scope, or defaults is design content, and design content goes "
                        + "through the operator and the design↔orchestrator double-check — not through whoever "
                        + "happens to be unblocking a pane.",
                },
                new OrchestratorSupervisionMustEscalate
                {
                    Category = "credential, security, and permission waits",
                    Reason =
                        "these are NEVER answerable by the design thread, with or without prior authorization: they "
                        + "always remain unanswered and always escalate to the operator. No grant makes them "
                        + "answerable.",
                },
            },
            BoundarySentence =
                "UNSTICKING A SESSION IS NOT DECIDING FOR IT. The design thread's job is to keep the session layer "
                + "alive so the role can do its own work — not to make the role's choices, and not to make the "
                + "operator's.",
            ProvisioningReference =
                "Provisioning is NOT repeated here — see `Terminal-workspace provisioning` for role folders, "
                + "workspace topology, shim-safe launch, actas/readiness, and the exclusivity/handover rules this "
                + "section supervises.",
            WatchdogSafetyRulesReference =
                "The watchdog safety rules apply to ALL supervision verbatim: no duplicate delegation, no clearing a "
                + "permission prompt, no cancelling or resetting in-flight work, no force-closing an issue/PR, and no "
                + "speculative durable-state surgery (no hand-edited labels, queue-state, or host metadata). See "
                + "`Design-thread watchdog (recommended safety net)`.",
        };
    }

    // G555: the cross-project isolation rules. This is the shared-machine
    // reality G549/G550 left implicit: every substrate is shared, so a
    // supervising thread that acts on an object it did not attribute is one
    // careless keystroke away from another team's outage. The rules narrow
    // WHICH objects may be acted on, never WHICH actions are allowed — G550's
    // authority boundary is untouched.
    private static OrchestratorCrossProjectIsolation BuildCrossProjectIsolation()
    {
        return new OrchestratorCrossProjectIsolation
        {
            Summary =
                "Assume you are NOT alone on this machine. Several project teams run simultaneously, and every "
                + "substrate below is shared across all of them — the workspace manager's server, the agmsg run "
                + "directory, the codex app-servers, the host repo. `Terminal-workspace provisioning` and "
                + "`Design-thread workspace supervision` describe how to build and keep ONE team; this section is "
                + "what keeps that team from damaging another. It narrows the OBJECTS you may act on to your own "
                + "team's; it does not widen or narrow what you may DO, so the supervision authority boundary "
                + "applies unchanged. Operator incident (2026-07-29): with several teams live, one project's design "
                + "thread damaged another project's resources and the operator had to intervene by hand.",
            AttributionBeforeMutation = new OrchestratorAttributionRule
            {
                Summary =
                    "Before you touch anything, establish that it belongs to YOUR team. "
                    + "Attribution is a positive result from the keys below — not the absence of evidence that it "
                    + "belongs to someone else, and not a name that merely looks familiar.",
                GatedMutations = new[]
                {
                    "injecting keys or text into a pane",
                    "killing a process",
                    "closing or restructuring a workspace",
                    "removing or rewriting a state file",
                },
                VerificationKeys = new[]
                {
                    new OrchestratorAttributionKey
                    {
                        Key = "workspace label",
                        HowToCheck =
                            "the workspace is labelled with YOUR team/project name. A workspace you did not create "
                            + "and cannot name is not yours.",
                    },
                    new OrchestratorAttributionKey
                    {
                        Key = "pane cwd",
                        HowToCheck =
                            "the pane's working directory is one of YOUR team's dedicated role folders. A pane whose "
                            + "cwd you do not recognize belongs to someone.",
                    },
                    new OrchestratorAttributionKey
                    {
                        Key = "process cwd",
                        HowToCheck =
                            "the process's own working directory — read it per pid before any kill, exactly as the "
                            + "2026-07-27 migration did when it spared another project's processes. A pid list "
                            + "filtered only by process NAME attributes nothing.",
                    },
                    new OrchestratorAttributionKey
                    {
                        Key = "agmsg `(team, role)` file naming",
                        HowToCheck =
                            "agmsg run-directory state files are named per `(team, role)`; a file whose team segment "
                            + "is not yours is another team's bridge/watcher state, however broken it looks.",
                    },
                },
                UnverifiableIsReadOnly =
                    "if you cannot positively establish ownership, the object is READ-ONLY to you: you may "
                    + "look and you may report — you may not mutate. Escalate to the operator instead of guessing: a "
                    + "wrong guess here is another team's outage, and the cost is theirs rather than yours, which is "
                    + "exactly why the default has to be refusal.",
            },
            OneWorkspacePerTeam =
                "one workspace per team, labelled with the team/project name. Never reuse, repurpose, or borrow "
                + "another team's workspace or its panes — not even an idle-looking one. A workspace is the unit an "
                + "operator reads to know whose work is whose; sharing one collapses that.",
            TeamExclusiveRoleFolders =
                "one folder belongs to exactly ONE team. Never launch your agents in "
                + "another team's folders. This is the same folder-scoping fact that forbids two roles sharing a "
                + "folder within a team (G521) — agmsg identity and the codex bridge are folder-scoped, so an agent "
                + "started in another team's folder takes over THEIR identity and delivery, not just its own.",
            SharedSubstrates = new[]
            {
                new OrchestratorSharedSubstrate
                {
                    Substrate = "workspace-manager server (e.g. the herdr server)",
                    SharingUnit = "one server process serving EVERY workspace on the machine",
                    OwnershipRule =
                        "ownership is per WORKSPACE, never the server. Act on your own workspace and its panes; "
                        + "never restart, reconfigure, or kill the shared server — doing so takes down every other "
                        + "team's workspace at once.",
                },
                new OrchestratorSharedSubstrate
                {
                    Substrate = "agmsg run directory (`~/.agents/skills/agmsg/run`)",
                    SharingUnit = "one directory holding bridge / watcher / app-server state for ALL teams",
                    OwnershipRule =
                        "ownership is per `(team, role)` FILE. Touch only files whose team segment is yours; never "
                        + "clear the directory wholesale to fix your own delivery — that is another team's bridge "
                        + "state you are deleting.",
                },
                new OrchestratorSharedSubstrate
                {
                    Substrate = "codex app-servers",
                    SharingUnit = "one app-server per FOLDER, and folders belong to teams",
                    OwnershipRule =
                        "ownership follows the folder. Verify the process's cwd before stopping an app-server; a "
                        + "same-named process rooted in another team's folder is theirs.",
                },
                new OrchestratorSharedSubstrate
                {
                    Substrate = "host repo",
                    SharingUnit = "one repo holding EVERY domain's metadata",
                    OwnershipRule =
                        "ownership is per DOMAIN path. Write only through the canonical commands for your own "
                        + "domain; queue-state is protected against concurrent writers by the no-item-loss "
                        + "invariant and stale-base re-application (G548), which is a safety net, not a licence to "
                        + "hand-edit another domain's state.",
                },
            },
            NonDestructiveRecovery = new OrchestratorNonDestructiveRecovery
            {
                Summary =
                    "When you find damage — including damage you caused — recovery is NON-DESTRUCTIVE. The instinct "
                    + "to tidy up is the failure mode: a broken artifact belonging to another team is still their "
                    + "evidence, and deleting it destroys their ability to diagnose what happened.",
                PreserveRule =
                    "PRESERVE and SET ASIDE another project's damaged artifacts — rename, move aside, or simply "
                    + "leave them in place and report. Never delete another team's workspace, panes, folders, "
                    + "processes' state, or files, however broken they look. Tell the operator and the affected "
                    + "team's thread what you found and what you set aside.",
                RebuildRule =
                    "REBUILD YOUR OWN fresh rather than repairing in place: create a new workspace, new panes, new "
                    + "role folders as needed, and re-run provisioning. Your own damaged artifacts may also be set "
                    + "aside rather than deleted when they carry evidence worth keeping.",
                DefaultIsRecreateNotCleanup =
                    "Recovery defaults to RECREATE, NOT CLEANUP.",
            },
        };
    }

    // G552: the design-decision hold contract. Everything here is guide-level
    // — the only code half of this slice is the `design-decision-pending`
    // detector, which reads what these rules put on disk. The MAY scope is
    // deliberately narrow: enumerated, mechanically fact-checkable classes
    // only, and the double-check rule's semantic scope is untouched.
    private static OrchestratorDesignDecisionHolds BuildDesignDecisionHolds(string domain, string targetRepo)
    {
        string Fill(string template) => template
            .Replace("<domain>", domain, StringComparison.Ordinal)
            .Replace("<owner/repo>", targetRepo, StringComparison.Ordinal);

        return new OrchestratorDesignDecisionHolds
        {
            Summary = Fill(
                "A hold blocked on a DESIGN DECISION must be visible and bounded. Visible: it is recorded as a "
                + "clarification artifact through the canonical clarify surface, so `automation stalled-work` and "
                + "`automation heartbeat` can see it — an agmsg message alone is invisible to every supervision "
                + "layer. Bounded: the operator may pre-delegate enumerated, mechanically fact-checkable decision "
                + "classes so a correction both threads can verify from repository facts does not wait on design at "
                + "all. Measured cost of getting this wrong: a nine-hour hold on a one-line wording ruling while "
                + "every technical check was green and `stalled-work` reported `stalled=false` throughout."),
            ClarificationBackedHold = new OrchestratorClarificationBackedHold
            {
                Summary = Fill(
                    "When the orchestrator or the reviewer blocks on a design decision, it RECORDS A CLARIFICATION "
                    + "ARTIFACT through the canonical clarify surface, in addition to whatever agmsg message it "
                    + "sends. The artifact is what makes the hold detectable; the message is only a notification."),
                RequiredFields = new[]
                {
                    Fill("domain — the blocked domain (`<domain>`), so the artifact is scoped to the right pipeline."),
                    "blocking execution unit — the unit that cannot proceed until this is answered.",
                    "question — what design must decide, stated so someone who was not in the thread can answer it.",
                    "recommended answer — when the asking thread already believes it knows the answer, state it and cite the facts that support it; design then confirms or overrides rather than starting from scratch.",
                },
                ContractViolationRule =
                    "An agmsg-only hold is a CONTRACT VIOLATION, not a shortcut. A block that exists only as messages "
                    + "is invisible to `stalled-work`, to `heartbeat`, and therefore to every watchdog and every "
                    + "operator glance — which is exactly how a nine-hour hold passed unnoticed with the pipeline "
                    + "reporting healthy. If you are waiting on design, the artifact exists; if the artifact does not "
                    + "exist, you are not waiting, you are stalled.",
                CanonicalCommands = new[]
                {
                    "record the hold — `intent-cli clarify open` (the canonical clarify surface; never hand-write the artifact)",
                    "see what is open — `intent-cli clarify list`",
                    "answer it — `intent-cli clarify answer` (design, or the operator on escalation)",
                    Fill("confirm it is visible — `intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --format json` reports `design-decision-pending`"),
                },
                PasteReadyInvocation =
                    "intent-cli clarify open <execution-unit> \\\n"
                    + "  --question \"<the actual design-blocking question, answerable by someone outside the thread>\" \\\n"
                    + "  --recommended-answer \"<what you believe the answer is, when you believe you know it>\" \\\n"
                    + "  --evidence \"<the repository facts that support the recommendation>\"",
            },
            ReviewerHoldRule = new OrchestratorReviewerHoldRule
            {
                Summary =
                    "The reviewer's hold rule is refined so a green-technical review never becomes an untracked wait. "
                    + "Evaluate what is actually pending before holding.",
                ResolveUnderAuthorityWhen =
                    "Technical checks are GREEN and the only pending item is NON-SEMANTIC and MECHANICALLY "
                    + "FACT-CHECKABLE from repository facts — resolve it under bounded default authority (below), "
                    + "log the resolution with the verifying facts, and proceed. Do not hold a green review on a "
                    + "question whose answer both threads can derive and cite.",
                RecordClarificationOtherwise =
                    "Anything else — a semantic or product question, a fact you cannot verify, or a class the "
                    + "operator has not delegated — becomes a recorded clarification and a VISIBLE pending state. The "
                    + "review is still held; the difference is that the hold is now on disk and detectable.",
                NeverUntrackedWait =
                    "there is no third option where the reviewer simply waits and says so in "
                    + "a message: either the item is resolved under granted authority with its evidence, or a "
                    + "clarification artifact exists. Silence with a message attached is the failure mode this rule "
                    + "exists to remove.",
            },
            BoundedDefaultAuthority = new OrchestratorBoundedDefaultAuthority
            {
                Summary =
                    "BOUNDED DEFAULT AUTHORITY lets the operator pre-delegate a small, enumerated set of decision "
                    + "classes that can be settled by checking repository facts rather than by judgment. It exists so "
                    + "a count correction does not cost nine hours. It is bounded in every direction: granted, "
                    + "enumerated, evidence-logged, amendable, and never semantic.",
                OperatorGrantRequirement =
                    "GRANTED, never assumed. The authority applies only to classes the OPERATOR has explicitly "
                    + "pre-delegated for this domain. Absent a grant, every design decision goes to design as before "
                    + "— the default is unchanged, and no thread may infer a delegation from the fact that an answer "
                    + "seems obvious.",
                FactCheckableClasses = new[]
                {
                    new OrchestratorFactCheckableClass
                    {
                        DecisionClass = "count and enumeration corrections",
                        VerifyingFacts =
                            "the count is derivable from repository facts both threads can read — e.g. a slice count "
                            + "derived from the merged PR list and the issue's own enumeration. Cite the list and the "
                            + "derivation.",
                    },
                    new OrchestratorFactCheckableClass
                    {
                        DecisionClass = "wording corrections that follow from a cited fact",
                        VerifyingFacts =
                            "the corrected wording is entailed by a fact in the repository (a merged PR title, a "
                            + "label state, a retired unit's own record), and the reviewer and orchestrator AGREE on "
                            + "both the fact and the correction. Disagreement is not fact-checkable — it escalates.",
                    },
                    new OrchestratorFactCheckableClass
                    {
                        DecisionClass = "cross-reference and link corrections",
                        VerifyingFacts =
                            "the target exists (or does not) in the repository as cited — verifiable by reading the "
                            + "referenced file, heading, issue, or PR.",
                    },
                    new OrchestratorFactCheckableClass
                    {
                        DecisionClass = "identifier and metadata mismatches against a canonical source",
                        VerifyingFacts =
                            "the canonical source is named and read — e.g. a version in `eng/version.json`, a unit id "
                            + "in a packet, a label in the canonical palette. The canonical source wins; the "
                            + "resolution cites it.",
                    },
                },
                EvidenceLoggingRule =
                    "MANDATORY EVIDENCE LOGGING. A resolution taken under this authority is recorded in the durable "
                    + "trail with the facts that verify it — what was decided, which repository facts entail it, and "
                    + "which threads agreed. An unlogged resolution is not a granted-authority resolution; it is an "
                    + "undocumented decision, and it is a violation of this contract.",
                EvidenceSink =
                    "The sink is the CANONICAL `clarify record` surface: the entry lands under `## Recently Resolved` "
                    + "in the domain's clarification return path (`intents/<domain>/clarifications/open.md`), where "
                    + "`Question` identifies the pending item, `Decision` records the decided value, and `Rationale` "
                    + "records the verified repository facts plus the reviewer/orchestrator agreement. The entry is "
                    + "durable and stays readable there, which is exactly what makes design's post-hoc amendment "
                    + "possible — design reads the recorded evidence and amends or reverses from it.",
                EvidenceOperation =
                    "# 1. write the decision artifact (## Question / ## Decision / ## Rationale)\n"
                    + "cat > /tmp/authority-decision.md <<'EOF'\n"
                    + "## Question\n"
                    + "<the pending item, identified so design can find it later>\n"
                    + "\n"
                    + "## Decision\n"
                    + "<the decided value>\n"
                    + "\n"
                    + "## Rationale\n"
                    + "<the verified repository facts that entail it, and which threads agreed>\n"
                    + "EOF\n"
                    + "\n"
                    + "# 2. record it in the durable trail (--dry-run first shows the intended update)\n"
                    + "intent-cli clarify record --domain <domain> --from-file /tmp/authority-decision.md",
                PostHocAmendmentRule =
                    "DESIGN MAY AMEND POST HOC. A granted-authority resolution is provisional in design's eyes: "
                    + "design can review the logged evidence afterwards and amend or reverse the decision. The "
                    + "authority buys latency, not finality — proceeding does not close the question against design.",
                SemanticExclusionRule =
                    "SEMANTIC AND PRODUCT DECISIONS ARE EXCLUDED, absolutely. Intent shaping, packet content and "
                    + "acceptance criteria, release scope, prioritization rulings, and anything requiring product or "
                    + "design judgment always go to design through the design↔orchestrator double-check rule, whose "
                    + "scope this contract does not touch. If settling the question requires deciding what SHOULD be "
                    + "true rather than checking what IS true, it is not fact-checkable and this authority does not "
                    + "reach it.",
            },
            DesignReminderLoop = new OrchestratorDesignReminderLoop
            {
                Summary =
                    "While a clarification stays open, the design thread is reminded on a fixed cadence. A recorded "
                    + "hold that nobody re-surfaces is still a slow hold — the artifact makes it detectable, the "
                    + "reminder makes it noticed.",
                Sender =
                    "The ORCHESTRATOR sends the reminder from its long-interval automation — the same wake that "
                    + "already runs the heartbeat check. No new scheduler, and the receivers stay loopless.",
                IntervalClass =
                    "30–60 minute class — the same low-frequency band as the heartbeat and the design-thread "
                    + "watchdog. Faster polling recreates the churn the message-driven model removes; slower lets a "
                    + "hold sit past the point an operator would want to know.",
                OnePerIntervalRule =
                    "AT MOST ONE reminder per interval PER OPEN CLARIFICATION. Two open clarifications produce at "
                    + "most two reminders in a wake; one clarification never produces two reminders in the same "
                    + "interval no matter how many wakes fire. This is the same one-message discipline the watchdog "
                    + "already follows.",
                StopCondition =
                    "STOP ON ANSWER. Once the clarification is answered (or applied, or cancelled) it is no longer "
                    + "open, `design-decision-pending` clears on its own, and the reminders stop. Never keep "
                    + "reminding against an answered clarification, and never re-open one to keep a thread's "
                    + "attention.",
                OperatorAppNote =
                    "The design thread runs in the OPERATOR APP by preference, which is what makes a reminder land "
                    + "either way: an OPEN design session receives the reminder immediately through its monitor, and "
                    + "a CLOSED one finds it waiting in the inbox on resume. Neither case requires design to be "
                    + "resident in the team workspace — there is no workspace-residency requirement here.",
            },
            DetectionReference = Fill(
                "Detection is `design-decision-pending` in `automation stalled-work`: it reads the domain's OPEN "
                + "clarification artifacts and reports each with its age, blocking execution unit, and question "
                + "summary, and `automation heartbeat` carries it in `message_body` like any other kind. Confirm a "
                + "hold is visible with `intent-cli automation stalled-work --domain <domain> --repo <owner/repo> "
                + "--format json`; if the hold is real but the kind is absent, the clarification artifact was never "
                + "recorded — which is the contract violation above, not a detector bug."),
        };
    }

    // G500: turn an orchestrator setup request into a concrete, operational
    // intake. The visible outcome is one of missing-inputs / setup-ready /
    // blocked. When inputs are complete the intake emits copy-paste agmsg
    // join/delivery commands and first role prompts per role; when a field is
    // missing it lists ONLY the missing fields; when an existing loop would
    // race it is blocked. The orchestrator is the only scheduled thread —
    // implementation/review are loopless receivers.
    private static OrchestratorSetupIntake BuildSetupIntake(IReadOnlyDictionary<string, string> values, bool herdrOnly)
    {
        var domain = values["<domain>"];
        var repo = values["<owner/repo>"];
        var agent = values["<agent>"];
        var orchestratorPath = values["<orchestrator-path>"];
        var implementationPath = values["<implementation-path>"];
        var reviewPath = values["<review-path>"];
        var team = values["<team>"];
        var deliveryMode = values["<delivery-mode>"];
        var loopPolicy = values["<existing-loop-policy>"];

        // The three role agents fall back to --agent when not explicitly set.
        string ResolveAgent(string key) =>
            values[key].Length > 0 ? values[key] : (string.Equals(agent, "<agent>", StringComparison.Ordinal) ? string.Empty : agent);

        var orchestratorAgent = ResolveAgent("<orchestrator-agent>");
        var implementerAgent = ResolveAgent("<implementer-agent>");
        var reviewerAgent = ResolveAgent("<reviewer-agent>");

        bool Supplied(string value, string placeholder) =>
            value.Length > 0 && !string.Equals(value, placeholder, StringComparison.Ordinal);

        // Required fields, in a stable order; a field is "missing" when unset.
        var required = new (string Label, bool Present)[]
        {
            ("domain", Supplied(domain, "<domain>")),
            ("target repo", Supplied(repo, "<owner/repo>")),
            ("orchestrator folder", Supplied(orchestratorPath, "<orchestrator-path>")),
            ("implementation folder", Supplied(implementationPath, "<implementation-path>")),
            ("review folder", Supplied(reviewPath, "<review-path>")),
            ("orchestrator agent", orchestratorAgent.Length > 0),
            ("implementer agent", implementerAgent.Length > 0),
            ("reviewer agent", reviewerAgent.Length > 0),
            // G570 (design ruling fb1913c8): the agmsg team name and delivery
            // mode are agmsg-ONLY inputs. Demanding them from a team that runs
            // herdr-only is the structural form of handing them agmsg
            // instructions — the setup would report missing-inputs forever for
            // fields its transport has no concept of.
            ("agmsg team name", herdrOnly || Supplied(team, "<team>")),
            ("delivery mode", herdrOnly || Supplied(deliveryMode, "<delivery-mode>")),
            ("existing-loop stop policy", loopPolicy.Length > 0),
        };

        var missing = required.Where(f => !f.Present).Select(f => f.Label).ToArray();

        var inputs = new OrchestratorSetupInputs
        {
            Domain = domain,
            TargetRepo = repo,
            OrchestratorFolder = orchestratorPath,
            ImplementationFolder = implementationPath,
            ReviewFolder = reviewPath,
            OrchestratorAgent = orchestratorAgent.Length > 0 ? orchestratorAgent : null,
            ImplementerAgent = implementerAgent.Length > 0 ? implementerAgent : null,
            ReviewerAgent = reviewerAgent.Length > 0 ? reviewerAgent : null,
            // G570 third repair: the agmsg team name and delivery mode are
            // agmsg-ONLY inputs. Under herdr-only they are not merely
            // unrequired — they are not part of the object at all, so a
            // consumer reading fields never sees an input its transport has no
            // concept of.
            Team = herdrOnly ? null : team,
            DeliveryMode = herdrOnly ? null : deliveryMode,
            ExistingLoopPolicy = loopPolicy.Length > 0 ? loopPolicy : null,
        };

        if (missing.Length > 0)
        {
            return new OrchestratorSetupIntake
            {
                Status = IntakeMissingInputs,
                Headline = $"missing-inputs — supply the {missing.Length} missing field(s) below to get a setup-ready plan.",
                MissingFields = missing,
                Inputs = inputs,
                LooplessReceiverNote = LooplessReceiverNote,
            };
        }

        // All inputs present. A kept existing loop for the same route would race
        // the orchestrator (mixed-mode timers) — block until the operator stops it.
        if (string.Equals(loopPolicy, ExistingLoopKeep, StringComparison.Ordinal))
        {
            return new OrchestratorSetupIntake
            {
                Status = IntakeBlocked,
                Headline =
                    "blocked — existing implementation/review timer loops for this domain/repo would race the "
                    + "orchestrator (mixed-mode). Stop the existing loops (or re-run with --existing-loop-policy "
                    + "will-stop) before starting orchestrator mode; receivers are never scheduled — orchestrator "
                    + "wakes are message-driven by default, with an explicit fallback/legacy timer as the only case "
                    + "where the orchestrator itself is scheduled.",
                MissingFields = Array.Empty<string>(),
                Inputs = inputs,
                LooplessReceiverNote = LooplessReceiverNote,
            };
        }

        // setup-ready: emit copy-paste agmsg commands + first role prompts.
        var agmsgCommands = new[]
        {
            $"agmsg join.sh {team} orchestrator {orchestratorAgent} {orchestratorPath}",
            $"agmsg delivery.sh set {deliveryMode} {orchestratorAgent} {orchestratorPath}",
            $"agmsg join.sh {team} implementation {implementerAgent} {implementationPath}",
            $"agmsg delivery.sh set {deliveryMode} {implementerAgent} {implementationPath}",
            $"agmsg join.sh {team} review {reviewerAgent} {reviewPath}",
            $"agmsg delivery.sh set {deliveryMode} {reviewerAgent} {reviewPath}",
        };

        string RolePrompt(string role, string roleAgent, string folder) =>
            $"You are the {role.ToUpperInvariant()} thread for domain `{domain}` against `{repo}` using `{roleAgent}`, "
            + $"running from `{folder}` as part of agmsg team `{team}` (delivery: {deliveryMode}). "
            + (string.Equals(role, "orchestrator", StringComparison.Ordinal)
                ? "Your steady state is MESSAGE-DRIVEN — implementation/review agmsg replies wake you; an orchestrator "
                  + "timer (Codex automation 5m or Claude `/loop 5m`) is an OPTIONAL fallback/legacy polling mode, not "
                  + "the default. You pace the implementation/review receivers over agmsg and never run their timers. "
                  + "See the full orchestrator prompt in the Thread prompts section."
                : "You are a LOOPLESS receiver: do NOT start your own recurring timer/loop — wait for an orchestrator "
                  + "delegation, act once, reply once, then wait. Your worker target comes from `intent-cli worker "
                  + "next-action`, not the agmsg text. See the full prompt in the Thread prompts section.");

        var rolePrompts = new[]
        {
            new OrchestratorThreadPrompt { Role = "orchestrator", Purpose = "First prompt — paste into the scheduled orchestrator thread.", Prompt = RolePrompt("orchestrator", orchestratorAgent, orchestratorPath) },
            new OrchestratorThreadPrompt { Role = "implementation", Purpose = "First prompt — paste into the loopless implementation receiver.", Prompt = RolePrompt("implementation", implementerAgent, implementationPath) },
            new OrchestratorThreadPrompt { Role = "review", Purpose = "First prompt — paste into the loopless review receiver.", Prompt = RolePrompt("review", reviewerAgent, reviewPath) },
        };

        // G570 third repair: the setup-ready OBJECT is mode-specific, not a
        // token-replaced agmsg object. Under herdr-only it emits no agmsg-only
        // fields at all — no commands array, no agmsg-shaped role prompts, no
        // agmsg validation steps — and its headline is pointer-only. A
        // consumer reading fields must not have to know that some values are
        // stand-ins; the fields simply are not there.
        if (herdrOnly)
        {
            return new OrchestratorSetupIntake
            {
                Status = IntakeSetupReady,
                Headline =
                    "setup-ready (herdr-only) — the registration, delivery-configuration and role-prompt steps of "
                    + "this intake are agmsg-only and do not apply. Their herdr-only counterparts ship in G571.",
                MissingFields = Array.Empty<string>(),
                Inputs = inputs,
                AgmsgCommands = null,
                RolePrompts = null,
                FirstValidation = null,
                LooplessReceiverNote = LooplessReceiverNote,
            };
        }

        return new OrchestratorSetupIntake
        {
            Status = IntakeSetupReady,
            Headline = "setup-ready — register the three roles with the agmsg commands, paste the first prompts, then run the first validation.",
            MissingFields = Array.Empty<string>(),
            Inputs = inputs,
            AgmsgCommands = agmsgCommands,
            RolePrompts = rolePrompts,
            FirstValidation = new[]
            {
                $"Preflight all three cwds BEFORE mutating: `{orchestratorPath}` (orchestrator), `{implementationPath}` (implementation), `{reviewPath}` (review) — clean `git status`, expected git remote/repo, expected branch/base, and no existing timer-loop for this domain/repo (see Preflight).",
                "Existing-loop conflict check: confirm no implementation/review recurring timer is running for this domain/repo (implementation/review stay loopless whether the orchestrator runs message-driven or on an explicit fallback/legacy timer).",
                "First read-only wake: run ONE confirm-only orchestrator wake — read state, send nothing.",
                "Receiver readiness: ping each receiver and require an ack BEFORE any real delegation — a registered+configured role is not ready until it acks (see the Receiver readiness section). A session launched before delivery was active may have missed earlier messages; resend or read with `inbox.sh`.",
            },
            LooplessReceiverNote = LooplessReceiverNote,
        };
    }

    private const string LooplessReceiverNote =
        "The implementation and review threads are loopless agmsg receivers — they must NOT run their own `/loop` or "
        + "recurring timer for the same domain/repo, whether the orchestrator runs message-driven (the default, woken "
        + "by agmsg replies) or on an explicit fallback/legacy timer (Codex 5m / Claude `/loop 5m`).";

    private static bool TryParseArguments(
        string[] args,
        out string format,
        out IReadOnlyDictionary<string, string> values,
        out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<domain>"] = "<domain>",
            ["<owner/repo>"] = "<owner/repo>",
            ["<agent>"] = "<agent>",
            ["<mode>"] = ModeSingleDomain,
            // G500: setup-intake inputs. Unset = the placeholder token (treated
            // as "missing" by the intake); the three role agents fall back to
            // <agent> when not explicitly supplied.
            ["<orchestrator-path>"] = "<orchestrator-path>",
            ["<implementation-path>"] = "<implementation-path>",
            ["<review-path>"] = "<review-path>",
            ["<orchestrator-agent>"] = string.Empty,
            ["<implementer-agent>"] = string.Empty,
            ["<reviewer-agent>"] = string.Empty,
            ["<team>"] = "<team>",
            ["<delivery-mode>"] = "<delivery-mode>",
            ["<existing-loop-policy>"] = string.Empty,
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!RequiresValue(arg))
            {
                values = parsed;
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                values = parsed;
                error = $"{arg} requires a value.";
                return false;
            }

            var value = args[++i];
            switch (arg)
            {
                case "--format":
                    format = value;
                    break;
                case "--domain":
                    parsed["<domain>"] = value;
                    break;
                case "--target-repo":
                    parsed["<owner/repo>"] = value;
                    break;
                case "--agent":
                    parsed["<agent>"] = value;
                    break;
                case "--mode":
                    parsed["<mode>"] = value;
                    break;
                case "--orchestrator-path":
                    parsed["<orchestrator-path>"] = value;
                    break;
                case "--implementation-path":
                    parsed["<implementation-path>"] = value;
                    break;
                case "--review-path":
                    parsed["<review-path>"] = value;
                    break;
                case "--orchestrator-agent":
                    parsed["<orchestrator-agent>"] = value;
                    break;
                case "--implementer-agent":
                    parsed["<implementer-agent>"] = value;
                    break;
                case "--reviewer-agent":
                    parsed["<reviewer-agent>"] = value;
                    break;
                case "--team":
                    parsed["<team>"] = value;
                    break;
                case "--delivery-mode":
                    parsed["<delivery-mode>"] = value;
                    break;
                case "--existing-loop-policy":
                    parsed["<existing-loop-policy>"] = value;
                    break;
            }
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            values = parsed;
            error = $"Unknown --format '{format}'. Supported: markdown, json.";
            return false;
        }

        var modeValue = parsed["<mode>"];
        if (!string.Equals(modeValue, ModeSingleDomain, StringComparison.Ordinal)
            && !string.Equals(modeValue, ModeMultiDomain, StringComparison.Ordinal))
        {
            values = parsed;
            error = $"Unknown --mode '{modeValue}'. Supported: single-domain, multi-domain.";
            return false;
        }

        var loopPolicy = parsed["<existing-loop-policy>"];
        if (loopPolicy.Length > 0
            && !string.Equals(loopPolicy, ExistingLoopNone, StringComparison.Ordinal)
            && !string.Equals(loopPolicy, ExistingLoopWillStop, StringComparison.Ordinal)
            && !string.Equals(loopPolicy, ExistingLoopKeep, StringComparison.Ordinal))
        {
            values = parsed;
            error = $"Unknown --existing-loop-policy '{loopPolicy}'. Supported: none, will-stop, keep.";
            return false;
        }

        values = parsed;
        return true;
    }

    private static bool RequiresValue(string arg) =>
        string.Equals(arg, "--format", StringComparison.Ordinal)
        || string.Equals(arg, "--domain", StringComparison.Ordinal)
        || string.Equals(arg, "--target-repo", StringComparison.Ordinal)
        || string.Equals(arg, "--agent", StringComparison.Ordinal)
        || string.Equals(arg, "--mode", StringComparison.Ordinal)
        || string.Equals(arg, "--orchestrator-path", StringComparison.Ordinal)
        || string.Equals(arg, "--implementation-path", StringComparison.Ordinal)
        || string.Equals(arg, "--review-path", StringComparison.Ordinal)
        || string.Equals(arg, "--orchestrator-agent", StringComparison.Ordinal)
        || string.Equals(arg, "--implementer-agent", StringComparison.Ordinal)
        || string.Equals(arg, "--reviewer-agent", StringComparison.Ordinal)
        || string.Equals(arg, "--team", StringComparison.Ordinal)
        || string.Equals(arg, "--delivery-mode", StringComparison.Ordinal)
        || string.Equals(arg, "--existing-loop-policy", StringComparison.Ordinal);

    // G500: render the operational setup intake at the top of the guide.
    private static void WriteSetupIntake(TextWriter writer, OrchestratorSetupIntake intake)
    {
        writer.WriteLine("## Setup intake");
        writer.WriteLine();
        // G570: the recorded session layer, stated before any transport-specific
        // setup step so an operator never follows the wrong one.
        if (intake.SessionLayerNote is { } sessionLayerNote)
        {
            writer.WriteLine($"- session layer: {sessionLayerNote}");
            writer.WriteLine();
        }

        writer.WriteLine($"- **status: `{intake.Status}`**");
        writer.WriteLine($"- {intake.Headline}");
        writer.WriteLine($"- {intake.LooplessReceiverNote}");
        writer.WriteLine();

        if (intake.MissingFields.Count > 0)
        {
            writer.WriteLine("### Missing inputs (supply only these)");
            writer.WriteLine();
            foreach (var field in intake.MissingFields)
            {
                writer.WriteLine($"- {field}");
            }
            writer.WriteLine();
        }

        if (intake.AgmsgCommands is { Count: > 0 })
        {
            writer.WriteLine("### agmsg registration + delivery (copy-paste)");
            writer.WriteLine();
            writer.WriteLine("```bash");
            foreach (var command in intake.AgmsgCommands)
            {
                writer.WriteLine(command);
            }
            writer.WriteLine("```");
            writer.WriteLine();
        }

        if (intake.RolePrompts is { Count: > 0 })
        {
            writer.WriteLine("### First role prompts");
            foreach (var prompt in intake.RolePrompts)
            {
                writer.WriteLine();
                writer.WriteLine($"#### {prompt.Role}");
                writer.WriteLine();
                writer.WriteLine("```text");
                writer.WriteLine(prompt.Prompt);
                writer.WriteLine("```");
            }
            writer.WriteLine();
        }

        if (intake.FirstValidation is { Count: > 0 })
        {
            writer.WriteLine("### First validation");
            writer.WriteLine();
            foreach (var step in intake.FirstValidation)
            {
                writer.WriteLine($"- {step}");
            }
            writer.WriteLine();
        }
    }

    // G549: render the provisioning section as a paste-ready checklist — the
    // six elements in execution order, then the flat checklist a design thread
    // can work straight down.
    private static void WriteTerminalWorkspaceProvisioning(TextWriter writer, OrchestratorTerminalWorkspaceProvisioning provisioning)
    {
        writer.WriteLine("## Terminal-workspace provisioning (G549)");
        writer.WriteLine();
        writer.WriteLine(provisioning.Summary);
        writer.WriteLine();

        writer.WriteLine("### Placeholders");
        writer.WriteLine();
        foreach (var placeholder in provisioning.Placeholders)
        {
            writer.WriteLine($"- `{placeholder.Token}` — {placeholder.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("### 1. Role folders (create them when absent)");
        writer.WriteLine();
        writer.WriteLine(provisioning.FolderProvisioning.Summary);
        writer.WriteLine();
        writer.WriteLine($"> **Never share a folder:** {provisioning.FolderProvisioning.NeverShareRule}");
        writer.WriteLine();
        foreach (var role in provisioning.FolderProvisioning.Roles)
        {
            writer.WriteLine($"- **{role.Role}** — folder `{role.Folder}` — clone of the {role.CloneSource}");
            writer.WriteLine();
            writer.WriteLine("  ```bash");
            writer.WriteLine($"  {role.CreateCommand}");
            writer.WriteLine("  ```");
            writer.WriteLine();
        }
        writer.WriteLine($"- **folder absent** — {provisioning.FolderProvisioning.AbsentFolderRule}");
        writer.WriteLine();
        writer.WriteLine("Verify before launching anything:");
        writer.WriteLine();
        foreach (var check in provisioning.FolderProvisioning.Verification)
        {
            writer.WriteLine($"- {check}");
        }
        writer.WriteLine();

        writer.WriteLine("### 2. Workspace topology");
        writer.WriteLine();
        writer.WriteLine(provisioning.Topology.Summary);
        writer.WriteLine();
        foreach (var rule in provisioning.Topology.Rules)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **design thread stays outside** — {provisioning.Topology.DesignThreadPosition}");
        writer.WriteLine();

        writer.WriteLine("### 3. Launch rules (and why)");
        writer.WriteLine();
        writer.WriteLine(provisioning.LaunchRules.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **codex — shim-safe typed launch** — {provisioning.LaunchRules.CodexShimRule}");
        writer.WriteLine($"- **claude — operator-chosen permission mode** — {provisioning.LaunchRules.ClaudePermissionModeRule}");
        writer.WriteLine($"- **attended first run** — {provisioning.LaunchRules.AttendedFirstRunRule}");
        writer.WriteLine();
        writer.WriteLine($"> **Warning:** {provisioning.LaunchRules.CodexDirectSpawnWarning}");
        writer.WriteLine();
        writer.WriteLine($"> **Authority boundary:** {provisioning.LaunchRules.AuthorityBoundary}");
        writer.WriteLine();

        writer.WriteLine("### 4. Role initialization (actas and readiness)");
        writer.WriteLine();
        writer.WriteLine(provisioning.RoleInitialization.Summary);
        writer.WriteLine();
        foreach (var form in provisioning.RoleInitialization.ActasForms)
        {
            writer.WriteLine($"- **{form.AgentType}** — `{form.ActasInvocation}` — {form.Note}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **readiness wait** — {provisioning.RoleInitialization.ReadinessWait}");
        writer.WriteLine();
        writer.WriteLine("#### Readiness layers (do not collapse them)");
        writer.WriteLine();
        writer.WriteLine($"- **1. delivery configuration** — {provisioning.RoleInitialization.ConfigurationProof}");
        foreach (var evidence in provisioning.RoleInitialization.LiveAttachmentEvidence)
        {
            writer.WriteLine($"- **2. live attachment ({evidence.AgentType})** — {evidence.Evidence}");
        }
        writer.WriteLine($"- **3. end-to-end** — {provisioning.RoleInitialization.EndToEndProof}");
        writer.WriteLine();
        writer.WriteLine($"- **ping test** — {provisioning.RoleInitialization.PingTestReference}");
        writer.WriteLine();

        var liveness = provisioning.RoleInitialization.VerifiedLiveness;
        writer.WriteLine("#### Verified liveness — a startup report is not readiness (G556)");
        writer.WriteLine();
        writer.WriteLine(liveness.Summary);
        writer.WriteLine();
        writer.WriteLine($"> **A startup report is NOT readiness:** {liveness.ReportIsNotReadiness}");
        writer.WriteLine();
        writer.WriteLine($"- **settle delay** — {liveness.SettleDelay}");
        writer.WriteLine();
        writer.WriteLine("After the settle delay, ALL THREE must still pass:");
        writer.WriteLine();
        foreach (var check in liveness.PostReportChecks)
        {
            writer.WriteLine($"- **{check.Check}** — {check.HowToVerify}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **early death is normal** — {liveness.EarlyDeathIsNormal.Summary}");
        writer.WriteLine($"- **transport-reset signature** — {liveness.EarlyDeathIsNormal.TransportResetSignature}");
        writer.WriteLine($"- **re-check obligation** — {liveness.EarlyDeathIsNormal.RecheckObligation}");
        writer.WriteLine();
        writer.WriteLine($"> **Shared app-server death mode:** {liveness.SharedAppServerDeathMode.Summary} {liveness.SharedAppServerDeathMode.BlastRadius} {liveness.SharedAppServerDeathMode.PreventionReference}");
        writer.WriteLine();

        writer.WriteLine("### 5. Role exclusivity and handover");
        writer.WriteLine();
        writer.WriteLine($"- **one holder per role** — {provisioning.ExclusivityHandover.OneHolderRule}");
        writer.WriteLine($"- **graceful drop first** — {provisioning.ExclusivityHandover.GracefulDropRule}");
        writer.WriteLine($"- **operator confirmation** — {provisioning.ExclusivityHandover.OperatorConfirmationRule}");
        writer.WriteLine($"- **successor claims after** — {provisioning.ExclusivityHandover.SuccessorClaimRule}");
        writer.WriteLine();

        writer.WriteLine($"### 6. Reference workspace manager — {provisioning.ReferenceManager.Name}");
        writer.WriteLine();
        writer.WriteLine(provisioning.ReferenceManager.Summary);
        writer.WriteLine();
        foreach (var surface in provisioning.ReferenceManager.Surfaces)
        {
            writer.WriteLine($"- {surface.Surface} — {surface.UsedFor}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **internals** — {provisioning.ReferenceManager.InternalsLinkOut}");
        writer.WriteLine($"- **substitution** — {provisioning.ReferenceManager.SubstitutionRule}");
        writer.WriteLine();

        writer.WriteLine("### Provisioning checklist (paste-ready)");
        writer.WriteLine();
        for (var i = 0; i < provisioning.Checklist.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {provisioning.Checklist[i]}");
        }
        writer.WriteLine();
    }

    // G550: render the supervision section — granted authority first (so the
    // boundary frames everything that follows), then session lifecycle, the
    // three layers with their cadences, and the two closed dialog lists.
    private static void WriteDesignWorkspaceSupervision(TextWriter writer, OrchestratorDesignWorkspaceSupervision supervision)
    {
        writer.WriteLine("## Design-thread workspace supervision (G550)");
        writer.WriteLine();
        writer.WriteLine(supervision.Summary);
        writer.WriteLine();

        writer.WriteLine("### Granted authority — session layer only");
        writer.WriteLine();
        writer.WriteLine(supervision.GrantedAuthority.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **authority is granted, not assumed** — {supervision.GrantedAuthority.OperatorGrantRule}");
        writer.WriteLine();
        writer.WriteLine("The design thread operates the session layer:");
        writer.WriteLine();
        foreach (var item in supervision.GrantedAuthority.DesignOperatesSessionLayer)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Workflow state ownership:** {supervision.GrantedAuthority.WorkflowStateOwnershipUnchanged}");
        writer.WriteLine();

        writer.WriteLine("### Session lifecycle (investigate, then replace gracefully)");
        writer.WriteLine();
        writer.WriteLine(supervision.SessionLifecycle.Summary);
        writer.WriteLine();
        foreach (var step in supervision.SessionLifecycle.UnresponsiveSessionInvestigation)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **one holder per role** — {supervision.SessionLifecycle.ExclusivityRule}");
        writer.WriteLine($"- **graceful drop** — {supervision.SessionLifecycle.GracefulDropRule}");
        writer.WriteLine($"- **operator-visible confirmation** — {supervision.SessionLifecycle.OperatorVisibleConfirmation}");
        writer.WriteLine();

        writer.WriteLine("### Three supervision layers");
        writer.WriteLine();
        foreach (var layer in supervision.SupervisionLayers)
        {
            writer.WriteLine($"- **{layer.Layer}**");
            writer.WriteLine($"  - purpose — {layer.Purpose}");
            writer.WriteLine($"  - cadence — {layer.Cadence}");
            writer.WriteLine($"  - note — {layer.Note}");
        }
        writer.WriteLine();
        writer.WriteLine("#### What the pane scan is looking for");
        writer.WriteLine();
        foreach (var stuck in supervision.PaneScanStuckStates)
        {
            writer.WriteLine($"- **{stuck.State}** — the scan sees: {stuck.WhatTheScanSees} Recovery: {stuck.Recovery}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Re-arm across restarts:** {supervision.RearmRule}");
        writer.WriteLine();

        writer.WriteLine("### Blocking dialogs — the boundary");
        writer.WriteLine();
        writer.WriteLine($"> **Verified read before answer:** {supervision.VerifiedReadRule}");
        writer.WriteLine();
        writer.WriteLine("#### MAY answer (only after the verified read)");
        writer.WriteLine();
        foreach (var entry in supervision.MayAnswer)
        {
            writer.WriteLine($"- **{entry.Dialog}** — verify: {entry.Verification}");
        }
        writer.WriteLine();
        writer.WriteLine("#### MUST escalate to the operator");
        writer.WriteLine();
        foreach (var entry in supervision.MustEscalate)
        {
            writer.WriteLine($"- **{entry.Category}** — {entry.Reason}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Boundary:** {supervision.BoundarySentence}");
        writer.WriteLine();
        writer.WriteLine($"- **provisioning** — {supervision.ProvisioningReference}");
        writer.WriteLine($"- **watchdog safety rules** — {supervision.WatchdogSafetyRulesReference}");
        writer.WriteLine();
    }

    // G555: render the cross-project isolation rules. Attribution comes first
    // because everything after it is conditional on having established
    // ownership; the substrate table is a table because the sharing UNIT is
    // the fact a reader needs per substrate.
    private static void WriteCrossProjectIsolation(TextWriter writer, OrchestratorCrossProjectIsolation isolation)
    {
        writer.WriteLine("## Cross-project isolation on a shared machine (G555)");
        writer.WriteLine();
        writer.WriteLine(isolation.Summary);
        writer.WriteLine();

        writer.WriteLine("### Attribution before mutation");
        writer.WriteLine();
        writer.WriteLine(isolation.AttributionBeforeMutation.Summary);
        writer.WriteLine();
        writer.WriteLine("Attribution is required before any of these:");
        writer.WriteLine();
        foreach (var mutation in isolation.AttributionBeforeMutation.GatedMutations)
        {
            writer.WriteLine($"- {mutation}");
        }
        writer.WriteLine();
        writer.WriteLine("Verify ownership with all four keys:");
        writer.WriteLine();
        foreach (var key in isolation.AttributionBeforeMutation.VerificationKeys)
        {
            writer.WriteLine($"- **{key.Key}** — {key.HowToCheck}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Unverifiable = read-only:** {isolation.AttributionBeforeMutation.UnverifiableIsReadOnly}");
        writer.WriteLine();

        writer.WriteLine("### Workspace and folder exclusivity");
        writer.WriteLine();
        writer.WriteLine($"- **one workspace per team** — {isolation.OneWorkspacePerTeam}");
        writer.WriteLine($"- **team-exclusive role folders** — {isolation.TeamExclusiveRoleFolders}");
        writer.WriteLine();

        writer.WriteLine("### Shared substrates and who owns what");
        writer.WriteLine();
        writer.WriteLine("| substrate | sharing unit | ownership rule |");
        writer.WriteLine("| --- | --- | --- |");
        foreach (var substrate in isolation.SharedSubstrates)
        {
            writer.WriteLine($"| {substrate.Substrate} | {substrate.SharingUnit} | {substrate.OwnershipRule} |");
        }
        writer.WriteLine();

        writer.WriteLine("### Non-destructive recovery");
        writer.WriteLine();
        writer.WriteLine(isolation.NonDestructiveRecovery.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **preserve theirs** — {isolation.NonDestructiveRecovery.PreserveRule}");
        writer.WriteLine($"- **rebuild yours** — {isolation.NonDestructiveRecovery.RebuildRule}");
        writer.WriteLine();
        writer.WriteLine($"> **{isolation.NonDestructiveRecovery.DefaultIsRecreateNotCleanup}**");
        writer.WriteLine();
    }

    // G552: render the design-decision hold contract — the hold rule first
    // (it is what makes everything else observable), then the reviewer
    // refinement, the bounded authority, and the reminder cadence.
    private static void WriteDesignDecisionHolds(TextWriter writer, OrchestratorDesignDecisionHolds holds)
    {
        writer.WriteLine("## Design-decision holds and bounded authority (G552)");
        writer.WriteLine();
        writer.WriteLine(holds.Summary);
        writer.WriteLine();

        writer.WriteLine("### Clarification-backed holds");
        writer.WriteLine();
        writer.WriteLine(holds.ClarificationBackedHold.Summary);
        writer.WriteLine();
        writer.WriteLine("Record these fields:");
        writer.WriteLine();
        foreach (var field in holds.ClarificationBackedHold.RequiredFields)
        {
            writer.WriteLine($"- {field}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Contract violation:** {holds.ClarificationBackedHold.ContractViolationRule}");
        writer.WriteLine();
        foreach (var command in holds.ClarificationBackedHold.CanonicalCommands)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();
        writer.WriteLine("Paste-ready — the OPEN artifact carries the real content, not a packet-derived synthesis:");
        writer.WriteLine();
        writer.WriteLine("```bash");
        writer.WriteLine(holds.ClarificationBackedHold.PasteReadyInvocation);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("### Reviewer hold rule (refined)");
        writer.WriteLine();
        writer.WriteLine(holds.ReviewerHoldRule.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **resolve under granted authority when** — {holds.ReviewerHoldRule.ResolveUnderAuthorityWhen}");
        writer.WriteLine($"- **record a clarification otherwise** — {holds.ReviewerHoldRule.RecordClarificationOtherwise}");
        writer.WriteLine();
        writer.WriteLine($"> **Never an untracked wait:** {holds.ReviewerHoldRule.NeverUntrackedWait}");
        writer.WriteLine();

        writer.WriteLine("### Bounded default authority");
        writer.WriteLine();
        writer.WriteLine(holds.BoundedDefaultAuthority.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **operator grant required** — {holds.BoundedDefaultAuthority.OperatorGrantRequirement}");
        writer.WriteLine();
        writer.WriteLine("#### Enumerated fact-checkable classes (the whole MAY scope)");
        writer.WriteLine();
        foreach (var entry in holds.BoundedDefaultAuthority.FactCheckableClasses)
        {
            writer.WriteLine($"- **{entry.DecisionClass}** — verify: {entry.VerifyingFacts}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **evidence logging** — {holds.BoundedDefaultAuthority.EvidenceLoggingRule}");
        writer.WriteLine($"- **evidence sink** — {holds.BoundedDefaultAuthority.EvidenceSink}");
        writer.WriteLine($"- **post-hoc amendment** — {holds.BoundedDefaultAuthority.PostHocAmendmentRule}");
        writer.WriteLine();
        writer.WriteLine("Paste-ready evidence operation:");
        writer.WriteLine();
        writer.WriteLine("```bash");
        writer.WriteLine(holds.BoundedDefaultAuthority.EvidenceOperation);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"> **Semantic exclusion:** {holds.BoundedDefaultAuthority.SemanticExclusionRule}");
        writer.WriteLine();

        writer.WriteLine("### Periodic design-reminder loop");
        writer.WriteLine();
        writer.WriteLine(holds.DesignReminderLoop.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **sender** — {holds.DesignReminderLoop.Sender}");
        writer.WriteLine($"- **interval** — {holds.DesignReminderLoop.IntervalClass}");
        writer.WriteLine($"- **one per interval per clarification** — {holds.DesignReminderLoop.OnePerIntervalRule}");
        writer.WriteLine($"- **stop on answer** — {holds.DesignReminderLoop.StopCondition}");
        writer.WriteLine($"- **operator app** — {holds.DesignReminderLoop.OperatorAppNote}");
        writer.WriteLine();

        writer.WriteLine($"- **detection** — {holds.DetectionReference}");
        writer.WriteLine();
    }

    private static void WriteMarkdown(TextWriter writer, OrchestratorThreadGuide guide)
    {
        // One declared identity, rendered per mode. The renderer holds no copy
        // of either title, so the declaration and the document cannot disagree.
        writer.WriteLine(SessionLayerSections.DocumentTitle.For(
            guide.SessionLayer?.Mode == SessionLayerMode.HerdrOnly));
        writer.WriteLine();

        // G570: which transport this rendering is for, before any
        // transport-specific instruction the reader might otherwise follow.
        if (guide.SessionLayer is { } sessionLayer)
        {
            writer.WriteLine("## Session layer");
            writer.WriteLine();
            writer.WriteLine(sessionLayer.Summary);
            writer.WriteLine();
            writer.WriteLine($"- {sessionLayer.Exclusivity}");
            writer.WriteLine($"- {sessionLayer.PreviewScoping}");
            writer.WriteLine($"- selection — {sessionLayer.Selection}");
            if (sessionLayer.ResidualAgmsgMechanics is { } residual)
            {
                writer.WriteLine($"- {residual}");
            }
            writer.WriteLine();
        }

        // G500: the setup intake comes FIRST — a design-thread agent must land on
        // an operational outcome (missing-inputs / setup-ready / blocked) before
        // the long reference material below.
        WriteSetupIntake(writer, guide.SetupIntake);

        writer.WriteLine(guide.Summary);
        writer.WriteLine();

        writer.WriteLine("## Mode separation");
        writer.WriteLine();
        writer.WriteLine($"- **timer-loop mode** — {guide.ModeSeparation.TimerLoopMode}");
        writer.WriteLine($"- **orchestrator-message mode** — {guide.ModeSeparation.OrchestratorMessageMode}");
        writer.WriteLine($"- **mixed-mode warning** — {guide.ModeSeparation.MixedModeWarning}");
        writer.WriteLine();

        writer.WriteLine("## Role boundary (design authors; orchestrator coordinates)");
        writer.WriteLine();
        writer.WriteLine(guide.RoleBoundary.Summary);
        writer.WriteLine();
        writer.WriteLine("### Design owns");
        writer.WriteLine();
        foreach (var item in guide.RoleBoundary.DesignOwns)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Orchestrator owns");
        writer.WriteLine();
        foreach (var item in guide.RoleBoundary.OrchestratorOwns)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **missing packet** — {guide.RoleBoundary.MissingPacketResponse}");
        writer.WriteLine($"- **release-prep** — {guide.RoleBoundary.ReleasePrepRule}");
        writer.WriteLine($"- **design↔orchestrator double-check** — {guide.RoleBoundary.DoubleCheckRule}");
        writer.WriteLine();
        writer.WriteLine("Structured packet-needed message (orchestrator → design):");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.RoleBoundary.MissingPacketMessageTemplate);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## Setup (starting orchestrator mode)");
        writer.WriteLine();
        writer.WriteLine(guide.Setup.Summary);
        writer.WriteLine();
        writer.WriteLine("### Decide / record");
        writer.WriteLine();
        foreach (var decision in guide.Setup.Decisions)
        {
            writer.WriteLine($"- {decision}");
        }
        writer.WriteLine();
        writer.WriteLine("### Setup checklist");
        writer.WriteLine();
        for (var i = 0; i < guide.Setup.Checklist.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.Setup.Checklist[i]}");
        }
        writer.WriteLine();
        writer.WriteLine("### agmsg commands");
        writer.WriteLine();
        foreach (var command in guide.Setup.AgmsgCommands)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **ping test** — {guide.Setup.PingTest}");
        writer.WriteLine();
        writer.WriteLine("### Cleanup");
        writer.WriteLine();
        foreach (var step in guide.Setup.Cleanup)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Warning:** {guide.Setup.Warning}");
        writer.WriteLine();

        writer.WriteLine("## Setup intake form");
        writer.WriteLine();
        writer.WriteLine(guide.IntakeForm.Summary);
        writer.WriteLine();
        writer.WriteLine("### Ask for / infer");
        writer.WriteLine();
        foreach (var question in guide.IntakeForm.Questions)
        {
            writer.WriteLine($"- {question}");
        }
        writer.WriteLine();
        writer.WriteLine("### Recommended defaults (when inputs are incomplete)");
        writer.WriteLine();
        foreach (var fallback in guide.IntakeForm.Defaults)
        {
            writer.WriteLine($"- {fallback}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **design delivery** — {guide.IntakeForm.DesignDeliveryNote}");
        writer.WriteLine();
        writer.WriteLine("### Role startup messages (assume the agmsg role)");
        writer.WriteLine();
        foreach (var startup in guide.IntakeForm.RoleStartupMessages)
        {
            writer.WriteLine($"- **{startup.AgentType}** — `{startup.ActasInvocation}` — {startup.Note}");
        }
        writer.WriteLine();

        WriteTerminalWorkspaceProvisioning(writer, guide.TerminalWorkspaceProvisioning);

        WriteDesignWorkspaceSupervision(writer, guide.DesignWorkspaceSupervision);

        WriteDesignDecisionHolds(writer, guide.DesignDecisionHolds);

        WriteCrossProjectIsolation(writer, guide.CrossProjectIsolation);

        writer.WriteLine("## Preflight (all three cwds)");
        writer.WriteLine();
        writer.WriteLine(guide.Preflight.Summary);
        writer.WriteLine();
        foreach (var check in guide.Preflight.Checks)
        {
            writer.WriteLine($"- {check}");
        }
        writer.WriteLine();

        writer.WriteLine("## Receiver readiness");
        writer.WriteLine();
        writer.WriteLine(guide.ReceiverReadiness.Summary);
        writer.WriteLine();
        writer.WriteLine("### Startup order");
        writer.WriteLine();
        for (var i = 0; i < guide.ReceiverReadiness.StartupOrder.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.ReceiverReadiness.StartupOrder[i]}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Send-before-ready:** {guide.ReceiverReadiness.SendBeforeReadyWarning}");
        writer.WriteLine();
        writer.WriteLine("Copy-paste operator message when receivers were launched after the initial messages were sent:");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.ReceiverReadiness.RecoveryMessageTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Readiness states");
        writer.WriteLine();
        foreach (var state in guide.ReceiverReadiness.States)
        {
            writer.WriteLine($"- **{state.State}** — {state.Meaning}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **ping/ack required** — {guide.ReceiverReadiness.PingAckRequired}");
        writer.WriteLine();
        writer.WriteLine("### If a receiver is not ready");
        writer.WriteLine();
        foreach (var item in guide.ReceiverReadiness.NotReadyRecovery)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **`watch.sh`** — {guide.ReceiverReadiness.WatchNote}");
        writer.WriteLine($"- **Codex Desktop app** — {guide.ReceiverReadiness.CodexDesktopNote}");
        writer.WriteLine();
        writer.WriteLine("### Diagnostic commands (agmsg scripts only)");
        writer.WriteLine();
        foreach (var command in guide.ReceiverReadiness.DiagnosticCommands)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();

        writer.WriteLine("## Troubleshooting");
        writer.WriteLine();
        foreach (var entry in guide.Troubleshooting)
        {
            writer.WriteLine($"- **{entry.Symptom}** — {entry.Action}");
        }
        writer.WriteLine();

        writer.WriteLine("## Monitor recovery");
        writer.WriteLine();
        foreach (var entry in guide.MonitorRecovery)
        {
            writer.WriteLine($"- **{entry.Symptom}** — {entry.Action}");
        }
        writer.WriteLine();

        writer.WriteLine("## Monitor tool vs delivery-mode (G511)");
        writer.WriteLine();
        writer.WriteLine(guide.MonitorToolDistinction.Summary);
        writer.WriteLine();
        writer.WriteLine(guide.MonitorToolDistinction.DeliveryModeNote);
        writer.WriteLine();
        writer.WriteLine("Live-attachment success markers — verify all four to confirm the inbox stream is live:");
        writer.WriteLine();
        foreach (var marker in guide.MonitorToolDistinction.SuccessMarkers)
        {
            writer.WriteLine($"- {marker}");
        }
        writer.WriteLine();
        writer.WriteLine("Failure markers — a receiver reporting `mode=monitor` may still be silently broken:");
        writer.WriteLine();
        foreach (var marker in guide.MonitorToolDistinction.FailureMarkers)
        {
            writer.WriteLine($"- {marker}");
        }
        writer.WriteLine();
        writer.WriteLine("Trust-repair runbook (when the success markers are missing):");
        writer.WriteLine();
        foreach (var step in guide.MonitorToolDistinction.TrustRepair)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine("Windows guidance:");
        writer.WriteLine();
        foreach (var note in guide.MonitorToolDistinction.WindowsGuidance)
        {
            writer.WriteLine($"- {note}");
        }
        writer.WriteLine();
        writer.WriteLine("Fallback ladder — orchestrator mode stays usable without realtime Monitor:");
        writer.WriteLine();
        foreach (var step in guide.MonitorToolDistinction.FallbackLadder)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine("Missing-Monitor project-settings diagnosis (G517) — when `ToolSearch select:Monitor` finds no Monitor tool at all:");
        writer.WriteLine();
        foreach (var step in guide.MonitorToolDistinction.ProjectSettingsDiagnosis)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Codex monitor (beta) failure modes (G521)");
        writer.WriteLine();
        writer.WriteLine($"> {guide.CodexBridgeGuidance.ObservedVersions}");
        writer.WriteLine();
        writer.WriteLine($"- **setup preflight** — {guide.CodexBridgeGuidance.SetupPreflight}");
        writer.WriteLine();
        writer.WriteLine("Healthy-state markers:");
        writer.WriteLine();
        foreach (var marker in guide.CodexBridgeGuidance.HealthyStateMarkers)
        {
            writer.WriteLine($"- {marker}");
        }
        writer.WriteLine();
        writer.WriteLine("Troubleshooting:");
        writer.WriteLine();
        foreach (var entry in guide.CodexBridgeGuidance.Troubleshooting)
        {
            writer.WriteLine($"- **{entry.Symptom}** — {entry.Action}");
        }
        writer.WriteLine();
        writer.WriteLine(guide.CodexBridgeGuidance.ReferenceLink);
        writer.WriteLine();

        writer.WriteLine("## Domain routing — single-domain vs multi-domain");
        writer.WriteLine();
        writer.WriteLine($"- selected mode: `{guide.DomainRouting.Mode}`");
        writer.WriteLine($"- **single-domain** — {guide.DomainRouting.SingleDomainRule}");
        writer.WriteLine($"- **multi-domain** — {guide.DomainRouting.MultiDomainRule}");
        writer.WriteLine($"- **execution-unit prefix** — {guide.DomainRouting.PrefixMismatchNote}");
        writer.WriteLine();
        writer.WriteLine("Routing metadata required for every multi-domain delegation:");
        writer.WriteLine();
        foreach (var field in guide.DomainRouting.RoutingMetadataFields)
        {
            writer.WriteLine($"- {field}");
        }
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DomainRouting.DelegationExample);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## Scheduled orchestrator cadence");
        writer.WriteLine();
        writer.WriteLine(guide.Scheduling.Summary);
        writer.WriteLine();
        writer.WriteLine($"- scheduled thread when an explicit timer is used: `{guide.Scheduling.ScheduledThread}` (the only thread ever scheduled)");
        writer.WriteLine($"- **receivers are loopless** — {guide.Scheduling.ReceiverNote}");
        writer.WriteLine();
        writer.WriteLine("### Codex automation (5m) — orchestrator (fallback/legacy, optional)");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.Scheduling.CodexSetupPrompt);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Claude `/loop 5m` — orchestrator (fallback/legacy, optional)");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.Scheduling.ClaudeLoopSetupPrompt);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Each orchestrator wake");
        writer.WriteLine();
        foreach (var responsibility in guide.Scheduling.WakeResponsibilities)
        {
            writer.WriteLine($"- {responsibility}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **repair** — {guide.Scheduling.RepairVsEscalate.Repair}");
        writer.WriteLine($"- **escalate** — {guide.Scheduling.RepairVsEscalate.Escalate}");
        writer.WriteLine();

        writer.WriteLine("## CI wait state");
        writer.WriteLine();
        writer.WriteLine(guide.CiWaitState.Summary);
        writer.WriteLine();
        foreach (var state in guide.CiWaitState.States)
        {
            writer.WriteLine($"- **{state.State}** — {state.Routing}");
        }
        writer.WriteLine();

        writer.WriteLine("## Draft PR reviewability");
        writer.WriteLine();
        writer.WriteLine(guide.DraftPrReviewability);
        writer.WriteLine();

        writer.WriteLine("## Next-slice publication");
        writer.WriteLine();
        writer.WriteLine(guide.NextSlicePublication.Summary);
        writer.WriteLine();
        writer.WriteLine($"- one_per_wake: {(guide.NextSlicePublication.OnePerWake ? "yes" : "no")}");
        writer.WriteLine();
        writer.WriteLine("### Publish only when ALL hold");
        writer.WriteLine();
        foreach (var precondition in guide.NextSlicePublication.Preconditions)
        {
            writer.WriteLine($"- {precondition}");
        }
        writer.WriteLine();
        writer.WriteLine("### Blocked by (hold or escalate)");
        writer.WriteLine();
        foreach (var blocker in guide.NextSlicePublication.Blockers)
        {
            writer.WriteLine($"- {blocker}");
        }
        writer.WriteLine();
        writer.WriteLine("### Canonical publish commands");
        writer.WriteLine();
        foreach (var command in guide.NextSlicePublication.CanonicalCommands)
        {
            writer.WriteLine($"- {command}");
        }
        writer.WriteLine();
        writer.WriteLine("### Post-publish verification");
        writer.WriteLine();
        foreach (var step in guide.NextSlicePublication.PostPublishVerification)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## End-of-wake check (G523/G524)");
        writer.WriteLine();
        writer.WriteLine(guide.EndOfWakeCheck.Summary);
        writer.WriteLine();
        writer.WriteLine($"- command: `{guide.EndOfWakeCheck.Command}`");
        writer.WriteLine($"- **never defer** — {guide.EndOfWakeCheck.NeverDeferRule}");
        writer.WriteLine($"- **escalate instead of defer** — {guide.EndOfWakeCheck.EscalateInsteadOfDeferRule}");
        writer.WriteLine();

        writer.WriteLine("## Dispatch verification (G524)");
        writer.WriteLine();
        writer.WriteLine(guide.DispatchVerification.Rule);
        writer.WriteLine();
        writer.WriteLine($"- {guide.DispatchVerification.DeadAddressExample}");
        writer.WriteLine();

        writer.WriteLine("## Dependency planning");
        writer.WriteLine();
        writer.WriteLine(guide.DependencyPlanning.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **selection rule** — {guide.DependencyPlanning.SelectionRule}");
        writer.WriteLine($"- **dependent hold** — {guide.DependencyPlanning.DependentHold}");
        writer.WriteLine();
        writer.WriteLine("### Dependency statuses");
        writer.WriteLine();
        foreach (var status in guide.DependencyPlanning.Statuses)
        {
            writer.WriteLine($"- **{status.Status}** — {status.Action}");
        }
        writer.WriteLine();
        writer.WriteLine("### Escalate only when");
        writer.WriteLine();
        foreach (var escalation in guide.DependencyPlanning.EscalationCases)
        {
            writer.WriteLine($"- {escalation}");
        }
        writer.WriteLine();

        writer.WriteLine("## Stale-thread health check");
        writer.WriteLine();
        writer.WriteLine(guide.StaleThreadHealthCheck.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **no-reply threshold** — {guide.StaleThreadHealthCheck.NoReplyThreshold}");
        writer.WriteLine();
        writer.WriteLine("### Procedure");
        writer.WriteLine();
        for (var i = 0; i < guide.StaleThreadHealthCheck.Procedure.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.StaleThreadHealthCheck.Procedure[i]}");
        }
        writer.WriteLine();
        writer.WriteLine("### Status-request message");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.StaleThreadHealthCheck.StatusRequestTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Receiver statuses");
        writer.WriteLine();
        foreach (var status in guide.StaleThreadHealthCheck.ReceiverStatuses)
        {
            writer.WriteLine($"- **{status.Status}** — {status.Meaning}");
        }
        writer.WriteLine();
        writer.WriteLine("### Health-check safety");
        writer.WriteLine();
        foreach (var rule in guide.StaleThreadHealthCheck.Safety)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();

        writer.WriteLine("## Design-thread escalation filter");
        writer.WriteLine();
        writer.WriteLine(guide.DesignThreadEscalation.Summary);
        writer.WriteLine();
        writer.WriteLine("### Kept internal (no design-thread message by default)");
        writer.WriteLine();
        foreach (var item in guide.DesignThreadEscalation.KeptInternal)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Escalate to the design thread when");
        writer.WriteLine();
        foreach (var item in guide.DesignThreadEscalation.EscalateWhen)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Design escalation message");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DesignThreadEscalation.EscalationMessageTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        foreach (var field in guide.DesignThreadEscalation.MessageFields)
        {
            writer.WriteLine($"- {field}");
        }
        writer.WriteLine();

        writer.WriteLine("## Design / human receiver (optional)");
        writer.WriteLine();
        writer.WriteLine(guide.DesignReceiver.Summary);
        writer.WriteLine();
        writer.WriteLine($"- optional for routine operation: {(guide.DesignReceiver.Optional ? "yes" : "no")} (recommended for escalation delivery)");
        writer.WriteLine();
        writer.WriteLine("### Four logical roles (when design receiving is enabled)");
        writer.WriteLine();
        foreach (var role in guide.DesignReceiver.Roles)
        {
            writer.WriteLine($"- {role}");
        }
        writer.WriteLine();
        writer.WriteLine("### Design receiver setup");
        writer.WriteLine();
        foreach (var step in guide.DesignReceiver.Setup)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();
        writer.WriteLine("### Minimal manual inbox trigger prompt (paste into the design thread)");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(guide.DesignReceiver.ManualInboxTriggerPrompt);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"> **Pre-start messages:** {guide.DesignReceiver.PreStartNote}");
        writer.WriteLine();

        writer.WriteLine("## Design handoff (start / resume)");
        writer.WriteLine();
        writer.WriteLine(guide.DesignHandoff.Summary);
        writer.WriteLine();
        writer.WriteLine("First message — design → orchestrator (paste into the design thread):");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DesignHandoff.FirstMessageTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"- **autonomous publish** — {guide.DesignHandoff.AutonomousPublishRule}");
        writer.WriteLine($"- **escalation boundary** — {guide.DesignHandoff.EscalationBoundary}");
        writer.WriteLine($"- **design inbox workflow** — {guide.DesignHandoff.DesignInboxWorkflow}");
        writer.WriteLine();

        writer.WriteLine("## Design-thread watchdog (recommended safety net)");
        writer.WriteLine();
        writer.WriteLine(guide.DesignWatchdog.Summary);
        writer.WriteLine();
        writer.WriteLine($"- optional: {(guide.DesignWatchdog.Optional ? "yes" : "no")}");
        writer.WriteLine($"- **frequency** — {guide.DesignWatchdog.Frequency}");
        writer.WriteLine();
        writer.WriteLine("Loop setup prompt (paste into the design thread):");
        writer.WriteLine();
        writer.WriteLine(guide.DesignWatchdog.LoopSetupPrompt);
        writer.WriteLine();
        writer.WriteLine($"- **failure visibility** — {guide.DesignWatchdog.FailureVisibilityRule}");
        writer.WriteLine();
        writer.WriteLine("Heartbeat command:");
        writer.WriteLine();
        writer.WriteLine("```");
        writer.WriteLine(guide.DesignWatchdog.HeartbeatCommandExample);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("### Watchdog checks");
        writer.WriteLine();
        foreach (var check in guide.DesignWatchdog.Checks)
        {
            writer.WriteLine($"- {check}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **action** — {guide.DesignWatchdog.Action}");
        writer.WriteLine();
        writer.WriteLine("Repair/status-request template:");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.DesignWatchdog.RepairStatusRequestTemplate);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"- **stop condition** — {guide.DesignWatchdog.StopCondition}");
        writer.WriteLine();
        writer.WriteLine("### Watchdog safety rules");
        writer.WriteLine();
        foreach (var rule in guide.DesignWatchdog.SafetyRules)
        {
            writer.WriteLine($"- {rule}");
        }
        writer.WriteLine();
        writer.WriteLine($"- **fallback timer** — {guide.DesignWatchdog.FallbackTimerNote}");
        writer.WriteLine($"- **measured weakness** — {guide.DesignWatchdog.MeasuredWeakness}");
        writer.WriteLine();

        writer.WriteLine("## Orchestrator-side long-interval automation (alternative safety net)");
        writer.WriteLine();
        writer.WriteLine(guide.OrchestratorAutomation.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **frequency** — {guide.OrchestratorAutomation.Frequency}");
        writer.WriteLine($"- **trade-off** — {guide.OrchestratorAutomation.TradeOff}");
        writer.WriteLine();
        writer.WriteLine("Command:");
        writer.WriteLine();
        writer.WriteLine("```");
        writer.WriteLine(guide.OrchestratorAutomation.CommandExample);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("Setup prompt (paste into the orchestrator thread):");
        writer.WriteLine();
        writer.WriteLine(guide.OrchestratorAutomation.SetupPrompt);
        writer.WriteLine();
        writer.WriteLine($"- **{guide.OrchestratorAutomation.RetiredCronNote}**");
        writer.WriteLine();

        writer.WriteLine("## Design traffic-controller playbook");
        writer.WriteLine();
        writer.WriteLine(guide.DesignTrafficController.Summary);
        writer.WriteLine();
        writer.WriteLine("### Playbook");
        writer.WriteLine();
        for (var i = 0; i < guide.DesignTrafficController.Playbook.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.DesignTrafficController.Playbook[i]}");
        }
        writer.WriteLine();
        writer.WriteLine("### \"Orchestrator appears idle\" diagnostic (before escalating)");
        writer.WriteLine();
        for (var i = 0; i < guide.DesignTrafficController.IdleDiagnostic.Count; i++)
        {
            writer.WriteLine($"{i + 1}. {guide.DesignTrafficController.IdleDiagnostic[i]}");
        }
        writer.WriteLine();
        writer.WriteLine($"> **Context-only:** {guide.DesignTrafficController.ContextOnlyRule}");
        writer.WriteLine();

        writer.WriteLine("## Managed worktree cleanup");
        writer.WriteLine();
        writer.WriteLine(guide.WorktreeManagement.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **managed root** — {guide.WorktreeManagement.ManagedRoot}");
        writer.WriteLine($"- **approval policy** — {guide.WorktreeManagement.ApprovalPolicyNote}");
        writer.WriteLine();
        writer.WriteLine("### Allocation");
        writer.WriteLine();
        foreach (var item in guide.WorktreeManagement.Allocation)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Safe cleanup");
        writer.WriteLine();
        foreach (var item in guide.WorktreeManagement.SafeCleanup)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();
        writer.WriteLine("### Refuse cleanup when");
        writer.WriteLine();
        foreach (var item in guide.WorktreeManagement.RefuseWhen)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Review delegation — managed worktrees and design alignment");
        writer.WriteLine();
        writer.WriteLine(guide.ReviewDelegationContract.Summary);
        writer.WriteLine();
        writer.WriteLine($"- **managed worktree root** — {guide.ReviewDelegationContract.ManagedWorktreeRoot}");
        writer.WriteLine($"- **prohibited pattern** — {guide.ReviewDelegationContract.ProhibitedPattern}");
        writer.WriteLine($"- **cleanup rule** — {guide.ReviewDelegationContract.CleanupRule}");
        writer.WriteLine($"- **unsafe/stale path rule** — {guide.ReviewDelegationContract.UnsafeStalePathRule}");
        writer.WriteLine();
        writer.WriteLine("Review delegation example (orchestrator → review):");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.ReviewDelegationContract.DelegationExample);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("Design-alignment sources a review reply may cite as checked:");
        writer.WriteLine();
        foreach (var source in guide.ReviewDelegationContract.DesignAlignmentSources)
        {
            writer.WriteLine($"- {source}");
        }
        writer.WriteLine();

        writer.WriteLine("## Thread prompts");
        foreach (var thread in guide.Threads)
        {
            writer.WriteLine();
            writer.WriteLine($"### {thread.Role}");
            writer.WriteLine();
            writer.WriteLine($"- purpose: {thread.Purpose}");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(thread.Prompt);
            writer.WriteLine("```");
        }
        writer.WriteLine();

        writer.WriteLine("## agmsg reply contract");
        writer.WriteLine();
        writer.WriteLine(guide.AgmsgReplyContract.Description);
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.AgmsgReplyContract.Accepted);
        writer.WriteLine(guide.AgmsgReplyContract.Progress);
        writer.WriteLine(guide.AgmsgReplyContract.Completed);
        writer.WriteLine(guide.AgmsgReplyContract.Blocked);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("Review `completed` reply — must include design-alignment evidence:");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.AgmsgReplyContract.ReviewCompletedExample);
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine($"- **review-incomplete rule** — {guide.AgmsgReplyContract.ReviewIncompleteRule}");
        writer.WriteLine();

        writer.WriteLine("## Orchestrator first wake");
        writer.WriteLine();
        foreach (var step in guide.OrchestratorFirstWake)
        {
            writer.WriteLine($"1. {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Safety boundaries");
        writer.WriteLine();
        foreach (var boundary in guide.SafetyBoundaries)
        {
            writer.WriteLine($"- {boundary}");
        }
        writer.WriteLine();

        writer.WriteLine("## Detailed guide commands");
        writer.WriteLine();
        foreach (var command in guide.DetailedGuideCommands)
        {
            writer.WriteLine($"- `{command}`");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide orchestrator-thread");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Renders paste-ready prompts for the PRIMARY agmsg-backed four-thread orchestrator model");
        writer.WriteLine("(design/orchestrator/implementation/review) plus the implementation/review threads it");
        writer.WriteLine("delegates to. agmsg is a signal layer only; intent-cli and GitHub remain authoritative.");
        writer.WriteLine("Timer-loop mode remains fully supported as the simpler alternative and is not replaced.");
        writer.WriteLine();
        writer.WriteLine("--mode single-domain (default) scopes the orchestrator to one domain and treats other-domain");
        writer.WriteLine("items visible in a shared host repo as out of scope. --mode multi-domain requires explicit");
        writer.WriteLine("routing metadata (domain, execution unit, target repo, implementation + review cwd/worktree,");
        writer.WriteLine("base branch policy, destination thread) for each delegation, since one repo may serve several");
        writer.WriteLine("domains. An execution-unit prefix mismatch alone is not treated as a wrong-repo signal.");
        writer.WriteLine();
        writer.WriteLine("Setup intake (rendered first): supply --domain, --target-repo, --orchestrator-path,");
        writer.WriteLine("--implementation-path, --review-path, --orchestrator-agent, --implementer-agent,");
        writer.WriteLine("--reviewer-agent, --team, --delivery-mode, and --existing-loop-policy (none|will-stop|keep) to");
        writer.WriteLine("get a setup-ready plan with copy-paste agmsg commands and first role prompts. Missing fields");
        writer.WriteLine("yield status missing-inputs (only the missing fields are listed); a kept existing loop yields");
        writer.WriteLine("status blocked. The role agents default to --agent when not set individually.");
    }
}

/// <summary>
/// G500: operational orchestrator setup intake. Status is one of
/// <c>missing-inputs</c> / <c>setup-ready</c> / <c>blocked</c>; setup-ready
/// carries copy-paste agmsg commands and first role prompts.
/// </summary>
internal sealed record OrchestratorSetupIntake
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("headline")]
    public required string Headline { get; init; }

    [JsonPropertyName("missing_fields")]
    public required IReadOnlyList<string> MissingFields { get; init; }

    [JsonPropertyName("inputs")]
    public required OrchestratorSetupInputs Inputs { get; init; }

    [JsonPropertyName("agmsg_commands")]
    public IReadOnlyList<string>? AgmsgCommands { get; init; }

    [JsonPropertyName("role_prompts")]
    public IReadOnlyList<OrchestratorThreadPrompt>? RolePrompts { get; init; }

    [JsonPropertyName("first_validation")]
    public IReadOnlyList<string>? FirstValidation { get; init; }

    [JsonPropertyName("loopless_receiver_note")]
    public required string LooplessReceiverNote { get; init; }

    /// <summary>
    /// G570: the session layer this setup is for, recorded at intake so a
    /// "we want herdr only" asked for at first contact is honoured and
    /// remembered rather than re-asked every wake.
    /// </summary>
    [JsonPropertyName("session_layer_mode")]
    public string? SessionLayerMode { get; init; }

    [JsonPropertyName("session_layer_note")]
    public string? SessionLayerNote { get; init; }
}

internal sealed record OrchestratorSetupInputs
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("orchestrator_folder")]
    public required string OrchestratorFolder { get; init; }

    [JsonPropertyName("implementation_folder")]
    public required string ImplementationFolder { get; init; }

    [JsonPropertyName("review_folder")]
    public required string ReviewFolder { get; init; }

    [JsonPropertyName("orchestrator_agent")]
    public string? OrchestratorAgent { get; init; }

    [JsonPropertyName("implementer_agent")]
    public string? ImplementerAgent { get; init; }

    [JsonPropertyName("reviewer_agent")]
    public string? ReviewerAgent { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("delivery_mode")]
    public required string DeliveryMode { get; init; }

    [JsonPropertyName("existing_loop_policy")]
    public string? ExistingLoopPolicy { get; init; }
}

internal sealed record OrchestratorThreadGuide
{
    /// <summary>G570: which session-layer transport this guide is rendering for, and how that was decided.</summary>
    [JsonPropertyName("session_layer")]
    public OrchestratorSessionLayer? SessionLayer { get; init; }

    [JsonPropertyName("setup_intake")]
    public required OrchestratorSetupIntake SetupIntake { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("mode_separation")]
    public required OrchestratorModeSeparation ModeSeparation { get; init; }

    [JsonPropertyName("role_boundary")]
    public required OrchestratorRoleBoundary RoleBoundary { get; init; }

    [JsonPropertyName("domain_routing")]
    public required OrchestratorDomainRouting DomainRouting { get; init; }

    [JsonPropertyName("scheduling")]
    public required OrchestratorScheduling Scheduling { get; init; }

    [JsonPropertyName("ci_wait_state")]
    public required OrchestratorCiWaitState CiWaitState { get; init; }

    [JsonPropertyName("draft_pr_reviewability")]
    public required string DraftPrReviewability { get; init; }

    [JsonPropertyName("next_slice_publication")]
    public required OrchestratorNextSlicePublication NextSlicePublication { get; init; }

    [JsonPropertyName("end_of_wake_check")]
    public required OrchestratorEndOfWakeCheck EndOfWakeCheck { get; init; }

    [JsonPropertyName("dispatch_verification")]
    public required OrchestratorDispatchVerification DispatchVerification { get; init; }

    [JsonPropertyName("dependency_planning")]
    public required OrchestratorDependencyPlanning DependencyPlanning { get; init; }

    [JsonPropertyName("stale_thread_health_check")]
    public required OrchestratorStaleThreadHealthCheck StaleThreadHealthCheck { get; init; }

    [JsonPropertyName("design_thread_escalation")]
    public required OrchestratorDesignThreadEscalation DesignThreadEscalation { get; init; }

    [JsonPropertyName("design_receiver")]
    public required OrchestratorDesignReceiver DesignReceiver { get; init; }

    [JsonPropertyName("design_handoff")]
    public required OrchestratorDesignHandoff DesignHandoff { get; init; }

    [JsonPropertyName("design_watchdog")]
    public required OrchestratorDesignWatchdog DesignWatchdog { get; init; }

    [JsonPropertyName("orchestrator_automation_alternative")]
    public required OrchestratorAutomationAlternative OrchestratorAutomation { get; init; }

    [JsonPropertyName("monitor_recovery")]
    public required IReadOnlyList<OrchestratorTroubleshooting> MonitorRecovery { get; init; }

    [JsonPropertyName("monitor_tool_distinction")]
    public required OrchestratorMonitorDistinction MonitorToolDistinction { get; init; }

    [JsonPropertyName("codex_bridge_guidance")]
    public required OrchestratorCodexBridgeGuidance CodexBridgeGuidance { get; init; }

    [JsonPropertyName("intake_form")]
    public required OrchestratorIntakeForm IntakeForm { get; init; }

    [JsonPropertyName("terminal_workspace_provisioning")]
    public required OrchestratorTerminalWorkspaceProvisioning TerminalWorkspaceProvisioning { get; init; }

    [JsonPropertyName("design_workspace_supervision")]
    public required OrchestratorDesignWorkspaceSupervision DesignWorkspaceSupervision { get; init; }

    [JsonPropertyName("design_decision_holds")]
    public required OrchestratorDesignDecisionHolds DesignDecisionHolds { get; init; }

    [JsonPropertyName("cross_project_isolation")]
    public required OrchestratorCrossProjectIsolation CrossProjectIsolation { get; init; }

    [JsonPropertyName("design_traffic_controller")]
    public required OrchestratorDesignTrafficController DesignTrafficController { get; init; }

    [JsonPropertyName("worktree_management")]
    public required OrchestratorWorktreeManagement WorktreeManagement { get; init; }

    [JsonPropertyName("review_delegation_contract")]
    public required OrchestratorReviewDelegationContract ReviewDelegationContract { get; init; }

    [JsonPropertyName("setup")]
    public required OrchestratorSetup Setup { get; init; }

    [JsonPropertyName("preflight")]
    public required OrchestratorPreflight Preflight { get; init; }

    [JsonPropertyName("troubleshooting")]
    public required IReadOnlyList<OrchestratorTroubleshooting> Troubleshooting { get; init; }

    [JsonPropertyName("receiver_readiness")]
    public required OrchestratorReceiverReadiness ReceiverReadiness { get; init; }

    [JsonPropertyName("threads")]
    public required IReadOnlyList<OrchestratorThreadPrompt> Threads { get; init; }

    [JsonPropertyName("agmsg_reply_contract")]
    public required OrchestratorReplyContract AgmsgReplyContract { get; init; }

    [JsonPropertyName("orchestrator_first_wake")]
    public required IReadOnlyList<string> OrchestratorFirstWake { get; init; }

    [JsonPropertyName("safety_boundaries")]
    public required IReadOnlyList<string> SafetyBoundaries { get; init; }

    [JsonPropertyName("detailed_guide_commands")]
    public required IReadOnlyList<string> DetailedGuideCommands { get; init; }
}

internal sealed record OrchestratorRoleBoundary
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("design_owns")]
    public required IReadOnlyList<string> DesignOwns { get; init; }

    [JsonPropertyName("orchestrator_owns")]
    public required IReadOnlyList<string> OrchestratorOwns { get; init; }

    [JsonPropertyName("missing_packet_response")]
    public required string MissingPacketResponse { get; init; }

    [JsonPropertyName("missing_packet_message_template")]
    public required string MissingPacketMessageTemplate { get; init; }

    [JsonPropertyName("release_prep_rule")]
    public required string ReleasePrepRule { get; init; }

    /// <summary>G540: neither thread decides design content alone — intent shaping, packet content/acceptance criteria, release scope, and prioritization rulings are always consulted between design and orchestrator.</summary>
    [JsonPropertyName("double_check_rule")]
    public required string DoubleCheckRule { get; init; }
}

internal sealed record OrchestratorModeSeparation
{
    [JsonPropertyName("timer_loop_mode")]
    public required string TimerLoopMode { get; init; }

    [JsonPropertyName("orchestrator_message_mode")]
    public required string OrchestratorMessageMode { get; init; }

    [JsonPropertyName("mixed_mode_warning")]
    public required string MixedModeWarning { get; init; }
}

internal sealed record OrchestratorDomainRouting
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("single_domain_rule")]
    public required string SingleDomainRule { get; init; }

    [JsonPropertyName("multi_domain_rule")]
    public required string MultiDomainRule { get; init; }

    [JsonPropertyName("routing_metadata_fields")]
    public required IReadOnlyList<string> RoutingMetadataFields { get; init; }

    [JsonPropertyName("delegation_example")]
    public required string DelegationExample { get; init; }

    [JsonPropertyName("prefix_mismatch_note")]
    public required string PrefixMismatchNote { get; init; }
}

internal sealed record OrchestratorScheduling
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("scheduled_thread")]
    public required string ScheduledThread { get; init; }

    [JsonPropertyName("receiver_note")]
    public required string ReceiverNote { get; init; }

    [JsonPropertyName("codex_setup_prompt")]
    public required string CodexSetupPrompt { get; init; }

    [JsonPropertyName("claude_loop_setup_prompt")]
    public required string ClaudeLoopSetupPrompt { get; init; }

    [JsonPropertyName("wake_responsibilities")]
    public required IReadOnlyList<string> WakeResponsibilities { get; init; }

    [JsonPropertyName("repair_vs_escalate")]
    public required OrchestratorRepairEscalate RepairVsEscalate { get; init; }
}

internal sealed record OrchestratorRepairEscalate
{
    [JsonPropertyName("repair")]
    public required string Repair { get; init; }

    [JsonPropertyName("escalate")]
    public required string Escalate { get; init; }
}

internal sealed record OrchestratorCiWaitState
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("states")]
    public required IReadOnlyList<OrchestratorCiState> States { get; init; }
}

internal sealed record OrchestratorCiState
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("routing")]
    public required string Routing { get; init; }
}

internal sealed record OrchestratorWorktreeManagement
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("managed_root")]
    public required string ManagedRoot { get; init; }

    [JsonPropertyName("allocation")]
    public required IReadOnlyList<string> Allocation { get; init; }

    [JsonPropertyName("safe_cleanup")]
    public required IReadOnlyList<string> SafeCleanup { get; init; }

    [JsonPropertyName("refuse_when")]
    public required IReadOnlyList<string> RefuseWhen { get; init; }

    [JsonPropertyName("approval_policy_note")]
    public required string ApprovalPolicyNote { get; init; }
}

internal sealed record OrchestratorReviewDelegationContract
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("managed_worktree_root")]
    public required string ManagedWorktreeRoot { get; init; }

    [JsonPropertyName("prohibited_pattern")]
    public required string ProhibitedPattern { get; init; }

    [JsonPropertyName("cleanup_rule")]
    public required string CleanupRule { get; init; }

    [JsonPropertyName("unsafe_stale_path_rule")]
    public required string UnsafeStalePathRule { get; init; }

    [JsonPropertyName("delegation_example")]
    public required string DelegationExample { get; init; }

    [JsonPropertyName("design_alignment_sources")]
    public required IReadOnlyList<string> DesignAlignmentSources { get; init; }
}

internal sealed record OrchestratorIntakeForm
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("questions")]
    public required IReadOnlyList<string> Questions { get; init; }

    [JsonPropertyName("defaults")]
    public required IReadOnlyList<string> Defaults { get; init; }

    [JsonPropertyName("design_delivery_note")]
    public required string DesignDeliveryNote { get; init; }

    [JsonPropertyName("role_startup_messages")]
    public required IReadOnlyList<OrchestratorRoleStartup> RoleStartupMessages { get; init; }
}

internal sealed record OrchestratorRoleStartup
{
    [JsonPropertyName("agent_type")]
    public required string AgentType { get; init; }

    [JsonPropertyName("actas_invocation")]
    public required string ActasInvocation { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }
}

/// <summary>
/// G549: terminal-workspace provisioning for a whole team — the setup step
/// that runs BEFORE the existing setup checklist can be executed at all.
/// Earlier guidance assumed the role folders and terminal sessions already
/// existed ("each role runs from its own folder, clone, or worktree") and said
/// nothing about creating them, so a design thread asked to "set this team up"
/// had to supply the missing knowledge itself. This section makes the flow
/// executable end to end with placeholders only: folder provisioning (with the
/// project-scoped-identity reason from G521), workspace topology, shim-safe
/// launch, actas + readiness, role exclusivity/handover, and herdr named as the
/// reference workspace manager whose internals are linked out rather than owned.
/// </summary>
internal sealed record OrchestratorTerminalWorkspaceProvisioning
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("placeholders")]
    public required IReadOnlyList<OrchestratorProvisioningPlaceholder> Placeholders { get; init; }

    [JsonPropertyName("folder_provisioning")]
    public required OrchestratorProvisioningFolders FolderProvisioning { get; init; }

    [JsonPropertyName("topology")]
    public required OrchestratorProvisioningTopology Topology { get; init; }

    [JsonPropertyName("launch_rules")]
    public required OrchestratorProvisioningLaunchRules LaunchRules { get; init; }

    [JsonPropertyName("role_initialization")]
    public required OrchestratorProvisioningRoleInitialization RoleInitialization { get; init; }

    [JsonPropertyName("exclusivity_handover")]
    public required OrchestratorProvisioningHandover ExclusivityHandover { get; init; }

    [JsonPropertyName("reference_manager")]
    public required OrchestratorProvisioningReferenceManager ReferenceManager { get; init; }

    [JsonPropertyName("checklist")]
    public required IReadOnlyList<string> Checklist { get; init; }
}

/// <summary>
/// G550: the other half of the design thread's workspace role. G549 documents
/// how a team comes into existence; this documents how the design thread keeps
/// it moving — under authority the operator granted, over the SESSION layer
/// only. Workflow state (labels, queue, publish) stays with intent-cli, GitHub,
/// and the orchestrator: this record moves no workflow authority. It exists
/// because the field practice lived only in session transcripts, and because a
/// claim lost in a design-session restart window stalled a published issue for
/// 5.5 hours with no supervision layer running.
/// </summary>
/// <summary>
/// G555: G549 provisions ONE team and G550 supervises ONE team; neither
/// mentioned that other teams are running on the same machine. Operator
/// incident (2026-07-29): with several project teams live at once, one
/// project's design thread damaged another project's resources and the
/// operator had to intervene by hand. A near-miss of the same class was
/// avoided earlier that week only by ad-hoc discipline — verifying each pid's
/// cwd before killing anything — discipline that lived in one session
/// transcript rather than in the guide.
///
/// Every substrate on the machine is shared. This record narrows the OBJECT
/// set a supervising thread may act on (only its own team's objects); it does
/// not change the ACTION set, so G550's authority boundary applies unchanged.
/// </summary>
internal sealed record OrchestratorCrossProjectIsolation
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("attribution_before_mutation")]
    public required OrchestratorAttributionRule AttributionBeforeMutation { get; init; }

    [JsonPropertyName("one_workspace_per_team")]
    public required string OneWorkspacePerTeam { get; init; }

    [JsonPropertyName("team_exclusive_role_folders")]
    public required string TeamExclusiveRoleFolders { get; init; }

    [JsonPropertyName("shared_substrates")]
    public required IReadOnlyList<OrchestratorSharedSubstrate> SharedSubstrates { get; init; }

    [JsonPropertyName("non_destructive_recovery")]
    public required OrchestratorNonDestructiveRecovery NonDestructiveRecovery { get; init; }
}

internal sealed record OrchestratorAttributionRule
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>G555: the mutations that require attribution first — nothing on this list is safe to do to an unattributed object.</summary>
    [JsonPropertyName("gated_mutations")]
    public required IReadOnlyList<string> GatedMutations { get; init; }

    /// <summary>G555: the four keys ownership is verified with.</summary>
    [JsonPropertyName("verification_keys")]
    public required IReadOnlyList<OrchestratorAttributionKey> VerificationKeys { get; init; }

    [JsonPropertyName("unverifiable_is_read_only")]
    public required string UnverifiableIsReadOnly { get; init; }
}

internal sealed record OrchestratorAttributionKey
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("how_to_check")]
    public required string HowToCheck { get; init; }
}

internal sealed record OrchestratorSharedSubstrate
{
    [JsonPropertyName("substrate")]
    public required string Substrate { get; init; }

    [JsonPropertyName("sharing_unit")]
    public required string SharingUnit { get; init; }

    [JsonPropertyName("ownership_rule")]
    public required string OwnershipRule { get; init; }
}

internal sealed record OrchestratorNonDestructiveRecovery
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("preserve_rule")]
    public required string PreserveRule { get; init; }

    [JsonPropertyName("rebuild_rule")]
    public required string RebuildRule { get; init; }

    /// <summary>G555: the operator's own instruction, kept as the rule's one-line form.</summary>
    [JsonPropertyName("default_is_recreate_not_cleanup")]
    public required string DefaultIsRecreateNotCleanup { get; init; }
}

internal sealed record OrchestratorDesignWorkspaceSupervision
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("granted_authority")]
    public required OrchestratorSupervisionAuthority GrantedAuthority { get; init; }

    [JsonPropertyName("session_lifecycle")]
    public required OrchestratorSupervisionSessionLifecycle SessionLifecycle { get; init; }

    [JsonPropertyName("supervision_layers")]
    public required IReadOnlyList<OrchestratorSupervisionLayer> SupervisionLayers { get; init; }

    /// <summary>G556: what the blocking-UI pane scan is looking FOR — including a pane showing a shell prompt where an agent should be.</summary>
    [JsonPropertyName("pane_scan_stuck_states")]
    public required IReadOnlyList<OrchestratorPaneStuckState> PaneScanStuckStates { get; init; }

    /// <summary>G550: session-scoped supervision schedulers die with the design session — they must survive or be re-armed.</summary>
    [JsonPropertyName("rearm_rule")]
    public required string RearmRule { get; init; }

    [JsonPropertyName("verified_read_rule")]
    public required string VerifiedReadRule { get; init; }

    [JsonPropertyName("may_answer")]
    public required IReadOnlyList<OrchestratorSupervisionMayAnswer> MayAnswer { get; init; }

    [JsonPropertyName("must_escalate")]
    public required IReadOnlyList<OrchestratorSupervisionMustEscalate> MustEscalate { get; init; }

    [JsonPropertyName("boundary_sentence")]
    public required string BoundarySentence { get; init; }

    [JsonPropertyName("provisioning_reference")]
    public required string ProvisioningReference { get; init; }

    [JsonPropertyName("watchdog_safety_rules_reference")]
    public required string WatchdogSafetyRulesReference { get; init; }
}

/// <summary>
/// G552: the design-decision half of the stall problem. G550 keeps the team's
/// sessions alive; nothing kept a DESIGN DECISION from stalling the pipeline
/// invisibly. Field incident (2026-07-28 16:11 → 07-29 01:29): the G551 review
/// held its final verdict for nine hours on a one-line wording ruling while
/// every technical check was green, the pending item was mechanically
/// fact-checkable, both threads knew the answer — and the hold lived only in
/// agmsg messages, so `automation stalled-work` reported `stalled=false`
/// throughout. Fourth design-absence stall in the field record.
///
/// Three layers, all guide-level except the detector: a hold blocked on design
/// MUST become a clarification artifact (agmsg-only is a contract violation);
/// `design-decision-pending` reads those artifacts so watchdogs see them; and
/// bounded default authority lets the operator pre-delegate enumerated,
/// mechanically fact-checkable classes — never semantic ones.
/// </summary>
internal sealed record OrchestratorDesignDecisionHolds
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("clarification_backed_hold")]
    public required OrchestratorClarificationBackedHold ClarificationBackedHold { get; init; }

    [JsonPropertyName("reviewer_hold_rule")]
    public required OrchestratorReviewerHoldRule ReviewerHoldRule { get; init; }

    [JsonPropertyName("bounded_default_authority")]
    public required OrchestratorBoundedDefaultAuthority BoundedDefaultAuthority { get; init; }

    [JsonPropertyName("design_reminder_loop")]
    public required OrchestratorDesignReminderLoop DesignReminderLoop { get; init; }

    [JsonPropertyName("detection_reference")]
    public required string DetectionReference { get; init; }
}

internal sealed record OrchestratorClarificationBackedHold
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("required_fields")]
    public required IReadOnlyList<string> RequiredFields { get; init; }

    /// <summary>G552: the sentence that makes an agmsg-only hold a contract violation rather than a style preference.</summary>
    [JsonPropertyName("contract_violation_rule")]
    public required string ContractViolationRule { get; init; }

    [JsonPropertyName("canonical_commands")]
    public required IReadOnlyList<string> CanonicalCommands { get; init; }

    /// <summary>G552 repair: a paste-ready `clarify open` invocation that persists the REAL question and its recommendation/evidence in the OPEN artifact.</summary>
    [JsonPropertyName("paste_ready_invocation")]
    public required string PasteReadyInvocation { get; init; }
}

internal sealed record OrchestratorReviewerHoldRule
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("resolve_under_authority_when")]
    public required string ResolveUnderAuthorityWhen { get; init; }

    [JsonPropertyName("record_clarification_otherwise")]
    public required string RecordClarificationOtherwise { get; init; }

    [JsonPropertyName("never_untracked_wait")]
    public required string NeverUntrackedWait { get; init; }
}

internal sealed record OrchestratorBoundedDefaultAuthority
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("operator_grant_requirement")]
    public required string OperatorGrantRequirement { get; init; }

    [JsonPropertyName("fact_checkable_classes")]
    public required IReadOnlyList<OrchestratorFactCheckableClass> FactCheckableClasses { get; init; }

    [JsonPropertyName("evidence_logging_rule")]
    public required string EvidenceLoggingRule { get; init; }

    /// <summary>G552 repair: the concrete durable sink for a granted-authority resolution — `clarify record --from-file`, whose entry lands under `## Recently Resolved`.</summary>
    [JsonPropertyName("evidence_sink")]
    public required string EvidenceSink { get; init; }

    /// <summary>G552 repair: the paste-ready operation that writes that evidence.</summary>
    [JsonPropertyName("evidence_operation")]
    public required string EvidenceOperation { get; init; }

    [JsonPropertyName("post_hoc_amendment_rule")]
    public required string PostHocAmendmentRule { get; init; }

    /// <summary>G552: semantic and product decisions are excluded outright — the double-check rule's scope is unchanged.</summary>
    [JsonPropertyName("semantic_exclusion_rule")]
    public required string SemanticExclusionRule { get; init; }
}

internal sealed record OrchestratorFactCheckableClass
{
    [JsonPropertyName("decision_class")]
    public required string DecisionClass { get; init; }

    [JsonPropertyName("verifying_facts")]
    public required string VerifyingFacts { get; init; }
}

internal sealed record OrchestratorDesignReminderLoop
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("sender")]
    public required string Sender { get; init; }

    [JsonPropertyName("interval_class")]
    public required string IntervalClass { get; init; }

    [JsonPropertyName("one_per_interval_rule")]
    public required string OnePerIntervalRule { get; init; }

    [JsonPropertyName("stop_condition")]
    public required string StopCondition { get; init; }

    /// <summary>G552: the design thread runs in the operator app by preference — an open session gets the reminder live, a closed one finds it on resume.</summary>
    [JsonPropertyName("operator_app_note")]
    public required string OperatorAppNote { get; init; }
}

internal sealed record OrchestratorSupervisionAuthority
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("operator_grant_rule")]
    public required string OperatorGrantRule { get; init; }

    [JsonPropertyName("design_operates_session_layer")]
    public required IReadOnlyList<string> DesignOperatesSessionLayer { get; init; }

    /// <summary>G550: workflow-state ownership is explicitly UNCHANGED — this slice adds a session-layer role only.</summary>
    [JsonPropertyName("workflow_state_ownership_unchanged")]
    public required string WorkflowStateOwnershipUnchanged { get; init; }
}

internal sealed record OrchestratorSupervisionSessionLifecycle
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("unresponsive_session_investigation")]
    public required IReadOnlyList<string> UnresponsiveSessionInvestigation { get; init; }

    [JsonPropertyName("exclusivity_rule")]
    public required string ExclusivityRule { get; init; }

    [JsonPropertyName("graceful_drop_rule")]
    public required string GracefulDropRule { get; init; }

    [JsonPropertyName("operator_visible_confirmation")]
    public required string OperatorVisibleConfirmation { get; init; }
}

internal sealed record OrchestratorPaneStuckState
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("what_the_scan_sees")]
    public required string WhatTheScanSees { get; init; }

    [JsonPropertyName("recovery")]
    public required string Recovery { get; init; }
}

internal sealed record OrchestratorSupervisionLayer
{
    [JsonPropertyName("layer")]
    public required string Layer { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("cadence")]
    public required string Cadence { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }
}

internal sealed record OrchestratorSupervisionMayAnswer
{
    [JsonPropertyName("dialog")]
    public required string Dialog { get; init; }

    [JsonPropertyName("verification")]
    public required string Verification { get; init; }
}

internal sealed record OrchestratorSupervisionMustEscalate
{
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

internal sealed record OrchestratorProvisioningPlaceholder
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}

internal sealed record OrchestratorProvisioningFolders
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>G521: agmsg identity and the codex monitor bridge are (project, type)-scoped, so two roles sharing one folder collide.</summary>
    [JsonPropertyName("never_share_rule")]
    public required string NeverShareRule { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<OrchestratorProvisioningRoleFolder> Roles { get; init; }

    [JsonPropertyName("absent_folder_rule")]
    public required string AbsentFolderRule { get; init; }

    [JsonPropertyName("verification")]
    public required IReadOnlyList<string> Verification { get; init; }
}

internal sealed record OrchestratorProvisioningRoleFolder
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("folder")]
    public required string Folder { get; init; }

    [JsonPropertyName("clone_source")]
    public required string CloneSource { get; init; }

    [JsonPropertyName("create_command")]
    public required string CreateCommand { get; init; }
}

internal sealed record OrchestratorProvisioningTopology
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("rules")]
    public required IReadOnlyList<string> Rules { get; init; }

    [JsonPropertyName("design_thread_position")]
    public required string DesignThreadPosition { get; init; }
}

internal sealed record OrchestratorProvisioningLaunchRules
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>G521: the codex shell shim must wrap the launch or the monitor bridge never arms.</summary>
    [JsonPropertyName("codex_shim_rule")]
    public required string CodexShimRule { get; init; }

    [JsonPropertyName("codex_direct_spawn_warning")]
    public required string CodexDirectSpawnWarning { get; init; }

    [JsonPropertyName("claude_permission_mode_rule")]
    public required string ClaudePermissionModeRule { get; init; }

    [JsonPropertyName("attended_first_run_rule")]
    public required string AttendedFirstRunRule { get; init; }

    /// <summary>
    /// G549 repair: attending a pane is not authority to decide for the
    /// operator. Design acts only on pane contents it has read, and only for
    /// explicitly authorized trust/allowlist cases; credential, security, and
    /// permission prompts escalate. Unsticking is not deciding.
    /// </summary>
    [JsonPropertyName("authority_boundary")]
    public required string AuthorityBoundary { get; init; }
}

internal sealed record OrchestratorProvisioningRoleInitialization
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("actas_forms")]
    public required IReadOnlyList<OrchestratorRoleStartup> ActasForms { get; init; }

    [JsonPropertyName("readiness_wait")]
    public required string ReadinessWait { get; init; }

    /// <summary>G549 repair: a delivery mode proves configuration, never live attachment.</summary>
    [JsonPropertyName("configuration_proof")]
    public required string ConfigurationProof { get; init; }

    /// <summary>G549 repair: live attachment evidence is agent-specific — Claude Monitor markers vs the codex bridge-alive marker.</summary>
    [JsonPropertyName("live_attachment_evidence")]
    public required IReadOnlyList<OrchestratorProvisioningLiveEvidence> LiveAttachmentEvidence { get; init; }

    /// <summary>G549 repair: ping/ack remains the only end-to-end proof.</summary>
    [JsonPropertyName("end_to_end_proof")]
    public required string EndToEndProof { get; init; }

    [JsonPropertyName("ping_test_reference")]
    public required string PingTestReference { get; init; }

    /// <summary>
    /// G556: a self-reported startup is NOT liveness. Provisioning concludes
    /// only after re-verification, at a settle delay past the report.
    /// </summary>
    [JsonPropertyName("verified_liveness")]
    public required OrchestratorVerifiedLiveness VerifiedLiveness { get; init; }
}

/// <summary>
/// G556: field incident (SekibanWasmRuntime team, 2026-07-29) — two codex
/// agents sent startup-complete reports and died seconds later when their
/// shared remote app-server was lost, dropping both TUIs to shell prompts. The
/// supervising thread kept "waiting for startup reports" while every agent was
/// already dead. The operator named the recurring pattern: threads claim to be
/// waiting for startup while nothing is actually running.
/// </summary>
internal sealed record OrchestratorVerifiedLiveness
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>G556: the sentence that makes a report insufficient on its own.</summary>
    [JsonPropertyName("report_is_not_readiness")]
    public required string ReportIsNotReadiness { get; init; }

    [JsonPropertyName("settle_delay")]
    public required string SettleDelay { get; init; }

    [JsonPropertyName("post_report_checks")]
    public required IReadOnlyList<OrchestratorLivenessCheck> PostReportChecks { get; init; }

    [JsonPropertyName("early_death_is_normal")]
    public required OrchestratorEarlyDeathMode EarlyDeathIsNormal { get; init; }

    [JsonPropertyName("shared_app_server_death_mode")]
    public required OrchestratorSharedAppServerDeath SharedAppServerDeathMode { get; init; }
}

internal sealed record OrchestratorLivenessCheck
{
    [JsonPropertyName("check")]
    public required string Check { get; init; }

    [JsonPropertyName("how_to_verify")]
    public required string HowToVerify { get; init; }
}

internal sealed record OrchestratorEarlyDeathMode
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("transport_reset_signature")]
    public required string TransportResetSignature { get; init; }

    [JsonPropertyName("recheck_obligation")]
    public required string RecheckObligation { get; init; }
}

internal sealed record OrchestratorSharedAppServerDeath
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("blast_radius")]
    public required string BlastRadius { get; init; }

    [JsonPropertyName("prevention_reference")]
    public required string PreventionReference { get; init; }
}

internal sealed record OrchestratorProvisioningLiveEvidence
{
    [JsonPropertyName("agent_type")]
    public required string AgentType { get; init; }

    [JsonPropertyName("evidence")]
    public required string Evidence { get; init; }
}

internal sealed record OrchestratorProvisioningHandover
{
    [JsonPropertyName("one_holder_rule")]
    public required string OneHolderRule { get; init; }

    [JsonPropertyName("graceful_drop_rule")]
    public required string GracefulDropRule { get; init; }

    [JsonPropertyName("operator_confirmation_rule")]
    public required string OperatorConfirmationRule { get; init; }

    [JsonPropertyName("successor_claim_rule")]
    public required string SuccessorClaimRule { get; init; }
}

internal sealed record OrchestratorProvisioningReferenceManager
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("surfaces")]
    public required IReadOnlyList<OrchestratorProvisioningSurface> Surfaces { get; init; }

    [JsonPropertyName("internals_link_out")]
    public required string InternalsLinkOut { get; init; }

    [JsonPropertyName("substitution_rule")]
    public required string SubstitutionRule { get; init; }
}

internal sealed record OrchestratorProvisioningSurface
{
    [JsonPropertyName("surface")]
    public required string Surface { get; init; }

    [JsonPropertyName("used_for")]
    public required string UsedFor { get; init; }
}

internal sealed record OrchestratorDesignTrafficController
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("playbook")]
    public required IReadOnlyList<string> Playbook { get; init; }

    [JsonPropertyName("idle_diagnostic")]
    public required IReadOnlyList<string> IdleDiagnostic { get; init; }

    [JsonPropertyName("context_only_rule")]
    public required string ContextOnlyRule { get; init; }
}

internal sealed record OrchestratorDesignHandoff
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("first_message_template")]
    public required string FirstMessageTemplate { get; init; }

    [JsonPropertyName("autonomous_publish_rule")]
    public required string AutonomousPublishRule { get; init; }

    [JsonPropertyName("escalation_boundary")]
    public required string EscalationBoundary { get; init; }

    [JsonPropertyName("design_inbox_workflow")]
    public required string DesignInboxWorkflow { get; init; }
}

/// <summary>
/// G539: the RECOMMENDED DEFAULT safety net — a watchdog loop run from the
/// design thread at a 30-minute-class interval, calling <c>intent-cli
/// automation heartbeat</c> and sending at most one canonical nudge when
/// stale. Supersedes G526's external-scheduler recommendation (see
/// <see cref="OrchestratorAutomationAlternative"/> for the retirement
/// rationale and the selectable orchestrator-side alternative).
/// </summary>
internal sealed record OrchestratorDesignWatchdog
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("optional")]
    public required bool Optional { get; init; }

    [JsonPropertyName("frequency")]
    public required string Frequency { get; init; }

    [JsonPropertyName("loop_setup_prompt")]
    public required string LoopSetupPrompt { get; init; }

    /// <summary>G539 repair round 1: silence is reserved for a healthy stale=false heartbeat result — a command failure or malformed output must be surfaced visibly, never silently swallowed/retried, and never fabricated into a sent nudge.</summary>
    [JsonPropertyName("failure_visibility_rule")]
    public required string FailureVisibilityRule { get; init; }

    [JsonPropertyName("heartbeat_command_example")]
    public required string HeartbeatCommandExample { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<string> Checks { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("repair_status_request_template")]
    public required string RepairStatusRequestTemplate { get; init; }

    [JsonPropertyName("stop_condition")]
    public required string StopCondition { get; init; }

    [JsonPropertyName("safety_rules")]
    public required IReadOnlyList<string> SafetyRules { get; init; }

    [JsonPropertyName("fallback_timer_note")]
    public required string FallbackTimerNote { get; init; }

    /// <summary>G526/G539: field-observed weakness of running the safety net from the design thread, weighed against the retired external scheduler's total silent failure.</summary>
    [JsonPropertyName("measured_weakness")]
    public required string MeasuredWeakness { get; init; }
}

/// <summary>
/// G539: the SELECTABLE ALTERNATIVE to the design-thread watchdog — the same
/// <c>intent-cli automation heartbeat</c> call, run from a long-interval
/// automation in the orchestrator's own thread instead of the design thread.
/// Also carries the retirement note for G526's external cron/launchd
/// OS-scheduler recommendation.
/// </summary>
internal sealed record OrchestratorAutomationAlternative
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("frequency")]
    public required string Frequency { get; init; }

    [JsonPropertyName("trade_off")]
    public required string TradeOff { get; init; }

    [JsonPropertyName("command_example")]
    public required string CommandExample { get; init; }

    [JsonPropertyName("setup_prompt")]
    public required string SetupPrompt { get; init; }

    [JsonPropertyName("retired_cron_note")]
    public required string RetiredCronNote { get; init; }
}

internal sealed record OrchestratorDesignReceiver
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("optional")]
    public required bool Optional { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<string> Roles { get; init; }

    [JsonPropertyName("setup")]
    public required IReadOnlyList<string> Setup { get; init; }

    [JsonPropertyName("manual_inbox_trigger_prompt")]
    public required string ManualInboxTriggerPrompt { get; init; }

    [JsonPropertyName("pre_start_note")]
    public required string PreStartNote { get; init; }
}

internal sealed record OrchestratorDesignThreadEscalation
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("kept_internal")]
    public required IReadOnlyList<string> KeptInternal { get; init; }

    [JsonPropertyName("escalate_when")]
    public required IReadOnlyList<string> EscalateWhen { get; init; }

    [JsonPropertyName("escalation_message_template")]
    public required string EscalationMessageTemplate { get; init; }

    [JsonPropertyName("message_fields")]
    public required IReadOnlyList<string> MessageFields { get; init; }
}

internal sealed record OrchestratorStaleThreadHealthCheck
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("no_reply_threshold")]
    public required string NoReplyThreshold { get; init; }

    [JsonPropertyName("procedure")]
    public required IReadOnlyList<string> Procedure { get; init; }

    [JsonPropertyName("status_request_template")]
    public required string StatusRequestTemplate { get; init; }

    [JsonPropertyName("receiver_statuses")]
    public required IReadOnlyList<OrchestratorReceiverStatus> ReceiverStatuses { get; init; }

    [JsonPropertyName("safety")]
    public required IReadOnlyList<string> Safety { get; init; }
}

internal sealed record OrchestratorReceiverStatus
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}

internal sealed record OrchestratorDependencyPlanning
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("selection_rule")]
    public required string SelectionRule { get; init; }

    [JsonPropertyName("statuses")]
    public required IReadOnlyList<OrchestratorDependencyStatus> Statuses { get; init; }

    [JsonPropertyName("dependent_hold")]
    public required string DependentHold { get; init; }

    [JsonPropertyName("escalation_cases")]
    public required IReadOnlyList<string> EscalationCases { get; init; }
}

internal sealed record OrchestratorDependencyStatus
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }
}

internal sealed record OrchestratorPreflight
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<string> Checks { get; init; }
}

internal sealed record OrchestratorTroubleshooting
{
    [JsonPropertyName("symptom")]
    public required string Symptom { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }
}

internal sealed record OrchestratorMonitorDistinction
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("delivery_mode_note")]
    public required string DeliveryModeNote { get; init; }

    [JsonPropertyName("success_markers")]
    public required IReadOnlyList<string> SuccessMarkers { get; init; }

    [JsonPropertyName("failure_markers")]
    public required IReadOnlyList<string> FailureMarkers { get; init; }

    [JsonPropertyName("trust_repair")]
    public required IReadOnlyList<string> TrustRepair { get; init; }

    [JsonPropertyName("windows_guidance")]
    public required IReadOnlyList<string> WindowsGuidance { get; init; }

    [JsonPropertyName("fallback_ladder")]
    public required IReadOnlyList<string> FallbackLadder { get; init; }

    [JsonPropertyName("project_settings_diagnosis")]
    public required IReadOnlyList<string> ProjectSettingsDiagnosis { get; init; }
}

internal sealed record OrchestratorCodexBridgeGuidance
{
    [JsonPropertyName("observed_versions")]
    public required string ObservedVersions { get; init; }

    [JsonPropertyName("setup_preflight")]
    public required string SetupPreflight { get; init; }

    [JsonPropertyName("healthy_state_markers")]
    public required IReadOnlyList<string> HealthyStateMarkers { get; init; }

    [JsonPropertyName("troubleshooting")]
    public required IReadOnlyList<OrchestratorTroubleshooting> Troubleshooting { get; init; }

    [JsonPropertyName("reference_link")]
    public required string ReferenceLink { get; init; }
}

internal sealed record OrchestratorReceiverReadiness
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("startup_order")]
    public required IReadOnlyList<string> StartupOrder { get; init; }

    [JsonPropertyName("send_before_ready_warning")]
    public required string SendBeforeReadyWarning { get; init; }

    [JsonPropertyName("recovery_message_template")]
    public required string RecoveryMessageTemplate { get; init; }

    [JsonPropertyName("states")]
    public required IReadOnlyList<OrchestratorReadinessState> States { get; init; }

    [JsonPropertyName("ping_ack_required")]
    public required string PingAckRequired { get; init; }

    [JsonPropertyName("not_ready_recovery")]
    public required IReadOnlyList<string> NotReadyRecovery { get; init; }

    [JsonPropertyName("watch_note")]
    public required string WatchNote { get; init; }

    [JsonPropertyName("codex_desktop_note")]
    public required string CodexDesktopNote { get; init; }

    [JsonPropertyName("diagnostic_commands")]
    public required IReadOnlyList<string> DiagnosticCommands { get; init; }
}

internal sealed record OrchestratorReadinessState
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}

internal sealed record OrchestratorSetup
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("decisions")]
    public required IReadOnlyList<string> Decisions { get; init; }

    [JsonPropertyName("checklist")]
    public required IReadOnlyList<string> Checklist { get; init; }

    [JsonPropertyName("agmsg_commands")]
    public required IReadOnlyList<string> AgmsgCommands { get; init; }

    [JsonPropertyName("ping_test")]
    public required string PingTest { get; init; }

    [JsonPropertyName("cleanup")]
    public required IReadOnlyList<string> Cleanup { get; init; }

    [JsonPropertyName("warning")]
    public required string Warning { get; init; }
}

internal sealed record OrchestratorNextSlicePublication
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("one_per_wake")]
    public required bool OnePerWake { get; init; }

    [JsonPropertyName("preconditions")]
    public required IReadOnlyList<string> Preconditions { get; init; }

    [JsonPropertyName("blockers")]
    public required IReadOnlyList<string> Blockers { get; init; }

    [JsonPropertyName("canonical_commands")]
    public required IReadOnlyList<string> CanonicalCommands { get; init; }

    [JsonPropertyName("post_publish_verification")]
    public required IReadOnlyList<string> PostPublishVerification { get; init; }
}

/// <summary>
/// G524: every orchestrator wake ends with a read-only stalled-work check
/// (G523) so a wake never ends leaving an actionable pending transition for
/// an unscheduled "next wake" that nothing would trigger — this closes the
/// measured publish-then-sleep / silent-completion stall classes.
/// </summary>
internal sealed record OrchestratorEndOfWakeCheck
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("never_defer_rule")]
    public required string NeverDeferRule { get; init; }

    [JsonPropertyName("escalate_instead_of_defer_rule")]
    public required string EscalateInsteadOfDeferRule { get; init; }
}

/// <summary>
/// G524: dispatch guidance requiring the recipient id to be verified
/// against the agmsg team roster before every send — agmsg accepts an
/// unknown recipient silently, so a typo'd/legacy id (e.g. `review` instead
/// of the registered `reviewer`) loses the message with no error surfaced
/// anywhere.
/// </summary>
internal sealed record OrchestratorDispatchVerification
{
    [JsonPropertyName("rule")]
    public required string Rule { get; init; }

    [JsonPropertyName("dead_address_example")]
    public required string DeadAddressExample { get; init; }
}

internal sealed record OrchestratorThreadPrompt
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record OrchestratorReplyContract
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("accepted")]
    public required string Accepted { get; init; }

    [JsonPropertyName("progress")]
    public required string Progress { get; init; }

    [JsonPropertyName("completed")]
    public required string Completed { get; init; }

    [JsonPropertyName("blocked")]
    public required string Blocked { get; init; }

    [JsonPropertyName("review_completed_example")]
    public required string ReviewCompletedExample { get; init; }

    [JsonPropertyName("review_incomplete_rule")]
    public required string ReviewIncompleteRule { get; init; }

    /// <summary>
    /// G564: the closeout report is the only place design reliably learns
    /// that a packet promised a write-back, so it must NAME the declared
    /// facets/targets and their recorded-or-pending state. Read-only
    /// propagation of packet metadata — the orchestrator never mutates host
    /// intent content.
    /// </summary>
    [JsonPropertyName("closeout_knowledge_write_back_rule")]
    public required string CloseoutKnowledgeWriteBackRule { get; init; }
}

/// <summary>
/// G570: the session-layer block every orchestrator-thread rendering carries.
/// It answers "which transport am I reading about, and did somebody choose it
/// or is this the default" before the reader reaches any transport-specific
/// section.
/// </summary>
internal sealed record OrchestratorSessionLayer
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("exclusivity")]
    public required string Exclusivity { get; init; }

    [JsonPropertyName("preview_scoping")]
    public required string PreviewScoping { get; init; }

    [JsonPropertyName("selection")]
    public required string Selection { get; init; }

    /// <summary>
    /// G570: honest about the boundary of this slice. Wholly agmsg-specific
    /// sections are replaced; MIXED sections that carry both canon and agmsg
    /// mechanics are left intact (removing them would remove mode-independent
    /// canon with them) and this sentence tells the reader how to read them
    /// until G571 restructures them. Null under agmsg.
    /// </summary>
    [JsonPropertyName("residual_agmsg_mechanics")]
    public string? ResidualAgmsgMechanics { get; init; }
}
