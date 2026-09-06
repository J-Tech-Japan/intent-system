using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G654: render-only, agent-kind-neutral design-thread operating contract.
/// This guide does not use terminal content as workflow evidence, supervise
/// work, mutate state, or launch a provider. Pane reads are scoped to
/// operational liveness diagnosis and are deliberately identical across
/// session layers.
/// </summary>
internal static class GuideDesignThreadCommand
{
    public const string CommandName = "intent-cli guide design-thread";
    internal const string OrcaWakeSendForm = "orca orchestration send --run <run-id> --to run:<run-id> --from <role> --subject {task_id} --body {summary}";
    internal const string OrcaCheckForm = "orca orchestration check --run <run-id> --wait --timeout-ms <timeout-ms> --json";
    internal const string SessionLayerInspectRoute =
        "Read-only live observation: `intent-cli session-layer inspect --domain <domain> --team <team> [--role <role>] [--tail <lines>] [--routing-root <host-root>] --format json` reads recorded roles and explicit herdr pane state without focus, prompt, key, or process-management operations.";
    private const string UsageLine =
        "Usage: intent-cli guide design-thread [--domain <name>] [--team <team>] [--routing-root <path>] [--format markdown|json]";

    private static readonly string[] ValidWakeOutcomes =
    {
        "Advance the canonical workflow and cite the changed canonical record or artifact.",
        "Confirm new evidence of real progress and cite the changed activity or artifact evidence.",
        "Discover the next actionable design, packet, or issue candidate and hand it to orchestration with its provenance.",
        "Report a blocker that only a human can resolve, naming the minimal concrete operation and why it cannot be automated.",
    };

    private static readonly string[] InvalidWakeOutcomes =
    {
        "`no-actionable`, `running=true`, liveness, an unchanged status, and `no change` are not outcomes while the project is unfinished.",
        "A request, dispatch, or statement that work is still live is not evidence of change.",
    };

    private static readonly string[] MergeTransaction =
    {
        "merge", "verify merge commit", "close linked issue", "transition queue",
        "append runs", "write back host state", "push host state",
    };

    private static readonly string[] DesignEscalations =
    {
        "milestone completion", "blocked work requiring design or human input", "canonical-state conflict",
        "decision or consultation", "permission, security, or credential boundary", "destructive action",
        "release or policy choice", "repeated bounded recovery failure", "unhealthy or absent supervision",
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            writer.WriteLine("Read-only G654 design-thread operating contract. Preview-through-1.x; no provider launch, supervision, canonical terminal parsing, or mutation. Pane reads are for operational liveness diagnosis only.");
            return 0;
        }

