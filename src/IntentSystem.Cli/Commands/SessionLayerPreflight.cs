using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G594: the single record-first session-layer readiness predicate consumed by
/// automation doctor, orchestrator READY guidance, and notify. The passive
/// phase never contacts a receiver and never infers a mode from live transport
/// state. Active receiver evidence is attached by the transport consumer.
/// </summary>
internal static class SessionLayerPreflight
{
    public const string Ready = "ready";
    public const string ConfigurationIncomplete = "configuration-incomplete";
    public const string CannotDetermine = "cannot-determine";
    public const string Unjudged = "unjudged";

    public const string ActiveSkipped = "skipped";
    public const string ActiveAcknowledged = "acknowledged";
    public const string ActiveObserved = "observed";
    public const string ActiveUnobservable = "unobservable";
    public const string ActiveNotObserved = "not-observed";
    public const string ActiveNotApplicable = "not-applicable";

    // The role-pane mapping is the delivery topology for the herdr-only
    // transport. Its schema deliberately remains unchanged by G594.
    public const string TopologyMode = SessionLayerMode.HerdrOnly;

    public static SessionLayerPreflightResult Analyze(
        string repoRoot,
        string? expectedDomain = null,
        string? expectedTeam = null,
        string? requiredRecipient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        SessionLayerModeState? modeState;
        string? modeReadError = null;
        try
        {
            modeState = SessionLayerModeStore.TryRead(repoRoot);
        }
        catch (InvalidOperationException exception)
        {
            modeState = null;
            modeReadError = exception.Message;
        }

        var topology = DiscoverTopology(repoRoot);
        var teamDeclared = !string.IsNullOrWhiteSpace(expectedTeam);
        var requestedResolution = expectedDomain is null
            ? new SessionLayerModeResolution
            {
                Mode = SessionLayerMode.Default,
                Source = SessionLayerModeSource.Default,
            }
            : SessionLayerModeStore.Resolve(modeState, expectedDomain, expectedTeam);
        var teams = teamDeclared
            ? [expectedTeam!]
            : modeState?.Entries
                .Where(entry => entry.Team is not null)
                .Select(entry => entry.Team!)
                .Concat(topology.Teams)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(team => team, StringComparer.Ordinal)
                .ToArray() ?? topology.Teams;

        if (teams.Length == 0)
        {
            if (modeReadError is not null || topology.Error is not null)
            {
                var finding = new SessionLayerPreflightFinding
                {
                    Team = null,
                    Role = "<preflight>",
                    Field = modeReadError is not null ? "session-layer-mode" : "topology",
                    Cause = modeReadError is not null ? "session-layer-mode-unreadable" : "topology-unreadable",
                    Message = modeReadError ?? topology.Error!,
                    RecordedMode = null,
                    TopologyMode = topology.FileExists ? TopologyMode : null,
                };
                return Aggregate(
                    expectedTeamDeclared: false,
                    [CannotDetermineScope(expectedDomain, null, finding)],
                    "Session-layer readiness cannot be determined from the present unreadable record. "
                    + "A cannot-determine result is never green.");
            }

            return AnonymousUnjudged(requestedResolution);
        }

        var scopes = teams.Select(team => AnalyzeScope(
            repoRoot,
            expectedDomain,
            team,
            modeState,
            modeReadError,
            topology,
            requiredRecipient)).ToArray();
        return Aggregate(
            teamDeclared,
            scopes,
            scopes.All(scope => scope.Ready)
                ? $"Session-layer passive preflight passed for {scopes.Length} named team scope(s)."
                : "Session-layer passive preflight did not pass. Follow every recorded finding before declaring READY or notifying.")
            with
        { Resolution = requestedResolution };
    }

    public static SessionLayerPreflightResult AnonymousUnjudged(
        SessionLayerModeResolution? resolution = null) => new()
        {
            Verdict = Unjudged,
            Ready = null,
            ExpectedTeamDeclared = false,
            Scopes = [],
            PassivePhase = new SessionLayerPreflightPhaseResult
            {
                Status = Unjudged,
                Checked = false,
                ContactedReceiver = false,
                Summary = "No named team was declared or discovered. Declare --domain and --team before "
                + "judging session-layer readiness; an anonymous empty root is not green or red.",
            },
            ActivePhase = SkippedActivePhase(),
            Summary = "Session-layer readiness is unjudged until an expected team is declared.",
            Resolution = resolution ?? new SessionLayerModeResolution
            {
                Mode = SessionLayerMode.Default,
                Source = SessionLayerModeSource.Default,
            },
        };

