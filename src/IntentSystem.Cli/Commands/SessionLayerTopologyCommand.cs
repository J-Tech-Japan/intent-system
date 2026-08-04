using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G592: canonical writer and read-only checks for the delivery topology that
/// notify already consumes. Values always come from the operator; this surface
/// never queries herdr, guesses an id, provisions a pane, or repairs a conflict.
/// </summary>
internal static class SessionLayerTopologyCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string Usage =
        "Usage: intent-cli session-layer topology record|show|validate [options]";
    private const string RecordUsage =
        "Usage: intent-cli session-layer topology record --domain <name> --team <name> --role <name> --resident herdr "
        + "--workspace-id <id> --pane-id <id> --cwd <path> [--kind <kind>] [--dry-run|--write] "
        + "[--format markdown|json]\n"
        + "   or: intent-cli session-layer topology record --domain <name> --team <name> --role <name> --resident external "
        + "--reader <routing-root-relative-path> [--frontend <name>] [--dry-run|--write] "
        + "[--format markdown|json]";
    private const string ShowUsage =
        "Usage: intent-cli session-layer topology show --domain <name> --team <name> [--format markdown|json]";
    private const string ValidateUsage =
        "Usage: intent-cli session-layer topology validate --domain <name> --team <name> [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 0 || (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal)))
        {
            writer.WriteLine(Usage);
            writer.WriteLine(RecordUsage);
            writer.WriteLine(ShowUsage);
            writer.WriteLine(ValidateUsage);
            return args.Length == 0 ? 1 : 0;
        }

        return args[0] switch
        {
            "record" => ExecuteRecord(context, args[1..], writer),
            "show" => ExecuteShow(context, args[1..], writer),
            "validate" => ExecuteValidate(context, args[1..], writer),
            _ => UnknownSubcommand(args[0], writer),
        };
    }

    internal static int ExecuteValidate(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(ValidateUsage);
            return 0;
        }

        if (!TryParseReadArguments(args, out var domain, out var team, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(ValidateUsage);
            return 1;
        }

        var validation = NotifyRoleTopologyStore.Validate(context.RepoRoot, domain!, team!);
        var result = new SessionLayerTopologyValidationResult
        {
            Valid = validation.Valid,
            Team = team!,
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(domain!, team!),
            Findings = validation.Findings,
            Summary = (validation.Valid
                ? $"Recorded delivery topology for team '{team}' is valid."
                : $"Recorded delivery topology for team '{team}' is invalid with "
                    + $"{validation.Findings.Count} finding(s). No topology was changed.")
                + FormatWarnings(validation.Warnings),
        };

        EmitValidation(writer, format, result);
        return result.Valid ? 0 : 1;
    }

    internal static int ExecuteShow(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(ShowUsage);
            return 0;
        }

        if (!TryParseReadArguments(args, out var domain, out var team, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(ShowUsage);
            return 1;
        }

        var validation = NotifyRoleTopologyStore.Validate(context.RepoRoot, domain!, team!);
        if (!validation.Valid)
        {
            var invalid = new SessionLayerTopologyShowResult
            {
                Valid = false,
                Team = team!,
                WorkspaceId = null,
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(domain!, team!),
                Roles = [],
                Findings = validation.Findings,
                Summary = $"Recorded delivery topology for team '{team}' is invalid; no delivery targets were "
                    + "invented or resolved." + FormatWarnings(validation.Warnings),
            };
            EmitShow(writer, format, invalid);
            return 1;
        }

        var topologyResolution = NotifyRoleTopologyStore.Resolve(context.RepoRoot, domain!, team!);
        if (!topologyResolution.Resolved)
        {
            var invalid = new SessionLayerTopologyShowResult
            {
                Valid = false,
                Team = team!,
                WorkspaceId = null,
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(domain!, team!),
                Roles = [],
                Findings =
                [
                    new SessionLayerTopologyFinding(
                        "<topology>",
                        "resolution",
                        topologyResolution.Cause!,
                        topologyResolution.Summary),
                ],
                Summary = topologyResolution.Summary,
            };
            EmitShow(writer, format, invalid);
            return 1;
        }

        var topology = topologyResolution.Topology!;
        var roles = new List<SessionLayerTopologyShownRole>();
        foreach (var (role, record) in topology.Roles.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var target = NotifyRoleTopologyStore.ResolveDeliveryTarget(context.RepoRoot, topology, role);
            if (!target.Resolved)
            {
                var invalid = new SessionLayerTopologyShowResult
                {
                    Valid = false,
                    Team = team!,
                    WorkspaceId = topology.WorkspaceId,
                    RecordPath = NotifyRoleTopologyStore.RelativePathFor(domain!, team!),
                    Roles = roles,
                    Findings =
                    [
                        new SessionLayerTopologyFinding(
                            role,
                            target.TargetKind ?? "delivery_target",
                            target.Cause!,
                            target.Summary),
                    ],
                    Summary = target.Summary,
                };
                EmitShow(writer, format, invalid);
                return 1;
            }

            roles.Add(new SessionLayerTopologyShownRole
            {
                Role = role,
                Resident = record.Resident,
                WorkspaceId = record.WorkspaceId ?? topology.WorkspaceId,
                DeliveryTargetKind = target.TargetKind!,
                DeliveryTarget = target.Target!,
                Cwd = record.Cwd,
                Kind = record.Kind,
                Frontend = record.Frontend,
            });
        }

        var result = new SessionLayerTopologyShowResult
        {
            Valid = true,
            Team = team!,
            WorkspaceId = topology.WorkspaceId,
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(domain!, team!),
            Roles = roles,
            Findings = [],
            Summary = $"Resolved {roles.Count} recorded delivery target(s) for team '{team}' without sending."
                + FormatWarnings(topologyResolution.Warnings),
        };
        EmitShow(writer, format, result);
        return 0;
    }

    internal static int ExecuteRecord(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(RecordUsage);
            return 0;
        }

        if (!TryParseRecordArguments(args, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(RecordUsage);
            return 1;
        }

        var result = SessionLayerTopologyWriter.Record(context.RepoRoot, request!);
        EmitRecord(writer, request!.Format, result);
        return result.Conflict ? 1 : 0;
    }

    private static bool TryParseReadArguments(
        string[] args,
        out string? domain,
        out string? team,
        out string format,
        out string error)
    {
        domain = null;
        team = null;
        format = FormatMarkdown;
        error = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!TryReadValue(args, ref index, "--domain", out domain, out error))
                    {
                        return false;
                    }
                    break;
                case "--team":
                    if (!TryReadValue(args, ref index, "--team", out team, out error))
                    {
                        return false;
                    }
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, "--format", out var requestedFormat, out error)
                        || !IsKnownFormat(requestedFormat!))
                    {
                        error = string.IsNullOrEmpty(error)
                            ? $"--format must be 'markdown' or 'json' (got '{requestedFormat}')."
                            : error;
                        return false;
                    }
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team))
        {
            error = "--domain and --team are required.";
            return false;
        }

        return true;
    }

    private static bool TryParseRecordArguments(
        string[] args,
        out SessionLayerTopologyRecordRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;
        string? domain = null;
        string? team = null;
        string? role = null;
        string? resident = null;
        string? workspaceId = null;
        string? paneId = null;
        string? cwd = null;
        string? kind = null;
        string? reader = null;
        string? frontend = null;
        var write = false;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            switch (option)
            {
                case "--domain":
                    if (!TryReadValue(args, ref index, option, out domain, out error)) return false;
                    break;
                case "--team":
                    if (!TryReadValue(args, ref index, option, out team, out error)) return false;
                    break;
                case "--role":
                    if (!TryReadValue(args, ref index, option, out role, out error)) return false;
                    break;
                case "--resident":
                    if (!TryReadValue(args, ref index, option, out resident, out error)) return false;
                    break;
                case "--workspace-id":
                    if (!TryReadValue(args, ref index, option, out workspaceId, out error)) return false;
                    break;
                case "--pane-id":
                    if (!TryReadValue(args, ref index, option, out paneId, out error)) return false;
                    break;
                case "--cwd":
                    if (!TryReadValue(args, ref index, option, out cwd, out error)) return false;
                    break;
                case "--kind":
                    if (!TryReadValue(args, ref index, option, out kind, out error)) return false;
                    break;
                case "--reader":
                    if (!TryReadValue(args, ref index, option, out reader, out error)) return false;
                    break;
                case "--frontend":
                    if (!TryReadValue(args, ref index, option, out frontend, out error)) return false;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    write = false;
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, option, out var requestedFormat, out error)
                        || !IsKnownFormat(requestedFormat!))
                    {
                        error = string.IsNullOrEmpty(error)
                            ? $"--format must be 'markdown' or 'json' (got '{requestedFormat}')."
                            : error;
                        return false;
                    }
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{option}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team)
            || string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(resident))
        {
            error = "--domain, --team, --role, and --resident are required.";
            return false;
        }

        if (resident is not (NotifyRecordedRole.HerdrResident or NotifyRecordedRole.ExternalResident))
        {
            error = $"--resident must be 'herdr' or 'external' (got '{resident}').";
            return false;
        }

        if (string.Equals(resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(workspaceId)
                || string.IsNullOrWhiteSpace(paneId)
                || string.IsNullOrWhiteSpace(cwd))
            {
                error = "A herdr resident requires --workspace-id, --pane-id, and --cwd.";
                return false;
            }

            if (reader is not null || frontend is not null)
            {
                error = "A herdr resident does not accept --reader or --frontend.";
                return false;
            }

            var paneWorkspace = WorkspaceFromPane(paneId);
            if (paneWorkspace is not null
                && !string.Equals(paneWorkspace, workspaceId, StringComparison.Ordinal))
            {
                error = $"--pane-id '{paneId}' belongs to workspace '{paneWorkspace}', not --workspace-id "
                    + $"'{workspaceId}'. Refusing to record a cross-workspace mapping.";
                return false;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reader))
            {
                error = "An external resident requires --reader.";
                return false;
            }

            if (workspaceId is not null || paneId is not null || cwd is not null || kind is not null)
            {
                error = "An external resident does not accept --workspace-id, --pane-id, --cwd, or --kind.";
                return false;
            }
        }

        request = new SessionLayerTopologyRecordRequest
        {
            Domain = domain,
            Team = team,
            Role = role,
            Resident = resident,
            WorkspaceId = workspaceId,
            PaneId = paneId,
            Cwd = cwd,
            Kind = kind,
            Reader = reader,
            Frontend = frontend,
            Write = write,
            Format = format,
        };
        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index].Trim();
        error = string.Empty;
        return true;
    }

    private static string? WorkspaceFromPane(string paneId)
    {
        var separator = paneId.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 ? paneId[..separator] : null;
    }

    private static bool IsKnownFormat(string format) =>
        string.Equals(format, FormatJson, StringComparison.Ordinal)
            || string.Equals(format, FormatMarkdown, StringComparison.Ordinal);

    private static string FormatWarnings(IReadOnlyList<string> warnings) => warnings.Count == 0
        ? string.Empty
        : " " + string.Join(" ", warnings);

    private static bool IsHelp(string[] args) =>
        args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal);

    private static int UnknownSubcommand(string subcommand, TextWriter writer)
    {
        writer.WriteLine($"Unknown session-layer topology subcommand '{subcommand}'.");
        writer.WriteLine(Usage);
        return 1;
    }

    private static void EmitValidation(
        TextWriter writer,
        string format,
        SessionLayerTopologyValidationResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            WriteJson(writer, result);
            return;
        }

        writer.WriteLine($"# Session-layer topology validation — {result.Team}");
        writer.WriteLine($"valid: {result.Valid.ToString().ToLowerInvariant()}");
        writer.WriteLine($"record: `{result.RecordPath}`");
        writer.WriteLine(result.Summary);
        foreach (var finding in result.Findings)
        {
            writer.WriteLine($"- role={finding.Role}; field={finding.Field}; cause={finding.Cause}; {finding.Message}");
        }
    }

    private static void EmitShow(TextWriter writer, string format, SessionLayerTopologyShowResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            WriteJson(writer, result);
            return;
        }

        writer.WriteLine($"# Session-layer delivery topology — {result.Team}");
        writer.WriteLine($"valid: {result.Valid.ToString().ToLowerInvariant()}");
        writer.WriteLine($"workspace_id: {result.WorkspaceId ?? "unresolved"}");
        writer.WriteLine($"record: `{result.RecordPath}`");
        writer.WriteLine(result.Summary);
        foreach (var role in result.Roles)
        {
            writer.WriteLine($"- {role.Role}: resident={role.Resident}; delivery_target={role.DeliveryTargetKind}:"
                + $"{role.DeliveryTarget}");
        }
        foreach (var finding in result.Findings)
        {
            writer.WriteLine($"- role={finding.Role}; field={finding.Field}; cause={finding.Cause}; {finding.Message}");
        }
    }

    private static void EmitRecord(TextWriter writer, string format, SessionLayerTopologyRecordResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            WriteJson(writer, result);
            return;
        }

        writer.WriteLine($"# Session-layer topology record — {result.Team} / {result.Role}");
        writer.WriteLine($"mode: {result.Mode}");
        writer.WriteLine($"applied: {result.Applied.ToString().ToLowerInvariant()}");
        writer.WriteLine($"changed: {result.Changed.ToString().ToLowerInvariant()}");
        writer.WriteLine($"already_recorded: {result.AlreadyRecorded.ToString().ToLowerInvariant()}");
        writer.WriteLine($"conflict: {result.Conflict.ToString().ToLowerInvariant()}");
        writer.WriteLine(result.Summary);
    }

    private static void WriteJson<T>(TextWriter writer, T result)
    {
        writer.Write(JsonSerializer.Serialize(result, JsonOptions));
        writer.WriteLine();
    }
}