        if (!TryParseArguments(args, out var domain, out var team, out var routingRoot, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var topology = ReviewSeatSelectionGuidanceResolver.ResolveTopology(routingRoot ?? string.Empty, domain, team);
        var result = BuildResult(
            domain,
            team,
            routingRoot,
            topology is null ? null : ReviewSeatSelectionGuidanceResolver.Create(topology),
            CreateOrcaOperatingBlock());
        if (string.Equals(format, "json", StringComparison.Ordinal))
        {
            writer.WriteLine(GuideRoleVocabulary.ProjectRenderedRoleValues(JsonSerializer.Serialize(result, JsonOptions)));
        }
        else
        {
            using var buffer = new StringWriter();
            WriteMarkdown(buffer, result);
            writer.Write(GuideRoleVocabulary.ProjectRenderedRoleValues(buffer.ToString()));
        }

        return 0;
    }

    internal static DesignThreadGuideResult BuildResult(
        string? domain,
        string? team,
        string? routingRoot,
        ReviewSeatSelectionGuidance? reviewSeatSelection = null,
        DesignThreadOrcaOperatingBlock? orcaOperatingBlock = null)
    {
        // G789: the static rule and non-normative Orca operating block are
        // always reachable. Readable topology only enriches the rule with
        // recorded team/kind-specific resolution; it must never gate the
        // baseline guidance.
        reviewSeatSelection ??= ReviewSeatSelectionGuidanceResolver.CreateStatic();
        orcaOperatingBlock ??= CreateOrcaOperatingBlock();
        var root = string.IsNullOrWhiteSpace(routingRoot) ? "<routing-root>" : routingRoot!;
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain.Trim();
        var teamArg = string.IsNullOrWhiteSpace(team) ? "<team>" : team.Trim();
        return new DesignThreadGuideResult
        {
            Process = "design-thread-operating-contract",
            PreviewStatus = "preview-through-1.x",
            AgentKindNeutral = true,
            SessionLayerRule = "The contract is identical in agmsg and herdr-only modes, with or without a named team. Re-read installed guides when the CLI version or session-layer configuration changes, not on every wake.",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            Team = string.IsNullOrWhiteSpace(team) ? null : team,
            RoutingRoot = root,
            Reachability = new DesignThreadReachability
            {
                Command = CommandName,
                Catalog = "intent-cli guide commands list --format json",
                Advisor = "intent-cli guide next --format json",
                ExternalReader = new DesignThreadExternalReader
                {
                    Command = $"intent-cli notify collect --domain {domainArg} --team {teamArg} --role design --since <cursor> --wait --timeout-ms <timeout-ms> --format json",
                    CursorRule = "The caller holds the opaque cursor: omit `--since` for the first receive; the next cursor is returned in each result, and the caller supplies that returned cursor in the next call.",
                    WaitRule = "A bounded wait is required: use `--wait` with a finite `--timeout-ms <timeout-ms>`; never use an unbounded wait.",
                },
            },
            WakeRule = new DesignThreadWakeRule { ValidOutcomes = ValidWakeOutcomes, NotOutcomes = InvalidWakeOutcomes },
            Provenance = new DesignThreadProvenance
            {
                Vocabulary = new[] { "candidate", "accepted design", "packet", "queued unit", "published unit", "WIP" },
                ExecutionUnitRule = "Do not assign or cite an execution-unit number unless that unit exists in canonical host state.",
                ExternalOriginFields = new[] { "source kind", "reference", "timestamp", "requesting party", "acceptance state" },
            },
            Approval = new DesignThreadApproval
            {
                ReadOnlyRule = "Read-only inspection requires no approval.",
                MergeRule = "Unless the operator explicitly says merge-only, a merge instruction authorizes the complete logical closeout transaction. Ask approval for that full transaction once; never request it piecemeal.",
                MergeTransaction = MergeTransaction,
                ExplicitAcceptanceStillRequired = new[] { "publication", "contract change", "priority change", "release" },
            },
            DialogAnsweringRule = DialogAnsweringRuleGuidance.Create(),
            ResidualApproval = new DesignThreadResidualApproval
            {
                PreviewStatus = "preview-through-1.x (G666/G682/G683/G689/G690)",
                Layers = new[]
                {
                    "Interim in-contract path: eliminate each known dialog through agent-side allow configuration recorded in that kind's G636 unattended-launch recipe fields; never answer the dialog.",
                    "G683 supervision reads dialog-blocked seats and emits only exact recipe-registry classes; unmatched text is class unknown and escalate-only, never fuzzy-classified.",
                    "G689 adds the shell-command class and extracts a payload without granting a bare answer; every compound AST segment needs project-test, owned-scratch-delete, or exact-command-once scope, with digest/dialog audit. G690 routes any declared authority through canonical `intent-cli notify adjudicate`: exact class/scope `answerable_by`, the hard risk floor, durable audit, and live pane/state-sequence/text-hash CAS must all permit the bounded answer. Design has no unscoped relay or direct `send-keys` path; design never relays keystrokes as a generic action. Any exact-match execution allowed by `dialog-answering/v1` remains a session-layer mechanical answer, never a design-owned relay.",
                },
                NoPolicyRule = "Without an exact validated pre-approve match or matching shell scoped policy, residual prompts are escalate-only; unknown, unmatched, bare shell classes, pre-escalate prompts, capability-denied prompts, and hard-risk-floor prompts execute no answer. A permitted answer still goes only through `notify adjudicate` with declared `answerable_by` and live CAS; no unscoped relay is valid.",
                Incident = "G666 measured the 2026-08-11 workspace wK Claude app safety relay refusal. Operator-filed #1469 then measured a 0.19.0 supervision cycle with 47 keys and no prompt/dialog/class/adjudication producer key, a review seat wedged three times in one day, and orchestration correctly refusing to fabricate a class. This is the same configured-looking-but-inert failure shape as #1465; the interim correction is agent-side allow configuration recorded in the G636 kind recipe fields, while G690 keeps residual adjudication scoped, audited, and CAS-bound.",
                WatcherBoundary = "Supervision classifies only literal registry entries and verifies every shell AST segment against scoped policy. A declared actor uses only canonical `notify adjudicate`; the shared pipeline resolves class/scope `answerable_by`, applies the hard risk floor, and rechecks live pane/state-sequence/text-hash CAS. For G689 owned-scratch-delete, policy evaluation receives the recorded current/last supervision cycle identity, so stale or caller-mismatched wake identities fail closed. No generic relay or direct `send-keys` path is exposed.",
                Formula = "four judgment-bearing threads plus one supervision process; approval handling creates no fifth seat.",
            },
            MergeAuthority = new DesignThreadMergeAuthority
            {
                Rule = "GitHub reviewDecision alone never proves a blocker. Compare the sources and attribute every fact to its owning system.",
                Facts = new[] { "intent-cli workflow labels", "exact PR head", "GitHub checks", "GitHub mergeability", "canonical queue state" },
            },
            DelegationVerification = new DesignThreadDelegationVerification
            {
                Layers = new[] { "canonical workflow status", "recorded session-layer agent state and G652 activity sub-verdicts", "real artifacts such as files, commits, and pull requests" },
                Rule = "Verify all three layers. `running=true` alone never proves progress; terminal content is never parsed as workflow evidence.",
            },
            ObservationBoundary = new DesignThreadObservationBoundary
            {
                PaneReadRule = "Terminal pane reading is permitted only for operational liveness diagnosis: determine whether a seat is alive or responding after an explicit operator or authorized orchestration diagnostic request.",
                CanonicalEvidenceRule = "Terminal content is never parsed, promoted, or cited as canonical workflow evidence. Canonical workflow evidence remains intent-cli/GitHub state, recorded activity, and real artifacts.",
                RecoveryOwnershipRule = "A liveness observation never transfers detection, classification, or authorized recovery ownership from orchestration to design; design remains observation-only outside its declared escalation and approval boundaries.",
                InspectRoute = SessionLayerInspectRoute,
                FallbackRoute = "If orchestration cannot read panes, use `intent-cli notify status --task-id <task-id> --domain <domain> --team <team> --routing-root <host-root> --format json` and the configured non-destructive `status-request`/canonical report route. Treat the returned liveness as observation only and escalate unresolved silence; do not infer workflow state or recovery ownership from it.",
                KeystrokeBoundary = "Keystrokes are never a generic design relay: apply G701 `dialog-answering/v1` exactly. The provisioner answers self-provisioned gates; design may mechanically answer only an exact dialog/action match already approved by the human through the session layer, with the human as decision actor and no per-action class generalization; every unapproved, unknown-origin, uncertain, or mismatching dialog goes through design to the human with grounds.",
            },
            TeamAndDutySplit = new DesignThreadTeamAndDutySplit
            {
                Formula = "four judgment-bearing threads plus one supervision process",
                MonitoringRule = "Watcher infrastructure is not a fifth role: it holds no conversation, makes no judgment, and spends no model tokens. Do not staff monitoring with a language-model seat.",
                OrchestrationOwnership = "Orchestration owns detection, classification, and authorized recovery for every stall class, including review wedges.",
                DesignMode = "Design is event-driven and receives only the agreed escalation set.",
                DesignEscalations = DesignEscalations,
                ReviewSeatSelection = reviewSeatSelection,
            },
            ResearchDelegation = ResearchDelegationContract.CreateGuidance(),
            NotifyDelegateAssignment = NotifyDelegateAssignmentGuidance.Create(domainArg, teamArg),
            Monitoring = new DesignThreadMonitoring
            {
                Separation = "Supervision runs outside the design conversation and is consulted at most once per design wake.",
                ResidualDesignCheck = "At low frequency, design runs the read-only `intent-cli notify supervise liveness --domain <domain> --team <team> --routing-root <host-root> --format json` surface. It reports the last completed supervision cycle, supervision state, declared bound, elapsed age, and scheduler-load evidence without running or depending on the supervisor; compare the age with the declared supervision-liveness bound and do not use a conversational heartbeat.",
                BoundRule = "The declared detection bound must be greater than the configured wake interval plus scheduling jitter.",
                GuideRefreshRule = "Re-read installed guides after a CLI version change or session-layer configuration change, not on every wake.",
                DeploymentRule = $"A design seat whose agent kind has no inbound app monitor must be a recorded resident herdr seat with cwd `{root}`, where persistent AGENTS rules apply. A kind with an inbound app monitor may use that external reader. This is a deployment rule, not a recommendation.",
            },
            Reporting = new DesignThreadReporting
            {
                Rule = "Report the wake outcome with evidence of change, never merely `requested`, `still live`, liveness, or an unchanged status.",
                HumanActionRule = "When human action is required, report the minimal concrete operation and why automation cannot perform it.",
            },
            NegativeInvariants = new[]
            {
                "No behavior change outside guide rendering and reachability surfaces.",
                "No terminal content parsed or promoted as canonical workflow evidence, no canonical state inferred from a pane, and no transfer of detection, classification, or recovery ownership from orchestration to design.",
                "No provider launch, hidden fifth role, or design-owned stall recovery.",
                "No generic design keystroke relay, watcher restart, correction, dialog answer, or key send outside the G701 exact-match session-layer rule.",
                "No fuzzy prompt classification, unvalidated rule, unaudited answer, unscoped key relay, or unscoped design answer path.",
                "No canonical identity inferred from prose, transport state, or an unaccepted candidate.",
                "No publication, contract, priority, release, destructive, permission, security, or credential decision is silently broadened.",
            },
            PacketAuthoringCheck = new DesignThreadPacketAuthoringCheck
            {
                BeforePublish = "Before publishing any packet, perform this authoring self-check; it is guidance for the design seat and does not add semantic lint to `packet draft`.",
                PerCriterionSatisfiability = "Per-criterion satisfiability: verify every criterion can be satisfied under the packet's own constraints.",
                NegativeCriterionScoping = "Negative-criterion scoping: check every negative criterion against every positive criterion, and scope the negative with a limiting word that names the one thing it protects.",
                RequestUpdateCondition = "Request an update before publication if any criterion conflicts with a required positive criterion, if a negative is not scoped with a limiting word, or if the requested evidence is unobtainable under the packet's own constraints.",
                DiscriminatingPair = "Name at least one named discriminating pair: a compliant case and a near case that proves the scoped rule catches the intended difference.",
                RecognitionExamples = new[]
                {
                    "G765 AC4/AC6 — unobtainable evidence: a live scheduler-state requirement conflicts with a no-OS-lifecycle-query boundary.",
                    "G767 AC1/AC6 — status versus oracle: a success-status interpretation conflicts with the full clean-payload oracle.",
                    "G769 AC3/AC4 — broad negative forbids the needed change: cwd independence needs a routing-root behavior that an unscoped negative would prohibit.",
                },
                G770ResolutionRule = "G770 resolution rule: scope the negative with a limiting word naming exactly what it protects — for example, `root resolution` — rather than broadening it to forbid the required change.",
            },
            ExternalResidenceOperatingContract = new DesignThreadExternalResidenceOperatingContract
            {
                FrontendRelabel = $"External-to-external frontend relabel: an external seat may change its frontend application freely; residence, reader, and routing root stay unchanged; no transition command is involved; do not use `session-layer topology update-residence`; frontend is an operator label, never a routing input; on an existing role, use `intent-cli session-layer topology update-field --domain {domainArg} --team {teamArg} --role <role> --field frontend --current <value|absent> --new <value|absent> --confirm-update-field --write --format json`.",
                RoutingRootMust = "Routing-root MUST: every notify send and receive uses the canonical routing root. A wrong root strands notify records outside canonical state while the sender still returns `delivered: true`.",
                CollectLoop = $"Collect loop: `intent-cli notify collect --domain {domainArg} --team {teamArg} --role design --since <cursor> --wait --timeout-ms <timeout-ms> --routing-root {root} --format json`; the caller holds the cursor, consumes the returned next cursor, and omits `--since` only for the first receive.",
                WakeChannelPattern = "Wake-channel pattern: canonical `intent-cli notify` is the durable record; a wake channel is a courtesy-only signal; dual-send is the practiced form. Bind durable wake addresses before reading and never substitute a terminal-only address for a durable bound address.",
                WakeChannelDeclaration = $"Declared external wake: an operator may record one literal one-line `--wake-command` template on an external role, for example `{OrcaWakeSendForm}`. On an existing external role, set or clear that label only with `intent-cli session-layer topology update-field --domain {domainArg} --team {teamArg} --role <role> --field wake_command --current <value|absent> --new <value|absent> --confirm-update-field --write --format json`. `notify delegate` renders `{{task_id}}` and `{{summary}}` only as text, leaves unknown placeholders untouched, and never executes, validates, health-checks, launches, or manages the command. The canonical notify write always comes first; the rendered declared wake is courtesy-only and never substitutes for that durable record.",
                OrcaWorkedExample = $"Non-normative Orca example: bind the design coordinator terminal before reading with `orca orchestration run-use --id <run-id>`, then use the blocking check `{OrcaCheckForm}`. Orca is only a courtesy wake receiver alongside canonical `intent-cli notify`; intent-cli neither launches nor manages Orca.",
                OrcaOperatingBlock = orcaOperatingBlock,
                ResidenceTransition = $"A herdr↔external residence change is a different operation from an external-to-external frontend relabel: `intent-cli session-layer topology update-residence --domain {domainArg} --team {teamArg} --role design --current-resident <herdr|external> --new-resident <herdr|external> [destination fields] --confirm-update-residence --write --format json`.",
            },
            UnreadableRepairResponse = "When liveness reports a non-zero `unreadable_record_count`, the sanctioned response is `intent-cli notify supervise repair-unreadable`: run `--dry-run` first, inspect the evidence, and use `--write` only second; it is never automatic and never performed on read. The repair quarantines unreadable lines verbatim as evidence and makes no reconstruction claim.",
        };
    }

    private static DesignThreadOrcaOperatingBlock CreateOrcaOperatingBlock() => new()
    {
        Label = "Non-normative Orca operating block",
        SetupOrder =
        [
            "Create or bind a Run before seat messages: `orca orchestration run-create --objective <text> [--from <handle>]` or `orca orchestration run-use --id <run-id> [--from <handle>]`.",
            "Share the resulting `<run-id>` with every sender before anyone addresses `run:<run-id>`.",
            "Each sender supplies its own `--from <role>` handle; it is a sender handle, not a routing identity.",
        ],
        SendForm = OrcaWakeSendForm,
        CheckForm = OrcaCheckForm,
        SharedChannel = "The same Orca channel carries herdr seats' courtesy wakes and design-to-design messages; neither replaces the canonical notify record.",
        DurableRecord = "Canonical `intent-cli notify` remains durable. This non-normative block adds no intent-cli option, and intent-cli neither launches nor manages Orca.",
    };

    private static void WriteMarkdown(TextWriter writer, DesignThreadGuideResult result)
    {
        writer.WriteLine("# Design-thread operating contract (G654)");
        writer.WriteLine();
        writer.WriteLine($"- status: **{result.PreviewStatus}**");
        writer.WriteLine($"- agent-kind-neutral: **{result.AgentKindNeutral.ToString().ToLowerInvariant()}**");
        if (result.Domain is not null) writer.WriteLine($"- domain: `{result.Domain}`");
        if (result.Team is not null) writer.WriteLine($"- team: `{result.Team}`");
        writer.WriteLine($"- routing root: `{result.RoutingRoot}`");
        writer.WriteLine($"- session layers: {result.SessionLayerRule}");
        writer.WriteLine();
        writer.WriteLine(GuideRoleVocabulary.RenderMarkdownBlock());
        writer.WriteLine();
        writer.WriteLine("## Reachability");
        writer.WriteLine($"- command: `{result.Reachability.Command}`");
        writer.WriteLine($"- catalog: `{result.Reachability.Catalog}`");
        writer.WriteLine($"- Architect-role advisor: `{result.Reachability.Advisor}` names this guide.");
        writer.WriteLine();
        writer.WriteLine("## External-resident Architect receive");
        writer.WriteLine($"- canonical receive: `{result.Reachability.ExternalReader.Command}`");
        writer.WriteLine($"- cursor: {result.Reachability.ExternalReader.CursorRule}");
        writer.WriteLine($"- wait: {result.Reachability.ExternalReader.WaitRule}");
        WriteList(writer, "## 1. Four-outcome wake rule", result.WakeRule.ValidOutcomes);
        WriteList(writer, "### Not outcomes", result.WakeRule.NotOutcomes);
        writer.WriteLine("## 2. Provenance vocabulary");
        writer.WriteLine($"- distinct states: {string.Join(" / ", result.Provenance.Vocabulary.Select(value => $"`{value}`"))}");
        writer.WriteLine($"- {result.Provenance.ExecutionUnitRule}");
        writer.WriteLine($"- Before prioritizing an external handoff, record: {string.Join(", ", result.Provenance.ExternalOriginFields)}.");
        writer.WriteLine();
        writer.WriteLine("## 3. Transaction-scoped approval");
        writer.WriteLine($"- {result.Approval.ReadOnlyRule}");
        writer.WriteLine($"- {result.Approval.MergeRule}");
        writer.WriteLine($"- closeout transaction: {string.Join(" -> ", result.Approval.MergeTransaction)}");
        writer.WriteLine($"- explicit acceptance remains required for: {string.Join(", ", result.Approval.ExplicitAcceptanceStillRequired)}.");
        writer.WriteLine();
        writer.WriteLine("## 3a. Residual approval boundary (G666)");
        writer.WriteLine($"- status: **{result.ResidualApproval.PreviewStatus}**");
        foreach (var layer in result.ResidualApproval.Layers) writer.WriteLine($"- {layer}");
        writer.WriteLine($"- **absent policy:** {result.ResidualApproval.NoPolicyRule}");
        writer.WriteLine($"- **measured incident:** {result.ResidualApproval.Incident}");
        writer.WriteLine($"- **watcher boundary:** {result.ResidualApproval.WatcherBoundary}");
        writer.WriteLine($"- **team formula:** {result.ResidualApproval.Formula}");
        writer.WriteLine();
        writer.WriteLine(DialogAnsweringRuleGuidance.RenderMarkdown());
        writer.WriteLine();
        writer.WriteLine("## 4. Merge-authority comparison");
        writer.WriteLine($"- {result.MergeAuthority.Rule}");
        foreach (var fact in result.MergeAuthority.Facts) writer.WriteLine($"- compare: {fact}");
        WriteList(writer, "## 5. Three-layer delegation verification", result.DelegationVerification.Layers);
        writer.WriteLine(result.DelegationVerification.Rule);
        writer.WriteLine();
        writer.WriteLine("## 5a. Terminal observation and keystroke boundary (G706)");
        writer.WriteLine($"- **pane read:** {result.ObservationBoundary.PaneReadRule}");
        writer.WriteLine($"- **canonical evidence:** {result.ObservationBoundary.CanonicalEvidenceRule}");
        writer.WriteLine($"- **recovery ownership:** {result.ObservationBoundary.RecoveryOwnershipRule}");
        writer.WriteLine($"- **inspect route:** {result.ObservationBoundary.InspectRoute}");
        writer.WriteLine($"- **fallback observation route:** {result.ObservationBoundary.FallbackRoute}");
        writer.WriteLine($"- **keystroke/dialog boundary:** {result.ObservationBoundary.KeystrokeBoundary}");
        writer.WriteLine();
        writer.WriteLine("## 6. Team formula, duty split, and monitoring separation");
        writer.WriteLine($"- formula: **{result.TeamAndDutySplit.Formula}**");
        writer.WriteLine($"- {result.TeamAndDutySplit.MonitoringRule}");
        writer.WriteLine($"- {result.TeamAndDutySplit.OrchestrationOwnership}");
        writer.WriteLine($"- {result.TeamAndDutySplit.DesignMode}");
        foreach (var item in result.TeamAndDutySplit.DesignEscalations) writer.WriteLine($"  - {item}");
        if (result.TeamAndDutySplit.ReviewSeatSelection is { } reviewSeatSelection)
        {
            writer.WriteLine("- **review-seat selection (G789):** " + reviewSeatSelection.MixedKindRule);
            writer.WriteLine("  - " + reviewSeatSelection.SingleKindAllowance);
            writer.WriteLine("  - " + reviewSeatSelection.RecordedFieldsDecide);
            if (reviewSeatSelection.RecordedSeatKinds is { } recordedSeatKinds)
            {
                writer.WriteLine("  - recorded topology: " + string.Join(", ", recordedSeatKinds));
                writer.WriteLine("  - selection: " + reviewSeatSelection.Selection);
            }
        }
        writer.WriteLine($"- {result.Monitoring.Separation}");
        writer.WriteLine($"- {result.Monitoring.ResidualDesignCheck}");
        writer.WriteLine($"- **unreadable-record response (G777):** {result.UnreadableRepairResponse}");
        writer.WriteLine($"- {result.Monitoring.BoundRule}");
        writer.WriteLine($"- {result.Monitoring.GuideRefreshRule}");
        writer.WriteLine($"- {result.Monitoring.DeploymentRule}");
        writer.WriteLine();
        writer.WriteLine("## 6a. Research delegation contract (G800)");
        writer.WriteLine($"- task kind: `{result.ResearchDelegation.TaskKind}`");
        writer.WriteLine($"- sender roles: {string.Join(", ", result.ResearchDelegation.SenderRoles.Select(role => $"`{role}`"))}");
        writer.WriteLine($"- recipient roles: {string.Join(", ", result.ResearchDelegation.RecipientRoles.Select(role => $"`{role}`"))}");
        writer.WriteLine($"- what goes down: {result.ResearchDelegation.WhatGoesDown}");
        writer.WriteLine($"- who receives: {result.ResearchDelegation.WhoReceives}");
        writer.WriteLine($"- what stays: {result.ResearchDelegation.WhatStays}");
        writer.WriteLine($"- sourced findings: {result.ResearchDelegation.SourcedFindingRule}");
        writer.WriteLine($"- no-ruling boundary: {result.ResearchDelegation.NoRulingBoundary}");
        writer.WriteLine($"- direct research: {result.ResearchDelegation.DirectResearchRule}");
        writer.WriteLine($"- visibility: {result.ResearchDelegation.VisibilityRule}");
        writer.WriteLine($"- size rule: {result.ResearchDelegation.NoSizeRule}");
        foreach (var example in result.ResearchDelegation.Examples) writer.WriteLine($"  - example: {example}");
        writer.WriteLine();
        writer.WriteLine("## 6b. Explicit notify delegate assignment (G809)");
        writer.WriteLine($"- precedence: {result.NotifyDelegateAssignment.Precedence}");
        writer.WriteLine($"- validation: {result.NotifyDelegateAssignment.Validation}");
        writer.WriteLine($"- dry-run: {result.NotifyDelegateAssignment.DryRun}");
        writer.WriteLine($"- authority: {result.NotifyDelegateAssignment.Authority}");
        writer.WriteLine($"- historical records: {result.NotifyDelegateAssignment.HistoricalRecords}");
        foreach (var example in result.NotifyDelegateAssignment.Examples) writer.WriteLine($"  - example: {example}");
        writer.WriteLine();
        writer.WriteLine("## 7. Outcome-shaped reporting");
        writer.WriteLine($"- {result.Reporting.Rule}");
        writer.WriteLine($"- {result.Reporting.HumanActionRule}");
        writer.WriteLine();
        writer.WriteLine("## 8. Packet-authoring self-check (G774)");
        writer.WriteLine($"- **before publish:** {result.PacketAuthoringCheck.BeforePublish}");
        writer.WriteLine($"- **per-criterion satisfiability:** {result.PacketAuthoringCheck.PerCriterionSatisfiability}");
        writer.WriteLine($"- **negative-criterion scoping:** {result.PacketAuthoringCheck.NegativeCriterionScoping}");
        writer.WriteLine($"- **request-update condition:** {result.PacketAuthoringCheck.RequestUpdateCondition}");
        writer.WriteLine($"- **discriminating pair:** {result.PacketAuthoringCheck.DiscriminatingPair}");
        foreach (var example in result.PacketAuthoringCheck.RecognitionExamples) writer.WriteLine($"- **recognition example:** {example}");
        writer.WriteLine($"- **G770 resolution:** {result.PacketAuthoringCheck.G770ResolutionRule}");
        writer.WriteLine();
        writer.WriteLine("## 9. External-residence operating contract (G775)");
        writer.WriteLine($"- **frontend relabel first:** {result.ExternalResidenceOperatingContract.FrontendRelabel}");
        writer.WriteLine($"- **routing-root law:** {result.ExternalResidenceOperatingContract.RoutingRootMust}");
        writer.WriteLine($"- **collect:** {result.ExternalResidenceOperatingContract.CollectLoop}");
        writer.WriteLine($"- **wake channel:** {result.ExternalResidenceOperatingContract.WakeChannelPattern}");
        writer.WriteLine($"- **declared wake:** {result.ExternalResidenceOperatingContract.WakeChannelDeclaration}");
        writer.WriteLine($"- **worked example:** {result.ExternalResidenceOperatingContract.OrcaWorkedExample}");
        if (result.ExternalResidenceOperatingContract.OrcaOperatingBlock is { } orcaOperatingBlock)
        {
            writer.WriteLine($"- **{orcaOperatingBlock.Label}:**");
            foreach (var step in orcaOperatingBlock.SetupOrder) writer.WriteLine($"  - {step}");
            writer.WriteLine($"  - send: `{orcaOperatingBlock.SendForm}`");
            writer.WriteLine($"  - check: `{orcaOperatingBlock.CheckForm}`");
            writer.WriteLine($"  - {orcaOperatingBlock.SharedChannel}");
            writer.WriteLine($"  - {orcaOperatingBlock.DurableRecord}");
        }
        writer.WriteLine($"- **different transition:** {result.ExternalResidenceOperatingContract.ResidenceTransition}");
        WriteList(writer, "## Negative invariants", result.NegativeInvariants);
    }

    private static void WriteList(TextWriter writer, string heading, IEnumerable<string> values)
    {
        writer.WriteLine();
        writer.WriteLine(heading);
        foreach (var value in values) writer.WriteLine($"- {value}");
        writer.WriteLine();
    }

    private static bool TryParseArguments(string[] args, out string? domain, out string? team, out string? routingRoot, out string format, out string error)
    {
        domain = team = routingRoot = null;
        format = "markdown";
        error = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is "--domain" or "--team" or "--routing-root" or "--format")
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    error = $"{argument} requires a value.";
                    return false;
                }
                var value = args[index].Trim();
                if (argument == "--domain") domain = value;
                else if (argument == "--team") team = value;
                else if (argument == "--routing-root") routingRoot = value;
                else if (value is "markdown" or "json") format = value;
                else
                {
                    error = $"--format must be 'markdown' or 'json' (got '{value}').";
                    return false;
                }
            }
            else
            {
                error = $"Unknown argument '{argument}'.";
                return false;
            }
        }
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record DesignThreadGuideResult
{
    public required string Process { get; init; }
    public required string PreviewStatus { get; init; }
    public required bool AgentKindNeutral { get; init; }
    public required string SessionLayerRule { get; init; }
    public string? Domain { get; init; }
    public string? Team { get; init; }
    public required string RoutingRoot { get; init; }
    public required DesignThreadReachability Reachability { get; init; }
    public required DesignThreadWakeRule WakeRule { get; init; }
    public required DesignThreadProvenance Provenance { get; init; }
    public required DesignThreadApproval Approval { get; init; }
    [JsonPropertyName("dialog_answering_rule")]
    public required DialogAnsweringRuleGuide DialogAnsweringRule { get; init; }
    public required DesignThreadResidualApproval ResidualApproval { get; init; }
    public required DesignThreadMergeAuthority MergeAuthority { get; init; }
    public required DesignThreadDelegationVerification DelegationVerification { get; init; }
    public required DesignThreadObservationBoundary ObservationBoundary { get; init; }
    public required DesignThreadTeamAndDutySplit TeamAndDutySplit { get; init; }
    [JsonPropertyName("research_delegation")]
    public required ResearchDelegationGuidance ResearchDelegation { get; init; }
    [JsonPropertyName("notify_delegate_assignment")]
    [JsonIgnore]
    public NotifyDelegateAssignmentGuidance NotifyDelegateAssignment { get; init; } = null!;
    public required DesignThreadMonitoring Monitoring { get; init; }
    public required DesignThreadReporting Reporting { get; init; }
    public required IReadOnlyList<string> NegativeInvariants { get; init; }
    public required DesignThreadPacketAuthoringCheck PacketAuthoringCheck { get; init; }
    public required DesignThreadExternalResidenceOperatingContract ExternalResidenceOperatingContract { get; init; }
    public required string UnreadableRepairResponse { get; init; }
}

