using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G592-compatible topology-health projection. G594 removes its independent
/// predicate: every field is now projected from the shared session-layer
/// preflight consumed by doctor, guide READY, and notify.
/// </summary>
internal static class SessionLayerTopologyHealth
{
    public static SessionLayerTopologyHealthResult Analyze(string repoRoot) =>
        FromPreflight(SessionLayerPreflight.Analyze(repoRoot));

    public static SessionLayerTopologyHealthResult FromPreflight(SessionLayerPreflightResult preflight)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        var teams = preflight.Scopes.Select(scope => new SessionLayerTopologyTeamHealth
        {
            Team = scope.Team ?? "<undeclared>",
            Valid = scope.Ready,
            Findings = scope.Findings.Select(finding => new SessionLayerTopologyFinding(
                finding.Role,
                finding.Field,
                finding.Cause,
                finding.Message)).ToArray(),
        }).ToArray();
        var status = preflight.Verdict switch
        {
            SessionLayerPreflight.Ready => "valid",
            SessionLayerPreflight.Unjudged => "unjudged",
            _ => "invalid",
        };
        var invalidCount = teams.Sum(team => team.Findings.Count);
        return new SessionLayerTopologyHealthResult
        {
            Status = status,
            Required = !string.Equals(preflight.Verdict, SessionLayerPreflight.Unjudged, StringComparison.Ordinal),
            RecordPath = NotifyRoleTopologyStore.RelativePath,
            Teams = teams,
            Summary = string.Equals(status, "valid", StringComparison.Ordinal)
                ? $"Shared session-layer preflight is structurally ready for {teams.Length} named team(s)."
                : string.Equals(status, "unjudged", StringComparison.Ordinal)
                    ? "No named team is declared or discovered; topology health is unjudged until an expected team is declared."
                    : $"Shared session-layer preflight is invalid with {invalidCount} finding(s). "
                        + "Run `intent-cli automation doctor --domain <domain> --team <team> --format json` and "
                        + "`intent-cli session-layer topology validate --team <team> --format json`, then follow "
                        + "their canonical session-layer/topology remedies.",
        };
    }
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
