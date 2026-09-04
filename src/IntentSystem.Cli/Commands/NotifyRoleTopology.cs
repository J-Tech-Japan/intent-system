using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyRecordedRole(
    string Resident,
    string? WorkspaceId,
    string? PaneId,
    string? Reader,
    string? Cwd,
    string? Kind,
    string? DeliveryMethod,
    string? Frontend,
    IReadOnlyList<string>? LaunchArguments = null,
    string? EnvelopeProfileReference = null,
    AgentLaunchEnvelopeProfile? EnvelopeProfileOverride = null,
    string? Model = null,
    string? ReasoningEffort = null,
    string? WakeCommand = null)
{
    public const string HerdrResident = "herdr";
    public const string ExternalResident = "external";
}

internal sealed record NotifyHostStateDeclaration(
    string Role,
    string Envelope);

internal sealed record NotifyTeamTopology(
    string SourcePath,
    string? Domain,
    string Team,
    string WorkspaceId,
    IReadOnlyDictionary<string, NotifyRecordedRole> Roles,
    IReadOnlyDictionary<string, AgentLaunchEnvelopeProfile> EnvelopeProfiles,
    NotifyHostStateDeclaration? HostState = null);

internal sealed record NotifyTopologyResolution
{
    public required bool Resolved { get; init; }
    public NotifyTeamTopology? Topology { get; init; }
    public string? Cause { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public required string Summary { get; init; }
}

internal sealed record NotifyRoleDeliveryResolution
{
    public required bool Resolved { get; init; }
    public required string Role { get; init; }
    public string? RecordedRole { get; init; }
    public string? Resident { get; init; }
    public string? TargetKind { get; init; }
    public string? Target { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

internal sealed record NotifyRecordedRoleResolution
{
    public required bool Resolved { get; init; }
    public required string Role { get; init; }
    public string? RecordedRole { get; init; }
    public NotifyRecordedRole? Record { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyFinding(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("cause")] string Cause,
    [property: JsonPropertyName("message")] string Message)
{
    [JsonPropertyName("is_informational")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsInformational { get; init; }
}

internal sealed record SessionLayerTopologyDeclaredRole(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort);

internal static class SessionLayerTopologyDeclaredValueRules
{
    public const int MaxLength = 256;
    public const string OperatorDeclarationSummary =
        "Model and reasoning effort are operator declarations, not measurements.";

    public static bool TryValidate(string? value, string field, out string error)
    {
        if (value is null)
        {
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{field} must be non-empty when supplied.";
            return false;
        }

        if (value.Length > MaxLength)
        {
            error = $"{field} must be at most {MaxLength} characters when supplied.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryRead(
        JsonElement role,
        string property,
        out string? value,
        out string error)
    {
        value = null;
        if (!role.TryGetProperty(property, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            error = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"Topology field '{property}' must be a string or null when present.";
            return false;
        }

        value = element.GetString();
        return TryValidate(value, $"Topology field '{property}'", out error);
    }
}

/// <summary>
/// G776: an external role may declare one literal, one-line courtesy wake
/// template. This is syntax-only validation: intent-cli neither parses shell
/// syntax nor attempts to execute, look up, validate, or health-check it.
/// </summary>
internal static class SessionLayerTopologyWakeCommandRules
{
    public static bool TryValidate(string? value, string field, out string error)
    {
        if (value is null)
        {
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{field} must be non-empty when supplied.";
            return false;
        }

        if (value.IndexOfAny(['\r', '\n']) >= 0)
        {
            error = $"{field} must be a one-line literal command template.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryRead(
        JsonElement role,
        string property,
        out string? value,
        out string error)
    {
        value = null;
        if (!role.TryGetProperty(property, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            error = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"Topology field '{property}' must be a string or null when present.";
            return false;
        }

        value = element.GetString();
        return TryValidate(value, $"Topology field '{property}'", out error);
    }
}

internal sealed record SessionLayerTopologyValidation
{
    public required bool Valid { get; init; }
    public required string Team { get; init; }
    public required string SourcePath { get; init; }
    public required IReadOnlyList<SessionLayerTopologyFinding> Findings { get; init; }
    public IReadOnlyList<SessionLayerTopologyDeclaredRole> RoleDeclarations { get; init; } = [];
    public NotifyHostStateDeclaration? HostState { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Reads the operator-supplied herdr logical-role topology. G592 adds the
/// canonical writer beside this shared resolver; notify remains a read-only
/// consumer and refuses missing, ambiguous, or unsafe records rather than
/// inventing a destination.
/// </summary>
internal static class NotifyRoleTopologyStore
{
    public const string HostStateRoleMissingCause = "host-state-role-missing";
    public const string HostStatePropertyName = "host_state";
    public const string LegacyRelativePath = ".intent-cli/role-pane-mapping.json";
    public const string TopologyDirectoryRelativePath = ".intent-cli/topology";
    public const string LocalIgnoreFileName = ".gitignore";

    // Kept for source-compatible legacy fixtures. New writers and readers must
    // use the domain/team overload below.
    public const string RelativePath = LegacyRelativePath;

    public static string ResolvePath(string routingRoot) => Path.GetFullPath(Path.Combine(
        routingRoot,
        LegacyRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    public static string RelativePathFor(string domain, string team) =>
        $"{TopologyDirectoryRelativePath}/{domain}/{team}.json";

    public static string ResolvePath(string routingRoot, string domain, string team) =>
        Path.GetFullPath(Path.Combine(
            routingRoot,
            RelativePathFor(ValidatePathSegment(domain, "domain"), ValidatePathSegment(team, "team"))
                .Replace('/', Path.DirectorySeparatorChar)));

    public static string ResolveLocalIgnorePath(string routingRoot) => Path.GetFullPath(Path.Combine(
        routingRoot,
        TopologyDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar),
        LocalIgnoreFileName));

    public static string TopologyRemedy(string team) =>
        $"Run `intent-cli session-layer topology validate --domain <domain> --team {team} --format json`, then use "
        + "`session-layer topology record --domain <domain> ... --write` to record the operator-supplied correction.";

    private static NotifyTopologyResolution LegacyCompatibilityReadRemoved(string legacyPath, string? domain, string team) =>
        Failure(
            "legacy-topology-compatibility-removed",
            $"Legacy topology file '{legacyPath}' was found for team '{team}', but the compatibility read has been removed. "
            + $"Run `intent-cli session-layer topology record --domain {domain ?? "<domain>"} --team {team} ... --write` "
            + "to declare the current shape, or run "
            + $"`intent-cli session-layer topology retire-legacy --domain {domain ?? "<domain>"} --team {team} "
            + "--evidence <evidence> --confirm-retire-legacy --write` to retire the legacy file with evidence.");

    public static NotifyTopologyResolution Resolve(string routingRoot, string team) =>
        Resolve(routingRoot, domain: null, team);

    public static NotifyTopologyResolution Resolve(string routingRoot, string? domain, string team)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            var legacyOnlyPath = ResolvePath(routingRoot);
            var matches = FindNewTopologyPaths(routingRoot, team).ToArray();
            return matches.Length == 1
                ? ResolveFromPath(matches[0], expectedDomain: null, team, requireIdentity: false)
                : File.Exists(legacyOnlyPath)
                    ? LegacyCompatibilityReadRemoved(legacyOnlyPath, null, team)
                    : ResolveFromPath(legacyOnlyPath, null, team, requireIdentity: false);
        }

        string newPath;
        try
        {
            newPath = ResolvePath(routingRoot, domain, team);
        }
        catch (ArgumentException exception)
        {
            return Failure("topology-invalid", exception.Message);
        }

        if (!File.Exists(newPath))
        {
            var legacyPath = ResolvePath(routingRoot);
            if (File.Exists(legacyPath))
            {
                return LegacyCompatibilityReadRemoved(legacyPath, domain, team);
            }

            return Failure(
                "topology-missing",
                $"Recorded role topology for domain '{domain}' team '{team}' was not found at '{newPath}'. "
                + $"Provision and record the team's workspace, roles, residences, panes/readers, then retry "
                + $"notify. {TopologyRemedy(team)}");
        }

        return ResolveFromPath(newPath, domain, team, requireIdentity: true);
    }

    private static NotifyTopologyResolution ResolveFromPath(
        string path,
        string? expectedDomain,
        string team,
        bool requireIdentity)
    {
        if (!File.Exists(path))
        {
            return Failure("topology-missing",
                $"Recorded role topology for team '{team}' was not found at '{path}'. {TopologyRemedy(team)}");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (requireIdentity)
            {
                var recordedDomain = ReadString(root, "domain");
                var recordedTeam = ReadString(root, "team");
                if (!string.Equals(recordedDomain, expectedDomain, StringComparison.Ordinal)
                    || !string.Equals(recordedTeam, team, StringComparison.Ordinal))
                {
                    return Failure(
                        "topology-identity-mismatch",
                        $"Topology file '{path}' identifies domain '{recordedDomain ?? "missing"}' team "
                        + $"'{recordedTeam ?? "missing"}', but its path was requested for domain '{expectedDomain}' "
                        + $"team '{team}'. Refusing the copied or misplaced machine record. {TopologyRemedy(team)}");
                }
            }

            if (!TrySelectTeam(root, team, out var teamElement, out var teamError))
            {
                return Failure(
                    "topology-team-missing",
                    $"Recorded role topology '{path}' {teamError} {TopologyRemedy(team)}");
            }

            var rolesElement = teamElement.TryGetProperty("roles", out var nestedRoles)
                ? nestedRoles
                : teamElement;
            if (rolesElement.ValueKind != JsonValueKind.Object)
            {
                return Failure(
                    "topology-invalid",
                    $"Recorded role topology '{path}' for team '{team}' has no object-valued 'roles'. Record the "
                    + $"team roster before retrying notify. {TopologyRemedy(team)}");
            }

            if (!TryReadEnvelopeProfiles(root, teamElement, out var envelopeProfiles, out var profileError))
            {
                return Failure("profile-invalid", profileError);
            }

            var roles = new Dictionary<string, NotifyRecordedRole>(StringComparer.Ordinal);
            foreach (var property in rolesElement.EnumerateObject())
            {
                if (IsTopologyEnvelopeProperty(property.Name))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    return Failure(
                        "topology-invalid",
                        $"Recorded role '{property.Name}' for team '{team}' in '{path}' is not an object. Repair "
                        + $"the role record before retrying notify. {TopologyRemedy(team)}");
                }

                var resident = ReadString(property.Value, "resident");
                if (resident is not (NotifyRecordedRole.HerdrResident or NotifyRecordedRole.ExternalResident))
                {
                    return Failure(
                        "topology-invalid",
                        $"Recorded role '{property.Name}' for team '{team}' in '{path}' has unsupported resident "
                        + $"'{resident ?? "missing"}'. Use 'herdr' or 'external' and retry. {TopologyRemedy(team)}");
                }

                var profileReference = ReadProfileReference(property.Value, out var profileReferenceError);
                if (profileReferenceError is not null)
                {
                    return Failure(
                        "profile-invalid",
                        $"Role '{property.Name}' has an invalid envelope profile reference: {profileReferenceError}");
                }

                AgentLaunchEnvelopeProfile? profileOverride = null;
                if (property.Value.TryGetProperty("envelope_profile_override", out var overrideElement)
                    || property.Value.TryGetProperty("profile_override", out overrideElement))
                {
                    if (!AgentLaunchEnvelopeProfileCodec.TryRead(
                            overrideElement,
                            ReadString(overrideElement, "name") ?? $"{property.Name}:override",
                            out profileOverride,
                            out var overrideError))
                    {
                        return Failure(
                            "profile-invalid",
                            $"Role '{property.Name}' has an invalid envelope profile override: {overrideError}");
                    }
                }

                if (profileReference is not null && profileOverride is not null)
                {
                    return Failure(
                        "profile-invalid",
                        $"Role '{property.Name}' records both an envelope profile reference and an override; refusing ambiguous baseline selection.");
                }

                var roleKind = ReadString(property.Value, "kind");
                if (!SessionLayerTopologyDeclaredValueRules.TryRead(
                        property.Value,
                        "model",
                        out var model,
                        out var modelError))
                {
                    return Failure(
                        "topology-invalid",
                        $"Role '{property.Name}' has an invalid declared model identity: {modelError}");
                }

                if (!SessionLayerTopologyDeclaredValueRules.TryRead(
                        property.Value,
                        "reasoning_effort",
                        out var reasoningEffort,
                        out var reasoningEffortError))
                {
                    return Failure(
                        "topology-invalid",
                        $"Role '{property.Name}' has an invalid declared reasoning effort: {reasoningEffortError}");
                }

                if (!SessionLayerTopologyWakeCommandRules.TryRead(
                        property.Value,
                        "wake_command",
                        out var wakeCommand,
                        out var wakeCommandError))
                {
                    return Failure(
                        "topology-invalid",
                        $"Role '{property.Name}' has an invalid declared wake command: {wakeCommandError}");
                }

                if (wakeCommand is not null
                    && !string.Equals(resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal))
                {
                    return Failure(
                        "topology-invalid",
                        $"Role '{property.Name}' declares wake_command but only an external resident may declare a courtesy wake command.");
                }

                if (profileReference is not null)
                {
                    if (!envelopeProfiles.TryGetValue(profileReference, out var referencedProfile))
                    {
                        return Failure(
                            "profile-invalid",
                            $"Role '{property.Name}' references missing envelope profile '{profileReference}'. No registry fallback is permitted.");
                    }

                    if (string.IsNullOrWhiteSpace(roleKind)
                        || !string.Equals(referencedProfile.Kind, roleKind, StringComparison.OrdinalIgnoreCase))
                    {
                        return Failure(
                            "profile-invalid",
                            $"Role '{property.Name}' kind '{roleKind ?? "missing"}' does not match referenced envelope profile '{profileReference}' kind '{referencedProfile.Kind}'. No registry fallback is permitted.");
                    }
                }

                if (profileOverride is not null
                    && (string.IsNullOrWhiteSpace(roleKind)
                        || !string.Equals(profileOverride.Kind, roleKind, StringComparison.OrdinalIgnoreCase)))
                {
                    return Failure(
                        "profile-invalid",
                        $"Role '{property.Name}' kind '{roleKind ?? "missing"}' does not match its envelope profile override kind '{profileOverride.Kind}'. No registry fallback is permitted.");
                }

                roles.Add(property.Name, new NotifyRecordedRole(
                    resident,
                    ReadString(property.Value, "workspace_id"),
                    ReadString(property.Value, "pane_id"),
                    ReadString(property.Value, "reader"),
                    ReadString(property.Value, "cwd"),
                    ReadString(property.Value, "kind"),
                    ReadString(property.Value, "delivery_method"),
                    ReadString(property.Value, "frontend"),
                    ReadStringArray(property.Value, "launch_args"),
                    profileReference,
                    profileOverride,
                    model,
                    reasoningEffort,
                    wakeCommand));
            }

            if (roles.Count == 0)
            {
                return Failure(
                    "topology-invalid",
                    $"Recorded role topology '{path}' for team '{team}' contains no roles. Record the team roster "
                    + $"before retrying notify. {TopologyRemedy(team)}");
            }

            var workspaceId = ReadString(teamElement, "workspace_id")
                ?? ReadNestedWorkspaceId(teamElement)
                ?? ConsistentWorkspaceFromRoles(roles.Values);
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return Failure(
                    "topology-invalid",
                    $"Recorded role topology '{path}' for team '{team}' has no unambiguous workspace_id. Record "
                    + $"the team's workspace explicitly before retrying notify. {TopologyRemedy(team)}");
            }

            foreach (var (role, record) in roles)
            {
                if (string.Equals(record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(record.WorkspaceId)
                    && !string.Equals(record.WorkspaceId, workspaceId, StringComparison.Ordinal))
                {
                    return Failure(
                        "topology-invalid",
                        $"Recorded herdr role '{role}' uses workspace '{record.WorkspaceId}', outside team '{team}' "
                        + $"workspace '{workspaceId}' in '{path}'. Repair the team-scoped mapping before retrying. "
                        + TopologyRemedy(team));
                }
            }

            if (!TryReadHostState(
                    root,
                    teamElement,
                    roles.Keys,
                    out var hostState,
                    out var hostStateError))
            {
                return Failure("host-state-invalid", hostStateError);
            }

            return new NotifyTopologyResolution
            {
                Resolved = true,
                Topology = new NotifyTeamTopology(
                    path,
                    expectedDomain,
                    team,
                    workspaceId,
                    roles,
                    envelopeProfiles,
                    hostState),
                Summary = $"Resolved recorded role topology for team '{team}' from '{path}'.",
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Failure(
                "topology-unreadable",
                $"Recorded role topology '{path}' for team '{team}' could not be read: {exception.Message} "
                + $"Repair the file and retry notify. {TopologyRemedy(team)}");
        }
    }

    /// <summary>
    /// Resolves the recorded delivery target without querying herdr or sending
    /// anything. Notify and <c>session-layer topology show</c> both use this
    /// function so their interpretation of pane and reader records cannot
    /// drift.
    /// </summary>
    public static NotifyRoleDeliveryResolution ResolveDeliveryTarget(
        string routingRoot,
        NotifyTeamTopology topology,
        string role)
    {
        var roleResolution = ResolveRecordedRole(topology, role);
        if (!roleResolution.Resolved || roleResolution.Record is not { } record)
        {
            return DeliveryFailure(
                role,
                roleResolution.Cause ?? "unknown-role",
                roleResolution.Summary);
        }

        if (string.Equals(record.Resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal))
        {
            if (!TryResolveReaderPath(routingRoot, record.Reader, out var readerPath, out var readerError))
            {
                return DeliveryFailure(
                    role,
                    "reader-unavailable",
                    $"External logical role '{role}' has no deliverable recorded reader in "
                    + $"'{topology.SourcePath}': {readerError}");
            }

            if (topology.Domain is not null
                && !NotifyEventWriter.TryResolveRecordedWritePath(
                    routingRoot,
                    topology.Domain,
                    topology.Team,
                    readerPath,
                    out readerPath,
                    out readerError))
            {
                return DeliveryFailure(
                    role,
                    "reader-unavailable",
                    $"External logical role '{role}' has no deliverable recorded reader in "
                    + $"'{topology.SourcePath}': {readerError}");
            }

            return new NotifyRoleDeliveryResolution
            {
                Resolved = true,
                Role = role,
                RecordedRole = roleResolution.RecordedRole,
                Resident = record.Resident,
                TargetKind = "reader",
                Target = readerPath,
                Summary = $"Resolved external logical role '{role}'"
                    + RoleAliasSuffix(role, roleResolution.RecordedRole)
                    + $" to recorded reader '{readerPath}'.",
            };
        }

        if (string.IsNullOrWhiteSpace(record.PaneId))
        {
            return DeliveryFailure(
                role,
                "pane-absent",
                $"Recorded topology '{topology.SourcePath}' gives herdr logical role '{role}' no pane_id.");
        }

        return new NotifyRoleDeliveryResolution
        {
            Resolved = true,
            Role = role,
            RecordedRole = roleResolution.RecordedRole,
            Resident = record.Resident,
            TargetKind = "pane",
            Target = record.PaneId,
            Summary = $"Resolved herdr logical role '{role}'"
                + RoleAliasSuffix(role, roleResolution.RecordedRole)
                + $" to recorded pane '{record.PaneId}' in workspace "
                + $"'{topology.WorkspaceId}'.",
        };
    }

    /// <summary>
    /// Resolves a requested logical role against the operator-recorded roster.
    /// The role-contract guidance owns normalization and its aliases; every
    /// topology consumer reaches this lookup instead of maintaining a second
    /// role map.
    /// </summary>
    public static NotifyRecordedRoleResolution ResolveRecordedRole(
        NotifyTeamTopology topology,
        string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return RoleResolutionFailure(
                topology,
                role,
                "unknown-role",
                "A non-empty logical role is required.");
        }

        if (topology.Roles.TryGetValue(role, out var exactRecord))
        {
            return new NotifyRecordedRoleResolution
            {
                Resolved = true,
                Role = role,
                RecordedRole = role,
                Record = exactRecord,
                Summary = $"Resolved recorded logical role '{role}' in topology '{topology.SourcePath}'.",
            };
        }

        var canonicalRole = GuideRoleContractGuidance.Normalize(role) ?? role;
        var aliasMatches = topology.Roles
            .Where(entry => string.Equals(
                GuideRoleContractGuidance.Normalize(entry.Key) ?? entry.Key,
                canonicalRole,
                StringComparison.Ordinal))
            .ToArray();
        if (aliasMatches.Length == 1)
        {
            var match = aliasMatches[0];
            return new NotifyRecordedRoleResolution
            {
                Resolved = true,
                Role = role,
                RecordedRole = match.Key,
                Record = match.Value,
                Summary = $"Resolved requested logical role '{role}' through accepted recorded alias '{match.Key}' "
                    + $"in topology '{topology.SourcePath}'.",
            };
        }

        if (aliasMatches.Length > 1)
        {
            return RoleResolutionFailure(
                topology,
                role,
                "ambiguous-role",
                $"Multiple recorded roles ({string.Join(", ", aliasMatches.Select(entry => $"'{entry.Key}'"))}) "
                + $"normalize to logical role '{canonicalRole}'. Keep one accepted spelling in the team record "
                + "before retrying notify.");
        }

        var acceptedName = string.Equals(canonicalRole, LogicalRoleNormalizer.Orchestrator, StringComparison.Ordinal)
            ? " The coordinating seat is accepted as canonical 'orchestrator' or accepted recorded alias 'orchestration'; an existing record under either name does not need renaming."
            : string.Empty;
        return RoleResolutionFailure(
            topology,
            role,
            "unknown-role",
            $"Recorded role topology '{topology.SourcePath}' for team '{topology.Team}' workspace "
            + $"'{topology.WorkspaceId}' does not contain logical role '{role}' (found in that team scope: "
            + $"{FormatRoles(topology.Roles.Keys)}). Record that role for this team before retrying notify."
            + acceptedName);
    }

    /// <summary>
    /// Reads the requested team independently from notify's fail-fast path and
    /// returns every authored-contract violation in one stable answer.
    /// </summary>
    public static SessionLayerTopologyValidation Validate(string routingRoot, string team) =>
        Validate(routingRoot, domain: null, team);

    public static SessionLayerTopologyValidation Validate(string routingRoot, string? domain, string team)
    {
        var findings = new List<SessionLayerTopologyFinding>();
        NotifyHostStateDeclaration? discoveredHostState = null;
        var path = string.IsNullOrWhiteSpace(domain)
            ? ResolvePath(routingRoot)
            : ResolvePath(routingRoot, domain, team);
        if (string.IsNullOrWhiteSpace(domain) && !File.Exists(path))
        {
            var matches = FindNewTopologyPaths(routingRoot, team).ToArray();
            if (matches.Length == 1)
            {
                path = matches[0];
            }
        }
        if (!File.Exists(path) && !string.IsNullOrWhiteSpace(domain))
        {
            var legacyPath = ResolvePath(routingRoot);
            if (File.Exists(legacyPath))
            {
                var compatibility = LegacyCompatibilityReadRemoved(legacyPath, domain, team);
                findings.Add(Finding("<topology>", "file", compatibility.Cause!, compatibility.Summary));
                return Validation(team, path, findings);
            }
        }

        var roleDeclarations = new List<SessionLayerTopologyDeclaredRole>();

        if (!File.Exists(path))
        {
            var resolution = Resolve(routingRoot, domain, team);
            findings.Add(Finding("<topology>", "file", resolution.Cause!, resolution.Summary));
            return Validation(team, path, findings);
        }

        if (!string.IsNullOrWhiteSpace(domain) && !string.Equals(path, ResolvePath(routingRoot), StringComparison.Ordinal))
        {
            try
            {
                using var identityDocument = JsonDocument.Parse(File.ReadAllText(path));
                var recordedDomain = ReadString(identityDocument.RootElement, "domain");
                var recordedTeam = ReadString(identityDocument.RootElement, "team");
                if (!string.Equals(recordedDomain, domain, StringComparison.Ordinal)
                    || !string.Equals(recordedTeam, team, StringComparison.Ordinal))
                {
                    findings.Add(Finding("<topology>", "identity", "topology-identity-mismatch",
                        $"Topology file '{path}' identifies domain '{recordedDomain ?? "missing"}' team "
                        + $"'{recordedTeam ?? "missing"}', but its path was requested for domain '{domain}' "
                        + $"team '{team}'. Refusing the copied or misplaced machine record."));
                    return Validation(team, path, findings);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // The detailed validator below emits the canonical unreadable finding.
            }
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!TrySelectTeam(document.RootElement, team, out var teamElement, out var teamError))
            {
                findings.Add(Finding("<topology>", "team", "topology-team-missing",
                    $"Topology file '{path}' {teamError}"));
                return Validation(team, path, findings);
            }

            var rolesElement = teamElement.TryGetProperty("roles", out var nestedRoles)
                ? nestedRoles
                : teamElement;
            if (rolesElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(Finding("<topology>", "roles", "topology-invalid",
                    $"Team '{team}' has no object-valued roles field."));
                return Validation(team, path, findings);
            }

            var teamWorkspaceId = ReadString(teamElement, "workspace_id")
                ?? ReadNestedWorkspaceId(teamElement);
            var roleCount = 0;
            foreach (var property in rolesElement.EnumerateObject())
            {
                if (IsTopologyEnvelopeProperty(property.Name))
                {
                    continue;
                }

                roleCount++;
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(Finding(property.Name, "role", "topology-invalid",
                        $"Role '{property.Name}' is not an object."));
                    continue;
                }

                var declaredModel = (string?)null;
                var declaredReasoningEffort = (string?)null;
                if (!SessionLayerTopologyDeclaredValueRules.TryRead(
                        property.Value,
                        "model",
                        out declaredModel,
                        out var modelError))
                {
                    findings.Add(Finding(property.Name, "model", "topology-invalid", modelError));
                }

                if (!SessionLayerTopologyDeclaredValueRules.TryRead(
                        property.Value,
                        "reasoning_effort",
                        out declaredReasoningEffort,
                        out var reasoningEffortError))
                {
                    findings.Add(Finding(
                        property.Name,
                        "reasoning_effort",
                        "topology-invalid",
                        reasoningEffortError));
                }

                roleDeclarations.Add(new(
                    property.Name,
                    declaredModel,
                    declaredReasoningEffort));

                var resident = ReadString(property.Value, "resident");
                var supportedResident = resident is NotifyRecordedRole.HerdrResident
                    or NotifyRecordedRole.ExternalResident;
                if (!supportedResident)
                {
                    findings.Add(Finding(property.Name, "resident", "topology-invalid",
                        $"Role '{property.Name}' field 'resident' is "
                        + $"'{resident ?? "missing"}'; supported values are 'herdr' and 'external'."));
                }

                var hasLegacyPane = property.Value.TryGetProperty("pane", out _);
                var paneBacked = string.Equals(resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
                    || hasLegacyPane
                    || property.Value.TryGetProperty("pane_id", out _);
                if (paneBacked && string.IsNullOrWhiteSpace(ReadString(property.Value, "pane_id")))
                {
                    findings.Add(Finding(property.Name, "pane_id", "pane-absent",
                        hasLegacyPane
                            ? $"Role '{property.Name}' uses unsupported field 'pane'; required field 'pane_id' is missing."
                            : $"Herdr role '{property.Name}' field 'pane_id' is missing or empty."));
                }

                if (string.Equals(resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal)
                    && !TryResolveReaderPath(
                        routingRoot,
                        ReadString(property.Value, "reader"),
                        out _,
                        out var readerError))
                {
                    findings.Add(Finding(property.Name, "reader", "reader-unavailable",
                        $"External role '{property.Name}' field 'reader' is unsafe or unavailable: {readerError}"));
                }

                var roleWorkspaceId = ReadString(property.Value, "workspace_id")
                    ?? WorkspaceFromPane(ReadString(property.Value, "pane_id"));
                if (!string.IsNullOrWhiteSpace(teamWorkspaceId)
                    && !string.IsNullOrWhiteSpace(roleWorkspaceId)
                    && !string.Equals(teamWorkspaceId, roleWorkspaceId, StringComparison.Ordinal))
                {
                    findings.Add(Finding(property.Name, "workspace_id", "workspace-mismatch",
                        $"Role '{property.Name}' field 'workspace_id' resolves to '{roleWorkspaceId}', not team "
                        + $"workspace '{teamWorkspaceId}'."));
                }
            }

            if (roleCount == 0)
            {
                findings.Add(Finding("<topology>", "roles", "topology-invalid",
                    $"Team '{team}' contains no recorded roles."));
            }

            var roleNames = rolesElement.EnumerateObject()
                .Where(property => !IsTopologyEnvelopeProperty(property.Name))
                .Select(property => property.Name)
                .ToArray();
            var hostStateDeclarationMissing = false;
            if (!TryReadHostState(
                    document.RootElement,
                    teamElement,
                    roleNames,
                    out var hostState,
                    out var hostStateError))
            {
                findings.Add(Finding(
                    "<host-state>",
                    HostStatePropertyName,
                    "host-state-invalid",
                    hostStateError));
            }
            else if (hostState is null)
            {
                hostStateDeclarationMissing = true;
            }
            else
            {
                discoveredHostState = hostState;
            }

            if (string.IsNullOrWhiteSpace(teamWorkspaceId))
            {
                var inferred = InferConsistentWorkspace(rolesElement);
                if (string.IsNullOrWhiteSpace(inferred))
                {
                    findings.Add(Finding("<topology>", "workspace_id", "topology-invalid",
                        $"Team '{team}' has no unambiguous field 'workspace_id'."));
                }
            }

            var profileResolution = Resolve(routingRoot, domain, team);
            if (!profileResolution.Resolved
                && string.Equals(profileResolution.Cause, "profile-invalid", StringComparison.Ordinal))
            {
                findings.Add(Finding("<topology>", "envelope_profile", "profile-invalid", profileResolution.Summary));
            }

            // A missing declaration is an actionable capacity finding for an
            // otherwise usable legacy topology. Do not obscure a malformed
            // record with a second, derivative finding; the caller must repair
            // the structural error first.
            if (hostStateDeclarationMissing && findings.All(finding => finding.IsInformational))
            {
                findings.Add(Finding(
                    "<host-state>",
                    "role",
                    HostStateRoleMissingCause,
                    HostStateRoleMissingMessage(team, path),
                    isInformational: true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            findings.Add(Finding("<topology>", "file", "topology-unreadable",
                $"Topology file '{path}' is unreadable: {exception.Message}"));
        }

        return Validation(
            team,
            path,
            findings,
            hostState: discoveredHostState,
            roleDeclarations: roleDeclarations);
    }

    public static bool TryResolveReaderPath(
        string routingRoot,
        string? recordedReader,
        out string readerPath,
        out string error)
    {
        readerPath = string.Empty;
        if (string.IsNullOrWhiteSpace(recordedReader))
        {
            error = "the reader field is missing or empty.";
            return false;
        }

        if (Path.IsPathRooted(recordedReader))
        {
            error = "the reader must be relative to --routing-root, not an absolute path.";
            return false;
        }

        try
        {
            var root = Path.GetFullPath(routingRoot);
            var candidate = Path.GetFullPath(Path.Combine(root, recordedReader));
            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidate.StartsWith(rootPrefix, pathComparison))
            {
                error = "the reader escapes --routing-root.";
                return false;
            }

            readerPath = candidate;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = $"the reader path is invalid: {exception.Message}";
            return false;
        }
    }

    private static bool TrySelectTeam(
        JsonElement root,
        string team,
        out JsonElement teamElement,
        out string error)
    {
        teamElement = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "is not a JSON object. Repair it before retrying notify.";
            return false;
        }

        if (root.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Object)
        {
            if (teams.TryGetProperty(team, out teamElement) && teamElement.ValueKind == JsonValueKind.Object)
            {
                error = string.Empty;
                return true;
            }

            error = $"does not contain team '{team}' under 'teams'. Record that team before retrying notify.";
            return false;
        }

        var recordedTeam = ReadString(root, "team");
        if (string.Equals(recordedTeam, team, StringComparison.Ordinal))
        {
            teamElement = root;
            error = string.Empty;
            return true;
        }

        if (root.TryGetProperty(team, out teamElement) && teamElement.ValueKind == JsonValueKind.Object)
        {
            error = string.Empty;
            return true;
        }

        error = $"records team '{recordedTeam ?? "none"}', not requested team '{team}'. Record the requested team "
            + "before retrying notify.";
        return false;
    }

    private static string? ReadProfileReference(JsonElement element, out string? error)
    {
        string? referenceError = null;

        bool TryReadReference(string propertyName, out bool present, out string? value)
        {
            present = false;
            value = null;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var candidate))
            {
                return true;
            }

            present = true;
            if (candidate.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (candidate.ValueKind != JsonValueKind.String)
            {
                referenceError = $"field '{propertyName}' is present with JSON kind '{candidate.ValueKind}', but a profile reference must be a string or null.";
                return false;
            }

            value = candidate.GetString();
            return true;
        }

        if (!TryReadReference("envelope_profile", out var directPresent, out var direct)
            || !TryReadReference("envelope_profile_ref", out var explicitPresent, out var explicitReference))
        {
            error = referenceError;
            return null;
        }

        error = null;
        if (directPresent
            && explicitPresent
            && direct is not null
            && explicitReference is not null
            && !string.Equals(direct, explicitReference, StringComparison.Ordinal))
        {
            error = $"envelope_profile '{direct}' conflicts with envelope_profile_ref '{explicitReference}'.";
            return null;
        }

        return direct ?? explicitReference;
    }

    private static bool TryReadEnvelopeProfiles(
        JsonElement root,
        JsonElement teamElement,
        out IReadOnlyDictionary<string, AgentLaunchEnvelopeProfile> profiles,
        out string error)
    {
        var result = new Dictionary<string, AgentLaunchEnvelopeProfile>(StringComparer.Ordinal);
        error = string.Empty;
        var profileErrorText = string.Empty;

        bool AddSource(JsonElement source)
        {
            foreach (var propertyName in new[] { "envelope_profiles", "profiles" })
            {
                if (!source.TryGetProperty(propertyName, out var profileNode))
                {
                    continue;
                }

                if (profileNode.ValueKind == JsonValueKind.Object
                    && profileNode.TryGetProperty("kind", out _))
                {
                    var profileName = ReadString(profileNode, "name");
                    if (string.IsNullOrWhiteSpace(profileName))
                    {
                        profileErrorText = $"Topology field '{propertyName}' is a profile object but has no name.";
                        return false;
                    }

                    if (!AddProfile(profileName!, profileNode)) return false;
                    continue;
                }

                if (profileNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in profileNode.EnumerateArray())
                    {
                        var profileName = ReadString(item, "name");
                        if (string.IsNullOrWhiteSpace(profileName))
                        {
                            profileErrorText = $"Topology field '{propertyName}' contains a profile without a name.";
                            return false;
                        }

                        if (!AddProfile(profileName!, item)) return false;
                    }
                    continue;
                }

                if (profileNode.ValueKind != JsonValueKind.Object)
                {
                    profileErrorText = $"Topology field '{propertyName}' must be an object or array.";
                    return false;
                }

                foreach (var profileProperty in profileNode.EnumerateObject())
                {
                    if (!AddProfile(profileProperty.Name, profileProperty.Value)) return false;
                }
            }

            return true;
        }

        bool AddProfile(string name, JsonElement element)
        {
            var declaredName = ReadString(element, "name");
            if (declaredName is not null && !string.Equals(declaredName, name, StringComparison.Ordinal))
            {
                profileErrorText = $"Envelope profile map key '{name}' conflicts with embedded profile name '{declaredName}'.";
                return false;
            }

            if (!AgentLaunchEnvelopeProfileCodec.TryRead(element, name, out var profile, out var profileError))
            {
                profileErrorText = profileError;
                return false;
            }

            if (result.TryGetValue(name, out var existing)
                && !string.Equals(existing.Digest, profile!.Digest, StringComparison.OrdinalIgnoreCase))
            {
                profileErrorText = $"Envelope profile '{name}' is declared more than once with different content.";
                return false;
            }

            result[name] = profile!;
            return true;
        }

        var hasTeams = root.TryGetProperty("teams", out _);
        if (!AddSource(hasTeams ? root : teamElement)
            || (hasTeams && !AddSource(teamElement)))
        {
            error = profileErrorText;
            profiles = new Dictionary<string, AgentLaunchEnvelopeProfile>(StringComparer.Ordinal);
            return false;
        }

        profiles = result;
        return true;
    }

    private static bool TryReadHostState(
        JsonElement root,
        JsonElement teamElement,
        IEnumerable<string> roleNames,
        out NotifyHostStateDeclaration? declaration,
        out string error)
    {
        declaration = null;
        error = string.Empty;

        JsonElement hostState;
        if (teamElement.TryGetProperty(HostStatePropertyName, out var teamHostState))
        {
            hostState = teamHostState;
        }
        else if (root.TryGetProperty(HostStatePropertyName, out var rootHostState))
        {
            hostState = rootHostState;
        }
        else
        {
            return true;
        }

        if (hostState.ValueKind != JsonValueKind.Object)
        {
            error = "Topology field 'host_state' must be an object with explicit 'role' and 'envelope' fields.";
            return false;
        }

        var role = ReadString(hostState, "role");
        var envelope = ReadString(hostState, "envelope")
            ?? ReadString(hostState, "envelope_profile");
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(envelope))
        {
            error = "Topology field 'host_state' must declare non-empty 'role' and 'envelope' values; authority is never inferred from resident, kind, or placement.";
            return false;
        }

        var names = roleNames.ToArray();
        var exact = names.Contains(role, StringComparer.Ordinal);
        var normalized = GuideRoleContractGuidance.Normalize(role) ?? role;
        var aliases = names.Where(name => string.Equals(
                GuideRoleContractGuidance.Normalize(name) ?? name,
                normalized,
                StringComparison.Ordinal))
            .ToArray();
        if (!exact && aliases.Length != 1)
        {
            error = $"Topology host_state.role '{role}' is not one uniquely recorded team role; authority is never inferred from resident, kind, external placement, or co-location. Record the role first, then record host_state explicitly.";
            return false;
        }

        declaration = new NotifyHostStateDeclaration(role, envelope);
        return true;
    }

    public static string HostStateRoleMissingMessage(string team, string path) =>
        $"Topology for team '{team}' remains valid for backward compatibility and needs no migration, but '{path}' declares no host-state role. This team cannot perform required host-state workflow work (including host-state publication and repository Git operations) before publish. Record an actually capable participant and an explicit host-state role plus envelope; a declaration alone does not supply a non-sandboxed participant, and resident, kind, external placement, or co-location never grants authority.";

    private static string? ConsistentWorkspaceFromRoles(IEnumerable<NotifyRecordedRole> roles)
    {
        var workspaceIds = roles
            .Where(role => string.Equals(role.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
            .Select(role => role.WorkspaceId ?? WorkspaceFromPane(role.PaneId))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return workspaceIds.Length == 1 ? workspaceIds[0] : null;
    }

    private static string? WorkspaceFromPane(string? paneId)
    {
        var separator = paneId?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return separator > 0 ? paneId![..separator] : null;
    }

    private static string? ReadNestedWorkspaceId(JsonElement element)
    {
        if (!element.TryGetProperty("workspace", out var workspace))
        {
            return null;
        }

        if (workspace.ValueKind == JsonValueKind.String)
        {
            return workspace.GetString();
        }

        return workspace.ValueKind == JsonValueKind.Object
            ? ReadString(workspace, "workspace_id") ?? ReadString(workspace, "id")
            : null;
    }

    private static bool IsTopologyEnvelopeProperty(string property) => property is
        "schema_version" or "team" or "workspace" or "workspace_id" or "tab_id" or "updated_at" or "roles"
        or "envelope_profiles" or "profiles" or HostStatePropertyName;

    private static string? InferConsistentWorkspace(JsonElement rolesElement)
    {
        var workspaceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in rolesElement.EnumerateObject())
        {
            if (IsTopologyEnvelopeProperty(property.Name)
                || property.Value.ValueKind != JsonValueKind.Object
                || !string.Equals(
                    ReadString(property.Value, "resident"),
                    NotifyRecordedRole.HerdrResident,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var workspaceId = ReadString(property.Value, "workspace_id")
                ?? WorkspaceFromPane(ReadString(property.Value, "pane_id"));
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                workspaceIds.Add(workspaceId);
            }
        }

        return workspaceIds.Count == 1 ? workspaceIds.Single() : null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string>? ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return [];
            }

            values.Add(item.GetString()!);
        }

        return values;
    }

    private static NotifyTopologyResolution Failure(string cause, string summary) => new()
    {
        Resolved = false,
        Cause = cause,
        Summary = summary,
    };

    private static bool Equivalent(NotifyTeamTopology left, NotifyTeamTopology right) =>
        string.Equals(left.WorkspaceId, right.WorkspaceId, StringComparison.Ordinal)
        && left.Roles.Count == right.Roles.Count
        && left.Roles.All(entry => right.Roles.TryGetValue(entry.Key, out var other)
            && Equals(entry.Value, other))
        && left.EnvelopeProfiles.Count == right.EnvelopeProfiles.Count
        && left.EnvelopeProfiles.All(entry => right.EnvelopeProfiles.TryGetValue(entry.Key, out var other)
            && Equals(entry.Value, other))
        && Equals(left.HostState, right.HostState);

    private static IEnumerable<string> FindNewTopologyPaths(string routingRoot, string team)
    {
        var root = Path.Combine(routingRoot, TopologyDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, $"{team}.json", SearchOption.AllDirectories);
    }

    private static string ValidatePathSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || value is "." or "..")
        {
            throw new ArgumentException($"Topology {name} '{value}' is not a safe single path segment.", name);
        }

        return value;
    }

    private static NotifyRoleDeliveryResolution DeliveryFailure(string role, string cause, string summary) => new()
    {
        Resolved = false,
        Role = role,
        Cause = cause,
        Summary = summary,
    };

    private static NotifyRecordedRoleResolution RoleResolutionFailure(
        NotifyTeamTopology topology,
        string role,
        string cause,
        string summary)
    {
        var canonicalRole = GuideRoleContractGuidance.Normalize(role) ?? role;
        var roleOptions = string.Equals(canonicalRole, LogicalRoleNormalizer.Orchestrator, StringComparison.Ordinal)
            ? "<orchestrator|orchestration>"
            : $"<{role}>";
        var domain = topology.Domain ?? "<domain>";
        var remedy = $" Record it with \u0060intent-cli session-layer topology record --domain {domain} --team "
            + $"{topology.Team} --role {roleOptions} ... --write\u0060; do not rename an existing accepted alias."
            + " Then rerun the heartbeat/notify command.";
        return new NotifyRecordedRoleResolution
        {
            Resolved = false,
            Role = role,
            Cause = cause,
            Summary = summary + remedy,
        };
    }

    private static string RoleAliasSuffix(string requestedRole, string? recordedRole) =>
        string.IsNullOrWhiteSpace(recordedRole)
            || string.Equals(requestedRole, recordedRole, StringComparison.Ordinal)
            ? string.Empty
            : $" through recorded alias '{recordedRole}'";

    private static string FormatRoles(IEnumerable<string> roles) => string.Join(", ", roles.OrderBy(role => role, StringComparer.Ordinal));

    private static SessionLayerTopologyFinding Finding(
        string role,
        string field,
        string cause,
        string message,
        bool isInformational = false) => new(role, field, cause, message)
        {
            IsInformational = isInformational,
        };

    private static SessionLayerTopologyValidation Validation(
        string team,
        string path,
        IReadOnlyList<SessionLayerTopologyFinding> findings,
        IReadOnlyList<string>? warnings = null,
        NotifyHostStateDeclaration? hostState = null,
        IReadOnlyList<SessionLayerTopologyDeclaredRole>? roleDeclarations = null) => new()
        {
            Valid = findings.All(finding => finding.IsInformational),
            Team = team,
            SourcePath = path,
            Findings = findings,
            HostState = hostState,
            Warnings = warnings ?? [],
            RoleDeclarations = roleDeclarations ?? [],
        };
}
