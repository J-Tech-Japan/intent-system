using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyRecordedRole(
    string Resident,
    string? WorkspaceId,
    string? PaneId,
    string? Reader)
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

/// <summary>
/// Reads the operator-owned herdr logical-role topology. This is deliberately a
/// read-only consumer: provisioning owns the mapping, while notify refuses
/// missing, ambiguous, or unsafe records instead of inventing a destination.
/// </summary>
internal static class NotifyRoleTopologyStore
{
    public const string RelativePath = ".intent-cli/role-pane-mapping.json";

    public static NotifyTopologyResolution Resolve(string routingRoot, string team)
    {
        var path = Path.GetFullPath(Path.Combine(
            routingRoot,
            RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(path))
        {
            return Failure(
                "topology-missing",
                $"Recorded role topology for team '{team}' was not found at '{path}'. Provision and record the "
                + "team's workspace, roles, residences, panes/readers, then retry notify.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!TrySelectTeam(document.RootElement, team, out var teamElement, out var teamError))
            {
                return Failure("topology-team-missing", $"Recorded role topology '{path}' {teamError}");
            }

            var rolesElement = teamElement.TryGetProperty("roles", out var nestedRoles)
                ? nestedRoles
                : teamElement;
            if (rolesElement.ValueKind != JsonValueKind.Object)
            {
                return Failure(
                    "topology-invalid",
                    $"Recorded role topology '{path}' for team '{team}' has no object-valued 'roles'. Record the "
                    + "team roster before retrying notify.");
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
                        + "the role record before retrying notify.");
                }

                var resident = ReadString(property.Value, "resident");
                if (resident is not (NotifyRecordedRole.HerdrResident or NotifyRecordedRole.ExternalResident))
                {
                    return Failure(
                        "topology-invalid",
                        $"Recorded role '{property.Name}' for team '{team}' in '{path}' has unsupported resident "
                        + $"'{resident ?? "missing"}'. Use 'herdr' or 'external' and retry.");
                }

                roles.Add(property.Name, new NotifyRecordedRole(
                    resident,
                    ReadString(property.Value, "workspace_id"),
                    ReadString(property.Value, "pane_id"),
                    ReadString(property.Value, "reader")));
            }

            if (roles.Count == 0)
            {
                return Failure(
                    "topology-invalid",
                    $"Recorded role topology '{path}' for team '{team}' contains no roles. Record the team roster "
                    + "before retrying notify.");
            }

            var workspaceId = ReadString(teamElement, "workspace_id")
                ?? ReadNestedWorkspaceId(teamElement)
                ?? ConsistentWorkspaceFromRoles(roles.Values);
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return Failure(
                    "topology-invalid",
                    $"Recorded role topology '{path}' for team '{team}' has no unambiguous workspace_id. Record "
                    + "the team's workspace explicitly before retrying notify.");
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
                        + $"workspace '{workspaceId}' in '{path}'. Repair the team-scoped mapping before retrying.");
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
                + "Repair the file and retry notify.");
        }
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
}