internal sealed record DesignThreadReachability
{
    public required string Command { get; init; }
    public required string Catalog { get; init; }
    public required string Advisor { get; init; }
    public required DesignThreadExternalReader ExternalReader { get; init; }
}
internal sealed record DesignThreadExternalReader
{
    public required string Command { get; init; }
    public required string CursorRule { get; init; }
    public required string WaitRule { get; init; }
}
internal sealed record DesignThreadWakeRule { public required IReadOnlyList<string> ValidOutcomes { get; init; } public required IReadOnlyList<string> NotOutcomes { get; init; } }
internal sealed record DesignThreadProvenance { public required IReadOnlyList<string> Vocabulary { get; init; } public required string ExecutionUnitRule { get; init; } public required IReadOnlyList<string> ExternalOriginFields { get; init; } }
internal sealed record DesignThreadApproval { public required string ReadOnlyRule { get; init; } public required string MergeRule { get; init; } public required IReadOnlyList<string> MergeTransaction { get; init; } public required IReadOnlyList<string> ExplicitAcceptanceStillRequired { get; init; } }
internal sealed record DesignThreadResidualApproval { public required string PreviewStatus { get; init; } public required IReadOnlyList<string> Layers { get; init; } public required string NoPolicyRule { get; init; } public required string Incident { get; init; } public required string WatcherBoundary { get; init; } public required string Formula { get; init; } }
internal sealed record DesignThreadMergeAuthority { public required string Rule { get; init; } public required IReadOnlyList<string> Facts { get; init; } }
internal sealed record DesignThreadDelegationVerification { public required IReadOnlyList<string> Layers { get; init; } public required string Rule { get; init; } }
internal sealed record DesignThreadObservationBoundary
{
    public required string PaneReadRule { get; init; }
    public required string CanonicalEvidenceRule { get; init; }
    public required string RecoveryOwnershipRule { get; init; }
    public required string InspectRoute { get; init; }
    public required string FallbackRoute { get; init; }
    public required string KeystrokeBoundary { get; init; }
}
internal sealed record DesignThreadTeamAndDutySplit
{
    public required string Formula { get; init; }
    public required string MonitoringRule { get; init; }
    public required string OrchestrationOwnership { get; init; }
    public required string DesignMode { get; init; }
    public required IReadOnlyList<string> DesignEscalations { get; init; }
    [JsonPropertyName("review_seat_selection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReviewSeatSelectionGuidance? ReviewSeatSelection { get; init; }
}
internal sealed record DesignThreadMonitoring { public required string Separation { get; init; } public required string ResidualDesignCheck { get; init; } public required string BoundRule { get; init; } public required string GuideRefreshRule { get; init; } public required string DeploymentRule { get; init; } }
internal sealed record DesignThreadReporting { public required string Rule { get; init; } public required string HumanActionRule { get; init; } }
internal sealed record DesignThreadPacketAuthoringCheck
{
    public required string BeforePublish { get; init; }
    public required string PerCriterionSatisfiability { get; init; }
    public required string NegativeCriterionScoping { get; init; }
    public required string RequestUpdateCondition { get; init; }
    public required string DiscriminatingPair { get; init; }
    public required IReadOnlyList<string> RecognitionExamples { get; init; }
    public required string G770ResolutionRule { get; init; }
}

internal sealed record DesignThreadExternalResidenceOperatingContract
{
    public required string FrontendRelabel { get; init; }
    public required string RoutingRootMust { get; init; }
    public required string CollectLoop { get; init; }
    public required string WakeChannelPattern { get; init; }
    public required string WakeChannelDeclaration { get; init; }
    public required string OrcaWorkedExample { get; init; }
    [JsonPropertyName("orca_operating_block")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DesignThreadOrcaOperatingBlock? OrcaOperatingBlock { get; init; }
    public required string ResidenceTransition { get; init; }
}

internal sealed record DesignThreadOrcaOperatingBlock
{
    public required string Label { get; init; }
    public required IReadOnlyList<string> SetupOrder { get; init; }
    public required string SendForm { get; init; }
    public required string CheckForm { get; init; }
    public required string SharedChannel { get; init; }
    public required string DurableRecord { get; init; }
}
