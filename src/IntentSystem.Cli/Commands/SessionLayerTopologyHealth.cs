using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G592: read-only topology health projection for automation doctor. A missing
/// topology is relevant only when herdr-only is recorded; an existing mapping
/// is always checked so stale hand-authored state cannot remain silent.
/// </summary>
internal static class SessionLayerTopologyHealth
{
    public static SessionLayerTopologyHealthResult Analyze(string repoRoot)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(repoRoot);
        if (!File.Exists(path))
        {
            SessionLayerModeState? modeState;
            try
            {
                modeState = SessionLayerModeStore.TryRead(repoRoot);
            }
            catch (InvalidOperationException exception)
            {
                return Invalid(
                    [CreateTeamHealth("<unknown>", "session-layer-mode", exception.Message)]);
            }

            var herdrTeams = modeState?.Entries
                .Where(entry => string.Equals(entry.Mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal))
                .Select(entry => entry.Team ?? $"<domain:{entry.Domain}>")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(team => team, StringComparer.Ordinal)
                .ToArray() ?? [];
            if (herdrTeams.Length == 0)
            {
                return new SessionLayerTopologyHealthResult
                {
                    Status = "not-required",
                    Required = false,
                    RecordPath = NotifyRoleTopologyStore.RelativePath,
                    Teams = [],
                    Summary = "No herdr-only session-layer scope or topology file is recorded; topology health is not required.",
                };
            }

            return Invalid(herdrTeams.Select(team => CreateTeamHealth(
                team,
                "file",
                $"Topology file '{path}' is absent for recorded herdr-only scope '{team}'.")).ToArray());
        }

        string[] teams;
        try
        {
            teams = DiscoverTeams(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Invalid([CreateTeamHealth("<unknown>", "file", $"Topology file '{path}' is unreadable: {exception.Message}")]);
        }

        if (teams.Length == 0)
        {
            return Invalid([CreateTeamHealth("<unknown>", "team", $"Topology file '{path}' contains no identifiable team.")]);
        }

        var teamHealth = teams
            .Select(team =>
            {
                var validation = NotifyRoleTopologyStore.Validate(repoRoot, team);
                return new SessionLayerTopologyTeamHealth
                {
                    Team = team,
                    Valid = validation.Valid,
                    Findings = validation.Findings,
                };
            })
            .ToArray();
        var valid = teamHealth.All(team => team.Valid);
        return new SessionLayerTopologyHealthResult
        {
            Status = valid ? "valid" : "invalid",
            Required = true,
            RecordPath = NotifyRoleTopologyStore.RelativePath,
            Teams = teamHealth,
            Summary = valid
                ? $"Recorded delivery topology is valid for {teamHealth.Length} team(s)."
                : $"Recorded delivery topology is invalid with {teamHealth.Sum(team => team.Findings.Count)} "
                    + "finding(s). Run `intent-cli session-layer topology validate --team <team> --format json`.",
        };
    }

    private static string[] DiscoverTeams(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (root.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Object)
        {
            return teams.EnumerateObject()
                .Where(team => team.Value.ValueKind == JsonValueKind.Object)
                .Select(team => team.Name)
                .OrderBy(team => team, StringComparer.Ordinal)
                .ToArray();
        }

        if (root.TryGetProperty("team", out var team)
            && team.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(team.GetString()))
        {
            return [team.GetString()!];
        }

        return root.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Object
                && property.Name is not ("workspace" or "roles"))
            .Select(property => property.Name)
            .OrderBy(teamName => teamName, StringComparer.Ordinal)
            .ToArray();
    }

    private static SessionLayerTopologyTeamHealth CreateTeamHealth(
        string team,
        string field,
        string message) => new()
    {
        Team = team,
        Valid = false,
        Findings =
        [
            new SessionLayerTopologyFinding("<topology>", field, "topology-invalid", message),
        ],
    };

    private static SessionLayerTopologyHealthResult Invalid(IReadOnlyList<SessionLayerTopologyTeamHealth> teams) => new()
    {
        Status = "invalid",
        Required = true,
        RecordPath = NotifyRoleTopologyStore.RelativePath,
        Teams = teams,
        Summary = $"Recorded delivery topology is invalid with {teams.Sum(team => team.Findings.Count)} finding(s). "
            + "Run `intent-cli session-layer topology validate --team <team> --format json`.",
    };
}

internal sealed record SessionLayerTopologyHealthResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    [JsonPropertyName("record_path")]
    public required string RecordPath { get; init; }

    [JsonPropertyName("teams")]
    public required IReadOnlyList<SessionLayerTopologyTeamHealth> Teams { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyTeamHealth
{
    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    [JsonPropertyName("findings")]
    public required IReadOnlyList<SessionLayerTopologyFinding> Findings { get; init; }
}
