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
        lock (Sync)
        {
            var current = ReadCurrent(path, out var error);
            if (error is not null)
            {
                return new NotifyReportOutboxWriteResult(false, path, error);
            }

            if (current.ContainsKey(entry.TaskId))
            {
                return new NotifyReportOutboxWriteResult(false, path, $"An outbox entry already exists for task '{entry.TaskId}'.");
            }

            return Append(path, new NotifyReportOutboxEvent { Kind = "record", Entry = entry });
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

    public static NotifyReportOutboxReadResult Find(string routingRoot, string domain, string team, string taskId)
    {
        var path = ResolvePath(routingRoot, domain, team);
        lock (Sync)
        {
            var current = ReadCurrent(path, out var error);
            return new NotifyReportOutboxReadResult(error is null, path, current.GetValueOrDefault(taskId), error);
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
                if (item.Entry is not null) current[item.Entry.TaskId] = item.Entry;
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
}

internal sealed record NotifyReportOutboxEntry
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
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

internal sealed record NotifyReportOutboxWriteResult(bool Written, string Path, string? Error);
internal sealed record NotifyReportOutboxReadResult(bool Resolved, string Path, NotifyReportOutboxEntry? Entry, string? Error);