    public static SessionLayerPreflightResult WithActivePhase(
        SessionLayerPreflightResult result,
        string status,
        bool contactedReceiver,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result with
        {
            ActivePhase = new SessionLayerPreflightPhaseResult
            {
                Status = status,
                Checked = status is not (ActiveSkipped or ActiveNotApplicable),
                ContactedReceiver = contactedReceiver,
                Summary = summary,
            },
        };
    }

    private static SessionLayerPreflightScopeResult AnalyzeScope(
        string repoRoot,
        string? expectedDomain,
        string team,
        SessionLayerModeState? modeState,
        string? modeReadError,
        TopologyDiscovery topology,
        string? requiredRecipient)
    {
        if (modeReadError is not null)
        {
            return CannotDetermineScope(expectedDomain, team, new SessionLayerPreflightFinding
            {
                Team = team,
                Role = "<preflight>",
                Field = "session-layer-mode",
                Cause = "session-layer-mode-unreadable",
                Message = modeReadError,
                RecordedMode = null,
                TopologyMode = topology.Teams.Contains(team, StringComparer.Ordinal) ? TopologyMode : null,
            });
        }

        var recordedDomains = modeState?.Entries
            .Where(entry => string.Equals(entry.Team, team, StringComparison.Ordinal))
            .Select(entry => entry.Domain)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .ToArray() ?? [];
        var domain = !string.IsNullOrWhiteSpace(expectedDomain)
            ? expectedDomain
            : recordedDomains.Length == 1
                ? recordedDomains[0]
                : null;

        if (domain is null && recordedDomains.Length > 1)
        {
            return CannotDetermineScope(null, team, new SessionLayerPreflightFinding
            {
                Team = team,
                Role = "<preflight>",
                Field = "domain",
                Cause = "expected-domain-required",
                Message = $"Named team '{team}' has session-layer records in more than one domain "
                    + $"({string.Join(", ", recordedDomains)}). Declare --domain so readiness is not guessed.",
                RecordedMode = null,
                TopologyMode = topology.Teams.Contains(team, StringComparer.Ordinal) ? TopologyMode : null,
            });
        }

        var resolution = domain is null
            ? new SessionLayerModeResolution
            {
                Mode = SessionLayerMode.Default,
                Source = SessionLayerModeSource.Default,
            }
            : SessionLayerModeStore.Resolve(modeState, domain, team);
        var topologyRecordedForTeam = topology.Teams.Contains(team, StringComparer.Ordinal);
        var findings = new List<SessionLayerPreflightFinding>();

        if (topology.Error is not null && resolution.Source != SessionLayerModeSource.Recorded)
        {
            findings.Add(MissingModeFinding(domain, team, topologyMode: TopologyMode));
            findings.Add(new SessionLayerPreflightFinding
            {
                Team = team,
                Role = "<topology>",
                Field = "file",
                Cause = "topology-unreadable",
                Message = topology.Error,
                RecordedMode = null,
                TopologyMode = TopologyMode,
            });
            return Scope(
                domain,
                team,
                ConfigurationIncomplete,
                resolution,
                TopologyMode,
                findings,
                $"Named team '{team}' is configuration-incomplete because its session-layer mode is unrecorded; "
                + "the contradictory topology record is also unreadable diagnostic evidence.");
        }

        if (topology.Error is not null)
        {
            findings.Add(new SessionLayerPreflightFinding
            {
                Team = team,
                Role = "<topology>",
                Field = "file",
                Cause = "topology-unreadable",
                Message = topology.Error,
                RecordedMode = resolution.Source == SessionLayerModeSource.Recorded ? resolution.Mode : null,
                TopologyMode = topology.FileExists ? TopologyMode : null,
            });
            return Scope(
                domain,
                team,
                CannotDetermine,
                resolution,
                topology.FileExists ? TopologyMode : null,
                findings,
                $"Cannot determine session-layer readiness for team '{team}' because the topology record is unreadable.");
        }

        SessionLayerTopologyValidation? validation = null;
        if (topologyRecordedForTeam || resolution.IsHerdrOnly)
        {
            validation = NotifyRoleTopologyStore.Validate(repoRoot, team);
            if (requiredRecipient is not null)
            {
                var routeRelevantFindings = validation.Findings
                    .Where(finding => !IsRoleDeliverabilityFinding(finding)
                        || string.Equals(finding.Role, requiredRecipient, StringComparison.Ordinal))
                    .ToArray();
                validation = validation with
                {
                    Valid = routeRelevantFindings.Length == 0,
                    Findings = routeRelevantFindings,
                };
            }
        }

        if (resolution.Source != SessionLayerModeSource.Recorded)
        {
            findings.Add(MissingModeFinding(
                domain,
                team,
                topologyRecordedForTeam ? TopologyMode : null));
            AddTopologyFindings(findings, validation, recordedMode: null);
            return Scope(
                domain,
                team,
                ConfigurationIncomplete,
                resolution,
                topologyRecordedForTeam ? TopologyMode : null,
                findings,
                $"Named team '{team}' is configuration-incomplete because its session-layer mode is unrecorded.");
        }

        if (string.Equals(resolution.Mode, SessionLayerMode.Agmsg, StringComparison.Ordinal)
            && topologyRecordedForTeam)
        {
            findings.Add(new SessionLayerPreflightFinding
            {
                Team = team,
                Role = "<topology>",
                Field = "mode",
                Cause = "topology-mode-mismatch",
                Message = $"Team '{team}' has recorded mode '{resolution.Mode}', but its recorded delivery "
                    + $"topology describes mode '{TopologyMode}'. The contradiction is diagnostic only: the record "
                    + "is not inferred, repaired, or changed. Remove the stale topology through an operator-approved "
                    + "repair or record the intended mode with the canonical command.",
                RecordedMode = resolution.Mode,
                TopologyMode = TopologyMode,
            });
            AddTopologyFindings(findings, validation, resolution.Mode);
            return Scope(
                domain,
                team,
                ConfigurationIncomplete,
                resolution,
                TopologyMode,
                findings,
                $"Session-layer topology mismatch for team '{team}': recorded mode '{resolution.Mode}', "
                + $"topology mode '{TopologyMode}'.");
        }

        if (resolution.IsHerdrOnly && (validation is null || !validation.Valid))
        {
            AddTopologyFindings(findings, validation, resolution.Mode);
            return Scope(
                domain,
                team,
                ConfigurationIncomplete,
                resolution,
                TopologyMode,
                findings,
                $"Team '{team}' records mode '{resolution.Mode}', but its required topology is incomplete. "
                + NotifyRoleTopologyStore.TopologyRemedy(team));
        }

        return Scope(
            domain,
            team,
            Ready,
            resolution,
            resolution.IsHerdrOnly ? TopologyMode : null,
            [],
            $"Passive session-layer structure is ready for team '{team}' in recorded mode '{resolution.Mode}'. "
            + "Only that recorded transport may be probed; active receiver readiness is reported separately.");
    }

