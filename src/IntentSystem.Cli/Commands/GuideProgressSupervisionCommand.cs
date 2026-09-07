using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Installed, metadata-free G812 contract. The guide is descriptive only: it
/// never reads host queue state, starts a provider, or performs recovery.
/// </summary>
internal static class GuideProgressSupervisionCommand
{
    public const string CommandName = "intent-cli guide progress-supervision";
    public static readonly IReadOnlyList<string> SupportedFormats = ["markdown", "json"];

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
        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine("Usage: intent-cli guide progress-supervision [--section role-contracts] [--format markdown|json]");
            return 0;
        }

        var format = "markdown";
        var section = string.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--format" when i + 1 < args.Length: format = args[++i]; break;
                case "--section" when i + 1 < args.Length: section = args[++i]; break;
                default:
                    writer.WriteLine($"invalid-guide-progress-supervision: unknown or incomplete option '{args[i]}'");
                    return 1;
            }
        }
        if (!SupportedFormats.Contains(format, StringComparer.Ordinal))
        {
            writer.WriteLine("invalid-guide-progress-supervision: --format must be markdown or json");
            return 1;
        }
        if (!string.IsNullOrWhiteSpace(section) && section != "role-contracts")
        {
            writer.WriteLine("invalid-guide-progress-supervision: supported section is role-contracts");
            return 1;
        }

        var guide = BuildGuide();
        if (format == "json")
        {
            var output = string.IsNullOrWhiteSpace(section)
                ? JsonSerializer.Serialize(guide, JsonOptions)
                : JsonSerializer.Serialize(new { route = CommandName, section, role_contracts = guide.RoleContracts }, JsonOptions);
            writer.WriteLine(CompletionChannelGuidance.AppendJson(output));
        }
        else
        {
            WriteMarkdown(writer, guide, section);
        }
        return 0;
    }

    internal static ProgressSupervisionGuide BuildGuide() => new()
    {
        ContractVersion = ProgressSupervisionConstants.SchemaVersion,
        Route = CommandName,
        ReadOnly = true,
        MetadataFree = true,
        Purpose = "Correctness-first progress supervision correlates durable transitions and bounded recovery; liveness, delivery, pane activity, model output, or a local commit are never completion.",
        Ownership = new[]
        {
            "Architect owns design decisions and attributed delegation.",
            "Builder owns code-to-PR and canonical completion after claim and selection gates.",
            "Reviewer owns independent exact-head validation and the canonical verdict.",
            "Orchestrator owns event-first detection, canonical selection, reconciliation, recovery, and escalation.",
            "Steward independently observes compact health and canonical receipts and escalates anomalies without inventing rulings or mutating host state.",
        },
        Transitions = new[]
        {
            "claim -> selected unit -> relevant commit -> remote PR -> independent review -> closeout -> canonical report receipt",
            "merge-ready -> canonical closeout (120s) -> next eligible work consumption (60s)",
            "completion event -> required recipient receipt -> consumption; delivery is not receipt and receipt is not consumption",
        },
        PhaseSkip = "A phase skip is permitted only with typed durable evidence (from/to phase, reason, Architect or Orchestrator approval, and evidence link); it never bypasses independent review or receipt obligations.",
        Deadlines = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["dispatch-to-claim"] = 60,
            ["claim-to-selected"] = 60,
            ["selected-to-relevant-commit"] = 300,
            ["commit-to-remote-pr"] = 600,
            ["pr-ready-to-independent-verdict"] = 900,
            ["verified-request-update-to-repair-selection"] = 60,
            ["terminal-disposition-to-closeout-report-receipt"] = 120,
            ["completed-to-next-eligible-work"] = 60,
        },
        Detection = "Event-first with independent F=30s, measured jitter J and maximum sweep P; D=ceil(F+J+P)+1 and D must be <=60s. A missing, stale, or corrupt source is explicit unknown, never healthy.",
        Recovery = "At a due transition the first action is a canonical recipient status/liveness probe; at most two recovery attempts are made for one unchanged phase episode with 30s backoff, then an attributable infrastructure escalation retains owner, deadline, attempts and result.",
        Bootstrap = "When the primary delegation route fails, the controller may use one validated host-side bootstrap adapter for one authorized published unit and the original intended recipient. Claim, WIP, preflight, failed-route evidence and exactly-once task/nonce identity are checked at execution; event-supplied commands and roots, topology relabeling, spoofed sender, and external-owner process operations are refused.",
        Evidence = "Versioned project-neutral evidence includes domain/team/project/repo/unit, phase/episode, claim and selected action, work root, commit/base/PR/head/review/report/ack/closeout identities, observed/freshness/eligibility/last-progress times, T/F/J/P/D, deadline, attempt/result/next action, owner/severity, source failures, links and a stable semantic fingerprint excluding poll age.",
        Health = "Compact health reports open/overdue counts, oldest overdue duration, last successful floor/writer, source failures, next owner/action/deadline and evidence links. A healthy record is fresh with no overdue transition; unknown is explicit.",
        IncidentLearning = "Every failure records an incident identity, evidence or known unknowns, owner, correction, verification and reusable lesson. Learning closes only after a content-bearing knowledge-writeback commit/path/digest is verified, or remains open as an owned linked learning task with deadline.",
        RoleContracts = RoleContracts(),
        Handoff = "G811 completion/return-ack/reconcile primitives carry completion to reviewer, orchestrator and steward. Each edge records event, normalized recipient identity, delivery, receipt and consumption separately; fanout is idempotent and a missing edge remains pending without repeating completed work.",
        ReviewDrift = "A current-head attributed canonical reviewer verdict outranks a stale selector wait. Repository/unit/head, reviewer, artifact, findings, verdict time and label-transition evidence are checked; obsolete, forged, superseded or unavailable verdicts refuse repair. A verified request-update starts T=60s from its durable verdict time and is actioned by T+D+30; GitHub review state is corroboration only.",
        FaultMatrix = ProgressFaultMatrix.All.Select(fault => fault.ToString()).ToArray(),
        NegativeBoundaries = new[]
        {
            "No model call, pane read, process start, shell command, focus, key send or external-owner herdr operation is part of deterministic detection or recovery.",
            "No polling assignment to Architect or Reviewer on unchanged health; Steward observes and escalates without design rulings.",
            "No manual labels, claim/WIP bypass, duplicate intake, topology mutation, or deadline widening to save cost.",
            "Benchmark and live qualification are evidence requirements; this guide does not claim that a live scheduler is installed or qualified.",
        },
        CostAwareContract = NotifyCostAwareSupervisionContract.BuildGuide(),
        Commands = new[]
        {
            "intent-cli guide progress-supervision --format markdown|json",
            "intent-cli guide progress-supervision --section role-contracts --format markdown|json",
            "intent-cli automation progress-supervision --domain <d> --team <t> --unit <u> --format json",
            "intent-cli automation progress-supervision --domain <d> --team <t> --unit <u> --evidence-file <file> --write --format json",
            "intent-cli notify supervise --event-mode --once --format json",
        },
    };

    private static IReadOnlyList<ProgressRoleContract> RoleContracts() =>
    [
        new() { CanonicalRole = "architect", Aliases = ["design"], Inputs = "operator intent and compact escalations", DurableOutputs = "reviewed packet/design ruling and bounded delegation", Eligibility = "bounded decision or packet amendment only", FailureAction = "escalate with owner, question, artifact and deadline" },
        new() { CanonicalRole = "orchestrator", Aliases = ["orchestration"], Inputs = "graph, claims, worker, PR/CI, verdict and receipt evidence", DurableOutputs = "selection, dispatch, reconciliation, recovery and health", Eligibility = "event-first plus qualified F30/D<=60 checks", FailureAction = "retain unit and perform authorized bounded recovery" },
        new() { CanonicalRole = "builder", Aliases = ["implementation"], Inputs = "published unit, claim and canonical selection", DurableOutputs = "relevant commit, PR, tests and canonical completion", Eligibility = "after claim, WIP and recipient gates", FailureAction = "report a concrete blocker with task/nonce/artifact" },
        new() { CanonicalRole = "reviewer", Aliases = ["review"], Inputs = "immutable packet, exact PR head and evidence", DurableOutputs = "head-bound verdict/report", Eligibility = "independent exact-head validation", FailureAction = "retain request-update or approval as durable verdict" },
        new() { CanonicalRole = "steward", Aliases = [], Inputs = "controller heartbeat and independently read canonical work/receipt identities", DurableOutputs = "attributed anomaly/escalation and external outcome report", Eligibility = "changed semantic fingerprint or due deadline, not every tick", FailureAction = "escalate exact anomaly to architect without inventing a ruling" },
    ];

    private static void WriteMarkdown(TextWriter writer, ProgressSupervisionGuide guide, string section)
    {
        writer.WriteLine("# Progress supervision — correctness-first controller (G812)");
        writer.WriteLine();
        writer.WriteLine($"- route: `{guide.Route}`");
        writer.WriteLine("- mode: read-only guide; metadata-free; no provider/model invocation");
        writer.WriteLine();
        writer.WriteLine("## Ownership");
        foreach (var item in guide.Ownership) writer.WriteLine($"- {item}");
        writer.WriteLine();
        writer.WriteLine("## Durable transition and deadlines");
        foreach (var item in guide.Transitions) writer.WriteLine($"- {item}");
        foreach (var pair in guide.Deadlines) writer.WriteLine($"- `{pair.Key}`: `{pair.Value}s`");
        writer.WriteLine($"- detection: {guide.Detection}");
        writer.WriteLine($"- phase skip: {guide.PhaseSkip}");
        writer.WriteLine();
        writer.WriteLine("## Recovery and bootstrap");
        writer.WriteLine($"- {guide.Recovery}");
        writer.WriteLine($"- {guide.Bootstrap}");
        writer.WriteLine();
        writer.WriteLine("## Evidence, health, and incident learning");
        writer.WriteLine($"- {guide.Evidence}");
        writer.WriteLine($"- {guide.Health}");
        writer.WriteLine($"- {guide.IncidentLearning}");
        writer.WriteLine();
        writer.WriteLine("## G810 cost-aware supervision");
        if (guide.CostAwareContract is { } costAware)
        {
            writer.WriteLine($"- contract: `{costAware.ContractVersion}`");
            writer.WriteLine($"- timing: {costAware.Timing}");
            writer.WriteLine($"- cost: {costAware.Cost}");
            writer.WriteLine($"- recovery: {costAware.Recovery}");
            writer.WriteLine($"- corruption: {costAware.Corruption}");
            writer.WriteLine($"- safety: {costAware.Safety}");
            writer.WriteLine($"- transferability: {costAware.Transferability}");
            foreach (var coverage in costAware.Coverage)
                writer.WriteLine($"- source `{coverage.Kind}`: source={coverage.AuthoritativeSource}; identity={coverage.Identity}; eligibility={coverage.Eligibility}; owner={coverage.Owner}; next={coverage.NextAction}");
        }
        writer.WriteLine();
        writer.WriteLine("## Role contracts");
        foreach (var contract in guide.RoleContracts)
            writer.WriteLine($"- `{contract.CanonicalRole}` (aliases: `{string.Join("`, `", contract.Aliases)}`): inputs={contract.Inputs}; outputs={contract.DurableOutputs}; eligibility={contract.Eligibility}; failure={contract.FailureAction}");
        writer.WriteLine();
        writer.WriteLine("## Completion-driven handoff and review drift");
        writer.WriteLine($"- {guide.Handoff}");
        writer.WriteLine($"- {guide.ReviewDrift}");
        writer.WriteLine($"- fault matrix: `{string.Join("`, `", guide.FaultMatrix)}`; every fault retains an open owner/deadline and cannot complete a unit by delivery or liveness alone.");
        writer.WriteLine();
        writer.WriteLine("## Commands");
        foreach (var command in guide.Commands) writer.WriteLine($"- `{command}`");
        writer.WriteLine();
        writer.WriteLine("## Negative boundaries");
        foreach (var item in guide.NegativeBoundaries) writer.WriteLine($"- {item}");
        if (section == "role-contracts")
        {
            writer.WriteLine();
            writer.WriteLine("## Requested section: role-contracts");
            foreach (var contract in guide.RoleContracts)
                writer.WriteLine($"- `{contract.CanonicalRole}`: detection inputs `{contract.Inputs}`; durable outputs `{contract.DurableOutputs}`; failure action `{contract.FailureAction}`");
        }
    }

    internal static string AppendMarkdown(string markdown) =>
        markdown + Environment.NewLine
        + "- **Progress supervision route (G812):** read the reusable contract with `intent-cli guide progress-supervision --format markdown|json`; it is metadata-free and does not add a host-state mutation route." + Environment.NewLine;

    internal static string AppendJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is JsonObject obj)
        {
            obj["progress_supervision_route"] = new JsonObject
            {
                ["command"] = CommandName,
                ["formats"] = new JsonArray("markdown", "json"),
                ["metadata_free"] = true,
                ["read_only"] = true,
            };
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>()) item["progress_supervision_route"] = JsonValue.Create(CommandName);
        }
        return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
    }
}

