using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G697: the installed, read-only recipe for deliberately moving a recorded
/// team topology. It names the canonical move command and its verification
/// sequence so an operator never has to discover the workflow by inspecting
/// the topology writer.
/// </summary>
internal static class TopologyWorkspaceMoveGuidance
{
    public const string GuideSurface = "guide topology-workspace-move";
    public const string TargetSurface = "CAS-guarded topology workspace move and verification";

    public static TopologyWorkspaceMoveGuide Create(string? domain = null, string? team = null)
    {
        var domainValue = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain.Trim();
        var teamValue = string.IsNullOrWhiteSpace(team) ? "<team>" : team.Trim();
        var move = $"intent-cli session-layer topology move --domain {domainValue} --team {teamValue} "
            + "--workspace-id <new-workspace-id> --pane-map <old-pane>=<new-pane> [--pane-map ...]";

        return new TopologyWorkspaceMoveGuide
        {
            GuideSurface = GuideSurface,
            ReadOnly = true,
            DryRunFirst = true,
            Routes =
            [
                new GuideReachabilityRoute
                {
                    GuideSurface = GuideSurface,
                    Role = "operator",
                    TargetSurface = TargetSurface,
                },
                new GuideReachabilityRoute
                {
                    GuideSurface = GuideSurface,
                    Role = "orchestrator",
                    TargetSurface = TargetSurface,
                },
            ],
            Commands = new TopologyWorkspaceMoveCommands
            {
                Inspect = $"intent-cli session-layer topology show --domain {domainValue} --team {teamValue} --format json",
                Preview = $"{move} --dry-run --format json",
                Apply = $"{move} --write --format json",
                Validate = $"intent-cli session-layer topology validate --domain {domainValue} --team {teamValue} --format json",
                NotifyPreflight = "intent-cli notify delegate --domain <domain> --team <team> --from <sender-role> "
                    + "--to <recipient-role> --report-to <orchestrator-role> --task-id <task-id> "
                    + "--objective <bounded-outcome> --input <reference> --expected-artifact <artifact> "
                    + "--result-nonce <nonce> --dry-run --format json",
            },
            PaneMapContract = "Supply one old-pane=new-pane pair for every recorded herdr pane. Roles that share one recorded old pane travel together under that single mapping. Two different old panes may not map to the same new pane, and one old pane may not map to two new panes; either is refused as ambiguous. New pane ids must belong to the supplied new workspace; external roles have no pane mapping.",
            PreservationContract = "The move changes only the team workspace id and recorded herdr role workspace_id/pane_id values. Role membership, cwd, kind, delivery_method, reader, frontend, launch arguments, profiles, and all other JSON fields remain unchanged.",
            CasContract = "The write holds the topology CAS lock and compares the recorded digest before replacement. Pass --current-digest from a prior preview when an operator wants an explicit stale-snapshot refusal; a changed record is never silently overwritten.",
            AuthorityBoundary = "The move is an explicit operator-supplied transition. It never queries herdr, discovers a workspace, provisions panes, changes role membership, or repairs a per-role record refusal. A per-role workspace mismatch remains fail-closed and points here as the sanctioned whole-team operation.",
        };
    }
}

internal sealed record TopologyWorkspaceMoveGuide
{
    [JsonPropertyName("guide_surface")]
    public required string GuideSurface { get; init; }

    [JsonPropertyName("read_only")]
    public required bool ReadOnly { get; init; }

    [JsonPropertyName("dry_run_first")]
    public required bool DryRunFirst { get; init; }

    [JsonPropertyName("routes")]
    public required IReadOnlyList<GuideReachabilityRoute> Routes { get; init; }

    [JsonPropertyName("commands")]
    public required TopologyWorkspaceMoveCommands Commands { get; init; }

    [JsonPropertyName("pane_map_contract")]
    public required string PaneMapContract { get; init; }

    [JsonPropertyName("preservation_contract")]
    public required string PreservationContract { get; init; }

    [JsonPropertyName("cas_contract")]
    public required string CasContract { get; init; }

    [JsonPropertyName("authority_boundary")]
    public required string AuthorityBoundary { get; init; }
}

internal sealed record TopologyWorkspaceMoveCommands
{
    [JsonPropertyName("inspect")]
    public required string Inspect { get; init; }

    [JsonPropertyName("preview")]
    public required string Preview { get; init; }

    [JsonPropertyName("apply")]
    public required string Apply { get; init; }

    [JsonPropertyName("validate")]
    public required string Validate { get; init; }

    [JsonPropertyName("notify_preflight")]
    public required string NotifyPreflight { get; init; }
}