    private static SessionLayerPreflightFinding MissingModeFinding(
        string? domain,
        string team,
        string? topologyMode)
    {
        var domainArgument = domain ?? "<domain>";
        return new SessionLayerPreflightFinding
        {
            Team = team,
            Role = "<preflight>",
            Field = "session-layer-mode",
            Cause = "session-layer-mode-unrecorded",
            Message = $"No session layer is recorded for named team '{team}'"
                + (domain is null ? "." : $" in domain '{domain}'.")
                + $" The resolved default '{SessionLayerMode.Default}' is not readiness evidence. Run "
                + $"`intent-cli session-layer set --domain {domainArgument} --team {team} "
                + "--mode agmsg|herdr-only --write`, then re-run the preflight.",
            RecordedMode = null,
            TopologyMode = topologyMode,
        };
    }

    private static bool IsRoleDeliverabilityFinding(SessionLayerTopologyFinding finding) =>
        finding.Cause is "reader-unavailable" or "pane-absent";

    private static void AddTopologyFindings(
        List<SessionLayerPreflightFinding> findings,
        SessionLayerTopologyValidation? validation,
        string? recordedMode)
    {
        if (validation is null)
        {
            return;
        }

        findings.AddRange(validation.Findings.Select(finding => new SessionLayerPreflightFinding
        {
            Team = validation.Team,
            Role = finding.Role,
            Field = finding.Field,
            Cause = finding.Cause,
            Message = finding.Message,
            RecordedMode = recordedMode,
            TopologyMode = TopologyMode,
        }));
    }

    private static SessionLayerPreflightScopeResult Scope(
        string? domain,
        string team,
        string verdict,
        SessionLayerModeResolution resolution,
        string? topologyMode,
        IReadOnlyList<SessionLayerPreflightFinding> findings,
        string summary) => new()
        {
            Domain = domain,
            Team = team,
            Verdict = verdict,
            Ready = string.Equals(verdict, Ready, StringComparison.Ordinal),
            Mode = resolution.Mode,
            ModeSource = resolution.Source == SessionLayerModeSource.Recorded ? "recorded" : "default",
            TopologyMode = topologyMode,
            Findings = findings,
            Summary = summary,
            Resolution = resolution,
        };

    private static SessionLayerPreflightScopeResult CannotDetermineScope(
        string? domain,
        string? team,
        SessionLayerPreflightFinding finding) => new()
        {
            Domain = domain,
            Team = team,
            Verdict = CannotDetermine,
            Ready = false,
            Mode = null,
            ModeSource = "unreadable",
            TopologyMode = finding.TopologyMode,
            Findings = [finding],
            Summary = finding.Message,
        };

