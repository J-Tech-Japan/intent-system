using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G654: render-only, agent-kind-neutral design-thread operating contract.
/// This guide does not inspect terminals, supervise work, mutate state, or
/// launch a provider. It is deliberately identical across session layers.
/// </summary>
internal static class GuideDesignThreadCommand
{
    public const string CommandName = "intent-cli guide design-thread";
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
            writer.WriteLine("Read-only G654 design-thread operating contract. Preview-through-1.x; no provider launch, supervision, terminal parsing, or mutation.");
            return 0;
        }

        if (!TryParseArguments(args, out var domain, out var team, out var routingRoot, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildResult(domain, team, routingRoot);
        if (string.Equals(format, "json", StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    internal static DesignThreadGuideResult BuildResult(string? domain, string? team, string? routingRoot)
    {
        var root = string.IsNullOrWhiteSpace(routingRoot) ? "<routing-root>" : routingRoot!;
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
            ResidualApproval = new DesignThreadResidualApproval
            {
                PreviewStatus = "preview-through-1.x (G666/G682/G683/G689)",
                Layers = new[]
                {
                    "Interim in-contract path: eliminate each known dialog through agent-side allow configuration recorded in that kind's G636 unattended-launch recipe fields; never answer the dialog.",
                    "G683 supervision reads dialog-blocked seats and emits only exact recipe-registry classes; unmatched text is class unknown and escalate-only, never fuzzy-classified.",
                    "G689 adds the shell-command class and extracts a payload without granting a bare answer; every compound AST segment needs project-test, owned-scratch-delete, or exact-command-once scope, with digest/dialog audit. A validated match wakes orchestration only; design never answers; design never relays keystrokes.",
                },
                NoPolicyRule = "Without an exact validated pre-approve match or matching shell scoped policy, residual prompts are escalate-only; unknown, unmatched, bare shell classes, and pre-escalate prompts execute no answer.",
                Incident = "G666 measured the 2026-08-11 workspace wK Claude app safety relay refusal. Operator-filed #1469 then measured a 0.19.0 supervision cycle with 47 keys and no prompt/dialog/class/adjudication producer key, a review seat wedged three times in one day, and orchestration correctly refusing to fabricate a class. This is the same configured-looking-but-inert failure shape as #1465; the interim correction is agent-side allow configuration recorded in the G636 kind recipe fields, never a dialog answer.",
                WatcherBoundary = "Supervision classifies only literal registry entries and verifies every shell AST segment against scoped policy. Its sole answer path is the audited, orchestration-owned exact key sequence of a matched pre-approve rule; it never exposes an unscoped relay.",
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
            TeamAndDutySplit = new DesignThreadTeamAndDutySplit
            {
                Formula = "four judgment-bearing threads plus one supervision process",
                MonitoringRule = "Watcher infrastructure is not a fifth role: it holds no conversation, makes no judgment, and spends no model tokens. Do not staff monitoring with a language-model seat.",
                OrchestrationOwnership = "Orchestration owns detection, classification, and authorized recovery for every stall class, including review wedges.",
                DesignMode = "Design is event-driven and receives only the agreed escalation set.",
                DesignEscalations = DesignEscalations,
            },
            Monitoring = new DesignThreadMonitoring
            {
                Separation = "Supervision runs outside the design conversation and is consulted at most once per design wake.",
                ResidualDesignCheck = "At low frequency, design checks the last completed supervision-cycle record. Compare its age with the declared supervision-liveness bound; do not use a conversational heartbeat.",
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
                "No terminal parsing, provider launch, hidden fifth role, or design-owned stall recovery.",
                "No design-thread approval-keystroke relay and no watcher restart, correction, dialog answer, or key send.",
                "No fuzzy prompt classification, unvalidated rule, unaudited answer, unscoped key relay, or design answer path.",
                "No canonical identity inferred from prose, transport state, or an unaccepted candidate.",
                "No publication, contract, priority, release, destructive, permission, security, or credential decision is silently broadened.",
            },
        };
    }

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
        writer.WriteLine("## Reachability");
        writer.WriteLine($"- command: `{result.Reachability.Command}`");
        writer.WriteLine($"- catalog: `{result.Reachability.Catalog}`");
        writer.WriteLine($"- design-role advisor: `{result.Reachability.Advisor}` names this guide.");
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
        writer.WriteLine("## 4. Merge-authority comparison");
        writer.WriteLine($"- {result.MergeAuthority.Rule}");
        foreach (var fact in result.MergeAuthority.Facts) writer.WriteLine($"- compare: {fact}");
        WriteList(writer, "## 5. Three-layer delegation verification", result.DelegationVerification.Layers);
        writer.WriteLine(result.DelegationVerification.Rule);
        writer.WriteLine();
        writer.WriteLine("## 6. Team formula, duty split, and monitoring separation");
        writer.WriteLine($"- formula: **{result.TeamAndDutySplit.Formula}**");
        writer.WriteLine($"- {result.TeamAndDutySplit.MonitoringRule}");
        writer.WriteLine($"- {result.TeamAndDutySplit.OrchestrationOwnership}");
        writer.WriteLine($"- {result.TeamAndDutySplit.DesignMode}");
        foreach (var item in result.TeamAndDutySplit.DesignEscalations) writer.WriteLine($"  - {item}");
        writer.WriteLine($"- {result.Monitoring.Separation}");
        writer.WriteLine($"- {result.Monitoring.ResidualDesignCheck}");
        writer.WriteLine($"- {result.Monitoring.BoundRule}");
        writer.WriteLine($"- {result.Monitoring.GuideRefreshRule}");
        writer.WriteLine($"- {result.Monitoring.DeploymentRule}");
        writer.WriteLine();
        writer.WriteLine("## 7. Outcome-shaped reporting");
        writer.WriteLine($"- {result.Reporting.Rule}");
        writer.WriteLine($"- {result.Reporting.HumanActionRule}");
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
    public required DesignThreadResidualApproval ResidualApproval { get; init; }
    public required DesignThreadMergeAuthority MergeAuthority { get; init; }
    public required DesignThreadDelegationVerification DelegationVerification { get; init; }
    public required DesignThreadTeamAndDutySplit TeamAndDutySplit { get; init; }
    public required DesignThreadMonitoring Monitoring { get; init; }
    public required DesignThreadReporting Reporting { get; init; }
    public required IReadOnlyList<string> NegativeInvariants { get; init; }
}

internal sealed record DesignThreadReachability { public required string Command { get; init; } public required string Catalog { get; init; } public required string Advisor { get; init; } }
internal sealed record DesignThreadWakeRule { public required IReadOnlyList<string> ValidOutcomes { get; init; } public required IReadOnlyList<string> NotOutcomes { get; init; } }
internal sealed record DesignThreadProvenance { public required IReadOnlyList<string> Vocabulary { get; init; } public required string ExecutionUnitRule { get; init; } public required IReadOnlyList<string> ExternalOriginFields { get; init; } }
internal sealed record DesignThreadApproval { public required string ReadOnlyRule { get; init; } public required string MergeRule { get; init; } public required IReadOnlyList<string> MergeTransaction { get; init; } public required IReadOnlyList<string> ExplicitAcceptanceStillRequired { get; init; } }
internal sealed record DesignThreadResidualApproval { public required string PreviewStatus { get; init; } public required IReadOnlyList<string> Layers { get; init; } public required string NoPolicyRule { get; init; } public required string Incident { get; init; } public required string WatcherBoundary { get; init; } public required string Formula { get; init; } }
internal sealed record DesignThreadMergeAuthority { public required string Rule { get; init; } public required IReadOnlyList<string> Facts { get; init; } }
internal sealed record DesignThreadDelegationVerification { public required IReadOnlyList<string> Layers { get; init; } public required string Rule { get; init; } }
internal sealed record DesignThreadTeamAndDutySplit { public required string Formula { get; init; } public required string MonitoringRule { get; init; } public required string OrchestrationOwnership { get; init; } public required string DesignMode { get; init; } public required IReadOnlyList<string> DesignEscalations { get; init; } }
internal sealed record DesignThreadMonitoring { public required string Separation { get; init; } public required string ResidualDesignCheck { get; init; } public required string BoundRule { get; init; } public required string GuideRefreshRule { get; init; } public required string DeploymentRule { get; init; } }
internal sealed record DesignThreadReporting { public required string Rule { get; init; } public required string HumanActionRule { get; init; } }