internal static class SessionLayerTopologyWriter
{
    private static readonly JsonSerializerOptions FileJsonOptions = new() { WriteIndented = true };

    private static readonly string[] KnownRoleFields =
    [
        "resident", "workspace_id", "pane_id", "cwd", "kind", "reader", "frontend",
    ];

    public static SessionLayerTopologyRecordResult Record(
        string routingRoot,
        SessionLayerTopologyRecordRequest request)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, request.Domain, request.Team);
        if (string.Equals(request.Resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal)
            && !NotifyRoleTopologyStore.TryResolveReaderPath(
                routingRoot,
                request.Reader,
                out _,
                out var readerError))
        {
            return Conflict(request, path, $"External role '{request.Role}' field 'reader' is unsafe: {readerError}");
        }

        JsonObject root;
        try
        {
            root = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new JsonException("the root is not a JSON object")
                : new JsonObject
                {
                    ["domain"] = request.Domain,
                    ["team"] = request.Team,
                    ["roles"] = new JsonObject(),
                };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Conflict(
                request,
                path,
                $"Topology file '{path}' is unreadable: {exception.Message} Refusing to overwrite it.");
        }

        var recordedDomain = ReadString(root, "domain");
        if (!string.Equals(recordedDomain, request.Domain, StringComparison.Ordinal))
        {
            return Conflict(request, path,
                $"Topology file '{path}' identifies domain '{recordedDomain ?? "missing"}', not requested domain "
                + $"'{request.Domain}'. Refusing to overwrite a copied or misplaced machine record.");
        }

        if (!TrySelectTeamForWrite(root, request.Team, out var team, out var selectError))
        {
            return Conflict(request, path, selectError);
        }

        if (!TrySelectRolesForWrite(team!, out var roles, out var rolesError))
        {
            return Conflict(request, path, rolesError);
        }

        if (string.Equals(request.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
        {
            var recordedWorkspace = ReadTeamWorkspace(team!);
            if (!string.IsNullOrWhiteSpace(recordedWorkspace)
                && !string.Equals(recordedWorkspace, request.WorkspaceId, StringComparison.Ordinal))
            {
                return Conflict(
                    request,
                    path,
                    $"Team '{request.Team}' already records workspace_id '{recordedWorkspace}', but role "
                    + $"'{request.Role}' requested '{request.WorkspaceId}'. Refusing to repair the conflict.");
            }

            if (string.IsNullOrWhiteSpace(recordedWorkspace))
            {
                team!["workspace_id"] = request.WorkspaceId;
            }
        }

        var requestedRole = CreateRole(request);
        if (roles!.TryGetPropertyValue(request.Role, out var existingNode))
        {
            if (existingNode is not JsonObject existingRole)
            {
                return Conflict(
                    request,
                    path,
                    $"Role '{request.Role}' already exists but is not an object. Refusing to replace it.");
            }

            if (!RoleMatches(existingRole, requestedRole))
            {
                return Conflict(
                    request,
                    path,
                    $"Role '{request.Role}' already has a conflicting recorded shape. Refusing to silently "
                    + "repair or replace it; validate and record an operator-approved non-conflicting role.");
            }

            return new SessionLayerTopologyRecordResult
            {
                Team = request.Team,
                Role = request.Role,
                Resident = request.Resident,
                Mode = request.Write ? "write" : "dry-run",
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
                Applied = false,
                Changed = false,
                AlreadyRecorded = true,
                Conflict = false,
                Summary = $"Role '{request.Role}' for team '{request.Team}' already exactly matches; idempotent no-op.",
            };
        }

        roles[request.Role] = requestedRole;
        var applied = false;
        if (request.Write)
        {
            try
            {
                EnsureLocalIgnore(routingRoot);
                WriteAtomically(path, root.ToJsonString(FileJsonOptions) + Environment.NewLine);
                applied = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Conflict(request, path, $"Topology file '{path}' could not be written: {exception.Message}");
            }
        }

        return new SessionLayerTopologyRecordResult
        {
            Team = request.Team,
            Role = request.Role,
            Resident = request.Resident,
            Mode = request.Write ? "write" : "dry-run",
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
            Applied = applied,
            Changed = true,
            AlreadyRecorded = false,
            Conflict = false,
            Summary = request.Write
                ? $"Recorded operator-supplied role '{request.Role}' for team '{request.Team}'."
                : $"Dry-run: would record operator-supplied role '{request.Role}' for team '{request.Team}'.",
        };
    }

    private static bool TrySelectTeamForWrite(
        JsonObject root,
        string teamName,
        out JsonObject? team,
        out string error)
    {
        team = null;
        error = string.Empty;
        if (root.Count == 0)
        {
            team = new JsonObject { ["roles"] = new JsonObject() };
            root["team"] = teamName;
            root["roles"] = team["roles"];
            return true;
        }

        if (root.TryGetPropertyValue("teams", out var teamsNode))
        {
            if (teamsNode is not JsonObject teams)
            {
                error = "Topology field 'teams' is not an object. Refusing to overwrite it.";
                return false;
            }

            if (teams.TryGetPropertyValue(teamName, out var teamNode))
            {
                if (teamNode is not JsonObject existingTeam)
                {
                    error = $"Topology team '{teamName}' is not an object. Refusing to overwrite it.";
                    return false;
                }
                team = existingTeam;
            }
            else
            {
                team = new JsonObject { ["roles"] = new JsonObject() };
                teams[teamName] = team;
            }
            return true;
        }

        var recordedTeam = ReadString(root, "team");
        if (recordedTeam is not null)
        {
            if (!string.Equals(recordedTeam, teamName, StringComparison.Ordinal))
            {
                error = $"Topology records team '{recordedTeam}', not requested team '{teamName}'. Refusing to "
                    + "reshape it silently.";
                return false;
            }
            team = root;
            return true;
        }

        if (root.TryGetPropertyValue(teamName, out var keyedTeamNode))
        {
            if (keyedTeamNode is not JsonObject keyedTeam)
            {
                error = $"Topology team '{teamName}' is not an object. Refusing to overwrite it.";
                return false;
            }
            team = keyedTeam;
            return true;
        }

        error = $"Topology does not identify team '{teamName}'. Refusing to reshape an ambiguous record silently.";
        return false;
    }

    private static bool TrySelectRolesForWrite(
        JsonObject team,
        out JsonObject? roles,
        out string error)
    {
        roles = null;
        error = string.Empty;
        if (team.TryGetPropertyValue("roles", out var rolesNode))
        {
            if (rolesNode is not JsonObject rolesObject)
            {
                error = "Topology field 'roles' is not an object. Refusing to overwrite it.";
                return false;
            }
            roles = rolesObject;
            return true;
        }

        var containsDirectRoles = team.Any(property =>
            !IsEnvelopeProperty(property.Key) && property.Value is JsonObject);
        if (containsDirectRoles)
        {
            roles = team;
            return true;
        }

        roles = new JsonObject();
        team["roles"] = roles;
        return true;
    }

    private static JsonObject CreateRole(SessionLayerTopologyRecordRequest request)
    {
        var role = new JsonObject { ["resident"] = request.Resident };
        Add(role, "workspace_id", request.WorkspaceId);
        Add(role, "pane_id", request.PaneId);
        Add(role, "cwd", request.Cwd);
        Add(role, "kind", request.Kind);
        Add(role, "reader", request.Reader);
        Add(role, "frontend", request.Frontend);
        return role;
    }

    private static bool RoleMatches(JsonObject existing, JsonObject requested) =>
        KnownRoleFields.All(field =>
            string.Equals(ReadString(existing, field), ReadString(requested, field), StringComparison.Ordinal));

    private static string? ReadTeamWorkspace(JsonObject team)
    {
        var direct = ReadString(team, "workspace_id");
        if (direct is not null)
        {
            return direct;
        }

        if (!team.TryGetPropertyValue("workspace", out var workspace))
        {
            return null;
        }

        return workspace switch
        {
            JsonValue value when value.TryGetValue<string>(out var workspaceId) => workspaceId,
            JsonObject workspaceObject => ReadString(workspaceObject, "workspace_id")
                ?? ReadString(workspaceObject, "id"),
            _ => null,
        };
    }

    private static string? ReadString(JsonObject value, string property) =>
        value.TryGetPropertyValue(property, out var node)
        && node is JsonValue jsonValue
        && jsonValue.TryGetValue<string>(out var text)
            ? text
            : null;

    private static void Add(JsonObject target, string property, string? value)
    {
        if (value is not null)
        {
            target[property] = value;
        }
    }

    private static bool IsEnvelopeProperty(string property) => property is
        "schema_version" or "team" or "workspace" or "workspace_id" or "tab_id" or "updated_at" or "roles";

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureLocalIgnore(string routingRoot)
    {
        var path = NotifyRoleTopologyStore.ResolveLocalIgnorePath(routingRoot);
        var content = "*" + Environment.NewLine;
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        WriteAtomically(path, content);
    }

    private static SessionLayerTopologyRecordResult Conflict(
        SessionLayerTopologyRecordRequest request,
        string path,
        string summary) => new()
        {
            Team = request.Team,
            Role = request.Role,
            Resident = request.Resident,
            Mode = request.Write ? "write" : "dry-run",
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
            Applied = false,
            Changed = false,
            AlreadyRecorded = false,
            Conflict = true,
            Summary = summary,
        };
}