    private static SessionLayerPreflightResult Aggregate(
        bool expectedTeamDeclared,
        IReadOnlyList<SessionLayerPreflightScopeResult> scopes,
        string summary)
    {
        var verdict = scopes.Any(scope => string.Equals(scope.Verdict, CannotDetermine, StringComparison.Ordinal))
            ? CannotDetermine
            : scopes.Any(scope => string.Equals(scope.Verdict, ConfigurationIncomplete, StringComparison.Ordinal))
                ? ConfigurationIncomplete
                : Ready;
        return new SessionLayerPreflightResult
        {
            Verdict = verdict,
            Ready = string.Equals(verdict, Ready, StringComparison.Ordinal),
            ExpectedTeamDeclared = expectedTeamDeclared,
            Scopes = scopes,
            PassivePhase = new SessionLayerPreflightPhaseResult
            {
                Status = verdict,
                Checked = true,
                ContactedReceiver = false,
                Summary = summary,
            },
            ActivePhase = SkippedActivePhase(),
            Summary = summary,
        };
    }

    private static SessionLayerPreflightPhaseResult SkippedActivePhase() => new()
    {
        Status = ActiveSkipped,
        Checked = false,
        ContactedReceiver = false,
        Summary = "Active receiver preflight was skipped. This does not invalidate the passive structural verdict; "
            + "a delivery surface must report its own bounded receiver outcome before claiming delivery.",
    };

    private static TopologyDiscovery DiscoverTopology(string repoRoot)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(repoRoot);
        if (!File.Exists(path))
        {
            return new TopologyDiscovery(false, [], null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new TopologyDiscovery(true, [], $"Topology file '{path}' is not a JSON object.");
            }

            string[] teams;
            if (root.TryGetProperty("teams", out var teamMap) && teamMap.ValueKind == JsonValueKind.Object)
            {
                teams = teamMap.EnumerateObject()
                    .Where(team => team.Value.ValueKind == JsonValueKind.Object)
                    .Select(team => team.Name)
                    .OrderBy(team => team, StringComparer.Ordinal)
                    .ToArray();
            }
            else if (root.TryGetProperty("team", out var team)
                && team.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(team.GetString()))
            {
                teams = [team.GetString()!];
            }
            else
            {
                teams = root.EnumerateObject()
                    .Where(property => property.Value.ValueKind == JsonValueKind.Object
                        && property.Name is not ("workspace" or "roles"))
                    .Select(property => property.Name)
                    .OrderBy(teamName => teamName, StringComparer.Ordinal)
                    .ToArray();
            }

            return new TopologyDiscovery(true, teams, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new TopologyDiscovery(true, [], $"Topology file '{path}' is unreadable: {exception.Message}");
        }
    }

    private sealed record TopologyDiscovery(bool FileExists, string[] Teams, string? Error);
}

internal sealed record SessionLayerPreflightResult
{
    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    [JsonPropertyName("ready")]
    public required bool? Ready { get; init; }

    [JsonPropertyName("expected_team_declared")]
    public required bool ExpectedTeamDeclared { get; init; }

    [JsonPropertyName("scopes")]
    public required IReadOnlyList<SessionLayerPreflightScopeResult> Scopes { get; init; }

    [JsonPropertyName("passive_phase")]
    public required SessionLayerPreflightPhaseResult PassivePhase { get; init; }

    [JsonPropertyName("active_phase")]
    public required SessionLayerPreflightPhaseResult ActivePhase { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    internal SessionLayerModeResolution? Resolution { get; init; }
}

internal sealed record SessionLayerPreflightScopeResult
{
    [JsonPropertyName("domain")]
    public required string? Domain { get; init; }

    [JsonPropertyName("team")]
    public required string? Team { get; init; }

    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }

    [JsonPropertyName("mode")]
    public required string? Mode { get; init; }

    [JsonPropertyName("mode_source")]
    public required string ModeSource { get; init; }

    [JsonPropertyName("topology_mode")]
    public required string? TopologyMode { get; init; }

    [JsonPropertyName("findings")]
    public required IReadOnlyList<SessionLayerPreflightFinding> Findings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    internal SessionLayerModeResolution? Resolution { get; init; }
}

internal sealed record SessionLayerPreflightFinding
{
    [JsonPropertyName("team")]
    public required string? Team { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("cause")]
    public required string Cause { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("recorded_mode")]
    public required string? RecordedMode { get; init; }

    [JsonPropertyName("topology_mode")]
    public required string? TopologyMode { get; init; }
}

internal sealed record SessionLayerPreflightPhaseResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("checked")]
    public required bool Checked { get; init; }

    [JsonPropertyName("contacted_receiver")]
    public required bool ContactedReceiver { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
