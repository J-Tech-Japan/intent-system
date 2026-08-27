using System.Security.Cryptography;
using System.Text;
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
        "Usage: intent-cli session-layer topology record|record-host-state|record-profile|show|validate|move|update-kind|update-field|retire-legacy [options]";
    private const string RecordUsage =
        "Usage: intent-cli session-layer topology record --domain <name> --team <name> --role <name> --resident herdr "
        + "--workspace-id <id> --pane-id <id> --cwd <path> [--kind <kind>] [--delivery-method inline|file-backed] "
        + "[--model <text>] [--reasoning-effort <text>] [--dry-run|--write] "
        + "[--format markdown|json]\n"
        + "   or: intent-cli session-layer topology record --domain <name> --team <name> --role <name> --resident external "
        + "--reader <routing-root-relative-path> [--frontend <name>] [--model <text>] [--reasoning-effort <text>] "
        + "[--dry-run|--write] "
        + "[--format markdown|json]\n"
        + "   Heartbeat coordination accepts recorded role 'orchestration' or the alias 'orchestrator'; existing "
        + "records under either name need no rename or migration.";
    private const string RecordHostStateUsage =
        "Usage: intent-cli session-layer topology record-host-state --domain <name> --team <name> --role <name> "
        + "--envelope <named-host-state-envelope> [--dry-run|--write] [--format markdown|json]";
    private const string ShowUsage =
        "Usage: intent-cli session-layer topology show --domain <name> --team <name> [--format markdown|json]";
    private const string ValidateUsage =
        "Usage: intent-cli session-layer topology validate --domain <name> --team <name> [--live] [--format markdown|json]";
    private const string MoveUsage =
        "Usage: intent-cli session-layer topology move --domain <name> --team <name> --workspace-id <new-id> "
        + "[--pane-map <old-pane>=<new-pane>]... [--current-digest <digest>] [--dry-run|--write] [--format json]";
    private const string UpdateKindUsage =
        "Usage: intent-cli session-layer topology update-kind --domain <name> --team <name> --role <name> "
        + "--current-kind <kind> --new-kind <kind> --confirm-update-kind [--dry-run|--write] [--format json]";
    private const string UpdateFieldUsage =
        "Usage: intent-cli session-layer topology update-field --domain <name> --team <name> --role <name> "
        + "--field delivery_method --current <value|absent> --new <value> --confirm-update-field "
        + "[--dry-run|--write] [--format json]";
    private const string RetireLegacyUsage =
        "Usage: intent-cli session-layer topology retire-legacy --domain <name> --team <name> "
        + "--evidence <named-fleet-migration-evidence> --confirm-retire-legacy --write [--format json]";
    private const string RecordProfileUsage =
        "Usage: intent-cli session-layer topology record-profile --domain <name> --team <name> "
        + "--profile-name <name> --kind <kind> --sandbox-mode <mode> --approval-mode <mode> "
        + "--roots-policy <policy> [--writable-root <path>]... --network-access <value> "
        + "--transport-mode <mode> --evidence <text> [--permission-option <flag>]... "
        + "[--network-url <url>]... [--recorded-at <timestamp>] [--role <role> [--role-override]] "
        + "--current-digest <digest|absent> --confirm-record-profile [--dry-run|--write] --format json";

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
            writer.WriteLine(RecordHostStateUsage);
            writer.WriteLine(ShowUsage);
            writer.WriteLine(ValidateUsage);
            writer.WriteLine(MoveUsage);
            writer.WriteLine(UpdateKindUsage);
            writer.WriteLine(UpdateFieldUsage);
            writer.WriteLine(RetireLegacyUsage);
            writer.WriteLine(RecordProfileUsage);
            return args.Length == 0 ? 1 : 0;
        }

        if (TryReadOption(args, "--domain", out var requestedDomain)
            && !string.IsNullOrWhiteSpace(requestedDomain))
        {
            var requestedTeam = TryReadOption(args, "--team", out var parsedTeam) && !string.IsNullOrWhiteSpace(parsedTeam)
                ? parsedTeam
                : null;
            try
            {
                if (TeamModeStore.Resolve(context.RepoRoot, requestedDomain!, requestedTeam).IsAuthoringOnly)
                {
                    return EmitNotApplicable(args[0], args, requestedDomain!, requestedTeam, writer);
                }
            }
            catch (InvalidOperationException exception)
            {
                writer.WriteLine($"team-mode-unreadable: {exception.Message}");
                return 1;
            }
        }

        return args[0] switch
        {
            "record" => ExecuteRecord(context, args[1..], writer),
            "record-host-state" => ExecuteRecordHostState(context, args[1..], writer),
            "record-profile" => ExecuteRecordProfile(context, args[1..], writer),
            "show" => ExecuteShow(context, args[1..], writer),
            "validate" => ExecuteValidate(context, args[1..], writer),
            "move" => ExecuteMove(context, args[1..], writer),
            "update-kind" => ExecuteUpdateKind(context, args[1..], writer),
            "update-field" => ExecuteUpdateField(context, args[1..], writer),
            "retire-legacy" => ExecuteRetireLegacy(context, args[1..], writer),
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

        if (!TryParseReadArguments(
                args,
                out var domain,
                out var team,
                out var format,
                out var error,
                out var live,
                allowLive: true))
        {
            writer.WriteLine(error);
            writer.WriteLine(ValidateUsage);
            return 1;
        }

        var validation = NotifyRoleTopologyStore.Validate(context.RepoRoot, domain!, team!);
        if (live)
        {
            validation = HerdrPaneLabelValidation.Apply(context.RepoRoot, domain!, team!, validation);
        }

        var informationalCount = validation.Findings.Count(finding => finding.IsInformational);
        var result = new SessionLayerTopologyValidationResult
        {
            Valid = validation.Valid,
                Team = team!,
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(domain!, team!),
                Findings = validation.Findings,
                RoleDeclarations = validation.RoleDeclarations,
                HostState = validation.HostState,
                Summary = (validation.Valid
                ? $"Recorded delivery topology for team '{team}' is valid."
                : $"Recorded delivery topology for team '{team}' is invalid with "
                    + $"{validation.Findings.Count} finding(s). No topology was changed.")
                + (informationalCount == 0
                    ? string.Empty
                    : $" {informationalCount} informational finding(s) do not affect valid.")
                + $" {SessionLayerTopologyDeclaredValueRules.OperatorDeclarationSummary}"
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

        if (!TryParseReadArguments(
                args,
                out var domain,
                out var team,
                out var format,
                out var error,
                out _,
                allowLive: false))
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
                EnvelopeProfiles = [],
                HostState = validation.HostState,
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
                EnvelopeProfiles = [],
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
                    EnvelopeProfiles = topology.EnvelopeProfiles.Values
                        .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(profile => new SessionLayerTopologyShownProfile
                        {
                            Name = profile.Name,
                            Kind = profile.Kind,
                            Digest = profile.Digest,
                            RecordedAt = profile.RecordedAt,
                        })
                        .ToArray(),
                    HostState = topology.HostState,
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
                Model = record.Model,
                ReasoningEffort = record.ReasoningEffort,
            });
        }

        var result = new SessionLayerTopologyShowResult
        {
            Valid = true,
            Team = team!,
            WorkspaceId = topology.WorkspaceId,
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(domain!, team!),
            Roles = roles,
            EnvelopeProfiles = topology.EnvelopeProfiles.Values
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(profile => new SessionLayerTopologyShownProfile
                {
                    Name = profile.Name,
                    Kind = profile.Kind,
                    Digest = profile.Digest,
                    RecordedAt = profile.RecordedAt,
                })
                .ToArray(),
            HostState = topology.HostState,
            Findings = [],
            Summary = $"Resolved {roles.Count} recorded delivery target(s) for team '{team}' without sending."
                + $" {SessionLayerTopologyDeclaredValueRules.OperatorDeclarationSummary}"
                + FormatWarnings(topologyResolution.Warnings),
        };
        EmitShow(writer, format, result);
        return 0;
    }

    internal static int ExecuteMove(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(MoveUsage);
            return 0;
        }

        if (!TryParseMoveArguments(args, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(MoveUsage);
            return 1;
        }

        var result = SessionLayerTopologyWriter.Move(context.RepoRoot, request!);
        WriteJson(writer, result);
        return result.Conflict ? 1 : 0;
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

    internal static int ExecuteRecordHostState(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(RecordHostStateUsage);
            return 0;
        }

        if (!TryParseRecordHostStateArguments(args, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(RecordHostStateUsage);
            return 1;
        }

        var result = SessionLayerTopologyWriter.RecordHostState(context.RepoRoot, request!);
        if (string.Equals(request!.Format, FormatJson, StringComparison.Ordinal))
        {
            WriteJson(writer, result);
        }
        else
        {
            writer.WriteLine($"# Session-layer host-state declaration — {result.Team}");
            writer.WriteLine($"mode: {result.Mode}");
            writer.WriteLine($"applied: {result.Applied.ToString().ToLowerInvariant()}");
            writer.WriteLine($"changed: {result.Changed.ToString().ToLowerInvariant()}");
            writer.WriteLine($"conflict: {result.Conflict.ToString().ToLowerInvariant()}");
            writer.WriteLine(result.Summary);
        }

        return result.Conflict ? 1 : 0;
    }

    internal static int ExecuteRecordProfile(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(RecordProfileUsage);
            return 0;
        }

        if (!TryParseRecordProfileArguments(args, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(RecordProfileUsage);
            return 1;
        }

        var result = SessionLayerTopologyWriter.RecordProfile(context.RepoRoot, request!);
        WriteJson(writer, result);
        return result.Conflict ? 1 : 0;
    }

    internal static int ExecuteUpdateKind(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(UpdateKindUsage);
            return 0;
        }

        if (!TryParseUpdateKindArguments(args, out var request, out var error))
        {
            writer.WriteLine(error); writer.WriteLine(UpdateKindUsage); return 1;
        }
        var result = SessionLayerTopologyWriter.UpdateKind(context.RepoRoot, request!);
        WriteJson(writer, result);
        return result.Conflict ? 1 : 0;
    }

    internal static int ExecuteUpdateField(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(UpdateFieldUsage);
            return 0;
        }

        if (!TryParseUpdateFieldArguments(args, out var request, out var error))
        {
            writer.WriteLine(error); writer.WriteLine(UpdateFieldUsage); return 1;
        }
        var result = SessionLayerTopologyWriter.UpdateField(context.RepoRoot, request!);
        WriteJson(writer, result);
        return result.Conflict ? 1 : 0;
    }

    internal static int ExecuteRetireLegacy(CliContext context, string[] args, TextWriter writer)
    {
        if (IsHelp(args))
        {
            writer.WriteLine(RetireLegacyUsage);
            return 0;
        }

        if (!TryParseRetireLegacyArguments(args, out var request, out var error))
        {
            writer.WriteLine(error); writer.WriteLine(RetireLegacyUsage); return 1;
        }
        var result = SessionLayerTopologyWriter.RetireLegacy(context.RepoRoot, request!);
        WriteJson(writer, result);
        return result.Conflict ? 1 : 0;
    }

    private static bool TryParseRecordProfileArguments(
        string[] args,
        out SessionLayerTopologyProfileRecordRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var writableRoots = new List<string>();
        var permissionOptions = new List<string>();
        var networkUrls = new List<string>();
        var confirm = false;
        var roleOverride = false;
        var requestedWrite = false;
        var requestedDryRun = false;
        var format = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            switch (option)
            {
                case "--confirm-record-profile":
                    confirm = true;
                    break;
                case "--role-override":
                    roleOverride = true;
                    break;
                case "--write":
                    requestedWrite = true;
                    break;
                case "--dry-run":
                    requestedDryRun = true;
                    break;
                case "--writable-root":
                    if (!TryReadValue(args, ref index, option, out var writableRoot, out error)) return false;
                    writableRoots.Add(writableRoot!);
                    break;
                case "--permission-option":
                    if (!TryReadValue(args, ref index, option, out var permissionOption, out error)) return false;
                    permissionOptions.Add(permissionOption!);
                    break;
                case "--network-url":
                    if (!TryReadValue(args, ref index, option, out var networkUrl, out error)) return false;
                    networkUrls.Add(networkUrl!);
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, option, out var requestedFormat, out error)) return false;
                    format = requestedFormat!;
                    break;
                case "--domain" or "--team" or "--profile-name" or "--kind" or "--sandbox-mode"
                    or "--approval-mode" or "--roots-policy" or "--network-access" or "--transport-mode"
                    or "--evidence" or "--recorded-at" or "--role" or "--current-digest":
                    if (!TryReadValue(args, ref index, option, out var value, out error)) return false;
                    values[option] = value!;
                    break;
                default:
                    error = $"Unknown argument '{option}'.";
                    return false;
            }
        }

        var required = new[]
        {
            "--domain", "--team", "--profile-name", "--kind", "--sandbox-mode", "--approval-mode",
            "--roots-policy", "--network-access", "--transport-mode", "--evidence", "--current-digest",
        };
        if (!confirm || (!requestedWrite && !requestedDryRun) || !string.Equals(format, FormatJson, StringComparison.Ordinal)
            || required.Any(name => !values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value)))
        {
            error = "--domain, --team, --profile-name, --kind, --sandbox-mode, --approval-mode, --roots-policy, "
                + "--network-access, --transport-mode, --evidence, --current-digest, --confirm-record-profile, "
                + "--format json, and either --write or --dry-run are required.";
            return false;
        }

        if (values["--profile-name"].IndexOf('/') >= 0
            || values["--profile-name"].IndexOf('\\') >= 0
            || values["--profile-name"] is "." or "..")
        {
            error = "--profile-name must be a safe single profile name.";
            return false;
        }

        if (!DateTimeOffset.TryParse(values.GetValueOrDefault("--recorded-at") ?? DateTimeOffset.UtcNow.ToString("O"), out _))
        {
            error = "--recorded-at must be a valid timestamp.";
            return false;
        }

        if (roleOverride && !values.ContainsKey("--role"))
        {
            error = "--role-override requires --role.";
            return false;
        }

        if (!string.Equals(values["--current-digest"], "absent", StringComparison.OrdinalIgnoreCase)
            && values["--current-digest"].Length < 16)
        {
            error = "--current-digest must be 'absent' or the digest returned by a previous record-profile operation.";
            return false;
        }

        request = new SessionLayerTopologyProfileRecordRequest
        {
            Domain = values["--domain"],
            Team = values["--team"],
            ProfileName = values["--profile-name"],
            Kind = values["--kind"],
            SandboxMode = values["--sandbox-mode"],
            ApprovalMode = values["--approval-mode"],
            RootsPolicy = values["--roots-policy"],
            WritableRoots = writableRoots,
            NetworkAccess = values["--network-access"],
            TransportMode = values["--transport-mode"],
            Evidence = values["--evidence"],
            RecordedAt = values.GetValueOrDefault("--recorded-at") ?? DateTimeOffset.UtcNow.ToString("O"),
            PermissionOptions = permissionOptions,
            NetworkUrls = networkUrls,
            Role = values.GetValueOrDefault("--role"),
            RoleOverride = roleOverride,
            CurrentDigest = values["--current-digest"],
            Write = requestedWrite && !requestedDryRun,
        };
        return true;
    }

    private static bool TryParseUpdateKindArguments(string[] args, out SessionLayerTopologyKindUpdateRequest? request, out string error)
    {
        request = null; error = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var confirm = false; var requestedWrite = false; var requestedDryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--confirm-update-kind") { confirm = true; continue; }
            if (args[i] == "--write") { requestedWrite = true; continue; }
            if (args[i] == "--dry-run") { requestedDryRun = true; continue; }
            if (args[i] == "--format")
            {
                if (++i >= args.Length || !string.Equals(args[i], FormatJson, StringComparison.Ordinal))
                { error = "update-kind supports only '--format json'."; return false; }
                continue;
            }
            if (args[i] is not ("--domain" or "--team" or "--role" or "--current-kind" or "--new-kind") || i + 1 >= args.Length)
            { error = $"Unknown or incomplete argument '{args[i]}'."; return false; }
            values[args[i]] = args[++i];
        }
        if (!confirm || (!requestedWrite && !requestedDryRun) || values.Keys.Count != 5 || values.Values.Any(string.IsNullOrWhiteSpace))
        { error = "--domain, --team, --role, --current-kind, --new-kind, --confirm-update-kind, and either --write or --dry-run are required."; return false; }
        // A dry-run is an explicit non-mutating request, irrespective of flag order.
        var write = requestedWrite && !requestedDryRun;
        request = new(values["--domain"], values["--team"], values["--role"], values["--current-kind"], values["--new-kind"], write);
        return true;
    }

    private static bool TryParseUpdateFieldArguments(string[] args, out SessionLayerTopologyFieldUpdateRequest? request, out string error)
    {
        request = null; error = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var confirm = false; var requestedWrite = false; var requestedDryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--confirm-update-field") { confirm = true; continue; }
            if (args[i] == "--write") { requestedWrite = true; continue; }
            if (args[i] == "--dry-run") { requestedDryRun = true; continue; }
            if (args[i] == "--format")
            {
                if (++i >= args.Length || !string.Equals(args[i], FormatJson, StringComparison.Ordinal))
                { error = "update-field supports only '--format json'."; return false; }
                continue;
            }
            if (args[i] is not ("--domain" or "--team" or "--role" or "--field" or "--current" or "--new") || i + 1 >= args.Length)
            { error = $"Unknown or incomplete argument '{args[i]}'."; return false; }
            values[args[i]] = args[++i];
        }
        if (!confirm || (!requestedWrite && !requestedDryRun) || values.Keys.Count != 6 || values.Values.Any(string.IsNullOrWhiteSpace))
        { error = "--domain, --team, --role, --field, --current, --new, --confirm-update-field, and either --write or --dry-run are required."; return false; }
        // A dry-run is an explicit non-mutating request, irrespective of flag order.
        var write = requestedWrite && !requestedDryRun;
        request = new(values["--domain"], values["--team"], values["--role"], values["--field"], values["--current"], values["--new"], write);
        return true;
    }

    private static bool TryParseRetireLegacyArguments(string[] args, out SessionLayerTopologyLegacyRetireRequest? request, out string error)
    {
        request = null; error = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var confirm = false; var write = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--confirm-retire-legacy") { confirm = true; continue; }
            if (args[i] == "--write") { write = true; continue; }
            if (args[i] == "--format")
            {
                if (++i >= args.Length || !string.Equals(args[i], FormatJson, StringComparison.Ordinal))
                { error = "retire-legacy supports only '--format json'."; return false; }
                continue;
            }
            if (args[i] is not ("--domain" or "--team" or "--evidence") || i + 1 >= args.Length)
            { error = $"Unknown or incomplete argument '{args[i]}'."; return false; }
            values[args[i]] = args[++i];
        }
        if (!confirm || !write || values.Keys.Count != 3 || values.Values.Any(string.IsNullOrWhiteSpace))
        { error = "--domain, --team, --evidence, --confirm-retire-legacy, and --write are required."; return false; }
        request = new(values["--domain"], values["--team"], values["--evidence"]);
        return true;
    }

    private static bool TryParseReadArguments(
        string[] args,
        out string? domain,
        out string? team,
        out string format,
        out string error,
        out bool live,
        bool allowLive)
    {
        domain = null;
        team = null;
        format = FormatMarkdown;
        error = string.Empty;
        live = false;
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
                case "--live" when allowLive:
                    live = true;
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
        string? deliveryMethod = null;
        string? reader = null;
        string? frontend = null;
        string? model = null;
        string? reasoningEffort = null;
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
                case "--delivery-method":
                    if (!TryReadValue(args, ref index, option, out deliveryMethod, out error)) return false;
                    break;
                case "--reader":
                    if (!TryReadValue(args, ref index, option, out reader, out error)) return false;
                    break;
                case "--frontend":
                    if (!TryReadValue(args, ref index, option, out frontend, out error)) return false;
                    break;
                case "--model":
                    if (!TryReadValue(args, ref index, option, out model, out error)) return false;
                    break;
                case "--reasoning-effort":
                    if (!TryReadValue(args, ref index, option, out reasoningEffort, out error)) return false;
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

        if (!SessionLayerTopologyDeclaredValueRules.TryValidate(model, "--model", out error)
            || !SessionLayerTopologyDeclaredValueRules.TryValidate(reasoningEffort, "--reasoning-effort", out error))
        {
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

            if (deliveryMethod is not null && deliveryMethod is not ("inline" or "file-backed"))
            {
                error = "--delivery-method must be inline or file-backed when supplied.";
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

            if (workspaceId is not null || paneId is not null || cwd is not null || kind is not null || deliveryMethod is not null)
            {
                error = "An external resident does not accept --workspace-id, --pane-id, --cwd, --kind, or --delivery-method.";
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
            DeliveryMethod = deliveryMethod,
            Reader = reader,
            Frontend = frontend,
            Model = model,
            ReasoningEffort = reasoningEffort,
            Write = write,
            Format = format,
        };
        return true;
    }

    private static bool TryParseRecordHostStateArguments(
        string[] args,
        out SessionLayerTopologyHostStateRecordRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;
        string? domain = null;
        string? team = null;
        string? role = null;
        string? envelope = null;
        var requestedWrite = false;
        var requestedDryRun = false;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!TryReadValue(args, ref index, "--domain", out domain, out error)) return false;
                    break;
                case "--team":
                    if (!TryReadValue(args, ref index, "--team", out team, out error)) return false;
                    break;
                case "--role":
                    if (!TryReadValue(args, ref index, "--role", out role, out error)) return false;
                    break;
                case "--envelope" or "--envelope-profile":
                    if (!TryReadValue(args, ref index, args[index], out var value, out error)) return false;
                    if (envelope is not null)
                    {
                        error = "--envelope and --envelope-profile may not both be supplied.";
                        return false;
                    }
                    envelope = value;
                    break;
                case "--write":
                    requestedWrite = true;
                    break;
                case "--dry-run":
                    requestedDryRun = true;
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

        if (string.IsNullOrWhiteSpace(domain)
            || string.IsNullOrWhiteSpace(team)
            || string.IsNullOrWhiteSpace(role)
            || string.IsNullOrWhiteSpace(envelope)
            || (!requestedWrite && !requestedDryRun))
        {
            error = "--domain, --team, --role, --envelope, and either --write or --dry-run are required.";
            return false;
        }

        if (requestedWrite && requestedDryRun)
        {
            error = "--write and --dry-run are mutually exclusive.";
            return false;
        }

        request = new SessionLayerTopologyHostStateRecordRequest
        {
            Domain = domain!,
            Team = team!,
            Role = role!,
            Envelope = envelope!,
            Write = requestedWrite,
            Format = format,
        };
        return true;
    }

    private static bool TryParseMoveArguments(
        string[] args,
        out SessionLayerTopologyMoveRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;
        string? domain = null;
        string? team = null;
        string? workspaceId = null;
        string? currentDigest = null;
        var paneMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var requestedWrite = false;
        var requestedDryRun = false;
        var format = FormatJson;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!TryReadValue(args, ref index, "--domain", out domain, out error)) return false;
                    break;
                case "--team":
                    if (!TryReadValue(args, ref index, "--team", out team, out error)) return false;
                    break;
                case "--workspace-id":
                    if (!TryReadValue(args, ref index, "--workspace-id", out workspaceId, out error)) return false;
                    break;
                case "--current-digest":
                    if (!TryReadValue(args, ref index, "--current-digest", out currentDigest, out error)) return false;
                    break;
                case "--pane-map":
                    if (!TryReadValue(args, ref index, "--pane-map", out var mapping, out error)) return false;
                    var separator = mapping!.IndexOf('=', StringComparison.Ordinal);
                    if (separator <= 0 || separator == mapping.Length - 1
                        || mapping.IndexOf('=', separator + 1) >= 0)
                    {
                        error = "--pane-map must be an old-pane=new-pane pair.";
                        return false;
                    }

                    var oldPane = mapping[..separator].Trim();
                    var newPane = mapping[(separator + 1)..].Trim();
                    if (oldPane.Length == 0 || newPane.Length == 0)
                    {
                        error = "--pane-map must name non-empty old and new pane ids.";
                        return false;
                    }

                    if (!paneMap.TryAdd(oldPane, newPane))
                    {
                        error = $"--pane-map names old pane '{oldPane}' more than once.";
                        return false;
                    }
                    break;
                case "--write":
                    requestedWrite = true;
                    break;
                case "--dry-run":
                    requestedDryRun = true;
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, "--format", out var requestedFormat, out error)) return false;
                    if (!string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = "move supports only '--format json'.";
                        return false;
                    }
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain)
            || string.IsNullOrWhiteSpace(team)
            || string.IsNullOrWhiteSpace(workspaceId))
        {
            error = "--domain, --team, and --workspace-id are required.";
            return false;
        }

        request = new SessionLayerTopologyMoveRequest
        {
            Domain = domain!,
            Team = team!,
            WorkspaceId = workspaceId!,
            PaneMap = paneMap,
            CurrentDigest = currentDigest,
            Write = requestedWrite && !requestedDryRun,
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

    private static bool TryReadOption(string[] args, string option, out string? value)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], option, StringComparison.Ordinal))
            {
                value = args[index + 1];
                return true;
            }
        }

        value = null;
        return false;
    }

    private static int EmitNotApplicable(
        string operation,
        string[] args,
        string domain,
        string? team,
        TextWriter writer)
    {
        var format = TryReadOption(args, "--format", out var requestedFormat)
            && string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal)
            ? FormatJson
            : FormatMarkdown;
        const string cause = "not-applicable-team-mode";
        var summary = "authoring-only team mode has no delivery topology; topology recording and resolution are not applicable.";
        if (format == FormatJson)
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                operation = $"topology-{operation}",
                domain,
                team,
                valid = false,
                conflict = true,
                cause,
                summary,
            }, JsonOptions));
        }
        else
        {
            writer.WriteLine($"# Session-layer topology — {team ?? domain}");
            writer.WriteLine("valid: false");
            writer.WriteLine($"cause: {cause}");
            writer.WriteLine(summary);
        }

        return 1;
    }

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
            var severity = finding.IsInformational ? "informational; " : string.Empty;
            writer.WriteLine($"- {severity}role={finding.Role}; field={finding.Field}; cause={finding.Cause}; {finding.Message}");
        }
        foreach (var declaration in result.RoleDeclarations)
        {
            writer.WriteLine($"- role={declaration.Role}; model={declaration.Model ?? "absent"}; "
                + $"reasoning_effort={declaration.ReasoningEffort ?? "absent"};");
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
                + $"{role.DeliveryTarget}; model={role.Model ?? "absent"}; "
                + $"reasoning_effort={role.ReasoningEffort ?? "absent"};");
        }
        foreach (var profile in result.EnvelopeProfiles)
        {
            writer.WriteLine($"- envelope_profile={profile.Name}; kind={profile.Kind}; digest={profile.Digest}; recorded_at={profile.RecordedAt}");
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
        "resident", "workspace_id", "pane_id", "cwd", "kind", "delivery_method", "reader", "frontend",
        "model", "reasoning_effort",
    ];

    public static SessionLayerTopologyRecordResult Record(
        string routingRoot,
        SessionLayerTopologyRecordRequest request)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, request.Domain, request.Team);
        if (!SessionLayerTopologyDeclaredValueRules.TryValidate(request.Model, "--model", out var declaredValueError)
            || !SessionLayerTopologyDeclaredValueRules.TryValidate(
                request.ReasoningEffort,
                "--reasoning-effort",
                out declaredValueError))
        {
            return Conflict(request, path, declaredValueError);
        }

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
                    + $"'{request.Role}' requested '{request.WorkspaceId}'. Refusing per-role repair. "
                    + "For an operator-approved whole-team rebuild, use `intent-cli session-layer topology move "
                    + "--domain <domain> --team <team> --workspace-id <new-workspace-id> --pane-map "
                    + "<old-pane>=<new-pane> --write`. The move lets roles sharing one old pane travel together "
                    + "to one new pane, while refusing different old panes that converge on one new pane.");
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
                ? $"Recorded operator-supplied role '{request.Role}' for team '{request.Team}'. "
                    + "Heartbeat coordination accepts recorded role 'orchestration' or the alias 'orchestrator'; "
                    + "existing records under either name need no rename or migration."
                : $"Dry-run: would record operator-supplied role '{request.Role}' for team '{request.Team}'. "
                    + "Heartbeat coordination accepts recorded role 'orchestration' or the alias 'orchestrator'; "
                    + "existing records under either name need no rename or migration.",
        };
    }

    public static SessionLayerTopologyHostStateRecordResult RecordHostState(
        string routingRoot,
        SessionLayerTopologyHostStateRecordRequest request)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, request.Domain, request.Team);
        JsonObject root;
        try
        {
            if (!File.Exists(path))
            {
                return HostStateConflict(
                    request,
                    path,
                    $"Topology file '{path}' is absent. Record the team topology and the serving role before declaring host-state authority.");
            }

            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("the root is not a JSON object");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return HostStateConflict(
                request,
                path,
                $"Topology file '{path}' is unreadable: {exception.Message} Refusing to overwrite it.");
        }

        var recordedDomain = ReadString(root, "domain");
        if (!string.Equals(recordedDomain, request.Domain, StringComparison.Ordinal))
        {
            return HostStateConflict(
                request,
                path,
                $"Topology file '{path}' identifies domain '{recordedDomain ?? "missing"}', not requested domain "
                + $"'{request.Domain}'. Refusing to overwrite a copied or misplaced machine record.");
        }

        if (!TrySelectTeamForWrite(root, request.Team, out var team, out var selectError))
        {
            return HostStateConflict(request, path, selectError);
        }

        if (!TrySelectRolesForWrite(team!, out var roles, out var rolesError)
            || !roles!.TryGetPropertyValue(request.Role, out var roleNode)
            || roleNode is not JsonObject)
        {
            return HostStateConflict(
                request,
                path,
                $"Host-state role '{request.Role}' is not one of the already recorded team roles. Record that role first; authority is never inferred from resident, kind, external placement, or co-location.");
        }

        var requested = new JsonObject
        {
            ["role"] = request.Role,
            ["envelope"] = request.Envelope,
        };
        if (team!.TryGetPropertyValue(NotifyRoleTopologyStore.HostStatePropertyName, out var existingNode))
        {
            if (existingNode is not JsonObject existing)
            {
                return HostStateConflict(
                    request,
                    path,
                    "Topology field 'host_state' is not an object. Refusing to replace a malformed declaration.");
            }

            var existingRole = ReadString(existing, "role");
            var existingEnvelope = ReadString(existing, "envelope")
                ?? ReadString(existing, "envelope_profile");
            if (!string.Equals(existingRole, request.Role, StringComparison.Ordinal)
                || !string.Equals(existingEnvelope, request.Envelope, StringComparison.Ordinal))
            {
                return HostStateConflict(
                    request,
                    path,
                    $"Topology already declares host_state role '{existingRole ?? "missing"}' with envelope "
                    + $"'{existingEnvelope ?? "missing"}', not the requested role '{request.Role}' and envelope "
                    + $"'{request.Envelope}'. Refusing to replace an operator-supplied authority declaration silently.");
            }

            return new SessionLayerTopologyHostStateRecordResult
            {
                Team = request.Team,
                Role = request.Role,
                Envelope = request.Envelope,
                Mode = request.Write ? "write" : "dry-run",
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
                Applied = false,
                Changed = false,
                AlreadyRecorded = true,
                Conflict = false,
                Summary = $"Host-state role '{request.Role}' with envelope '{request.Envelope}' for team '{request.Team}' already exactly matches; idempotent no-op. The declaration records routing authority; it does not provision a non-sandboxed participant.",
            };
        }

        team[NotifyRoleTopologyStore.HostStatePropertyName] = requested;
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
                return HostStateConflict(request, path, $"Topology file '{path}' could not be written: {exception.Message}");
            }
        }

        return new SessionLayerTopologyHostStateRecordResult
        {
            Team = request.Team,
            Role = request.Role,
            Envelope = request.Envelope,
            Mode = request.Write ? "write" : "dry-run",
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
            Applied = applied,
            Changed = true,
            AlreadyRecorded = false,
            Conflict = false,
            Summary = request.Write
                ? $"Recorded explicit host-state role '{request.Role}' with envelope '{request.Envelope}' for team '{request.Team}'. The declaration is the sole authority route; it does not provision a non-sandboxed participant."
                : $"Dry-run: would record explicit host-state role '{request.Role}' with envelope '{request.Envelope}' for team '{request.Team}'. The declaration is the sole authority route; it does not provision a non-sandboxed participant.",
        };
    }

    public static SessionLayerTopologyMoveResult Move(
        string routingRoot,
        SessionLayerTopologyMoveRequest request)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, request.Domain, request.Team);
        FileStream? casLock = null;
        try
        {
            if (request.Write)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                casLock = AcquireCasLock(path);
                EnsureLocalIgnore(routingRoot);
            }

            if (!File.Exists(path))
            {
                return MoveConflict(
                    request,
                    path,
                    currentDigest: null,
                    $"Topology record '{path}' is absent; refusing to move an unrecorded team.");
            }

            var originalBytes = File.ReadAllBytes(path);
            var currentDigest = ComputeTopologyDigest(originalBytes);
            if (request.CurrentDigest is not null
                && !string.Equals(request.CurrentDigest, currentDigest, StringComparison.OrdinalIgnoreCase))
            {
                return MoveConflict(
                    request,
                    path,
                    currentDigest,
                    $"Topology move lost its CAS: current digest is '{currentDigest}', not supplied digest "
                    + $"'{request.CurrentDigest}'. Refusing to overwrite a concurrently changed record.");
            }

            var validation = NotifyRoleTopologyStore.Validate(routingRoot, request.Domain, request.Team);
            if (!validation.Valid)
            {
                return MoveConflict(
                    request,
                    path,
                    currentDigest,
                    "Topology record is invalid; refusing move. "
                    + string.Join(" ", validation.Findings.Select(finding => finding.Message)));
            }

            var root = JsonNode.Parse(originalBytes) as JsonObject
                ?? throw new JsonException("the root is not an object");
            var before = root.DeepClone();
            var teamError = string.Empty;
            var rolesError = string.Empty;
            if (!TrySelectTeamForWrite(root, request.Team, out var team, out teamError)
                || !TrySelectRolesForWrite(team!, out var roles, out rolesError))
            {
                return MoveConflict(
                    request,
                    path,
                    currentDigest,
                    teamError.Length == 0 ? rolesError : teamError);
            }

            var recordedWorkspace = ReadTeamWorkspace(team!);
            if (string.IsNullOrWhiteSpace(recordedWorkspace))
            {
                return MoveConflict(
                    request,
                    path,
                    currentDigest,
                    $"Topology team '{request.Team}' has no unambiguous workspace_id; refusing move.");
            }

            var recordedPanes = new HashSet<string>(StringComparer.Ordinal);
            var mappedNewPanes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (roleName, roleNode) in roles!.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (roleNode is not JsonObject role)
                {
                    return MoveConflict(
                        request,
                        path,
                        currentDigest,
                        $"Role '{roleName}' is not an object; refusing move.");
                }

                if (!string.Equals(ReadString(role, "resident"), NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
                {
                    continue;
                }

                var oldPane = ReadString(role, "pane_id");
                if (string.IsNullOrWhiteSpace(oldPane))
                {
                    return MoveConflict(
                        request,
                        path,
                        currentDigest,
                        $"Herdr role '{roleName}' has no pane_id; refusing move.");
                }

                recordedPanes.Add(oldPane);
                if (!request.PaneMap.TryGetValue(oldPane, out var newPane))
                {
                    return MoveConflict(
                        request,
                        path,
                        currentDigest,
                        $"No --pane-map was supplied for recorded herdr pane '{oldPane}' (role '{roleName}'); "
                        + "refusing a partial workspace move.");
                }

                if (mappedNewPanes.TryGetValue(newPane, out var mappedOldPane)
                    && !string.Equals(mappedOldPane, oldPane, StringComparison.Ordinal))
                {
                    return MoveConflict(
                        request,
                        path,
                        currentDigest,
                        $"--pane-map maps distinct recorded old panes '{mappedOldPane}' and '{oldPane}' to new "
                        + $"pane '{newPane}'; refusing an ambiguous workspace move.");
                }

                mappedNewPanes.TryAdd(newPane, oldPane);

                var paneWorkspace = WorkspaceFromPane(newPane);
                if (paneWorkspace is not null
                    && !string.Equals(paneWorkspace, request.WorkspaceId, StringComparison.Ordinal))
                {
                    return MoveConflict(
                        request,
                        path,
                        currentDigest,
                        $"New pane '{newPane}' belongs to workspace '{paneWorkspace}', not requested workspace "
                        + $"'{request.WorkspaceId}'; refusing a cross-workspace mapping.");
                }

                role["workspace_id"] = request.WorkspaceId;
                role["pane_id"] = newPane;
            }

            var unknownPanes = request.PaneMap.Keys
                .Where(oldPane => !recordedPanes.Contains(oldPane))
                .OrderBy(oldPane => oldPane, StringComparer.Ordinal)
                .ToArray();
            if (unknownPanes.Length > 0)
            {
                return MoveConflict(
                    request,
                    path,
                    currentDigest,
                    $"--pane-map names unrecorded old pane(s): {string.Join(", ", unknownPanes)}; refusing a "
                    + "partial or ambiguous workspace move.");
            }

            SetTeamWorkspace(team!, request.WorkspaceId);
            var after = root.DeepClone();
            var changed = !JsonNode.DeepEquals(before, after);

            if (request.Write && changed)
            {
                // The lock serializes canonical writers. The second digest
                // read is the compare step for writers that do not use this
                // lock, so a concurrent edit cannot be silently replaced.
                var beforeWriteDigest = ComputeTopologyDigest(File.ReadAllBytes(path));
                if (!string.Equals(beforeWriteDigest, currentDigest, StringComparison.OrdinalIgnoreCase))
                {
                    return MoveConflict(
                        request,
                        path,
                        beforeWriteDigest,
                        $"Topology move lost its CAS: current digest is '{beforeWriteDigest}', not the "
                        + $"snapshot digest '{currentDigest}'. Refusing to overwrite a concurrently changed record.");
                }

                WriteAtomically(path, root.ToJsonString(FileJsonOptions) + Environment.NewLine);
            }

            var resultDigest = changed && request.Write
                ? ComputeTopologyDigest(File.ReadAllBytes(path))
                : ComputeTopologyDigest(Encoding.UTF8.GetBytes(root.ToJsonString(FileJsonOptions) + Environment.NewLine));
            return new SessionLayerTopologyMoveResult
            {
                Team = request.Team,
                Mode = request.Write ? "write" : "dry-run",
                RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
                PreviousWorkspaceId = recordedWorkspace,
                WorkspaceId = request.WorkspaceId,
                CurrentDigest = currentDigest,
                ResultDigest = resultDigest,
                Before = before,
                After = after,
                Applied = request.Write && changed,
                Changed = changed,
                AlreadyRecorded = !changed,
                Conflict = false,
                Summary = request.Write
                    ? changed
                        ? $"Moved recorded topology for team '{request.Team}' from workspace '{recordedWorkspace}' "
                            + $"to '{request.WorkspaceId}' with {recordedPanes.Count} pane mapping(s)."
                        : $"Topology for team '{request.Team}' already records the requested workspace and panes; idempotent no-op."
                    : changed
                        ? $"Dry-run: would move recorded topology for team '{request.Team}' from workspace '{recordedWorkspace}' "
                            + $"to '{request.WorkspaceId}' with {recordedPanes.Count} pane mapping(s)."
                        : $"Dry-run: topology for team '{request.Team}' already records the requested workspace and panes; idempotent no-op.",
            };
        }
        catch (IOException exception)
        {
            return MoveConflict(
                request,
                path,
                currentDigest: null,
                $"Topology move could not acquire its CAS lock or read/write the record: {exception.Message}");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or JsonException)
        {
            return MoveConflict(
                request,
                path,
                currentDigest: null,
                $"Topology move could not read the record: {exception.Message}");
        }
        finally
        {
            casLock?.Dispose();
        }
    }

    public static SessionLayerTopologyProfileRecordResult RecordProfile(
        string routingRoot,
        SessionLayerTopologyProfileRecordRequest request)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, request.Domain, request.Team);
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
            return ProfileConflict(request, path, $"Topology file '{path}' is unreadable: {exception.Message}");
        }

        var recordedDomain = ReadString(root, "domain");
        if (!string.Equals(recordedDomain, request.Domain, StringComparison.Ordinal))
        {
            return ProfileConflict(request, path,
                $"Topology file '{path}' identifies domain '{recordedDomain ?? "missing"}', not requested domain "
                + $"'{request.Domain}'. Refusing to overwrite a copied or misplaced machine record.");
        }

        if (!TrySelectTeamForWrite(root, request.Team, out var team, out var selectError))
        {
            return ProfileConflict(request, path, selectError);
        }

        JsonObject profileMap;
        if (team!.TryGetPropertyValue("envelope_profiles", out var profileNode))
        {
            if (profileNode is not JsonObject existingMap || existingMap.ContainsKey("kind"))
            {
                return ProfileConflict(request, path,
                    "Topology field 'envelope_profiles' must be a named object map; refusing to reshape it.");
            }
            profileMap = existingMap;
        }
        else if (team.TryGetPropertyValue("profiles", out var legacyProfileNode))
        {
            if (legacyProfileNode is not JsonObject legacyMap || legacyMap.ContainsKey("kind"))
            {
                return ProfileConflict(request, path,
                    "Topology field 'profiles' must be a named object map; refusing to reshape it.");
            }
            profileMap = legacyMap;
        }
        else
        {
            profileMap = new JsonObject();
            team["envelope_profiles"] = profileMap;
        }

        AgentLaunchEnvelopeProfile? existingProfile = null;
        if (profileMap.TryGetPropertyValue(request.ProfileName, out var existingProfileNode))
        {
            if (existingProfileNode is null)
            {
                return ProfileConflict(request, path, $"Envelope profile '{request.ProfileName}' is null; refusing to replace it.");
            }

            try
            {
                using var profileDocument = JsonDocument.Parse(existingProfileNode.ToJsonString());
                if (!AgentLaunchEnvelopeProfileCodec.TryRead(
                        profileDocument.RootElement,
                        request.ProfileName,
                        out existingProfile,
                        out var profileError))
                {
                    return ProfileConflict(request, path, profileError);
                }
            }
            catch (JsonException exception)
            {
                return ProfileConflict(request, path,
                    $"Envelope profile '{request.ProfileName}' is not valid JSON: {exception.Message}");
            }
        }

        var profile = AgentLaunchEnvelopeProfileCodec.WithDigest(new AgentLaunchEnvelopeProfile
        {
            Name = request.ProfileName,
            Kind = request.Kind,
            SandboxMode = request.SandboxMode,
            ApprovalMode = request.ApprovalMode,
            RootsPolicy = request.RootsPolicy,
            WritableRoots = request.WritableRoots,
            NetworkAccess = request.NetworkAccess,
            TransportMode = request.TransportMode,
            Evidence = request.Evidence,
            RecordedAt = request.RecordedAt,
            PermissionOptions = request.PermissionOptions,
            NetworkUrls = request.NetworkUrls,
        });

        if (existingProfile is null
            ? !string.Equals(request.CurrentDigest, "absent", StringComparison.OrdinalIgnoreCase)
            : !string.Equals(request.CurrentDigest, existingProfile.Digest, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileConflict(request, path,
                existingProfile is null
                    ? $"Envelope profile '{request.ProfileName}' is absent, but current digest was '{request.CurrentDigest}'. Refusing stale or ambiguous creation."
                    : $"Envelope profile '{request.ProfileName}' current digest is '{existingProfile.Digest}', not supplied digest '{request.CurrentDigest}'. Refusing stale CAS update.");
        }

        var profileChanged = existingProfile is null
            || !string.Equals(existingProfile.Digest, profile.Digest, StringComparison.OrdinalIgnoreCase);
        var bindingChanged = false;
        if (request.Role is { } roleName)
        {
            if (!TrySelectRolesForWrite(team, out var roles, out var rolesError)
                || !roles!.TryGetPropertyValue(roleName, out var roleNode)
                || roleNode is not JsonObject role)
            {
                return ProfileConflict(request, path,
                    $"Role '{roleName}' is not a valid existing topology role. {rolesError}");
            }

            var roleKind = ReadString(role, "kind");
            if (!string.Equals(roleKind, request.Kind, StringComparison.OrdinalIgnoreCase))
            {
                return ProfileConflict(request, path,
                    $"Role '{roleName}' records kind '{roleKind ?? "missing"}', not profile kind '{request.Kind}'. Refusing a kind-mismatched baseline.");
            }

            if (request.RoleOverride)
            {
                var oldOverrideDigest = TryReadRoleOverrideDigest(role);
                bindingChanged = !string.Equals(oldOverrideDigest, profile.Digest, StringComparison.OrdinalIgnoreCase)
                    || ReadString(role, "envelope_profile") is not null
                    || ReadString(role, "envelope_profile_ref") is not null;
                role["envelope_profile_override"] = AgentLaunchEnvelopeProfileCodec.ToJsonObject(profile);
                role.Remove("envelope_profile");
                role.Remove("envelope_profile_ref");
                role.Remove("profile_override");
            }
            else
            {
                bindingChanged = !string.Equals(ReadString(role, "envelope_profile"), request.ProfileName, StringComparison.Ordinal)
                    || ReadString(role, "envelope_profile_ref") is not null
                    || role.ContainsKey("envelope_profile_override")
                    || role.ContainsKey("profile_override");
                role["envelope_profile"] = request.ProfileName;
                role.Remove("envelope_profile_ref");
                role.Remove("envelope_profile_override");
                role.Remove("profile_override");
            }
        }

        profileMap[request.ProfileName] = AgentLaunchEnvelopeProfileCodec.ToJsonObject(profile);
        var changed = profileChanged || bindingChanged;
        var applied = false;
        if (request.Write && changed)
        {
            try
            {
                EnsureLocalIgnore(routingRoot);
                WriteAtomically(path, root.ToJsonString(FileJsonOptions) + Environment.NewLine);
                applied = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ProfileConflict(request, path, $"Topology file '{path}' could not be written: {exception.Message}");
            }
        }

        return new SessionLayerTopologyProfileRecordResult
        {
            Team = request.Team,
            ProfileName = request.ProfileName,
            Role = request.Role,
            Mode = request.Write ? "write" : "dry-run",
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
            Applied = applied,
            Changed = changed,
            AlreadyRecorded = !changed,
            Conflict = false,
            Digest = profile.Digest!,
            Summary = request.Write
                ? changed
                    ? $"Recorded operator-supplied envelope profile '{request.ProfileName}' for kind '{request.Kind}'."
                    : $"Envelope profile '{request.ProfileName}' and its binding were already exactly recorded; idempotent no-op."
                : changed
                    ? $"Dry-run: would record envelope profile '{request.ProfileName}' for kind '{request.Kind}'."
                    : $"Dry-run: envelope profile '{request.ProfileName}' and its binding already exactly match.",
        };
    }

    private static string? TryReadRoleOverrideDigest(JsonObject role)
    {
        foreach (var property in new[] { "envelope_profile_override", "profile_override" })
        {
            if (!role.TryGetPropertyValue(property, out var node) || node is null)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(node.ToJsonString());
                var profileName = document.RootElement.TryGetProperty("name", out var nameElement)
                    && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? $"role:{property}"
                    : $"role:{property}";
                return AgentLaunchEnvelopeProfileCodec.TryRead(
                    document.RootElement,
                    profileName,
                    out var profile,
                    out _)
                    ? profile?.Digest
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    public static SessionLayerTopologyKindUpdateResult UpdateKind(string routingRoot, SessionLayerTopologyKindUpdateRequest request)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, request.Domain, request.Team);
        var validation = NotifyRoleTopologyStore.Validate(routingRoot, request.Domain, request.Team);
        if (!validation.Valid)
            return new(request.Team, request.Role, request.CurrentKind, request.NewKind, request.Write ? "write" : "dry-run", false, false, true,
                $"Topology record is invalid; refusing update-kind. {string.Join(" ", validation.Findings.Select(f => f.Message))}",
                AgentLaunchRecipeRegistry.Describe(request.NewKind));
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("the root is not an object");
            if (!TrySelectTeamForWrite(root, request.Team, out var team, out var error)
                || !TrySelectRolesForWrite(team!, out var roles, out error)
                || !roles!.TryGetPropertyValue(request.Role, out var roleNode) || roleNode is not JsonObject role)
                return new(request.Team, request.Role, request.CurrentKind, request.NewKind, request.Write ? "write" : "dry-run", false, false, true,
                    $"Role '{request.Role}' is not a valid recorded role. {error}",
                    AgentLaunchRecipeRegistry.Describe(request.NewKind));
            var current = ReadString(role, "kind");
            if (!string.Equals(current, request.CurrentKind, StringComparison.Ordinal))
                return new(request.Team, request.Role, request.CurrentKind, request.NewKind, request.Write ? "write" : "dry-run", false, false, true,
                    $"Role '{request.Role}' records kind '{current ?? "missing"}', not stated current kind '{request.CurrentKind}'. Refusing update-kind.",
                    AgentLaunchRecipeRegistry.Describe(request.NewKind));
            role["kind"] = request.NewKind;
            if (request.Write) WriteAtomically(path, root.ToJsonString(FileJsonOptions) + Environment.NewLine);
            return new(request.Team, request.Role, request.CurrentKind, request.NewKind, request.Write ? "write" : "dry-run", request.Write, true, false,
                request.Write
                    ? $"Updated only kind for role '{request.Role}' in team '{request.Team}'. "
                        + AgentLaunchRecipeRegistry.Describe(request.NewKind).Summary
                    : $"Dry-run: would update only kind for role '{request.Role}'. "
                        + AgentLaunchRecipeRegistry.Describe(request.NewKind).Summary,
                AgentLaunchRecipeRegistry.Describe(request.NewKind));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(request.Team, request.Role, request.CurrentKind, request.NewKind, request.Write ? "write" : "dry-run", false, false, true,
                $"Topology file '{path}' could not be updated: {exception.Message}",
                AgentLaunchRecipeRegistry.Describe(request.NewKind));
        }
    }

    public static SessionLayerTopologyFieldUpdateResult UpdateField(string routingRoot, SessionLayerTopologyFieldUpdateRequest request)
    {
        var mode = request.Write ? "write" : "dry-run";
        if (!string.Equals(request.Field, "delivery_method", StringComparison.Ordinal))
            return new(request.Team, request.Role, request.Field, request.CurrentValue, request.NewValue, mode, false, false, true,
                $"Field '{request.Field}' is not in the topology update registry. Only 'delivery_method' is allowed.");
        if (request.NewValue is not ("inline" or "file-backed"))
            return new(request.Team, request.Role, request.Field, request.CurrentValue, request.NewValue, mode, false, false, true,
                $"Field 'delivery_method' must be 'inline' or 'file-backed', not '{request.NewValue}'.");

        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, request.Domain, request.Team);
        var validation = NotifyRoleTopologyStore.Validate(routingRoot, request.Domain, request.Team);
        if (!validation.Valid)
            return new(request.Team, request.Role, request.Field, request.CurrentValue, request.NewValue, mode, false, false, true,
                $"Topology record is invalid; refusing update-field. {string.Join(" ", validation.Findings.Select(f => f.Message))}");
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("the root is not an object");
            if (!TrySelectTeamForWrite(root, request.Team, out var team, out var error)
                || !TrySelectRolesForWrite(team!, out var roles, out error)
                || !roles!.TryGetPropertyValue(request.Role, out var roleNode) || roleNode is not JsonObject role)
                return new(request.Team, request.Role, request.Field, request.CurrentValue, request.NewValue, mode, false, false, true,
                    $"Role '{request.Role}' is not a valid recorded role. {error}");

            var hasField = role.TryGetPropertyValue(request.Field, out var fieldNode);
            var actual = hasField ? fieldNode?.GetValue<string>() : null;
            var matches = string.Equals(request.CurrentValue, "absent", StringComparison.Ordinal)
                ? !hasField
                : hasField && string.Equals(actual, request.CurrentValue, StringComparison.Ordinal);
            if (!matches)
            {
                var recorded = hasField ? actual ?? "invalid" : "absent";
                return new(request.Team, request.Role, request.Field, request.CurrentValue, request.NewValue, mode, false, false, true,
                    $"Role '{request.Role}' records {request.Field} '{recorded}', not stated current value '{request.CurrentValue}'. Refusing update-field.");
            }

            role[request.Field] = request.NewValue;
            if (request.Write) WriteAtomically(path, root.ToJsonString(FileJsonOptions) + Environment.NewLine);
            return new(request.Team, request.Role, request.Field, request.CurrentValue, request.NewValue, mode, request.Write, true, false,
                request.Write
                    ? $"Updated only {request.Field} for role '{request.Role}' in team '{request.Team}'."
                    : $"Dry-run: would update only {request.Field} for role '{request.Role}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(request.Team, request.Role, request.Field, request.CurrentValue, request.NewValue, mode, false, false, true,
                $"Topology file '{path}' could not be updated: {exception.Message}");
        }
    }

    public static SessionLayerTopologyLegacyRetireResult RetireLegacy(string routingRoot, SessionLayerTopologyLegacyRetireRequest request)
    {
        var legacyPath = NotifyRoleTopologyStore.ResolvePath(routingRoot);
        if (!TryValidateCurrentRecord(routingRoot, request.Domain, request.Team, out var validationError))
            return new(request.Team, false, true, $"A valid per-team record is required before retiring legacy topology. {validationError}");
        if (!File.Exists(legacyPath))
            return new(request.Team, false, true, $"Legacy topology file '{legacyPath}' is absent; nothing was retired.");
        try
        {
            var evidencePath = SessionLayerTopologyRetirementEvidence.ResolvePath(routingRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            File.AppendAllText(evidencePath, JsonSerializer.Serialize(new
            {
                timestamp_utc = DateTimeOffset.UtcNow,
                host = Environment.MachineName,
                domain = request.Domain,
                team = request.Team,
                retired_path = NotifyRoleTopologyStore.LegacyRelativePath,
                evidence = request.Evidence,
            }) + Environment.NewLine);
            File.Delete(legacyPath);
            return new(request.Team, true, false, $"Retired '{NotifyRoleTopologyStore.LegacyRelativePath}' with named migration evidence.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return new(request.Team, false, true, $"Legacy topology could not be retired: {exception.Message}"); }
    }

    private static bool TryValidateCurrentRecord(string routingRoot, string domain, string team, out string error)
    {
        error = string.Empty;
        var path = NotifyRoleTopologyStore.ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path)) { error = $"Current per-team record '{path}' is absent."; return false; }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("domain", out var domainValue) || !string.Equals(domainValue.GetString(), domain, StringComparison.Ordinal)
                || !root.TryGetProperty("team", out var teamValue) || !string.Equals(teamValue.GetString(), team, StringComparison.Ordinal)
                || !root.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Object || !roles.EnumerateObject().Any())
            { error = $"Current per-team record '{path}' is invalid."; return false; }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { error = $"Current per-team record '{path}' is unreadable: {exception.Message}"; return false; }
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
        Add(role, "delivery_method", request.DeliveryMethod);
        Add(role, "reader", request.Reader);
        Add(role, "frontend", request.Frontend);
        Add(role, "model", request.Model);
        Add(role, "reasoning_effort", request.ReasoningEffort);
        return role;
    }

    private static bool RoleMatches(JsonObject existing, JsonObject requested) =>
        KnownRoleFields.All(field =>
            string.Equals(ReadString(existing, field), ReadString(requested, field), StringComparison.Ordinal));

    private static string? ReadTeamWorkspace(JsonObject team)
    {
        var direct = ReadString(team, "workspace_id");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (!team.TryGetPropertyValue("workspace", out var workspace))
        {
            return null;
        }

        if (workspace is JsonValue value && value.TryGetValue<string>(out var workspaceId))
        {
            return workspaceId;
        }

        if (workspace is JsonObject workspaceObject)
        {
            var nestedWorkspaceId = ReadString(workspaceObject, "workspace_id");
            return !string.IsNullOrWhiteSpace(nestedWorkspaceId)
                ? nestedWorkspaceId
                : ReadString(workspaceObject, "id");
        }

        return null;
    }

    private static void SetTeamWorkspace(JsonObject team, string workspaceId)
    {
        if (team.ContainsKey("workspace_id"))
        {
            team["workspace_id"] = workspaceId;
        }

        if (team.TryGetPropertyValue("workspace", out var workspace))
        {
            switch (workspace)
            {
                case JsonValue:
                    team["workspace"] = workspaceId;
                    break;
                case JsonObject workspaceObject:
                    if (workspaceObject.ContainsKey("workspace_id"))
                    {
                        workspaceObject["workspace_id"] = workspaceId;
                    }
                    else if (workspaceObject.ContainsKey("id"))
                    {
                        workspaceObject["id"] = workspaceId;
                    }
                    else
                    {
                        workspaceObject["workspace_id"] = workspaceId;
                    }
                    break;
                default:
                    team["workspace_id"] = workspaceId;
                    break;
            }
        }
        else if (!team.ContainsKey("workspace_id"))
        {
            team["workspace_id"] = workspaceId;
        }
    }

    private static string? WorkspaceFromPane(string paneId)
    {
        var separator = paneId.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 ? paneId[..separator] : null;
    }

    private static FileStream AcquireCasLock(string path) => new(
        path + ".lock",
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.None);

    private static string ComputeTopologyDigest(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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
        "schema_version" or "team" or "workspace" or "workspace_id" or "tab_id" or "updated_at" or "roles"
        or "envelope_profiles" or "profiles" or NotifyRoleTopologyStore.HostStatePropertyName;

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

    private static SessionLayerTopologyMoveResult MoveConflict(
        SessionLayerTopologyMoveRequest request,
        string path,
        string? currentDigest,
        string summary) => new()
        {
            Team = request.Team,
            Mode = request.Write ? "write" : "dry-run",
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
            PreviousWorkspaceId = null,
            WorkspaceId = request.WorkspaceId,
            CurrentDigest = currentDigest,
            ResultDigest = null,
            Before = null,
            After = null,
            Applied = false,
            Changed = false,
            AlreadyRecorded = false,
            Conflict = true,
            Summary = summary,
        };

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

    private static SessionLayerTopologyHostStateRecordResult HostStateConflict(
        SessionLayerTopologyHostStateRecordRequest request,
        string path,
        string summary) => new()
        {
            Team = request.Team,
            Role = request.Role,
            Envelope = request.Envelope,
            Mode = request.Write ? "write" : "dry-run",
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
            Applied = false,
            Changed = false,
            AlreadyRecorded = false,
            Conflict = true,
            Summary = summary,
        };

    private static SessionLayerTopologyProfileRecordResult ProfileConflict(
        SessionLayerTopologyProfileRecordRequest request,
        string path,
        string summary) => new()
        {
            Team = request.Team,
            ProfileName = request.ProfileName,
            Role = request.Role,
            Mode = request.Write ? "write" : "dry-run",
            RecordPath = NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team),
            Applied = false,
            Changed = false,
            AlreadyRecorded = false,
            Conflict = true,
            Digest = string.Empty,
            Summary = summary,
        };
}

