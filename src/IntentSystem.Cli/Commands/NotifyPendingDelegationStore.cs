using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyPendingStoreWriteResult(bool Written, string Path, string? Error);

internal sealed record NotifyPendingReconciliationResult(
    bool Applied,
    bool AlreadyConverged,
    string Path,
    NotifyPendingDelegation? Record,
    NotifyPendingDelegation? Preview,
    string? Error);

internal sealed record NotifyPendingDisposition
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("actor")] public required string Actor { get; init; }
    [JsonPropertyName("timestamp")] public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("superseding_task_id")] public string? SupersedingTaskId { get; init; }
    [JsonPropertyName("applied_outcome_evidence")] public string? AppliedOutcomeEvidence { get; init; }
}

internal sealed record NotifyPendingDelegation
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("delegating_role")] public string? DelegatingRole { get; init; }
    [JsonPropertyName("recipient_role")] public required string RecipientRole { get; init; }
    [JsonPropertyName("report_to_role")] public string? ReportToRole { get; init; }
    [JsonPropertyName("recipient_identity")] public required string RecipientIdentity { get; init; }
    [JsonPropertyName("expected_artifact")] public required string ExpectedArtifact { get; init; }
    [JsonPropertyName("expected_artifacts")] public IReadOnlyList<string>? ExpectedArtifacts { get; init; }
    [JsonPropertyName("objective")] public string? Objective { get; init; }
    [JsonPropertyName("inputs")] public IReadOnlyList<string>? Inputs { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("dispatched_at")] public required DateTimeOffset DispatchedAt { get; init; }
    [JsonPropertyName("transport_mode")] public string? TransportMode { get; init; }
    [JsonPropertyName("resident")] public string? Resident { get; init; }
    [JsonPropertyName("workspace_id")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("pane_id")] public string? PaneId { get; init; }
    [JsonPropertyName("reader")] public string? Reader { get; init; }
    [JsonPropertyName("cwd")] public string? Cwd { get; init; }
    [JsonPropertyName("kind")] public string? Kind { get; init; }
    [JsonPropertyName("launch_args")] public IReadOnlyList<string>? LaunchArguments { get; init; }
    [JsonPropertyName("ruling")] public NotifyRuling? Ruling { get; init; }
    [JsonPropertyName("report_arrived")] public bool ReportArrived { get; init; }
    [JsonPropertyName("report_status")] public string? ReportStatus { get; init; }
    [JsonPropertyName("report_artifact")] public string? ReportArtifact { get; init; }
    [JsonPropertyName("report_summary")] public string? ReportSummary { get; init; }
    [JsonPropertyName("reported_at")] public DateTimeOffset? ReportedAt { get; init; }
    [JsonPropertyName("disposition")] public NotifyPendingDisposition? Disposition { get; init; }

    internal bool IsOpen => !ReportArrived && Disposition is null;
    internal string SettlementBasis => Disposition is not null ? "disposition" : ReportArrived ? "report" : "open";
}

