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
    public const string ActiveInProgress = "in-progress";
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

        var topology = DiscoverTopology(repoRoot, expectedDomain);
        var markers = DiscoverMarkers(repoRoot);
        var teamDeclared = !string.IsNullOrWhiteSpace(expectedTeam);
        var requestedResolution = expectedDomain is null
            ? new SessionLayerModeResolution
            {
                Mode = SessionLayerMode.Default,
                Source = SessionLayerModeSource.Default,
            }
            : SessionLayerModeStore.Resolve(modeState, expectedDomain, expectedTeam);
        // G696: a domain-scoped preflight must select teams from that domain
        // before analyzing any scope. The old union used every team recorded
        // in the repository and every topology file, so a request for one
        // domain emitted findings for unrelated domains (and could even make
        // the unrelated topology affect the aggregate verdict). Topology
        // scopes retain their domain identity while the legacy path remains
        // unscoped and therefore is only considered by an unscoped request.
        var topologyTeams = expectedDomain is null
            ? topology.Teams
            : topology.Scopes
                .Where(scope => string.Equals(scope.Domain, expectedDomain, StringComparison.Ordinal))
                .Select(scope => scope.Team)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(team => team, StringComparer.Ordinal)
                .ToArray();
        var recordedTeams = modeState?.Entries
            .Where(entry => entry.Team is not null
                && (expectedDomain is null
                    || string.Equals(entry.Domain, expectedDomain, StringComparison.Ordinal)))
            .Select(entry => entry.Team!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(team => team, StringComparer.Ordinal)
            .ToArray() ?? [];
        var teams = teamDeclared
            ? [expectedTeam!]
            : recordedTeams
                .Concat(topologyTeams)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(team => team, StringComparer.Ordinal)
                .ToArray();

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
            markers,
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
        MarkerDiscovery markers,
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
                TopologyMode = topology.HasTeam(expectedDomain, team) ? TopologyMode : null,
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
                TopologyMode = topology.HasTeam(expectedDomain, team) ? TopologyMode : null,
            });
        }

        var resolution = domain is null
            ? new SessionLayerModeResolution
            {
                Mode = SessionLayerMode.Default,
                Source = SessionLayerModeSource.Default,
            }
            : SessionLayerModeStore.Resolve(modeState, domain, team);
        var topologyRecordedForTeam = topology.HasTeam(expectedDomain, team);
        var findings = new List<SessionLayerPreflightFinding>();

        // G601 markers supplement—not replace—the canonical record. Include
        // their evidence even when another structural check also fails.
        if (resolution.Source == SessionLayerModeSource.Recorded)
        {
            AddMarkerFindings(findings, markers, domain!, team, resolution);
            AddResidueFindings(findings, repoRoot, team, resolution);
        }

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
            validation = NotifyRoleTopologyStore.Validate(repoRoot, domain, team);
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

        var markerInvalid = findings.Any(finding => finding.Cause is "marker-drift" or "marker-malformed" or "marker-unreadable");
        return Scope(
            domain,
            team,
            markerInvalid ? ConfigurationIncomplete : Ready,
            resolution,
            resolution.IsHerdrOnly ? TopologyMode : null,
            findings,
            markerInvalid
                ? $"Session-layer marker verification did not pass for team '{team}'."
                : $"Passive session-layer structure is ready for team '{team}' in recorded mode '{resolution.Mode}'. "
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

    private static void AddMarkerFindings(
        List<SessionLayerPreflightFinding> findings,
        MarkerDiscovery markers,
        string domain,
        string team,
        SessionLayerModeResolution resolution)
    {
        foreach (var error in markers.Errors)
        {
            findings.Add(new SessionLayerPreflightFinding
            {
                Team = team,
                Role = error.File,
                Field = "marker",
                Cause = error.Cause,
                Message = error.Message,
                RecordedMode = resolution.Mode,
                TopologyMode = null,
            });
        }

        var matching = markers.Blocks
            .Where(marker => string.Equals(marker.Domain, domain, StringComparison.Ordinal)
                && string.Equals(marker.Team, team, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length == 0)
        {
            AddMarkerNotGeneratedFinding(findings, "<marker>", domain, team, resolution.Mode,
                $"No managed marker has been generated for team '{team}' in domain '{domain}'.");
            return;
        }

        var recordHash = SessionLayerMarkerStore.Hash(resolution.Entry!);
        foreach (var marker in matching)
        {
            if (marker.IsEmpty)
            {
                // The documented start/end-only block is deliberately the
                // generator's valid target. It has no generated claim yet,
                // but is not malformed and must not prevent delivery.
                AddMarkerNotGeneratedFinding(findings, marker.File, domain, team, resolution.Mode,
                    $"Managed marker in '{marker.File}' for team '{team}' is an empty generation placeholder.");
                continue;
            }

            if (!marker.IsGenerated)
            {
                findings.Add(new SessionLayerPreflightFinding
                {
                    Team = team,
                    Role = marker.File,
                    Field = "marker",
                    Cause = "marker-malformed",
                    Message = $"Managed marker in '{marker.File}' for team '{team}' is malformed and cannot be trusted.",
                    RecordedMode = resolution.Mode,
                    TopologyMode = null,
                });
                continue;
            }

            if (!string.Equals(marker.Mode, resolution.Mode, StringComparison.Ordinal)
                || !string.Equals(marker.RecordHash, recordHash, StringComparison.Ordinal))
            {
                findings.Add(new SessionLayerPreflightFinding
                {
                    Team = team,
                    Role = marker.File,
                    Field = "marker",
                    Cause = "marker-drift",
                    Message = $"Managed marker in '{marker.File}' claims mode '{marker.Mode}' and record hash '{marker.RecordHash}', but canonical record truth is mode '{resolution.Mode}' and record hash '{recordHash}'. Regenerate the marker before declaring READY.",
                    RecordedMode = resolution.Mode,
                    TopologyMode = null,
                });
            }
        }
    }

    private static void AddMarkerNotGeneratedFinding(
        List<SessionLayerPreflightFinding> findings,
        string role,
        string domain,
        string team,
        string recordedMode,
        string detail)
    {
        findings.Add(new SessionLayerPreflightFinding
        {
            Team = team,
            Role = role,
            Field = "marker",
            Cause = "marker-not-generated",
            Message = $"{detail} This is informational; generate it with `intent-cli session-layer marker generate --domain {domain} --team {team} --file <AGENTS.md|CLAUDE.md> --write`.",
            RecordedMode = recordedMode,
            TopologyMode = null,
        });
    }

    private static void AddResidueFindings(
        List<SessionLayerPreflightFinding> findings,
        string repoRoot,
        string team,
        SessionLayerModeResolution resolution)
    {
        foreach (var residue in SessionLayerMigration.Discover(repoRoot, resolution.Mode))
        {
            findings.Add(new SessionLayerPreflightFinding
            {
                Team = team,
                Role = residue.Path,
                Field = "residue",
                Cause = SessionLayerMigration.ResidueCause,
                Message = $"Known '{residue.OwningMode}' {residue.Artifact} residue is present at '{residue.Path}', "
                    + $"while the canonical record names '{resolution.Mode}'. A team runs exactly ONE session-layer "
                    + "mode at a time; review and remove or disable this user-managed residue. This advisory finding "
                    + "never infers, changes, or overrides the recorded mode.",
                RecordedMode = resolution.Mode,
                TopologyMode = null,
            });
        }
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

    private static TopologyDiscovery DiscoverTopology(string repoRoot, string? expectedDomain)
    {
        var legacyPath = NotifyRoleTopologyStore.ResolvePath(repoRoot);
        var topologyRoot = Path.Combine(
            repoRoot,
            NotifyRoleTopologyStore.TopologyDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var discoveredPaths = Directory.Exists(topologyRoot)
            ? Directory.EnumerateFiles(topologyRoot, "*.json", SearchOption.AllDirectories).ToArray()
            : [];
        if (File.Exists(legacyPath))
        {
            discoveredPaths = [.. discoveredPaths, legacyPath];
        }

        // G696: an invalid or stale topology in another domain must not
        // poison a domain-scoped observation. The legacy path has no encoded
        // domain and remains visible for compatibility; new paths are
        // filtered by their domain directory before parsing.
        var paths = expectedDomain is null
            ? discoveredPaths
            : discoveredPaths
                .Where(path => DomainFromTopologyPath(path, topologyRoot) is null
                    || string.Equals(DomainFromTopologyPath(path, topologyRoot), expectedDomain, StringComparison.Ordinal))
                .ToArray();

        if (paths.Length == 0)
        {
            return new TopologyDiscovery(false, [], null);
        }

        var scopes = new List<TopologyTeamScope>();
        try
        {
            foreach (var path in paths)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new TopologyDiscovery(true, [], $"Topology file '{path}' is not a JSON object.");
                }

                var pathDomain = DomainFromTopologyPath(path, topologyRoot);
                var recordedDomain = root.TryGetProperty("domain", out var domain)
                    && domain.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(domain.GetString())
                    ? domain.GetString()
                    : null;
                // The path is the canonical scope for new topology files. If
                // a legacy/flat fixture carries an envelope domain, retain it
                // so filtering can still be domain-aware.
                var scopeDomain = pathDomain ?? recordedDomain;

                void AddScope(string teamName)
                {
                    if (!string.IsNullOrWhiteSpace(teamName))
                    {
                        scopes.Add(new TopologyTeamScope(scopeDomain, teamName));
                    }
                }

                if (root.TryGetProperty("teams", out var teamMap) && teamMap.ValueKind == JsonValueKind.Object)
                {
                    foreach (var team in teamMap.EnumerateObject()
                        .Where(team => team.Value.ValueKind == JsonValueKind.Object)
                        .Select(team => team.Name))
                    {
                        AddScope(team);
                    }
                }
                else if (root.TryGetProperty("team", out var team)
                    && team.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(team.GetString()))
                {
                    AddScope(team.GetString()!);
                }
                else
                {
                    foreach (var property in root.EnumerateObject()
                        .Where(property => property.Value.ValueKind == JsonValueKind.Object
                            && property.Name is not ("domain" or "team" or "workspace" or "roles" or "teams"))
                        .Select(property => property.Name))
                    {
                        AddScope(property);
                    }
                }
            }

            var uniqueScopes = scopes
                .Distinct()
                .OrderBy(scope => scope.Domain, StringComparer.Ordinal)
                .ThenBy(scope => scope.Team, StringComparer.Ordinal)
                .ToArray();
            return new TopologyDiscovery(true, uniqueScopes, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new TopologyDiscovery(true, [], $"Topology discovery is unreadable: {exception.Message}");
        }
    }

    private static string? DomainFromTopologyPath(string path, string topologyRoot)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(topologyRoot));
        var fullPath = Path.GetFullPath(path);
        var rootPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var relative = Path.GetRelativePath(fullRoot, fullPath);
        var directory = Path.GetDirectoryName(relative);
        if (string.IsNullOrWhiteSpace(directory) || directory == ".")
        {
            return null;
        }

        return directory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
    }

    private static MarkerDiscovery DiscoverMarkers(string repoRoot)
    {
        var blocks = new List<SessionLayerMarkerBlock>();
        var errors = new List<MarkerDiscoveryError>();
        try
        {
            foreach (var path in SessionLayerMarkerStore.StartupFiles(repoRoot))
            {
                var relative = Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                try
                {
                    var parsed = SessionLayerMarkerStore.Parse(relative, File.ReadAllText(path));
                    if (parsed.Error is not null)
                    {
                        errors.Add(new(relative, "marker-malformed", parsed.Error));
                    }
                    else
                    {
                        blocks.AddRange(parsed.Blocks);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new(relative, "marker-unreadable", $"Managed marker file '{relative}' is unreadable: {exception.Message}"));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add(new("<startup-files>", "marker-unreadable", $"Startup marker discovery failed: {exception.Message}"));
        }

        return new MarkerDiscovery(blocks, errors);
    }

    private sealed record TopologyTeamScope(string? Domain, string Team);

    private sealed record TopologyDiscovery(
        bool FileExists,
        IReadOnlyList<TopologyTeamScope> Scopes,
        string? Error)
    {
        public IReadOnlyList<string> Teams => Scopes
            .Select(scope => scope.Team)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(team => team, StringComparer.Ordinal)
            .ToArray();

        public bool HasTeam(string? domain, string team) => Scopes.Any(scope =>
            string.Equals(scope.Team, team, StringComparison.Ordinal)
            && (domain is null || string.Equals(scope.Domain, domain, StringComparison.Ordinal)));
    }
    private sealed record MarkerDiscovery(IReadOnlyList<SessionLayerMarkerBlock> Blocks, IReadOnlyList<MarkerDiscoveryError> Errors);
    private sealed record MarkerDiscoveryError(string File, string Cause, string Message);
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