/// <summary>
/// G614: tracked, fleet-citable retirement evidence. This deliberately lives
/// outside the machine-local, ignored topology directory so a later ledger
/// decision can cite entries accumulated across hosts.
/// </summary>
internal static class SessionLayerTopologyRetirementEvidence
{
    public const string RelativePath = ".intent-cli/legacy-topology-retirements.jsonl";

    public static string ResolvePath(string routingRoot) => Path.GetFullPath(Path.Combine(
        routingRoot,
        RelativePath.Replace('/', Path.DirectorySeparatorChar)));
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
    public string? DeliveryMethod { get; init; }
    public string? Reader { get; init; }
    public string? Frontend { get; init; }
    public string? Model { get; init; }
    public string? ReasoningEffort { get; init; }
    public required bool Write { get; init; }
    public required string Format { get; init; }
}

internal sealed record SessionLayerTopologyHostStateRecordRequest
{
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string Role { get; init; }
    public required string Envelope { get; init; }
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

internal sealed record SessionLayerTopologyHostStateRecordResult
{
    public required string Team { get; init; }
    public required string Role { get; init; }
    public required string Envelope { get; init; }
    public required string Mode { get; init; }
    public required string RecordPath { get; init; }
    public required bool Applied { get; init; }
    public required bool Changed { get; init; }
    public required bool AlreadyRecorded { get; init; }
    public required bool Conflict { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyMoveRequest
{
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string WorkspaceId { get; init; }
    public required IReadOnlyDictionary<string, string> PaneMap { get; init; }
    public string? CurrentDigest { get; init; }
    public required bool Write { get; init; }
    public required string Format { get; init; }
}

internal sealed record SessionLayerTopologyMoveResult
{
    public required string Team { get; init; }
    public required string Mode { get; init; }
    public required string RecordPath { get; init; }
    public string? PreviousWorkspaceId { get; init; }
    public required string WorkspaceId { get; init; }
    public string? CurrentDigest { get; init; }
    public string? ResultDigest { get; init; }
    public JsonNode? Before { get; init; }
    public JsonNode? After { get; init; }
    public required bool Applied { get; init; }
    public required bool Changed { get; init; }
    public required bool AlreadyRecorded { get; init; }
    public required bool Conflict { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyProfileRecordRequest
{
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string ProfileName { get; init; }
    public required string Kind { get; init; }
    public required string SandboxMode { get; init; }
    public required string ApprovalMode { get; init; }
    public required string RootsPolicy { get; init; }
    public required IReadOnlyList<string> WritableRoots { get; init; }
    public required string NetworkAccess { get; init; }
    public required string TransportMode { get; init; }
    public required string Evidence { get; init; }
    public required string RecordedAt { get; init; }
    public required IReadOnlyList<string> PermissionOptions { get; init; }
    public required IReadOnlyList<string> NetworkUrls { get; init; }
    public string? Role { get; init; }
    public required bool RoleOverride { get; init; }
    public required string CurrentDigest { get; init; }
    public required bool Write { get; init; }
}

internal sealed record SessionLayerTopologyProfileRecordResult
{
    public required string Team { get; init; }
    public required string ProfileName { get; init; }
    public string? Role { get; init; }
    public required string Mode { get; init; }
    public required string RecordPath { get; init; }
    public required bool Applied { get; init; }
    public required bool Changed { get; init; }
    public required bool AlreadyRecorded { get; init; }
    public required bool Conflict { get; init; }
    public required string Digest { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyKindUpdateRequest(string Domain, string Team, string Role, string CurrentKind, string NewKind, bool Write);
internal sealed record SessionLayerTopologyKindUpdateResult(
    string Team,
    string Role,
    string CurrentKind,
    string NewKind,
    string Mode,
    bool Applied,
    bool Changed,
    bool Conflict,
    string Summary,
    AgentLaunchRecipeResolution? Recipe = null);
internal sealed record SessionLayerTopologyFieldUpdateRequest(string Domain, string Team, string Role, string Field, string CurrentValue, string NewValue, bool Write);
internal sealed record SessionLayerTopologyFieldUpdateResult(string Team, string Role, string Field, string CurrentValue, string NewValue, string Mode, bool Applied, bool Changed, bool Conflict, string Summary);
internal sealed record SessionLayerTopologyLegacyRetireRequest(string Domain, string Team, string Evidence);
internal sealed record SessionLayerTopologyLegacyRetireResult(string Team, bool Retired, bool Conflict, string Summary);

internal sealed record SessionLayerTopologyValidationResult
{
    public required bool Valid { get; init; }
    public required string Team { get; init; }
    public required string RecordPath { get; init; }
    public required IReadOnlyList<SessionLayerTopologyFinding> Findings { get; init; }
    public IReadOnlyList<SessionLayerTopologyDeclaredRole> RoleDeclarations { get; init; } = [];
    public NotifyHostStateDeclaration? HostState { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyShowResult
{
    public required bool Valid { get; init; }
    public required string Team { get; init; }
    public required string? WorkspaceId { get; init; }
    public required string RecordPath { get; init; }
    public required IReadOnlyList<SessionLayerTopologyShownRole> Roles { get; init; }
    public required IReadOnlyList<SessionLayerTopologyShownProfile> EnvelopeProfiles { get; init; }
    public NotifyHostStateDeclaration? HostState { get; init; }
    public required IReadOnlyList<SessionLayerTopologyFinding> Findings { get; init; }
    public required string Summary { get; init; }
}

internal sealed record SessionLayerTopologyShownProfile
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string? Digest { get; init; }
    public required string RecordedAt { get; init; }
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
    public string? Model { get; init; }
    public string? ReasoningEffort { get; init; }
}