internal sealed record NotifyPendingLookup
{
    public required bool Resolved { get; init; }
    public NotifyPendingDelegation? Record { get; init; }
    public IReadOnlyList<string> KnownTaskIds { get; init; } = [];
    public string? Path { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Durable, team-scoped delegation lifecycle records. Each line is either a
/// dispatch, disposition, or matching report snapshot; the latest line for a
/// task is the authoritative state. The append-only shape keeps dispatch,
/// explicit settlement, and resolution auditable without making the existing
/// six-field event channel a second, incompatible state store.
/// </summary>
internal static class NotifyPendingDelegationStore
{
    private const string RelativeDirectory = ".intent-cli/notify";
    private const string FileName = "pending.jsonl";
    private const string DispatchEvent = "dispatch";
    private const string ReportEvent = "report";
    private const string DispositionEvent = "disposition";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Sync = new();

    internal static Func<string, string, NotifyPendingStoreWriteResult>? WriteOverride { get; set; }

    public static string ResolvePath(string routingRoot, string domain, string team) => Path.GetFullPath(Path.Combine(
        routingRoot,
        RelativeDirectory,
        ValidateSegment(domain, "domain"),
        ValidateSegment(team, "team"),
        FileName));

    public static NotifyPendingStoreWriteResult WriteDispatch(string routingRoot, NotifyPendingDelegation record)
    {
        string path;
        string? error;
        try
        {
            path = ResolvePath(routingRoot, record.Domain, record.Team);
            error = null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            path = Path.Combine(record.Domain, record.Team, FileName);
            error = exception.Message;
        }
        if (error is not null)
        {
            return new NotifyPendingStoreWriteResult(false, path, error);
        }

        lock (Sync)
        {
            var current = ReadCurrent(path, out var readError);
            if (readError is not null)
            {
                return new NotifyPendingStoreWriteResult(false, path, readError);
            }

            return Append(path, record with
            {
                ReportArrived = false,
                ReportStatus = null,
                ReportArtifact = null,
                ReportSummary = null,
                ReportedAt = null,
                Disposition = null,
            });
        }
    }

    public static NotifyPendingStoreWriteResult WriteReport(
        string routingRoot,
        NotifyPendingDelegation record,
        string status,
        string artifact,
        string summary,
        DateTimeOffset reportedAt)
    {
        var path = ResolvePath(routingRoot, record.Domain, record.Team);
        var normalizedSummary = NotifyEventWriter.NormalizeSummary(summary);
        lock (Sync)
        {
            var current = ReadCurrent(path, out var readError);
            if (readError is not null)
            {
                return new NotifyPendingStoreWriteResult(false, path, readError);
            }

            if (current.TryGetValue(record.TaskId, out var currentRecord))
            {
                if (!string.Equals(currentRecord.ResultNonce, record.ResultNonce, StringComparison.Ordinal))
                {
                    return new NotifyPendingStoreWriteResult(false, path,
                        $"Task '{record.TaskId}' changed result nonce before its report could be recorded.");
                }

                if (currentRecord.ReportArrived)
                {
                    var sameReport = string.Equals(currentRecord.ReportStatus, status, StringComparison.Ordinal)
                        && string.Equals(currentRecord.ReportArtifact, artifact, StringComparison.Ordinal)
                        && string.Equals(currentRecord.ReportSummary, normalizedSummary, StringComparison.Ordinal);
                    return new NotifyPendingStoreWriteResult(
                        sameReport,
                        path,
                        sameReport ? null : $"Task '{record.TaskId}' already has a different report.");
                }

                record = currentRecord;
            }

            return Append(path, record with
            {
                ReportArrived = true,
                ReportStatus = status,
                ReportArtifact = artifact,
                ReportSummary = normalizedSummary,
                ReportedAt = reportedAt,
            });
        }
    }

    /// <summary>
    /// Reconciles one delivered sender-local report into the host-owned
    /// pending delegation state. The task and result nonce identify the
    /// dispatch generation; an already matching report is a successful
    /// replay, while a different report is a durable conflict.
    /// </summary>
    public static NotifyPendingReconciliationResult ReconcileReport(
        string routingRoot,
        NotifyPendingDelegation record,
        string status,
        string artifact,
        string summary,
        DateTimeOffset reportedAt,
        bool write = true)
    {
        string path;
        try
        {
            path = ResolvePath(routingRoot, record.Domain, record.Team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new NotifyPendingReconciliationResult(false, false, routingRoot, null, null, exception.Message);
        }

        var normalizedSummary = NotifyEventWriter.NormalizeSummary(summary);
        lock (Sync)
        {
            var current = ReadCurrent(path, out var readError);
            if (readError is not null)
            {
                return new NotifyPendingReconciliationResult(false, false, path, null, null, readError);
            }

            if (!current.TryGetValue(record.TaskId, out var currentRecord))
            {
                return new NotifyPendingReconciliationResult(
                    false,
                    false,
                    path,
                    null,
                    null,
                    $"Task '{record.TaskId}' is unknown in the pending delegation store.");
            }

            if (!string.Equals(currentRecord.ResultNonce, record.ResultNonce, StringComparison.Ordinal))
            {
                return new NotifyPendingReconciliationResult(
                    false,
                    false,
                    path,
                    currentRecord,
                    null,
                    $"Task '{record.TaskId}' changed result nonce before its report could be reconciled.");
            }

            if (currentRecord.ReportArrived)
            {
                var sameReport = string.Equals(currentRecord.ReportStatus, status, StringComparison.Ordinal)
                    && string.Equals(currentRecord.ReportArtifact, artifact, StringComparison.Ordinal)
                    && string.Equals(currentRecord.ReportSummary, normalizedSummary, StringComparison.Ordinal);
                return sameReport
                    ? new NotifyPendingReconciliationResult(false, true, path, currentRecord, currentRecord, null)
                    : new NotifyPendingReconciliationResult(
                        false,
                        false,
                        path,
                        currentRecord,
                        null,
                        $"Task '{record.TaskId}' already has a different report; refusing to overwrite host-owned pending state.");
            }

            if (currentRecord.Disposition is not null)
            {
                return new NotifyPendingReconciliationResult(
                    false,
                    false,
                    path,
                    currentRecord,
                    null,
                    $"Task '{record.TaskId}' is already settled by disposition; sender-local report reconciliation cannot overwrite it.");
            }

            var updated = currentRecord with
            {
                ReportArrived = true,
                ReportStatus = status,
                ReportArtifact = artifact,
                ReportSummary = normalizedSummary,
                ReportedAt = reportedAt,
            };
            if (!write)
            {
                return new NotifyPendingReconciliationResult(false, false, path, currentRecord, updated, null);
            }

            var append = Append(path, updated);
            return new NotifyPendingReconciliationResult(
                append.Written,
                false,
                path,
                append.Written ? updated : currentRecord,
                updated,
                append.Error);
        }
    }

    public static NotifyPendingStoreWriteResult WriteDisposition(
        string routingRoot,
        NotifyPendingDelegation record,
        NotifyPendingDisposition disposition)
    {
        var path = ResolvePath(routingRoot, record.Domain, record.Team);
        lock (Sync)
        {
            var current = ReadCurrent(path, out var readError);
            if (readError is not null)
            {
                return new NotifyPendingStoreWriteResult(false, path, readError);
            }

            if (!current.TryGetValue(record.TaskId, out var currentRecord))
            {
                return new NotifyPendingStoreWriteResult(
                    false,
                    path,
                    $"Task '{record.TaskId}' is unknown in the pending delegation store.");
            }

            if (!currentRecord.IsOpen)
            {
                return new NotifyPendingStoreWriteResult(
                    false,
                    path,
                    $"Task '{record.TaskId}' is already settled ({currentRecord.SettlementBasis}).");
            }

            if (!string.Equals(currentRecord.ResultNonce, record.ResultNonce, StringComparison.Ordinal))
            {
                return new NotifyPendingStoreWriteResult(
                    false,
                    path,
                    $"Task '{record.TaskId}' changed before its disposition could be recorded.");
            }

            return Append(path, currentRecord with { Disposition = disposition });
        }
    }

    public static NotifyPendingLookup Find(
        string routingRoot,
        string? domain,
        string? team,
        string taskId)
    {
        IReadOnlyList<string> paths;
        try
        {
            paths = ResolveCandidatePaths(routingRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return new NotifyPendingLookup
            {
                Resolved = false,
                Error = exception.Message,
            };
        }

        var records = new List<NotifyPendingDelegation>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var current = ReadCurrent(path, out var error);
            if (error is not null)
            {
                return new NotifyPendingLookup
                {
                    Resolved = false,
                    Path = path,
                    Error = error,
                };
            }

            foreach (var record in current.Values)
            {
                if (record.IsOpen)
                {
                    known.Add(record.TaskId);
                }
                if (string.Equals(record.TaskId, taskId, StringComparison.Ordinal))
                {
                    records.Add(record);
                }
            }
        }

        if (records.Count == 1)
        {
            return new NotifyPendingLookup
            {
                Resolved = true,
                Record = records[0],
                Path = ResolvePath(routingRoot, records[0].Domain, records[0].Team),
                KnownTaskIds = known.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            };
        }

        if (records.Count > 1)
        {
            return new NotifyPendingLookup
            {
                Resolved = false,
                Error = $"task id '{taskId}' is ambiguous across multiple team stores.",
                KnownTaskIds = known.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            };
        }

        return new NotifyPendingLookup
        {
            Resolved = false,
            KnownTaskIds = known.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        };
    }

    public static IReadOnlyList<NotifyPendingDelegation> ReadOpen(
        string routingRoot,
        string? domain,
        string? team,
        out string? error)
    {
        error = null;
        var records = new List<NotifyPendingDelegation>();
        IReadOnlyList<string> paths;
        try
        {
            paths = ResolveCandidatePaths(routingRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            error = exception.Message;
            return records;
        }

        foreach (var path in paths)
        {
            var current = ReadCurrent(path, out var readError);
            if (readError is not null)
            {
                error = readError;
                return [];
            }

            records.AddRange(current.Values.Where(value => value.IsOpen));
        }

        return records.OrderBy(value => value.DispatchedAt).ToArray();
    }

    /// <summary>
    /// Reads the current record for every dispatch generation in one team
    /// ledger. Unlike <see cref="ReadOpen"/>, settled children remain visible:
    /// their original dispatch is still evidence that the recipient delegated
    /// the delivered unit downstream.
    /// </summary>
    public static IReadOnlyList<NotifyPendingDelegation> ReadAll(
        string routingRoot,
        string domain,
        string team,
        out string? error)
    {
        string path;
        try
        {
            path = ResolvePath(routingRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = exception.Message;
            return [];
        }

        lock (Sync)
        {
            var current = ReadCurrent(path, out error);
            return error is null
                ? current.Values
                    .OrderBy(value => value.DispatchedAt)
                    .ThenBy(value => value.TaskId, StringComparer.Ordinal)
                    .ToArray()
                : [];
        }
    }

    private static NotifyPendingStoreWriteResult Append(string path, NotifyPendingDelegation record)
    {
        var eventRecord = new NotifyPendingEvent
        {
            Event = record.ReportArrived
                ? ReportEvent
                : record.Disposition is not null
                    ? DispositionEvent
                    : DispatchEvent,
            Domain = record.Domain,
            Team = record.Team,
            TaskId = record.TaskId,
            DelegatingRole = record.DelegatingRole,
            RecipientRole = record.RecipientRole,
            ReportToRole = record.ReportToRole,
            RecipientIdentity = record.RecipientIdentity,
            ExpectedArtifact = record.ExpectedArtifact,
            ExpectedArtifacts = record.ExpectedArtifacts,
            Objective = record.Objective,
            Inputs = record.Inputs,
            ResultNonce = record.ResultNonce,
            DispatchedAt = record.DispatchedAt,
            TransportMode = record.TransportMode,
            Resident = record.Resident,
            WorkspaceId = record.WorkspaceId,
            PaneId = record.PaneId,
            Reader = record.Reader,
            Cwd = record.Cwd,
            Kind = record.Kind,
            LaunchArguments = record.LaunchArguments,
            Ruling = record.Ruling,
            ReportArrived = record.ReportArrived,
            ReportStatus = record.ReportStatus,
            ReportArtifact = record.ReportArtifact,
            ReportSummary = record.ReportSummary,
            ReportedAt = record.ReportedAt,
            Disposition = record.Disposition,
        };
        var line = JsonSerializer.Serialize(eventRecord, JsonOptions);
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(line);
            return new NotifyPendingStoreWriteResult(true, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new NotifyPendingStoreWriteResult(false, path, exception.Message);
        }
    }

    private static Dictionary<string, NotifyPendingDelegation> ReadCurrent(string path, out string? error)
    {
        error = null;
        var current = new Dictionary<string, NotifyPendingDelegation>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return current;
        }

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var eventRecord = JsonSerializer.Deserialize<NotifyPendingEvent>(line, JsonOptions)
                    ?? throw new InvalidDataException("pending record line was empty.");
                if (eventRecord.Event is not (DispatchEvent or ReportEvent or DispositionEvent)
                    || string.IsNullOrWhiteSpace(eventRecord.TaskId))
                {
                    throw new InvalidDataException("pending record line has an unsupported event shape.");
                }

                var record = eventRecord.ToRecord();
                if (eventRecord.Event == DispatchEvent)
                {
                    current[record.TaskId] = record with
                    {
                        ReportArrived = false,
                        ReportStatus = null,
                        ReportArtifact = null,
                        ReportSummary = null,
                        ReportedAt = null,
                        Disposition = null,
                    };
                    continue;
                }

                if (!current.TryGetValue(record.TaskId, out var existing))
                {
                    throw new InvalidDataException(
                        $"report event for task '{record.TaskId}' has no preceding dispatch.");
                }

                if (eventRecord.Event == ReportEvent)
                {
                    current[record.TaskId] = record with
                    {
                        ReportArrived = true,
                        Disposition = record.Disposition ?? existing.Disposition,
                    };
                    continue;
                }

                if (eventRecord.Disposition is null || !existing.IsOpen)
                {
                    throw new InvalidDataException(
                        $"disposition event for task '{record.TaskId}' does not settle an open dispatch.");
                }

                current[record.TaskId] = record with
                {
                    ReportArrived = false,
                    Disposition = eventRecord.Disposition,
                };
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = $"Pending delegation store '{path}' could not be read: {exception.Message}";
        }

        return current;
    }

    private static IReadOnlyList<string> ResolveCandidatePaths(string routingRoot, string? domain, string? team)
    {
        var root = Path.GetFullPath(routingRoot);
        if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(team))
        {
            return [ResolvePath(root, domain, team)];
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            var domainRoot = Path.Combine(root, RelativeDirectory, ValidateSegment(domain, "domain"));
            return !Directory.Exists(domainRoot)
                ? []
                : Directory.EnumerateFiles(domainRoot, FileName, SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            throw new ArgumentException("--team cannot be supplied without --domain.");
        }

        var notifyRoot = Path.Combine(root, RelativeDirectory);
        if (!Directory.Exists(notifyRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(notifyRoot, FileName, SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ValidateSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} must be a safe path segment.", name);
        }

        return value;
    }

    private sealed record NotifyPendingEvent
    {
        [JsonPropertyName("event")] public required string Event { get; init; }
        [JsonPropertyName("domain")] public required string Domain { get; init; }
        [JsonPropertyName("team")] public required string Team { get; init; }
        [JsonPropertyName("task_id")] public required string TaskId { get; init; }
        [JsonPropertyName("delegating_role")] public string? DelegatingRole { get; init; }
        [JsonPropertyName("recipient_role")] public required string RecipientRole { get; init; }
        [JsonPropertyName("report_to_role")] public string? ReportToRole { get; init; }
        [JsonPropertyName("recipient_identity")] public required string RecipientIdentity { get; init; }
        [JsonPropertyName("expected_artifact")] public required string ExpectedArtifact { get; init; }
        [JsonPropertyName("expected_artifacts")] public IReadOnlyList<string>? ExpectedArtifacts { get; init; }
        [JsonPropertyName("objective")] public string? Objective { get; init; }
        [JsonPropertyName("inputs")] public IReadOnlyList<string>? Inputs { get; init; }
        [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
        [JsonPropertyName("dispatched_at")] public required DateTimeOffset DispatchedAt { get; init; }
        [JsonPropertyName("transport_mode")] public string? TransportMode { get; init; }
        [JsonPropertyName("resident")] public string? Resident { get; init; }
        [JsonPropertyName("workspace_id")] public string? WorkspaceId { get; init; }
        [JsonPropertyName("pane_id")] public string? PaneId { get; init; }
        [JsonPropertyName("reader")] public string? Reader { get; init; }
        [JsonPropertyName("cwd")] public string? Cwd { get; init; }
        [JsonPropertyName("kind")] public string? Kind { get; init; }
        [JsonPropertyName("launch_args")] public IReadOnlyList<string>? LaunchArguments { get; init; }
        [JsonPropertyName("ruling")] public NotifyRuling? Ruling { get; init; }
        [JsonPropertyName("report_arrived")] public bool ReportArrived { get; init; }
        [JsonPropertyName("report_status")] public string? ReportStatus { get; init; }
        [JsonPropertyName("report_artifact")] public string? ReportArtifact { get; init; }
        [JsonPropertyName("report_summary")] public string? ReportSummary { get; init; }
        [JsonPropertyName("reported_at")] public DateTimeOffset? ReportedAt { get; init; }
        [JsonPropertyName("disposition")] public NotifyPendingDisposition? Disposition { get; init; }

        public NotifyPendingDelegation ToRecord() => new()
        {
            Domain = Domain,
            Team = Team,
            TaskId = TaskId,
            DelegatingRole = DelegatingRole,
            RecipientRole = RecipientRole,
            ReportToRole = ReportToRole,
            RecipientIdentity = RecipientIdentity,
            ExpectedArtifact = ExpectedArtifact,
            ExpectedArtifacts = ExpectedArtifacts,
            Objective = Objective,
            Inputs = Inputs,
            ResultNonce = ResultNonce,
            DispatchedAt = DispatchedAt,
            TransportMode = TransportMode,
            Resident = Resident,
            WorkspaceId = WorkspaceId,
            PaneId = PaneId,
            Reader = Reader,
            Cwd = Cwd,
            Kind = Kind,
            LaunchArguments = LaunchArguments,
            Ruling = Ruling,
            ReportArrived = ReportArrived,
            ReportStatus = ReportStatus,
            ReportArtifact = ReportArtifact,
            ReportSummary = ReportSummary,
            ReportedAt = ReportedAt,
            Disposition = Disposition,
        };
    }

}
