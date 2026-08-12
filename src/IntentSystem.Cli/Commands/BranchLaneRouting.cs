using System.Security.Cryptography;
using System.Text;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G668's preview branch-lane vocabulary. A registry definition is mutable
/// host configuration; a <see cref="BranchRoutingSnapshot"/> is the immutable
/// fact copied into a newly accepted packet.
/// </summary>
internal sealed record BranchLaneDefinition
{
    public required string Id { get; init; }

    public required string StartBranch { get; init; }

    public required string PrBaseBranch { get; init; }

    public required string LandingMode { get; init; }
}

internal sealed record BranchLaneRegistry
{
    public required string DefinitionRevision { get; init; }

    public string? DefaultLane { get; init; }

    public required IReadOnlyDictionary<string, BranchLaneDefinition> Lanes { get; init; }

    public bool IsConfigured => Lanes.Count > 0;
}

internal sealed record BranchRoutingSnapshot
{
    public required string LaneId { get; init; }

    public required string DefinitionRevision { get; init; }

    public required string StartBranch { get; init; }

    public required string PrBaseBranch { get; init; }

    public required string LandingMode { get; init; }
}

internal sealed record BranchLaneSelection
{
    public required BranchLaneDefinition Definition { get; init; }

    public required BranchRoutingSnapshot Snapshot { get; init; }

    public required string Source { get; init; }

    public bool IsExplicit => true;
}

/// <summary>
/// Resolves the G668 registry and keeps the pre-G668 policy path intact when
/// no registry is configured. The compatibility adapter is deliberately
/// internal: legacy policy names and their existing JSON surfaces do not
/// change, while the rest of the packet flow can reason over one lane shape.
/// </summary>
internal static class BranchLaneResolver
{
    public const string SourceExplicit = "explicit";
    public const string SourceDomainDefault = "domain-default";

    public static BranchLaneSelection? ResolveForDraft(
        IntentSystem.Cli.Models.ProjectConfig project,
        string domain,
        string? requestedLane)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var registry = project.BranchLanes is not null
            && project.BranchLanes.TryGetValue(domain.Trim(), out var domainRegistry)
            ? domainRegistry
            : null;
        if (registry is null || !registry.IsConfigured)
        {
            if (!string.IsNullOrWhiteSpace(requestedLane))
            {
                throw new InvalidOperationException(
                    $"Branch lane '{requestedLane}' was requested, but no named branch-lane registry is configured. "
                    + "Declare [project.branch_lanes] before selecting a lane.");
            }

            // Registry-less hosts retain the exact legacy packet/body shape.
            // The adapter exists for comparisons and future callers, but no
            // snapshot is materialised on this compatibility path.
            return null;
        }

        var laneId = string.IsNullOrWhiteSpace(requestedLane)
            ? registry.DefaultLane
            : requestedLane.Trim();
        var source = string.IsNullOrWhiteSpace(requestedLane)
            ? SourceDomainDefault
            : SourceExplicit;

        if (string.IsNullOrWhiteSpace(laneId))
        {
            throw new InvalidOperationException(
                "A named branch lane is required when a registry is configured and no default_lane is declared. "
                + "Pass `--lane <id>` or configure `default_lane`.");
        }

        if (!registry.Lanes.TryGetValue(laneId, out var definition))
        {
            throw new InvalidOperationException(
                $"Unknown branch lane '{laneId}'. Configured lanes: {string.Join(", ", registry.Lanes.Keys.OrderBy(key => key, StringComparer.Ordinal))}.");
        }

