namespace IntentSystem.Cli.Commands;

internal sealed record OrcaRunTeamShapeResult
{
    public string? TeamShape { get; init; }
    public string? RequiredSeat { get; init; }
    public string? BindingLocation { get; init; }
    public string? TeamMode { get; init; }
    public string? Cause { get; init; }
    public string? Fix { get; init; }
    public string? Message { get; init; }
    public bool Resolved => Cause is null && TeamShape is not null;
}

internal static class OrcaRunTeamShape
{
    private static readonly HashSet<string> FiveSeatRoles = new(StringComparer.Ordinal)
    {
        LogicalRoleNormalizer.Architect,
        LogicalRoleNormalizer.Orchestrator,
        LogicalRoleNormalizer.Builder,
        LogicalRoleNormalizer.Reviewer,
        LogicalRoleNormalizer.Steward,
    };

    private static readonly HashSet<string> FourSeatRoles = new(StringComparer.Ordinal)
    {
        LogicalRoleNormalizer.Architect,
        LogicalRoleNormalizer.Orchestrator,
        LogicalRoleNormalizer.Builder,
        LogicalRoleNormalizer.Reviewer,
    };

    public static OrcaRunTeamShapeResult Resolve(
        string routingRoot,
        string domain,
        string team,
        TeamModeState? teamModeState = null)
    {
        TeamModeState? state;
        try
        {
            state = teamModeState ?? TeamModeStore.TryRead(routingRoot);
        }
        catch (InvalidOperationException exception)
        {
            return new OrcaRunTeamShapeResult
            {
                Cause = "team-shape-unreadable",
                Message = exception.Message,
            };
        }

        TeamModeResolution resolution;
        try
        {
            resolution = TeamModeStore.Resolve(state, domain, team);
        }
        catch (InvalidOperationException exception)
        {
            return new OrcaRunTeamShapeResult
            {
                Cause = "team-shape-unreadable",
                Message = exception.Message,
            };
        }

        if (resolution.Source == TeamModeSource.Default)
        {
            return new OrcaRunTeamShapeResult
            {
                Cause = "team-shape-unrecorded",
                Fix = $"intent-cli team-mode set --domain {domain} --team {team} --mode delivery|solo-conductor --write",
                Message = $"No team mode is recorded for team '{team}' in domain '{domain}'.",
            };
        }

        if (TeamMode.IsAuthoringOnly(resolution.Mode))
        {
            return new OrcaRunTeamShapeResult
            {
                TeamMode = resolution.Mode,
                Cause = "team-shape-not-bindable",
                Message = $"Team mode '{TeamMode.AuthoringOnly}' does not bind Orca Run mailboxes.",
            };
        }

        if (TeamMode.IsSoloConductor(resolution.Mode))
        {
            if (resolution.Entry?.Team is null)
            {
                return new OrcaRunTeamShapeResult
                {
                    TeamMode = resolution.Mode,
                    Cause = "team-shape-solo-entry-not-team-scoped",
                    Message = "solo-conductor requires a team-scoped team-mode entry.",
                };
            }

            return new OrcaRunTeamShapeResult
            {
                TeamShape = "solo-conductor",
                RequiredSeat = LogicalRoleNormalizer.Architect,
                BindingLocation = OrcaRunBinding.OrcaRunFileLocation,
                TeamMode = resolution.Mode,
            };
        }

        var topologyResolution = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!topologyResolution.Resolved)
        {
            return new OrcaRunTeamShapeResult
            {
                TeamMode = resolution.Mode,
                Cause = "topology-unresolved",
                Message = topologyResolution.Summary,
            };
        }

        var canonicalRoles = new HashSet<string>(StringComparer.Ordinal);
        var unrecognizedKeys = new List<string>();
        foreach (var roleKey in topologyResolution.Topology!.Roles.Keys)
        {
            if (!LogicalRoleNormalizer.TryNormalize(roleKey, out var canonical, out _))
            {
                unrecognizedKeys.Add(roleKey);
                continue;
            }

            canonicalRoles.Add(canonical!);
        }

        if (unrecognizedKeys.Count > 0)
        {
            return new OrcaRunTeamShapeResult
            {
                TeamMode = resolution.Mode,
                Cause = "team-shape-unrecognized",
                Message = $"Unrecognized role key(s): {string.Join(", ", unrecognizedKeys.OrderBy(k => k, StringComparer.Ordinal))}.",
            };
        }

        if (FiveSeatRoles.SetEquals(canonicalRoles))
        {
            return new OrcaRunTeamShapeResult
            {
                TeamShape = "five-seat",
                RequiredSeat = LogicalRoleNormalizer.Steward,
                BindingLocation = OrcaRunBinding.TopologyRoleLocation,
                TeamMode = resolution.Mode,
            };
        }

        if (FourSeatRoles.SetEquals(canonicalRoles))
        {
            return new OrcaRunTeamShapeResult
            {
                TeamShape = "four-seat",
                RequiredSeat = LogicalRoleNormalizer.Architect,
                BindingLocation = OrcaRunBinding.TopologyRoleLocation,
                TeamMode = resolution.Mode,
            };
        }

        var missing = FiveSeatRoles.Except(canonicalRoles).OrderBy(r => r, StringComparer.Ordinal).ToArray();
        return new OrcaRunTeamShapeResult
        {
            TeamMode = resolution.Mode,
            Cause = "team-shape-unrecognized",
            Message = missing.Length == 0
                ? "The recorded roster does not match a four- or five-seat shape."
                : $"Missing canonical role(s): {string.Join(", ", missing)}.",
        };
    }
}
