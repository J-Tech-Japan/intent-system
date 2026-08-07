using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyPendingStoreWriteResult(bool Written, string Path, string? Error);

internal sealed record NotifyPendingDelegation
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("recipient_role")] public required string RecipientRole { get; init; }
    [JsonPropertyName("recipient_identity")] public required string RecipientIdentity { get; init; }
    [JsonPropertyName("expected_artifact")] public required string ExpectedArtifact { get; init; }
    [JsonPropertyName("dispatched_at")] public required DateTimeOffset DispatchedAt { get; init; }
    [JsonPropertyName("transport_mode")] public string? TransportMode { get; init; }
    [JsonPropertyName("resident")] public string? Resident { get; init; }
    [JsonPropertyName("workspace_id")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("pane_id")] public string? PaneId { get; init; }
    [JsonPropertyName("reader")] public string? Reader { get; init; }
    [JsonPropertyName("report_arrived")] public bool ReportArrived { get; init; }
    [JsonPropertyName("report_status")] public string? ReportStatus { get; init; }
    [JsonPropertyName("report_artifact")] public string? ReportArtifact { get; init; }
    [JsonPropertyName("report_summary")] public string? ReportSummary { get; init; }
    [JsonPropertyName("reported_at")] public DateTimeOffset? ReportedAt { get; init; }
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
/// dispatch snapshot or the matching report snapshot; the latest line for a
/// task is the authoritative state. The append-only shape keeps dispatch and
/// resolution auditable without making the existing six-field event channel a
/// second, incompatible state store.
/// </summary>
internal static class NotifyPendingDelegationStore
{
    private const string RelativeDirectory = ".intent-cli/notify";
    private const string FileName = "pending.jsonl";
    private const string DispatchEvent = "dispatch";
    private const string ReportEvent = "report";

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
        lock (Sync)
        {
            return Append(path, record with
            {
                ReportArrived = true,
                ReportStatus = status,
                ReportArtifact = artifact,
                ReportSummary = summary,
                ReportedAt = reportedAt,
            });
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
                if (!record.ReportArrived)
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

            records.AddRange(current.Values.Where(value => !value.ReportArrived));
        }

        return records.OrderBy(value => value.DispatchedAt).ToArray();
    }

    private static NotifyPendingStoreWriteResult Append(string path, NotifyPendingDelegation record)
    {
        var eventRecord = new NotifyPendingEvent
        {
            Event = record.ReportArrived ? ReportEvent : DispatchEvent,
            Domain = record.Domain,
            Team = record.Team,
            TaskId = record.TaskId,
            RecipientRole = record.RecipientRole,
            RecipientIdentity = record.RecipientIdentity,
            ExpectedArtifact = record.ExpectedArtifact,
            DispatchedAt = record.DispatchedAt,
            TransportMode = record.TransportMode,
            Resident = record.Resident,
            WorkspaceId = record.WorkspaceId,
            PaneId = record.PaneId,
            Reader = record.Reader,
            ReportArrived = record.ReportArrived,
            ReportStatus = record.ReportStatus,
            ReportArtifact = record.ReportArtifact,
            ReportSummary = record.ReportSummary,
            ReportedAt = record.ReportedAt,
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
                if (eventRecord.Event is not (DispatchEvent or ReportEvent)
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
                    };
                    continue;
                }

                if (!current.ContainsKey(record.TaskId))
                {
                    throw new InvalidDataException(
                        $"report event for task '{record.TaskId}' has no preceding dispatch.");
                }

                current[record.TaskId] = record with { ReportArrived = true };
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

        if (!string.IsNullOrWhiteSpace(domain) || !string.IsNullOrWhiteSpace(team))
        {
            throw new ArgumentException("--domain and --team must be supplied together.");
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
        [JsonPropertyName("recipient_role")] public required string RecipientRole { get; init; }
        [JsonPropertyName("recipient_identity")] public required string RecipientIdentity { get; init; }
        [JsonPropertyName("expected_artifact")] public required string ExpectedArtifact { get; init; }
        [JsonPropertyName("dispatched_at")] public required DateTimeOffset DispatchedAt { get; init; }
        [JsonPropertyName("transport_mode")] public string? TransportMode { get; init; }
        [JsonPropertyName("resident")] public string? Resident { get; init; }
        [JsonPropertyName("workspace_id")] public string? WorkspaceId { get; init; }
        [JsonPropertyName("pane_id")] public string? PaneId { get; init; }
        [JsonPropertyName("reader")] public string? Reader { get; init; }
        [JsonPropertyName("report_arrived")] public bool ReportArrived { get; init; }
        [JsonPropertyName("report_status")] public string? ReportStatus { get; init; }
        [JsonPropertyName("report_artifact")] public string? ReportArtifact { get; init; }
        [JsonPropertyName("report_summary")] public string? ReportSummary { get; init; }
        [JsonPropertyName("reported_at")] public DateTimeOffset? ReportedAt { get; init; }

        public NotifyPendingDelegation ToRecord() => new()
        {
            Domain = Domain,
            Team = Team,
            TaskId = TaskId,
            RecipientRole = RecipientRole,
            RecipientIdentity = RecipientIdentity,
            ExpectedArtifact = ExpectedArtifact,
            DispatchedAt = DispatchedAt,
            TransportMode = TransportMode,
            Resident = Resident,
            WorkspaceId = WorkspaceId,
            PaneId = PaneId,
            Reader = Reader,
            ReportArrived = ReportArrived,
            ReportStatus = ReportStatus,
            ReportArtifact = ReportArtifact,
            ReportSummary = ReportSummary,
            ReportedAt = ReportedAt,
        };
    }

}
