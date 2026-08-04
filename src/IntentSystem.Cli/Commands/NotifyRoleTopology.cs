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
    string? Frontend)
{
    public const string HerdrResident = "herdr";
    public const string ExternalResident = "external";
}

internal sealed record NotifyTeamTopology(
    string SourcePath,
    string WorkspaceId,
    IReadOnlyDictionary<string, NotifyRecordedRole> Roles);

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
    public string? Resident { get; init; }
    public string? TargetKind { get; init; }
    public string? Target { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyFinding(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("cause")] string Cause,
    [property: JsonPropertyName("message")] string Message);

internal sealed record SessionLayerTopologyValidation
{
    public required bool Valid { get; init; }
    public required string Team { get; init; }
    public required string SourcePath { get; init; }
    public required IReadOnlyList<SessionLayerTopologyFinding> Findings { get; init; }
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
        $"Run `intent-cli session-layer topology validate --team {team} --format json`, then use "
        + "`session-layer topology record ... --write` to record the operator-supplied correction.";

    public static NotifyTopologyResolution Resolve(string routingRoot, string team) =>
        Resolve(routingRoot, domain: null, team);

    public static NotifyTopologyResolution Resolve(string routingRoot, string? domain, string team)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            var legacyOnlyPath = ResolvePath(routingRoot);
            if (File.Exists(legacyOnlyPath))
            {
                return ResolveFromPath(legacyOnlyPath, null, team, requireIdentity: false);
            }

            var matches = FindNewTopologyPaths(routingRoot, team).ToArray();
            return matches.Length == 1
                ? ResolveFromPath(matches[0], expectedDomain: null, team, requireIdentity: false)
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

        var legacyPath = ResolvePath(routingRoot);
        if (!File.Exists(newPath))
        {
            var legacy = ResolveFromPath(legacyPath, null, team, requireIdentity: false);
            if (!legacy.Resolved)
            {
                return Failure(
                    "topology-missing",
                    $"Recorded role topology for domain '{domain}' team '{team}' was not found at '{newPath}'. "
                    + $"Provision and record the team's workspace, roles, residences, panes/readers, then retry "
                    + $"notify. {TopologyRemedy(team)}");
            }

            return legacy with
            {
                Warnings =
                [
                    $"Deprecated topology compatibility read from '{legacyPath}'; run "
                    + $"`intent-cli session-layer topology record --team {team} ... --write` to record this "
                    + "machine's topology at its per-team local path.",
                ],
                Summary = legacy.Summary + " Deprecated legacy topology compatibility read; re-record with "
                    + $"`intent-cli session-layer topology record --team {team} ... --write`.",
            };
        }

        var current = ResolveFromPath(newPath, domain, team, requireIdentity: true);
        if (!current.Resolved || !File.Exists(legacyPath))
        {
            return current;
        }

        var legacyForComparison = ResolveFromPath(legacyPath, null, team, requireIdentity: false);
        if (!legacyForComparison.Resolved || !Equivalent(current.Topology!, legacyForComparison.Topology!))
        {
            return Failure(
                "topology-location-conflict",
                $"Topology records for domain '{domain}' team '{team}' disagree between new path '{newPath}' and "
                + $"legacy path '{legacyPath}'. Refusing to prefer either machine topology. "
                + TopologyRemedy(team));
        }

        return current;
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

                roles.Add(property.Name, new NotifyRecordedRole(
                    resident,
                    ReadString(property.Value, "workspace_id"),
                    ReadString(property.Value, "pane_id"),
                    ReadString(property.Value, "reader"),
                    ReadString(property.Value, "cwd"),
                    ReadString(property.Value, "kind"),
                    ReadString(property.Value, "frontend")));
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

            return new NotifyTopologyResolution
            {
                Resolved = true,
                Topology = new NotifyTeamTopology(path, workspaceId, roles),
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
        if (!topology.Roles.TryGetValue(role, out var record))
        {
            return DeliveryFailure(
                role,
                "unknown-role",
                $"Recorded role topology '{topology.SourcePath}' does not contain logical role '{role}'.");
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

            return new NotifyRoleDeliveryResolution
            {
                Resolved = true,
                Role = role,
                Resident = record.Resident,
                TargetKind = "reader",
                Target = readerPath,
                Summary = $"Resolved external logical role '{role}' to recorded reader '{readerPath}'.",
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
            Resident = record.Resident,
            TargetKind = "pane",
            Target = record.PaneId,
            Summary = $"Resolved herdr logical role '{role}' to recorded pane '{record.PaneId}' in workspace "
                + $"'{topology.WorkspaceId}'.",
        };
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
        if (!File.Exists(path) && !string.IsNullOrWhiteSpace(domain) && File.Exists(ResolvePath(routingRoot)))
        {
            path = ResolvePath(routingRoot);
        }
        var warnings = !string.IsNullOrWhiteSpace(domain)
            && string.Equals(path, ResolvePath(routingRoot), StringComparison.Ordinal)
            && File.Exists(path)
                ? new[]
                {
                    $"Deprecated topology compatibility read from '{path}'; run "
                    + $"`intent-cli session-layer topology record --team {team} ... --write` to re-record this "
                    + "machine's topology at its per-team local path.",
                }
                : [];

        if (!File.Exists(path))
        {
            var resolution = Resolve(routingRoot, domain, team);
            findings.Add(Finding("<topology>", "file", resolution.Cause!, resolution.Summary));
            return Validation(team, path, findings, warnings);
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
                    return Validation(team, path, findings, warnings);
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
                return Validation(team, path, findings, warnings);
            }

            var rolesElement = teamElement.TryGetProperty("roles", out var nestedRoles)
                ? nestedRoles
                : teamElement;
            if (rolesElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(Finding("<topology>", "roles", "topology-invalid",
                    $"Team '{team}' has no object-valued roles field."));
                return Validation(team, path, findings, warnings);
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

            if (string.IsNullOrWhiteSpace(teamWorkspaceId))
            {
                var inferred = InferConsistentWorkspace(rolesElement);
                if (string.IsNullOrWhiteSpace(inferred))
                {
                    findings.Add(Finding("<topology>", "workspace_id", "topology-invalid",
                        $"Team '{team}' has no unambiguous field 'workspace_id'."));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            findings.Add(Finding("<topology>", "file", "topology-unreadable",
                $"Topology file '{path}' is unreadable: {exception.Message}"));
        }

        return Validation(team, path, findings, warnings);
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
        "schema_version" or "team" or "workspace" or "workspace_id" or "tab_id" or "updated_at" or "roles";

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
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
            && Equals(entry.Value, other));

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

    private static SessionLayerTopologyFinding Finding(
        string role,
        string field,
        string cause,
        string message) => new(role, field, cause, message);

    private static SessionLayerTopologyValidation Validation(
        string team,
        string path,
        IReadOnlyList<SessionLayerTopologyFinding> findings,
        IReadOnlyList<string>? warnings = null) => new()
        {
            Valid = findings.Count == 0,
            Team = team,
            SourcePath = path,
            Findings = findings,
            Warnings = warnings ?? [],
        };
}