internal sealed record ProgressSupervisionGuide
{
    [JsonPropertyName("contract_version")] public required string ContractVersion { get; init; }
    [JsonPropertyName("route")] public required string Route { get; init; }
    [JsonPropertyName("read_only")] public bool ReadOnly { get; init; }
    [JsonPropertyName("metadata_free")] public bool MetadataFree { get; init; }
    [JsonPropertyName("purpose")] public required string Purpose { get; init; }
    [JsonPropertyName("ownership")] public IReadOnlyList<string> Ownership { get; init; } = [];
    [JsonPropertyName("transitions")] public IReadOnlyList<string> Transitions { get; init; } = [];
    [JsonPropertyName("deadlines_seconds")] public IReadOnlyDictionary<string, int> Deadlines { get; init; } = new Dictionary<string, int>();
    [JsonPropertyName("detection")] public required string Detection { get; init; }
    [JsonPropertyName("phase_skip")] public required string PhaseSkip { get; init; }
    [JsonPropertyName("recovery")] public required string Recovery { get; init; }
    [JsonPropertyName("bootstrap")] public required string Bootstrap { get; init; }
    [JsonPropertyName("evidence")] public required string Evidence { get; init; }
    [JsonPropertyName("health")] public required string Health { get; init; }
    [JsonPropertyName("incident_learning")] public required string IncidentLearning { get; init; }
    [JsonPropertyName("role_contracts")] public IReadOnlyList<ProgressRoleContract> RoleContracts { get; init; } = [];
    [JsonPropertyName("handoff")] public required string Handoff { get; init; }
    [JsonPropertyName("review_drift")] public required string ReviewDrift { get; init; }
    [JsonPropertyName("fault_matrix")] public IReadOnlyList<string> FaultMatrix { get; init; } = [];
    [JsonPropertyName("commands")] public IReadOnlyList<string> Commands { get; init; } = [];
    [JsonPropertyName("negative_boundaries")] public IReadOnlyList<string> NegativeBoundaries { get; init; } = [];
    [JsonPropertyName("cost_aware_contract")] public NotifyCostAwareGuide? CostAwareContract { get; init; }
}

internal sealed record ProgressRoleContract
{
    [JsonPropertyName("canonical_role")] public required string CanonicalRole { get; init; }
    [JsonPropertyName("aliases")] public IReadOnlyList<string> Aliases { get; init; } = [];
    [JsonPropertyName("detection_inputs")] public required string Inputs { get; init; }
    [JsonPropertyName("durable_outputs")] public required string DurableOutputs { get; init; }
    [JsonPropertyName("eligibility_deadlines")] public required string Eligibility { get; init; }
    [JsonPropertyName("failure_action")] public required string FailureAction { get; init; }
}

internal static class ProgressSupervisionGuidance
{
    public static string AppendMarkdown(string markdown) => GuideProgressSupervisionCommand.AppendMarkdown(markdown);
    public static string AppendJson(string json) => GuideProgressSupervisionCommand.AppendJson(json);
}
