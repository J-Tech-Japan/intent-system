using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class OrcaRunRecordCommand
{
    public static Action? AfterTopologyLockHook { get; set; }

    private const string FormatJson = "json";
    private const string Operation = "topology-record-orca-run";

    private const string Usage =
        "Usage: intent-cli session-layer topology record-orca-run --domain <d> --team <t> --role <role> "
        + "--current <run-id|absent|malformed> --new <run-id|absent> [--receive-policy orca-push|inbox-pull] "
        + "[--frontend <name>] --confirm-record-orca-run (--dry-run|--write) --format json";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        if (!string.Equals(request!.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine("--format must be 'json'.");
            writer.WriteLine(Usage);
            return 1;
        }

        if (!ValidateRunIdField(request.NewRunId, "new", out error, out var malformedField)
            || !ValidateRunIdField(request.CurrentToken, "current", out error, out malformedField))
        {
            writer.WriteLine(JsonSerializer.Serialize(Conflict(request!, cause: "orca-run-id-malformed", field: malformedField, summary: error), JsonOptions));
            return 1;
        }

        if (!string.Equals(request.NewRunId, "absent", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(request.ReceivePolicy))
            {
                WriteResult(writer, Conflict(request, "receive-policy-missing", summary: "--receive-policy is required when --new is a run id."));
                return 1;
            }

            if (!OrcaRunBinding.IsValidReceivePolicy(request.ReceivePolicy))
            {
                WriteResult(writer, Conflict(request, "receive-policy-invalid", summary: $"receive_policy '{request.ReceivePolicy}' is not orca-push or inbox-pull."));
                return 1;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.ReceivePolicy))
        {
            WriteResult(writer, Conflict(request, "receive-policy-not-accepted", summary: "--receive-policy is not accepted with --new absent."));
            return 1;
        }

        FileStream? teamModeLock = null;
        FileStream? topologyLock = null;
        try
        {
            if (request.Write)
            {
                OrcaRunTeamModeLock.EnsureLockDirectory(context.RepoRoot);
                teamModeLock = OrcaRunTeamModeLock.TryAcquire(context.RepoRoot, out var busyMessage);
                if (teamModeLock is null)
                {
                    WriteResult(writer, Conflict(request, "record-lock-busy", field: "team-mode-lock", summary: busyMessage ?? "team-mode lock is busy."));
                    return 1;
                }
            }

            TeamModeState? teamModeState;
            try
            {
                teamModeState = TeamModeStore.TryRead(context.RepoRoot);
            }
            catch (InvalidOperationException exception)
            {
                WriteResult(writer, Conflict(request, "team-shape-unreadable", summary: exception.Message));
                return 1;
            }

            var shape = OrcaRunTeamShape.Resolve(context.RepoRoot, request.Domain, request.Team, teamModeState);
            if (!shape.Resolved)
            {
                WriteResult(writer, ShapeConflict(request, shape));
                return 1;
            }

            string recordPath = shape.BindingLocation == OrcaRunBinding.TopologyRoleLocation
                ? NotifyRoleTopologyStore.RelativePathFor(request.Domain, request.Team)
                : OrcaRunSoloStore.RelativePathFor(request.Domain, request.Team);

            byte[]? lockedTopologyBytes = null;
            string? lockedTopologyDigest = null;
            string? soloDigest = null;
            if (request.Write && shape.BindingLocation == OrcaRunBinding.TopologyRoleLocation)
            {
                SessionLayerTopologyWriter.EnsureLocalIgnorePublic(context.RepoRoot);
                var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, request.Domain, request.Team);
                try
                {
                    topologyLock = SessionLayerTopologyWriter.AcquireCasLockPublic(topologyPath);
                }
                catch (IOException exception)
                {
                    WriteResult(writer, Conflict(request, "record-lock-busy", field: "topology-lock", summary: exception.Message));
                    return 1;
                }

                lockedTopologyBytes = File.Exists(topologyPath) ? File.ReadAllBytes(topologyPath) : [];
                lockedTopologyDigest = OrcaRunTeamModeLock.ComputeDigest(lockedTopologyBytes);
                AfterTopologyLockHook?.Invoke();
                var lockedShape = OrcaRunTeamShape.Resolve(context.RepoRoot, request.Domain, request.Team, teamModeState);
                if (!string.Equals(lockedShape.BindingLocation, shape.BindingLocation, StringComparison.Ordinal)
                    || !string.Equals(lockedShape.TeamShape, shape.TeamShape, StringComparison.Ordinal))
                {
                    WriteResult(writer, Conflict(request, "record-cas-lost", summary: "topology shape changed under lock."));
                    return 1;
                }
            }
            else if (request.Write && shape.BindingLocation == OrcaRunBinding.OrcaRunFileLocation)
            {
                soloDigest = OrcaRunSoloStore.ComputeDigest(context.RepoRoot, request.Domain, request.Team);
            }

            if (shape.BindingLocation == OrcaRunBinding.TopologyRoleLocation)
            {
                var resolution = NotifyRoleTopologyStore.Resolve(context.RepoRoot, request.Domain, request.Team);
                if (!resolution.Resolved || resolution.Topology is null
                    || !resolution.Topology.Roles.TryGetValue(request.Role, out var roleRecord))
                {
                    WriteResult(writer, Conflict(request, "role-not-recorded", summary: $"Role '{request.Role}' is not recorded."));
                    return 1;
                }

                if (!LogicalRoleNormalizer.TryNormalize(request.Role, out var canonical, out _)
                    || !string.Equals(canonical, shape.RequiredSeat, StringComparison.Ordinal))
                {
                    WriteResult(writer, Conflict(
                        request,
                        "binding-seat-not-allowed",
                        summary: $"Team shape '{shape.TeamShape}' requires seat '{shape.RequiredSeat}', not '{request.Role}'."));
                    return 1;
                }

                if (!string.IsNullOrWhiteSpace(request.Frontend))
                {
                    WriteResult(writer, Conflict(request, "frontend-not-accepted", fix: "intent-cli session-layer topology update-field --field frontend …", summary: "--frontend is accepted only for solo-conductor."));
                    return 1;
                }

                if (!string.Equals(request.NewRunId, "absent", StringComparison.Ordinal))
                {
                    var policyError = ValidateReceivePolicy(request, roleRecord, request.ReceivePolicy!);
                    if (policyError is not null)
                    {
                        WriteResult(writer, policyError);
                        return 1;
                    }
                }

                var duplicateKeys = OrcaRunBindingHealth.FindTopologyBindingRoleKeys(context.RepoRoot, request.Domain, request.Team);
                var conflictingRole = duplicateKeys.FirstOrDefault(key => !string.Equals(key, request.Role, StringComparison.Ordinal));
                if (conflictingRole is not null)
                {
                    WriteResult(writer, Conflict(request, "binding-duplicate", summary: $"Another role already records orca_run: '{conflictingRole}'."));
                    return 1;
                }

                var current = ReadTopologyCurrent(roleRecord);
                if (!CurrentMatches(request.CurrentToken, current))
                {
                    WriteResult(writer, Conflict(request, "current-mismatch", summary: CurrentMismatchMessage(request.CurrentToken, current)));
                    return 1;
                }

                if (request.Write)
                {
                    if (lockedTopologyDigest is null
                        || !string.Equals(OrcaRunTeamModeLock.ComputeDigest(File.Exists(NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, request.Domain, request.Team)) ? File.ReadAllBytes(NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, request.Domain, request.Team)) : []), lockedTopologyDigest, StringComparison.Ordinal))
                    {
                        WriteResult(writer, Conflict(request, "record-cas-lost", summary: "topology digest changed before write."));
                        return 1;
                    }
                }

                return FinishTopologyWrite(context, writer, request, shape, roleRecord, recordPath, canonical!);
            }

            // solo-conductor path
            if (!LogicalRoleNormalizer.TryNormalize(request.Role, out var soloCanonical, out _)
                || !string.Equals(soloCanonical, LogicalRoleNormalizer.Architect, StringComparison.Ordinal))
            {
                WriteResult(writer, Conflict(request, "binding-seat-not-allowed", summary: "solo-conductor binds only design/architect."));
                return 1;
            }

            if (string.Equals(request.NewRunId, "absent", StringComparison.Ordinal))
            {
                var soloRead = OrcaRunSoloStore.TryRead(context.RepoRoot, request.Domain, request.Team);
                if (soloRead.Exists && !soloRead.IsUnparseable
                    && (!string.Equals(soloRead.Domain, request.Domain, StringComparison.Ordinal)
                        || !string.Equals(soloRead.Team, request.Team, StringComparison.Ordinal)))
                {
                    WriteResult(writer, Conflict(request, "binding-identity-mismatch", summary: "solo binding file names another domain or team."));
                    return 1;
                }

                var absentCurrent = ReadSoloCurrent(soloRead);
                if (!CurrentMatches(request.CurrentToken, absentCurrent))
                {
                    WriteResult(writer, Conflict(request, "current-mismatch", summary: CurrentMismatchMessage(request.CurrentToken, absentCurrent)));
                    return 1;
                }

                if (request.Write)
                {
                    if (!string.Equals(OrcaRunSoloStore.ComputeDigest(context.RepoRoot, request.Domain, request.Team), soloDigest, StringComparison.Ordinal))
                    {
                        WriteResult(writer, Conflict(request, "record-cas-lost", summary: "solo binding digest changed before write."));
                        return 1;
                    }

                    OrcaRunSoloStore.Delete(context.RepoRoot, request.Domain, request.Team);
                }

                WriteResult(writer, Success(
                    request,
                    shape,
                    recordPath,
                    soloCanonical!,
                    absentCurrent,
                    "absent",
                    applied: request.Write && soloRead.Exists,
                    changed: soloRead.Exists));
                return 0;
            }

            if (string.IsNullOrWhiteSpace(request.Frontend))
            {
                WriteResult(writer, Conflict(request, "frontend-missing", summary: "--frontend is required for solo-conductor."));
                return 1;
            }

            var soloRecord = new NotifyRecordedRole(
                NotifyRecordedRole.ExternalResident, null, null, null, null, null, null, request.Frontend);
            var soloPolicyError = ValidateReceivePolicy(request, soloRecord, request.ReceivePolicy!);
            if (soloPolicyError is not null)
            {
                WriteResult(writer, soloPolicyError);
                return 1;
            }

            if (OrcaRunBindingHealth.HasTopologyRoleBinding(context.RepoRoot, request.Domain, request.Team))
            {
                WriteResult(writer, Conflict(request, "binding-location-conflict", summary: "topology role already records orca_run."));
                return 1;
            }

            var existingSolo = OrcaRunSoloStore.TryRead(context.RepoRoot, request.Domain, request.Team);
            if (existingSolo.Exists && !existingSolo.IsUnparseable
                && (!string.Equals(existingSolo.Domain, request.Domain, StringComparison.Ordinal)
                    || !string.Equals(existingSolo.Team, request.Team, StringComparison.Ordinal)))
            {
                WriteResult(writer, Conflict(request, "binding-identity-mismatch", summary: "solo binding file names another domain or team."));
                return 1;
            }

            var soloCurrent = ReadSoloCurrent(existingSolo);
            if (!CurrentMatches(request.CurrentToken, soloCurrent))
            {
                WriteResult(writer, Conflict(request, "current-mismatch", summary: CurrentMismatchMessage(request.CurrentToken, soloCurrent)));
                return 1;
            }

            var alreadyRecorded = existingSolo.Exists
                && !existingSolo.IsUnparseable
                && string.Equals(existingSolo.RunId, request.NewRunId, StringComparison.Ordinal)
                && string.Equals(existingSolo.ReceivePolicy, request.ReceivePolicy, StringComparison.Ordinal)
                && string.Equals(existingSolo.Frontend, request.Frontend, StringComparison.Ordinal);

            if (request.Write && !alreadyRecorded)
            {
                if (!string.Equals(OrcaRunSoloStore.ComputeDigest(context.RepoRoot, request.Domain, request.Team), soloDigest, StringComparison.Ordinal))
                {
                    WriteResult(writer, Conflict(request, "record-cas-lost", summary: "solo binding digest changed before write."));
                    return 1;
                }

                OrcaRunSoloStore.Write(
                    context.RepoRoot,
                    request.Domain,
                    request.Team,
                    request.Role,
                    request.NewRunId,
                    request.ReceivePolicy!,
                    request.Frontend);
            }

            WriteResult(writer, Success(
                request,
                shape,
                recordPath,
                soloCanonical!,
                soloCurrent,
                request.NewRunId,
                applied: request.Write && !alreadyRecorded,
                changed: !alreadyRecorded,
                alreadyRecorded: alreadyRecorded,
                receivePolicy: request.ReceivePolicy,
                frontend: request.Frontend));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteResult(writer, Conflict(request!, "record-write-failed", summary: exception.Message));
            return 1;
        }
        finally
        {
            topologyLock?.Dispose();
            teamModeLock?.Dispose();
        }
    }

    private static int FinishTopologyWrite(
        CliContext context,
        TextWriter writer,
        RecordOrcaRunRequest request,
        OrcaRunTeamShapeResult shape,
        NotifyRecordedRole roleRecord,
        string recordPath,
        string canonicalRole)
    {
        var current = ReadTopologyCurrent(roleRecord);
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, request.Domain, request.Team);
        var beforeText = File.Exists(topologyPath) ? GuardedFileRead.ReadAllText(topologyPath) : null;
        var document = JsonNode.Parse(beforeText ?? "{}") as JsonObject ?? new JsonObject();
        JsonObject teamObject;
        if (document.TryGetPropertyValue("teams", out var teamsNode) && teamsNode is JsonObject teamsObject
            && teamsObject.TryGetPropertyValue(request.Team, out var selectedTeam)
            && selectedTeam is JsonObject selectedTeamObject)
        {
            teamObject = selectedTeamObject;
        }
        else if (document.TryGetPropertyValue("roles", out _))
        {
            teamObject = document;
        }
        else
        {
            teamObject = document;
        }

        if (teamObject["roles"] is not JsonObject rolesObject)
        {
            rolesObject = new JsonObject();
            teamObject["roles"] = rolesObject;
        }

        if (rolesObject[request.Role] is not JsonObject roleObject)
        {
            WriteResult(writer, Conflict(request, "role-not-recorded", summary: $"Role '{request.Role}' is not recorded."));
            return 1;
        }

        if (string.Equals(request.NewRunId, "absent", StringComparison.Ordinal))
        {
            roleObject.Remove("orca_run");
        }
        else
        {
            roleObject["orca_run"] = new JsonObject
            {
                ["run_id"] = request.NewRunId,
                ["receive_policy"] = request.ReceivePolicy,
            };
        }

        var content = document.ToJsonString(SessionLayerTopologyWriter.FileJsonOptionsPublic) + Environment.NewLine;
        var alreadyRecorded = string.Equals(request.NewRunId, "absent", StringComparison.Ordinal)
            ? string.Equals(current, "absent", StringComparison.Ordinal)
            : string.Equals(current, request.NewRunId, StringComparison.Ordinal)
                && string.Equals(roleRecord.OrcaRun?.ReceivePolicy, request.ReceivePolicy, StringComparison.Ordinal);
        if (request.Write && !alreadyRecorded)
        {
            SessionLayerTopologyWriter.WriteAtomicallyPublic(topologyPath, content);
        }

        WriteResult(writer, Success(
            request,
            shape,
            recordPath,
            canonicalRole,
            current,
            request.NewRunId,
            applied: request.Write && !alreadyRecorded,
            changed: !alreadyRecorded,
            alreadyRecorded: alreadyRecorded,
            receivePolicy: request.ReceivePolicy,
            frontend: roleRecord.Frontend));
        return 0;
    }

    private static OrcaRunRecordResult? ValidateReceivePolicy(
        RecordOrcaRunRequest request,
        NotifyRecordedRole record,
        string policy)
    {
        OrcaRunBinding.EvaluateReceivePolicyForSeat(record, policy, out var cause, out var fix);
        if (cause is null)
        {
            return null;
        }

        return Conflict(
            request,
            cause,
            fix: cause == "receive-policy-herdr-seat"
                ? OrcaRunBinding.BuildHerdrFix(request.Domain, request.Team, request.Role)
                : fix,
            summary: cause);
    }

    private static string ReadTopologyCurrent(NotifyRecordedRole record)
    {
        if (record.OrcaRun is null)
        {
            return "absent";
        }

        if (!record.OrcaRun.WellFormed || record.OrcaRun.MalformedCause is not null)
        {
            return "malformed";
        }

        if (record.OrcaRun.RunId is null || !OrcaRunBinding.IsValidRunId(record.OrcaRun.RunId))
        {
            return "malformed";
        }

        return record.OrcaRun.RunId;
    }

    private static string ReadSoloCurrent(SoloBindingReadResult read)
    {
        if (!read.Exists || read.IsUnparseable)
        {
            return read.Exists && read.IsUnparseable ? "malformed" : "absent";
        }

        if (read.OrcaRunAbsent || read.OrcaRunNull || !read.HasOrcaRunObject || read.RunId is null)
        {
            return "malformed";
        }

        if (!OrcaRunBinding.IsValidRunId(read.RunId))
        {
            return "malformed";
        }

        return read.RunId;
    }

    private static bool CurrentMatches(string stated, string recorded)
    {
        return string.Equals(stated, recorded, StringComparison.Ordinal);
    }

    private static string CurrentMismatchMessage(string stated, string recorded) =>
        $"Recorded current is '{recorded}', not stated '{stated}'.";

    private static bool ValidateRunIdField(string? value, string field, out string error, out string invalidField)
    {
        error = string.Empty;
        invalidField = field;
        if (value is null)
        {
            error = $"{field} is required.";
            return false;
        }

        if (string.Equals(value, "malformed", StringComparison.Ordinal))
        {
            if (string.Equals(field, "new", StringComparison.Ordinal))
            {
                error = "new value 'malformed' is reserved for --current.";
                return false;
            }

            return true;
        }

        if (string.Equals(value, "absent", StringComparison.Ordinal))
        {
            return true;
        }

        if (!OrcaRunBinding.IsValidRunId(value))
        {
            error = $"{field} value '{value}' does not match ^run_[0-9a-f]{{12}}$.";
            return false;
        }

        return true;
    }

    private static OrcaRunRecordResult ShapeConflict(RecordOrcaRunRequest request, OrcaRunTeamShapeResult shape) =>
        Conflict(request, shape.Cause ?? "team-shape-unreadable", fix: shape.Fix, summary: shape.Message ?? shape.Cause ?? "shape unresolved");

    private static OrcaRunRecordResult Conflict(
        RecordOrcaRunRequest request,
        string cause,
        string? field = null,
        string? fix = null,
        string? summary = null) => new()
        {
            Operation = Operation,
            Domain = request.Domain,
            Team = request.Team,
            Role = request.Role,
            Mode = request.Write ? "write" : "dry-run",
            Conflict = true,
            Cause = cause,
            Field = field,
            Fix = fix,
            Summary = summary ?? cause,
        };

    private static OrcaRunRecordResult Success(
        RecordOrcaRunRequest request,
        OrcaRunTeamShapeResult shape,
        string recordPath,
        string canonicalRole,
        string current,
        string newValue,
        bool applied,
        bool changed,
        bool alreadyRecorded = false,
        string? receivePolicy = null,
        string? frontend = null) => new()
        {
            Operation = Operation,
            Domain = request.Domain,
            Team = request.Team,
            Role = request.Role,
            CanonicalRole = canonicalRole,
            TeamShape = shape.TeamShape,
            TeamMode = shape.TeamMode,
            BindingLocation = shape.BindingLocation,
            RecordPath = recordPath,
            Current = current,
            New = newValue,
            ReceivePolicy = receivePolicy,
            Frontend = frontend,
            Mode = request.Write ? "write" : "dry-run",
            Applied = applied,
            Changed = changed,
            AlreadyRecorded = alreadyRecorded,
            Conflict = false,
            Summary = (alreadyRecorded
                ? "Binding already recorded; no rewrite was performed."
                : request.Write
                    ? "Recorded Orca Run binding."
                    : "Dry run planned Orca Run binding.")
                + " " + OrcaRunBinding.SuccessSummarySuffix,
        };

    private static void WriteResult(TextWriter writer, OrcaRunRecordResult result) =>
        writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

    private static bool TryParse(string[] args, out RecordOrcaRunRequest? request, out string error)
    {
        string? domain = null;
        string? team = null;
        string? role = null;
        string? current = null;
        string? newValue = null;
        string? receivePolicy = null;
        string? frontend = null;
        var write = false;
        var dryRun = false;
        var confirm = false;
        var format = FormatJson;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!TryRead(args, ref index, out domain, out error)) { request = null; return false; }
                    break;
                case "--team":
                    if (!TryRead(args, ref index, out team, out error)) { request = null; return false; }
                    break;
                case "--role":
                    if (!TryRead(args, ref index, out role, out error)) { request = null; return false; }
                    break;
                case "--current":
                    if (!TryRead(args, ref index, out current, out error)) { request = null; return false; }
                    break;
                case "--new":
                    if (!TryRead(args, ref index, out newValue, out error)) { request = null; return false; }
                    break;
                case "--receive-policy":
                    if (!TryRead(args, ref index, out receivePolicy, out error)) { request = null; return false; }
                    break;
                case "--frontend":
                    if (!TryRead(args, ref index, out frontend, out error)) { request = null; return false; }
                    break;
                case "--confirm-record-orca-run":
                    confirm = true;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--format":
                    if (!TryRead(args, ref index, out format, out error)) { request = null; return false; }
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    request = null;
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(role)
            || current is null || newValue is null || !confirm || (write == dryRun))
        {
            error = "Missing required option or confirm, or both/neither of --dry-run / --write.";
            request = null;
            return false;
        }

        request = new RecordOrcaRunRequest(domain!, team!, role!, current, newValue, receivePolicy, frontend, write, format!, dryRun);
        return true;
    }

    private static bool TryRead(string[] args, ref int index, out string? value, out string error)
    {
        if (index + 1 >= args.Length)
        {
            value = null;
            error = $"{args[index]} requires a value.";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record RecordOrcaRunRequest(
        string Domain,
        string Team,
        string Role,
        string CurrentToken,
        string NewRunId,
        string? ReceivePolicy,
        string? Frontend,
        bool Write,
        string Format,
        bool DryRun);
}

internal sealed record OrcaRunRecordResult
{
    public required string Operation { get; init; }
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string Role { get; init; }
    public string? CanonicalRole { get; init; }
    public string? TeamShape { get; init; }
    public string? TeamMode { get; init; }
    public string? BindingLocation { get; init; }
    public string? RecordPath { get; init; }
    public string? Current { get; init; }
    public string? New { get; init; }
    public string? ReceivePolicy { get; init; }
    public string? Frontend { get; init; }
    public required string Mode { get; init; }
    public bool Applied { get; init; }
    public bool Changed { get; init; }
    public bool AlreadyRecorded { get; init; }
    public bool Conflict { get; init; }
    public string? Cause { get; init; }
    public string? Field { get; init; }
    public string? Fix { get; init; }
    public required string Summary { get; init; }
}
