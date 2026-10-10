using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// A release-only exception for the three explicitly supported, missing
/// post-closeout receipts. The G857 gate remains the authority for every
/// completion fact; this class only recognizes its closed missing-duty shape
/// and persists an audit of the waived observations.
/// </summary>
internal static class ClaimLoopEvidenceOverride
{
    private const string AuditRoot = ".intent-cli/loop-evidence-overrides";
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal static ClaimLoopEvidenceOverrideWriterOperations WriterOperations { get; set; } = new(
        audit => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(audit, AuditJsonOptions) + Environment.NewLine),
        (path, bytes) => File.WriteAllBytes(path, bytes));

    public static ClaimLoopEvidenceOverrideEligibility Evaluate(SoloConductorClaimReleaseGateResult gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        var completion = gate.Completion;
        if (!gate.IsApplicable || completion is null)
            return Refuse("not-applicable", "Solo-conductor completion evidence is not applicable to this canonical snapshot.");
        if (completion.Applicability is null || completion.Duties is null)
            return Refuse("duty-shape-unknown", "The completion result is missing its applicability or duty structure.");
        if (completion.Applicability.State != "applicable"
            || completion.Applicability.Cause != "recorded-solo-conductor"
            || completion.Applicability.Mode != "solo-conductor")
            return Refuse("applicability-unavailable", "Recorded solo-conductor applicability is not established.");
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(completion.ExecutionUnit, out _)
            || !SafeSegment(completion.Domain)
            || !SafeSegment(completion.Team)
            || !SafeRepo(completion.TargetRepo)
            || completion.LinkedPr is not > 0
            || !IsObjectId(completion.CanonicalSnapshotOid)
            || !SafeRef(completion.CanonicalTargetRef)
            || !SafeRelativePath(gate.RunLogRelativePath))
            return Refuse("identity-unavailable", "The exact unit, domain, team, repository, PR, host snapshot, target ref, or selected run log is not safe.");

        var duties = completion.Duties;
        var byId = new Dictionary<string, SoloConductorClaimReleaseDuty>(StringComparer.Ordinal);
        foreach (var duty in duties)
        {
            if (duty is null || !byId.TryAdd(duty.Id, duty))
                return Refuse("duty-shape-unknown", "The completion duty set contains a null or duplicate duty.");
        }

        if (!Exact(byId, "closeout-queue", "satisfied", "completed-queue-item-present")
            || !Exact(byId, "pr-merged", "satisfied", "canonical-run-receipt-present")
            || !Exact(byId, "closeout-recorded", "satisfied", "canonical-run-receipt-present"))
            return Refuse("closeout-incomplete", "The completed queue and both identity-matched closeout run duties must be satisfied.");

        var hasKnowledgePair = byId.ContainsKey("knowledge-architect") && byId.ContainsKey("knowledge-orchestrator");
        var hasKnowledgeNa = byId.ContainsKey("knowledge-writeback");
        if (hasKnowledgePair == hasKnowledgeNa)
            return Refuse("duty-shape-unknown", "Knowledge duties do not match exactly one supported required or not-applicable shape.");

        if (hasKnowledgePair)
        {
            if (!KnowledgeDuty(byId["knowledge-architect"], "architect", out _)
                || !KnowledgeDuty(byId["knowledge-orchestrator"], "orchestrator", out _))
                return Refuse("duty-shape-unknown", "Knowledge duties contain an unknown state, cause, or role.");
        }
        else if (!Exact(byId, "knowledge-writeback", "not-applicable", "explicit-no-required-duty"))
        {
            return Refuse("duty-shape-unknown", "The knowledge not-applicable duty is not an explicit no-required-duty declaration.");
        }

        var hasGuide = byId.ContainsKey("guide-reachability");
        if (!hasGuide || !(Exact(byId, "guide-reachability", "satisfied", "architect-guide-record-present")
            || Exact(byId, "guide-reachability", "not-applicable", "explicit-no-role-facing-surface")
            || Exact(byId, "guide-reachability", "missing", "architect-guide-record-missing")))
            return Refuse("duty-shape-unknown", "The guide duty is missing or has an unsupported state or cause.");

        var allowedIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "closeout-queue", "pr-merged", "closeout-recorded", "guide-reachability",
        };
        if (hasKnowledgePair)
        {
            allowedIds.Add("knowledge-architect");
            allowedIds.Add("knowledge-orchestrator");
        }
        else
        {
            allowedIds.Add("knowledge-writeback");
        }
        if (byId.Keys.Any(id => !allowedIds.Contains(id)) || byId.Count != allowedIds.Count)
            return Refuse("duty-shape-unknown", "The completion contains an unknown or missing mandatory duty.");

        var skipped = new List<ClaimLoopEvidenceOverrideSkippedDuty>();
        if (hasKnowledgePair)
        {
            AddMissingKnowledge(byId["knowledge-architect"], "architect", skipped);
            AddMissingKnowledge(byId["knowledge-orchestrator"], "orchestrator", skipped);
        }
        if (byId["guide-reachability"].State == "missing")
            skipped.Add(ClaimLoopEvidenceOverrideSkippedDuty.From(byId["guide-reachability"], "architect"));

        return skipped.Count == 0
            ? Refuse("no-missing-receipts", "The initial canonical completion has no required missing receipt to waive.")
            : new ClaimLoopEvidenceOverrideEligibility(true, "allowed-missing-receipts", "Only explicitly allowed attributed receipts are missing after completed closeout.", skipped);
    }

    public static ClaimLoopEvidenceOverrideWriteResult Write(
        string transactionRoot,
        ClaimRequest request,
        ClaimRecord holder,
        SoloConductorClaimReleaseGateResult gate,
        ClaimLoopEvidenceOverrideEligibility eligibility,
        string historyPath,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionRoot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyPath);
        if (!eligibility.Eligible || gate.Completion is not { } completion || gate.RunLogRelativePath is not { } runLogPath)
            throw new InvalidOperationException("Loop-evidence override audit requires a current eligible canonical gate result.");

        var auditPath = AuditPath(completion.ExecutionUnit, Path.GetFileName(historyPath));
        var auditAbsolute = ResolveContained(transactionRoot, auditPath);
        var runAbsolute = ResolveContained(transactionRoot, runLogPath);
        ValidatePathAncestors(transactionRoot, auditAbsolute, allowMissing: true);
        ValidatePathAncestors(transactionRoot, runAbsolute, allowMissing: false);
        if (PathExistsOrLink(auditAbsolute))
            throw new IOException($"Loop-evidence override audit destination already exists: {auditPath}");
        var runAttributes = File.GetAttributes(runAbsolute);
        if ((runAttributes & FileAttributes.Directory) != 0 || (runAttributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Selected canonical run log is not a regular file: {runLogPath}");

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A nonblank release reason is required for the loop-evidence override audit.");

        var record = new ClaimLoopEvidenceOverrideAudit
        {
            SchemaVersion = 1,
            Operation = "claim-release",
            Scope = request.Scope,
            ExecutionUnit = completion.ExecutionUnit,
            Domain = completion.Domain!,
            Team = holder.Team,
            Actor = request.Actor,
            NormalizedRole = LogicalRoleNormalizer.Builder,
            DisplacedClaimedAt = holder.ClaimedAt,
            TargetRepo = completion.TargetRepo!,
            LinkedPr = completion.LinkedPr!.Value,
            CanonicalSnapshotOid = completion.CanonicalSnapshotOid,
            CanonicalTargetRef = completion.CanonicalTargetRef,
            RecordedAt = now,
            Reason = reason,
            ReleaseHistoryPath = NormalizeRelative(historyPath),
            RunLogPath = NormalizeRelative(runLogPath),
            SkippedDuties = eligibility.SkippedDuties,
        };
        var auditBytes = WriterOperations.SerializeAudit(record);
        var runEvent = new RunEvent
        {
            Ts = now,
            ExecutionUnit = completion.ExecutionUnit,
            Event = "loop-evidence-override",
            By = "intent-cli claim release",
            Reason = reason,
            Repo = completion.TargetRepo,
            Pr = completion.LinkedPr,
            LinkedPr = $"https://github.com/{completion.TargetRepo}/pull/{completion.LinkedPr.Value}",
            TeamMode = "solo-conductor",
            ActorRole = LogicalRoleNormalizer.Builder,
            ResultRef = auditPath,
        };
        var line = Encoding.UTF8.GetBytes(RunLogSerializer.SerializeLine(runEvent) + "\n");
        var priorLog = File.ReadAllBytes(runAbsolute);
        var separatorLength = priorLog.Length > 0 && priorLog[^1] is not (byte)'\n' and not (byte)'\r' ? 1 : 0;
        var appendedLog = new byte[priorLog.Length + separatorLength + line.Length];
        Buffer.BlockCopy(priorLog, 0, appendedLog, 0, priorLog.Length);
        if (separatorLength != 0) appendedLog[priorLog.Length] = (byte)'\n';
        Buffer.BlockCopy(line, 0, appendedLog, priorLog.Length + separatorLength, line.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(auditAbsolute)!);
        ValidatePathAncestors(transactionRoot, auditAbsolute, allowMissing: true);
        using (var stream = new FileStream(auditAbsolute, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(auditBytes);
        WriterOperations.WriteRunLog(runAbsolute, appendedLog);
        return new ClaimLoopEvidenceOverrideWriteResult(auditPath, NormalizeRelative(historyPath), NormalizeRelative(runLogPath));
    }

    public static ClaimLoopEvidenceOverridePaths PlannedPaths(string unit, string scope, string runLogPath)
    {
        var claimName = Path.GetFileNameWithoutExtension(ClaimCommand.ClaimPath(scope));
        const string filename = "<release-history-filename>";
        return new ClaimLoopEvidenceOverridePaths(
            $"{ClaimCommand.ClaimsDirectory}/history/{claimName}/{filename}",
            AuditPath(unit, filename),
            NormalizeRelative(runLogPath));
    }

    private static bool KnowledgeDuty(SoloConductorClaimReleaseDuty duty, string role, out bool missing)
    {
        missing = duty.State == "missing";
        return missing
            ? duty.Cause == "attributed-knowledge-record-missing"
            : duty.State == "satisfied" && duty.Cause == "attributed-knowledge-record-present";
    }

    private static void AddMissingKnowledge(
        SoloConductorClaimReleaseDuty duty,
        string role,
        ICollection<ClaimLoopEvidenceOverrideSkippedDuty> skipped)
    {
        if (duty.State == "missing") skipped.Add(ClaimLoopEvidenceOverrideSkippedDuty.From(duty, role));
    }

    private static bool Exact(IReadOnlyDictionary<string, SoloConductorClaimReleaseDuty> duties, string id, string state, string cause) =>
        duties.TryGetValue(id, out var duty) && duty.State == state && duty.Cause == cause;

    private static ClaimLoopEvidenceOverrideEligibility Refuse(string cause, string detail) =>
        new(false, cause, detail, []);

    private static string AuditPath(string unit, string historyFilename) =>
        $"{AuditRoot}/{unit}/{historyFilename}";

    private static bool SafeRepo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(SafeSegment);
    }

    private static bool SafeSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not "." and not ".."
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsObjectId(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static bool SafeRef(string value) =>
        value.StartsWith("refs/heads/", StringComparison.Ordinal)
        && value.Length > "refs/heads/".Length
        && value.Split('/').All(part => part.Length > 0 && part is not "." and not ".." && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'));

    private static bool SafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains('\\')) return false;
        return value.Split('/').All(part => part.Length > 0 && part is not "." and not "..");
    }

    private static string NormalizeRelative(string value) => value.Replace('\\', '/');

    private static string ResolveContained(string root, string relative)
    {
        if (!SafeRelativePath(relative)) throw new IOException($"Unsafe transaction-relative path: {relative}");
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var check = Path.GetRelativePath(fullRoot, full);
        if (check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(check))
            throw new IOException($"Transaction path escaped its root: {relative}");
        return full;
    }

    private static void ValidatePathAncestors(string root, string destination, bool allowMissing)
    {
        var fullRoot = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(fullRoot, destination);
        var cursor = fullRoot;
        var segments = relative.Split(Path.DirectorySeparatorChar);
        for (var index = 0; index < segments.Length; index++)
        {
            cursor = Path.Combine(cursor, segments[index]);
            var isFinal = index == segments.Length - 1;
            if (!PathExistsOrLink(cursor))
            {
                if (allowMissing || isFinal) continue;
                throw new IOException($"Required transaction file is missing: {relative}");
            }
            var attributes = File.GetAttributes(cursor);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Transaction path follows a link: {relative}");
            if (!isFinal && (attributes & FileAttributes.Directory) == 0)
                throw new IOException($"Transaction path ancestor is not a directory: {relative}");
        }
    }

    private static bool PathExistsOrLink(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        try { return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

internal sealed record ClaimLoopEvidenceOverrideEligibility(
    [property: JsonPropertyName("eligible")] bool Eligible,
    [property: JsonPropertyName("cause")] string Cause,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("skipped_duties")] IReadOnlyList<ClaimLoopEvidenceOverrideSkippedDuty> SkippedDuties);

internal sealed record ClaimLoopEvidenceOverrideSkippedDuty
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("cause")] public required string Cause { get; init; }
    [JsonPropertyName("detail")] public required string Detail { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("declared_facets")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? DeclaredFacets { get; init; }
    [JsonPropertyName("declared_targets")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? DeclaredTargets { get; init; }
    [JsonPropertyName("declared_routes")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<SoloConductorClaimReleaseRoute>? DeclaredRoutes { get; init; }
    [JsonPropertyName("evidence")] public required IReadOnlyList<SoloConductorClaimReleaseEvidence> Evidence { get; init; }
    [JsonPropertyName("recovery_commands")] public required IReadOnlyList<string> RecoveryCommands { get; init; }
    [JsonPropertyName("publication_step")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PublicationStep { get; init; }
    [JsonPropertyName("repair_unavailable_reason")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RepairUnavailableReason { get; init; }

    public static ClaimLoopEvidenceOverrideSkippedDuty From(SoloConductorClaimReleaseDuty duty, string role) => new()
    {
        Id = duty.Id, State = duty.State, Cause = duty.Cause, Detail = duty.Detail, Role = role,
        DeclaredFacets = duty.DeclaredFacets, DeclaredTargets = duty.DeclaredTargets, DeclaredRoutes = duty.DeclaredRoutes,
        Evidence = duty.Evidence, RecoveryCommands = duty.RecoveryCommands, PublicationStep = duty.PublicationStep,
        RepairUnavailableReason = duty.RepairUnavailableReason,
    };
}

internal sealed record ClaimLoopEvidenceOverrideAudit
{
    [JsonPropertyName("schema_version")] public required int SchemaVersion { get; init; }
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("actor")] public required string Actor { get; init; }
    [JsonPropertyName("normalized_role")] public required string NormalizedRole { get; init; }
    [JsonPropertyName("displaced_claimed_at")] public required DateTimeOffset DisplacedClaimedAt { get; init; }
    [JsonPropertyName("target_repo")] public required string TargetRepo { get; init; }
    [JsonPropertyName("linked_pr")] public required int LinkedPr { get; init; }
    [JsonPropertyName("canonical_snapshot_oid")] public required string CanonicalSnapshotOid { get; init; }
    [JsonPropertyName("canonical_target_ref")] public required string CanonicalTargetRef { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("release_history_path")] public required string ReleaseHistoryPath { get; init; }
    [JsonPropertyName("run_log_path")] public required string RunLogPath { get; init; }
    [JsonPropertyName("skipped_duties")] public required IReadOnlyList<ClaimLoopEvidenceOverrideSkippedDuty> SkippedDuties { get; init; }
}

internal sealed record ClaimLoopEvidenceOverrideWriteResult(string AuditPath, string HistoryPath, string RunLogPath);

internal sealed record ClaimLoopEvidenceOverridePaths(string HistoryPath, string AuditPath, string RunLogPath);

internal sealed record ClaimLoopEvidenceOverrideWriterOperations(
    Func<ClaimLoopEvidenceOverrideAudit, byte[]> SerializeAudit,
    Action<string, byte[]> WriteRunLog);
