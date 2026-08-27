using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Sender-side durable report state. A report is recorded before a transport
/// attempt so a failed delivery can be collected without asking a recipient
/// to repeat work or re-dispatching its task.
/// </summary>
internal static class NotifyReportOutboxStore
{
    private const string RelativeDirectory = ".intent-cli/notify";
    private const string FileName = "report-outbox.jsonl";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Sync = new();

    internal static Func<string, string, NotifyReportOutboxWriteResult>? WriteOverride { get; set; }

    public static string ResolvePath(string routingRoot, string domain, string team) => Path.GetFullPath(Path.Combine(
        routingRoot, RelativeDirectory, ValidateSegment(domain), ValidateSegment(team), FileName));

    public static NotifyReportOutboxWriteResult WriteNew(string routingRoot, NotifyReportOutboxEntry entry)
    {
        var path = ResolvePath(routingRoot, entry.Domain, entry.Team);
        var persisted = string.IsNullOrWhiteSpace(entry.EntryId)
            ? entry with { EntryId = Guid.NewGuid().ToString("N") }
            : entry;
        lock (Sync)
        {
            var current = ReadCurrent(path, out var error);
            if (error is not null)
            {
                return new NotifyReportOutboxWriteResult(false, path, error);
            }

            var existingGeneration = current.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.TaskId, persisted.TaskId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(persisted.ResultNonce)
                && string.Equals(candidate.ResultNonce, persisted.ResultNonce, StringComparison.Ordinal));
            if (existingGeneration is not null)
            {
                var state = string.Equals(existingGeneration.DeliveryState, "undelivered", StringComparison.Ordinal)
                    ? $"An undelivered report outbox entry already exists for task '{persisted.TaskId}' and its current dispatch generation. "
                      + $"Recover it with '{BuildCollectCommand(routingRoot, persisted)}'; do not re-delegate the task."
                    : $"A report outbox entry already exists for task '{persisted.TaskId}' and its current dispatch generation.";
                return new NotifyReportOutboxWriteResult(false, path, state);
            }

            var write = Append(path, new NotifyReportOutboxEvent { Kind = "record", Entry = persisted });
            return write with { Entry = write.Written ? persisted : null };
        }
    }

    public static NotifyReportOutboxWriteResult MarkUndelivered(string routingRoot, NotifyReportOutboxEntry entry, string error) =>
        Append(ResolvePath(routingRoot, entry.Domain, entry.Team), new NotifyReportOutboxEvent
        {
            Kind = "delivery-failed",
            Entry = entry with { DeliveryState = "undelivered", DeliveryError = error, LastAttemptAt = NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow },
        });

    public static NotifyReportOutboxWriteResult MarkDelivered(string routingRoot, NotifyReportOutboxEntry entry) =>
        Append(ResolvePath(routingRoot, entry.Domain, entry.Team), new NotifyReportOutboxEvent
        {
            Kind = "delivered",
            Entry = entry with { DeliveryState = "delivered", DeliveryError = null, LastAttemptAt = NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow, DeliveredAt = NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow },
        });

    public static NotifyReportOutboxReadResult Find(string routingRoot, string domain, string team, string taskId) =>
        FindCurrent(routingRoot, domain, team, taskId, resultNonce: null, matchGeneration: false);

    public static NotifyReportOutboxReadResult Find(
        string routingRoot,
        string domain,
        string team,
        string taskId,
        string? resultNonce) =>
        FindCurrent(routingRoot, domain, team, taskId, resultNonce, matchGeneration: true);

    public static NotifyReportOutboxReadResult FindUndelivered(
        string routingRoot,
        string domain,
        string team,
        string taskId) =>
        FindCurrent(routingRoot, domain, team, taskId, resultNonce: null, matchGeneration: false, undeliveredOnly: true);

    private static NotifyReportOutboxReadResult FindCurrent(
        string routingRoot,
        string domain,
        string team,
        string taskId,
        string? resultNonce,
        bool matchGeneration,
        bool undeliveredOnly = false)
    {
        var path = ResolvePath(routingRoot, domain, team);
        lock (Sync)
        {
            var current = ReadCurrent(path, out var error);
            var entry = current.Values
                .Where(candidate => string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal)
                    && (!matchGeneration || string.Equals(candidate.ResultNonce, resultNonce, StringComparison.Ordinal))
                    && (!undeliveredOnly || string.Equals(candidate.DeliveryState, "undelivered", StringComparison.Ordinal)))
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefault();
            return new NotifyReportOutboxReadResult(error is null, path, entry, error);
        }
    }

    public static IReadOnlyList<NotifyReportOutboxEntry> ReadUndelivered(string routingRoot, string domain, string team, out string? error)
    {
        var path = ResolvePath(routingRoot, domain, team);
        lock (Sync)
        {
            var current = ReadCurrent(path, out error);
            return error is null
                ? current.Values.Where(entry => string.Equals(entry.DeliveryState, "undelivered", StringComparison.Ordinal)).ToArray()
                : [];
        }
    }

    private static Dictionary<string, NotifyReportOutboxEntry> ReadCurrent(string path, out string? error)
    {
        error = null;
        var current = new Dictionary<string, NotifyReportOutboxEntry>(StringComparer.Ordinal);
        if (!File.Exists(path)) return current;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonSerializer.Deserialize<NotifyReportOutboxEvent>(line, JsonOptions)
                    ?? throw new InvalidDataException("A report outbox event was empty.");
                if (item.Entry is not null) current[EntryKey(item.Entry)] = item.Entry;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = $"Report outbox '{path}' could not be read: {exception.Message}";
        }
        return current;
    }

    private static NotifyReportOutboxWriteResult Append(string path, NotifyReportOutboxEvent item)
    {
        var line = JsonSerializer.Serialize(item, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride) return writeOverride(path, line);
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line, new UTF8Encoding(false));
                return new NotifyReportOutboxWriteResult(true, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NotifyReportOutboxWriteResult(false, path, exception.Message);
            }
        }
    }

    private static string ValidateSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 || value is "." or "..")
            throw new ArgumentException($"Outbox path segment '{value}' is unsafe.");
        return value;
    }

    private static string EntryKey(NotifyReportOutboxEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.EntryId)
            ? entry.EntryId
            : $"legacy:{entry.TaskId}:{entry.ResultNonce ?? string.Empty}";

    public static string BuildCollectCommand(string routingRoot, NotifyReportOutboxEntry entry) =>
        BuildCollectCommand(routingRoot, routingRoot, entry);

    public static string BuildCollectCommand(string routingRoot, string reportRoot, NotifyReportOutboxEntry entry)
    {
        var reportRootArgument = string.Equals(
            Path.GetFullPath(reportRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(routingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.Ordinal)
            ? string.Empty
            : $" --report-root {reportRoot}";
        return $"intent-cli notify collect --domain {entry.Domain} --team {entry.Team} --task-id {entry.TaskId} --write --routing-root {routingRoot}{reportRootArgument}";
    }
}

internal sealed record NotifyReportOutboxEntry
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("entry_id")] public string? EntryId { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("from_role")] public required string FromRole { get; init; }
    [JsonPropertyName("to_role")] public required string ToRole { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("artifact")] public required string Artifact { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("last_attempt_at")] public DateTimeOffset? LastAttemptAt { get; init; }
    [JsonPropertyName("delivered_at")] public DateTimeOffset? DeliveredAt { get; init; }
    [JsonPropertyName("delivery_state")] public required string DeliveryState { get; init; }
    [JsonPropertyName("delivery_error")] public string? DeliveryError { get; init; }
}

internal sealed record NotifyReportOutboxEvent
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("entry")] public NotifyReportOutboxEntry? Entry { get; init; }
}

internal sealed record NotifyReportOutboxWriteResult(
    bool Written,
    string Path,
    string? Error,
    NotifyReportOutboxEntry? Entry = null);
internal sealed record NotifyReportOutboxReadResult(bool Resolved, string Path, NotifyReportOutboxEntry? Entry, string? Error);
