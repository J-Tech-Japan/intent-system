using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G807: the Steward seat's role-facing operating contract. This route is
/// deliberately render-only and metadata-free: it explains what the
/// transmission boundary may do, what it must hand off, and which refusals
/// are intentional without adding authority to the Steward role.
/// </summary>
internal static class GuideStewardThreadCommand
{
    internal const string CommandName = "intent-cli guide steward-thread";
    internal const string ContractVersion = "g807-steward-thread/v1";
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string UsageLine =
        "Usage: intent-cli guide steward-thread [--format markdown|json]";

    internal static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseFormat(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var guide = BuildGuide();
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(guide, JsonOptions));
        }
        else
        {
            WriteMarkdown(writer, guide);
        }

        return 0;
    }

    internal static StewardThreadGuide BuildGuide() => new()
    {
        Route = CommandName,
        Role = LogicalRoleNormalizer.Steward,
        ContractVersion = ContractVersion,
        ReadOnly = true,
        MetadataFree = true,
        IdentityAndReaderPath = new StewardIdentityAndReaderPath
        {
            Identity = "Steward is the transport boundary: it relays recorded work and reports observable delivery without deciding design or review questions.",
            ReaderPath = "Reader path: read the Steward's `intent-cli notify` inbox/report surfaces from the child or role worktree; the guide itself never reads `.intent-cli/config.toml`, queue-state, packets, intents, panes, or a runtime.",
            EntryPoint = "intent-cli guide steward-thread --format markdown|json",
        },
        MayActAlone = new[]
        {
            "Read an assigned notification and preserve its task, sender, recipient, and evidence fields.",
            "Relay an already-authorized opaque ruling or delivery report byte-identically through the canonical notify surface.",
            "Report delivery state, an observed blocker, or a missing upstream decision to the orchestrator.",
            "Use `intent-cli notify adjudicate live-pair --pane <pane-id> --format json` to obtain the current CAS pair before an approved dialog action.",
        },
        HandoffRules = new StewardHandoffRules
        {
            Design = "Questions about product intent, design, or policy go to the `architect` through `intent-cli notify delegate --from steward --to architect`; the Steward does not answer them.",
            Review = "Questions about correctness, acceptance, or review judgment go to the `reviewer` through `intent-cli notify delegate --from steward --to reviewer`; the Steward does not answer them.",
            Orchestration = "Dispatch, queue, lifecycle, and recovery work go to the `orchestrator` through `intent-cli notify delegate --from steward --to orchestrator`; the Steward does not create a replacement intake.",
            Evidence = "A handoff carries the original task, expected artifact, source evidence, and recipient; missing or ambiguous evidence is reported rather than invented.",
        },
        RefusalsAndReporting = new StewardRefusalsAndReporting
        {
            MissingDelegation = "`intent-cli notify delegate --from steward` refuses when there is no real upstream delegation; name the missing task and the judgement seat instead of fabricating one.",
            RulingBoundary = "A research or progress report that contains a ruling is refused unless it carries the real upstream `architect` or `reviewer` decision; the Steward may relay but never authors that ruling.",
            ReportRoute = "Use `intent-cli notify report --from steward --to orchestrator --task-id <task-id> --summary <summary> --format json` for delivery state, refusal evidence, and unresolved handoffs.",
            NoFreshIntake = "A recorded pending delegation is resolved by its named recipient and delivery state; the Steward never recommends a fresh intake to hide a misroute.",
        },
        DialogPath = new StewardDialogPath
        {
            LivePair = "`intent-cli notify adjudicate live-pair --pane <pane-id> --format json` returns the live state-sequence and text-hash pair for a read-only CAS check.",
            Action = "Only an operator-approved, exact class/scope action may continue through `intent-cli notify adjudicate`; a stale or mismatching pair is refused.",
            Boundary = "The live-pair route supplies CAS inputs; it does not grant the Steward design or review authority and it never sends an unscoped answer.",
        },
        WorkingTreeDiscipline = new StewardWorkingTreeDiscipline
        {
            Rule = "Keep the Steward in its assigned role worktree; do not create or register another worktree, switch a task's branch, or edit host metadata to make a delivery appear complete.",
            Writes = "The guide is read-only. Code, queue state, labels, merges, releases, and package publishing remain owned by their canonical roles and commands.",
            Evidence = "When a source, recipient, or delivery fact is unavailable, preserve the refusal and report the exact observable evidence.",
        },
        G796Boundary = "G796 boundary — Steward is not a specialist: it carries weight on transmission only, answers neither design nor review questions itself, and relays an upstream ruling without changing its bytes, digest, or origin.",
        NegativeBoundaries = new[]
        {
            "No vendor or runtime is a role; no vendor default is introduced. No size threshold changes this guide.",
            "No G796 routing or ruling boundary is weakened; a missing upstream delegation or ruling remains a hard refusal.",
            "No guide route, queue-state field, lifecycle label, or product behavior is renamed or mutated by rendering this contract.",
        },
    };

    private static bool TryParseFormat(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--format", StringComparison.Ordinal))
            {
                error = $"Unknown argument '{args[index]}'.";
                return false;
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[++index]))
            {
                error = "--format requires markdown or json.";
                return false;
            }

            format = args[index];
            if (format is not FormatJson and not FormatMarkdown)
            {
                error = "--format must be markdown or json.";
                return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide steward-thread");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only, metadata-free Steward operating contract: relay evidence, hand off judgment, and preserve the G796 ruling boundary.");
    }

    private static void WriteMarkdown(TextWriter writer, StewardThreadGuide guide)
    {
        writer.WriteLine("# intent-cli — Steward thread operating contract (G807)");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("## Identity and reader path");
        writer.WriteLine();
        writer.WriteLine($"- identity: {guide.IdentityAndReaderPath.Identity}");
        writer.WriteLine($"- reader path: {guide.IdentityAndReaderPath.ReaderPath}");
        writer.WriteLine($"- entry point: `{guide.IdentityAndReaderPath.EntryPoint}`");
        writer.WriteLine();
        writer.WriteLine("## What Steward may do alone");
        writer.WriteLine();
        foreach (var action in guide.MayActAlone)
        {
            writer.WriteLine($"- {action}");
        }

        writer.WriteLine();
        writer.WriteLine("## Handoff rules");
        writer.WriteLine();
        writer.WriteLine($"- design: {guide.HandoffRules.Design}");
        writer.WriteLine($"- review: {guide.HandoffRules.Review}");
        writer.WriteLine($"- orchestration: {guide.HandoffRules.Orchestration}");
        writer.WriteLine($"- evidence: {guide.HandoffRules.Evidence}");
        writer.WriteLine();
        writer.WriteLine("## Refusals and report routing");
        writer.WriteLine();
        writer.WriteLine($"- missing delegation: {guide.RefusalsAndReporting.MissingDelegation}");
        writer.WriteLine($"- ruling boundary: {guide.RefusalsAndReporting.RulingBoundary}");
        writer.WriteLine($"- report route: {guide.RefusalsAndReporting.ReportRoute}");
        writer.WriteLine($"- existing intake: {guide.RefusalsAndReporting.NoFreshIntake}");
        writer.WriteLine();
        writer.WriteLine("## Dialog path");
        writer.WriteLine();
        writer.WriteLine($"- live pair: {guide.DialogPath.LivePair}");
        writer.WriteLine($"- action: {guide.DialogPath.Action}");
        writer.WriteLine($"- boundary: {guide.DialogPath.Boundary}");
        writer.WriteLine();
        writer.WriteLine("## Working-tree discipline");
        writer.WriteLine();
        writer.WriteLine($"- rule: {guide.WorkingTreeDiscipline.Rule}");
        writer.WriteLine($"- writes: {guide.WorkingTreeDiscipline.Writes}");
        writer.WriteLine($"- evidence: {guide.WorkingTreeDiscipline.Evidence}");
        writer.WriteLine();
        writer.WriteLine("## G796 boundary");
        writer.WriteLine();
        writer.WriteLine(guide.G796Boundary);
        writer.WriteLine();
        writer.WriteLine("## Negative boundaries");
        writer.WriteLine();
        foreach (var boundary in guide.NegativeBoundaries)
        {
            writer.WriteLine($"- {boundary}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

internal sealed record StewardThreadGuide
{
    [JsonPropertyName("route")] public required string Route { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("contract_version")] public required string ContractVersion { get; init; }
    [JsonPropertyName("read_only")] public required bool ReadOnly { get; init; }
    [JsonPropertyName("metadata_free")] public required bool MetadataFree { get; init; }
    [JsonPropertyName("identity_and_reader_path")] public required StewardIdentityAndReaderPath IdentityAndReaderPath { get; init; }
    [JsonPropertyName("may_act_alone")] public required IReadOnlyList<string> MayActAlone { get; init; }
    [JsonPropertyName("handoff_rules")] public required StewardHandoffRules HandoffRules { get; init; }
    [JsonPropertyName("refusals_and_reporting")] public required StewardRefusalsAndReporting RefusalsAndReporting { get; init; }
    [JsonPropertyName("dialog_path")] public required StewardDialogPath DialogPath { get; init; }
    [JsonPropertyName("working_tree_discipline")] public required StewardWorkingTreeDiscipline WorkingTreeDiscipline { get; init; }
    [JsonPropertyName("g796_boundary")] public required string G796Boundary { get; init; }
    [JsonPropertyName("negative_boundaries")] public required IReadOnlyList<string> NegativeBoundaries { get; init; }
}

internal sealed record StewardIdentityAndReaderPath
{
    [JsonPropertyName("identity")] public required string Identity { get; init; }
    [JsonPropertyName("reader_path")] public required string ReaderPath { get; init; }
    [JsonPropertyName("entry_point")] public required string EntryPoint { get; init; }
}

internal sealed record StewardHandoffRules
{
    [JsonPropertyName("design")] public required string Design { get; init; }
    [JsonPropertyName("review")] public required string Review { get; init; }
    [JsonPropertyName("orchestration")] public required string Orchestration { get; init; }
    [JsonPropertyName("evidence")] public required string Evidence { get; init; }
}

internal sealed record StewardRefusalsAndReporting
{
    [JsonPropertyName("missing_delegation")] public required string MissingDelegation { get; init; }
    [JsonPropertyName("ruling_boundary")] public required string RulingBoundary { get; init; }
    [JsonPropertyName("report_route")] public required string ReportRoute { get; init; }
    [JsonPropertyName("no_fresh_intake")] public required string NoFreshIntake { get; init; }
}

internal sealed record StewardDialogPath
{
    [JsonPropertyName("live_pair")] public required string LivePair { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
    [JsonPropertyName("boundary")] public required string Boundary { get; init; }
}

internal sealed record StewardWorkingTreeDiscipline
{
    [JsonPropertyName("rule")] public required string Rule { get; init; }
    [JsonPropertyName("writes")] public required string Writes { get; init; }
    [JsonPropertyName("evidence")] public required string Evidence { get; init; }
}