internal sealed record SessionLayerTopologyRecordRequest
{
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string Role { get; init; }
    public required string Resident { get; init; }
    public string? WorkspaceId { get; init; }
    public string? PaneId { get; init; }
    public string? Cwd { get; init; }
    public string? Kind { get; init; }
    public string? Reader { get; init; }
    public string? Frontend { get; init; }
    public required bool Write { get; init; }
    public required string Format { get; init; }
}

internal sealed record SessionLayerTopologyRecordResult
{
    public required string Team { get; init; }
    public required string Role { get; init; }
    public required string Resident { get; init; }
    public required string Mode { get; init; }
    public required string RecordPath { get; init; }
    public required bool Applied { get; init; }
    public required bool Changed { get; init; }
    public required bool AlreadyRecorded { get; init; }
    public required bool Conflict { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyValidationResult
{
    public required bool Valid { get; init; }
    public required string Team { get; init; }
    public required string RecordPath { get; init; }
    public required IReadOnlyList<SessionLayerTopologyFinding> Findings { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyShowResult
{
    public required bool Valid { get; init; }
    public required string Team { get; init; }
    public required string? WorkspaceId { get; init; }
    public required string RecordPath { get; init; }
    public required IReadOnlyList<SessionLayerTopologyShownRole> Roles { get; init; }
    public required IReadOnlyList<SessionLayerTopologyFinding> Findings { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyShownRole
{
    public required string Role { get; init; }
    public required string Resident { get; init; }
    public required string WorkspaceId { get; init; }
    public required string DeliveryTargetKind { get; init; }
    public required string DeliveryTarget { get; init; }
    public string? Cwd { get; init; }
    public string? Kind { get; init; }
    public string? Frontend { get; init; }
}