        return new BranchLaneSelection
        {
            Definition = definition,
            Source = source,
            Snapshot = new BranchRoutingSnapshot
            {
                LaneId = definition.Id,
                DefinitionRevision = registry.DefinitionRevision,
                StartBranch = definition.StartBranch,
                PrBaseBranch = definition.PrBaseBranch,
                LandingMode = definition.LandingMode
            }
        };
    }

    public static QueueRoutingSnapshot ToQueueProjection(BranchRoutingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new QueueRoutingSnapshot
        {
            LaneId = snapshot.LaneId,
            DefinitionRevision = snapshot.DefinitionRevision,
            StartBranch = snapshot.StartBranch,
            PrBaseBranch = snapshot.PrBaseBranch,
            LandingMode = snapshot.LandingMode,
        };
    }

    public static BranchRoutingSnapshot FromQueueProjection(QueueRoutingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new BranchRoutingSnapshot
        {
            LaneId = snapshot.LaneId,
            DefinitionRevision = snapshot.DefinitionRevision,
            StartBranch = snapshot.StartBranch,
            PrBaseBranch = snapshot.PrBaseBranch,
            LandingMode = snapshot.LandingMode,
        };
    }

    public static BranchLaneDefinition AdaptLegacy(
        string? policy,
        string effectiveBaseBranch)
    {
        var effectivePolicy = string.IsNullOrWhiteSpace(policy)
            ? CliRuntimeContracts.DefaultBaseBranchPolicy
            : policy.Trim();

        if (!BaseBranchPolicyContract.IsKnownPolicy(effectivePolicy))
        {
            throw new InvalidOperationException(
                $"Unknown base branch policy '{effectivePolicy}'. Expected '{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveBaseBranch);

        return new BranchLaneDefinition
        {
            Id = effectivePolicy,
            StartBranch = effectiveBaseBranch.Trim(),
            PrBaseBranch = effectiveBaseBranch.Trim(),
            LandingMode = string.Equals(
                effectivePolicy,
                CliRuntimeContracts.MainAiBaseBranchPolicy,
                StringComparison.Ordinal)
                ? "integration-batch"
                : "direct"
        };
    }

    public static BranchRoutingSnapshot? TryReadSnapshot(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        if (!PacketYamlDocument.TryParse(yaml, out var document, out _)
            || document is null)
        {
            return null;
        }

        return TryReadSnapshot(document.Fields);
    }

    public static BranchRoutingSnapshot? TryReadSnapshot(
        IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var laneId = Lookup(fields,
            "implementation_issue_packet.routing_snapshot.lane_id",
            "routing_snapshot.lane_id");
        var definitionRevision = Lookup(fields,
            "implementation_issue_packet.routing_snapshot.definition_revision",
            "routing_snapshot.definition_revision");
        var startBranch = Lookup(fields,
            "implementation_issue_packet.routing_snapshot.start_branch",
            "routing_snapshot.start_branch");
        var prBaseBranch = Lookup(fields,
            "implementation_issue_packet.routing_snapshot.pr_base_branch",
            "routing_snapshot.pr_base_branch");
        var landingMode = Lookup(fields,
            "implementation_issue_packet.routing_snapshot.landing_mode",
            "routing_snapshot.landing_mode");

        if (laneId is null
            && definitionRevision is null
            && startBranch is null
            && prBaseBranch is null
            && landingMode is null)
        {
            return null;
        }

        if (laneId is null
            || definitionRevision is null
            || startBranch is null
            || prBaseBranch is null
            || landingMode is null)
        {
            throw new InvalidOperationException(
                "routing_snapshot must declare lane_id, definition_revision, start_branch, pr_base_branch, and landing_mode.");
        }

        var declaredLane = Lookup(fields,
            "implementation_issue_packet.branch_lane",
            "branch_lane");
        if (declaredLane is not null
            && !string.Equals(declaredLane, laneId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"branch_lane '{declaredLane}' disagrees with routing_snapshot lane_id '{laneId}'.");
        }

        return new BranchRoutingSnapshot
        {
            LaneId = laneId,
            DefinitionRevision = definitionRevision,
            StartBranch = startBranch,
            PrBaseBranch = prBaseBranch,
            LandingMode = landingMode
        };
    }

    public static string? TryReadDeclaredLane(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Lookup(fields,
            "implementation_issue_packet.branch_lane",
            "branch_lane");
    }

    public static string? TryReadLaneSource(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Lookup(fields,
            "implementation_issue_packet.branch_lane_source",
            "branch_lane_source");
    }

    public static string ComputeDefinitionRevision(
        IReadOnlyDictionary<string, BranchLaneDefinition> lanes,
        string? declaredRevision)
    {
        ArgumentNullException.ThrowIfNull(lanes);

        if (!string.IsNullOrWhiteSpace(declaredRevision))
        {
            return declaredRevision.Trim();
        }

        var canonical = string.Join(
            "\n",
            lanes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Join(
                    "|",
                    pair.Value.Id,
                    pair.Value.StartBranch,
                    pair.Value.PrBaseBranch,
                    pair.Value.LandingMode)));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return $"derived:{digest[..16]}";
    }

    private static string? Lookup(
        IReadOnlyDictionary<string, string> fields,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

/// <summary>Small YAML renderer used by packet draft and projection repair.</summary>
internal static class BranchLaneRoutingYaml
{
    public static string RenderFields(
        BranchLaneSelection selection,
        string indent = "  ")
    {
        ArgumentNullException.ThrowIfNull(selection);

        var snapshot = selection.Snapshot;
        return $"{indent}branch_lane: {Scalar(snapshot.LaneId)}\n"
            + $"{indent}branch_lane_source: {Scalar(selection.Source)}\n"
            + $"{indent}routing_snapshot:\n"
            + $"{indent}  lane_id: {Scalar(snapshot.LaneId)}\n"
            + $"{indent}  definition_revision: {Scalar(snapshot.DefinitionRevision)}\n"
            + $"{indent}  start_branch: {Scalar(snapshot.StartBranch)}\n"
            + $"{indent}  pr_base_branch: {Scalar(snapshot.PrBaseBranch)}\n"
            + $"{indent}  landing_mode: {Scalar(snapshot.LandingMode)}\n";
    }

    public static string InjectIntoPacketYaml(
        string yaml,
        string declaredLane,
        string laneSource,
        BranchRoutingSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredLane);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneSource);
        ArgumentNullException.ThrowIfNull(snapshot);

        const string reviewSection = "review_context_packet:";
        var marker = yaml.IndexOf(reviewSection, StringComparison.Ordinal);
        if (marker < 0)
        {
            throw new InvalidOperationException(
                "Projection packet YAML did not contain the review_context_packet section needed to materialise the routing snapshot.");
        }

        var insertion = "  branch_lane: " + Scalar(declaredLane) + "\n"
            + "  branch_lane_source: " + Scalar(laneSource) + "\n"
            + "  routing_snapshot:\n"
            + "    lane_id: " + Scalar(snapshot.LaneId) + "\n"
            + "    definition_revision: " + Scalar(snapshot.DefinitionRevision) + "\n"
            + "    start_branch: " + Scalar(snapshot.StartBranch) + "\n"
            + "    pr_base_branch: " + Scalar(snapshot.PrBaseBranch) + "\n"
            + "    landing_mode: " + Scalar(snapshot.LandingMode) + "\n";

        return yaml.Insert(marker, insertion);
    }

    private static string Scalar(string value)
    {
        if (value.Length > 0
            && value.All(character => char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.' or '/'))
        {
            return value;
        }

        return "\""
            + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
            + "\"";
    }
}
