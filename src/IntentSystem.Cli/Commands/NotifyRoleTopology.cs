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
}

/// <summary>
/// Reads the operator-supplied herdr logical-role topology. G592 adds the
/// canonical writer beside this shared resolver; notify remains a read-only
/// consumer and refuses missing, ambiguous, or unsafe records rather than
/// inventing a destination.
/// </summary>
internal static class NotifyRoleTopologyStore
{
    public const string RelativePath = ".intent-cli/role-pane-mapping.json";

    public static string ResolvePath(string routingRoot) => Path.GetFullPath(Path.Combine(
        routingRoot,
        RelativePath.Replace('/', Path.DirectorySeparatorChar)));

    public static string TopologyRemedy(string team) =>
        $"Run `intent-cli session-layer topology validate --team {team} --format json`, then use "
        + "`session-layer topology record ... --write` to record the operator-supplied correction.";

    public static NotifyTopologyResolution Resolve(string routingRoot, string team)
    {
        var path = ResolvePath(routingRoot);
        if (!File.Exists(path))
        {
            return Failure(
                "topology-missing",
                $"Recorded role topology for team '{team}' was not found at '{path}'. Provision and record the "
                + $"team's workspace, roles, residences, panes/readers, then retry notify. {TopologyRemedy(team)}");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!TrySelectTeam(document.RootElement, team, out var teamElement, out var teamError))
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
    public static SessionLayerTopologyValidation Validate(string routingRoot, string team)
    {
        var path = ResolvePath(routingRoot);
        var findings = new List<SessionLayerTopologyFinding>();
        if (!File.Exists(path))
        {
            findings.Add(Finding("<topology>", "file", "topology-missing",
                $"Topology file '{path}' is absent."));
            return Validation(team, path, findings);
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

        return Validation(team, path, findings);
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
        IReadOnlyList<SessionLayerTopologyFinding> findings) => new()
    {
        Valid = findings.Count == 0,
        Team = team,
        SourcePath = path,
        Findings = findings,
    };
}
