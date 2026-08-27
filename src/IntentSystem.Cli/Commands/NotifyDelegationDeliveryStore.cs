using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyDelegationDeliveryEvidence
{
    [JsonPropertyName("event")] public required string Event { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("delivery_succeeded")] public required bool DeliverySucceeded { get; init; }
    [JsonPropertyName("delivered_at")] public required DateTimeOffset DeliveredAt { get; init; }
}

internal sealed record NotifyDelegationDeliveryLookup
{
    public required bool Resolved { get; init; }
    public required string Path { get; init; }
    public NotifyDelegationDeliveryEvidence? Evidence { get; init; }
    public string? Error { get; init; }
}

internal sealed record NotifyDelegationDeliveryWriteResult(
    bool Written,
    string Path,
    string? Error);

internal static class NotifyDelegationDeliveryStore
{
    private const string RelativeDirectory = ".intent-cli/notify";
    private const string FileName = "delivery.jsonl";
    private const string DeliveryEvent = "delivery";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Sync = new();

    public static string ResolvePath(string routingRoot, string domain, string team) => Path.GetFullPath(Path.Combine(
        routingRoot,
        RelativeDirectory,
        ValidateSegment(domain, "domain"),
        ValidateSegment(team, "team"),
        FileName));

    public static NotifyDelegationDeliveryWriteResult Write(
        string routingRoot,
        NotifyPendingDelegation record,
        DateTimeOffset deliveredAt)
    {
        string path;
        try
        {
            path = ResolvePath(routingRoot, record.Domain, record.Team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new NotifyDelegationDeliveryWriteResult(
                false,
                Path.Combine(routingRoot, RelativeDirectory, record.Domain, record.Team, FileName),
                exception.Message);
        }

        lock (Sync)
        {
            var current = ReadCurrent(path, out var readError);
            if (readError is not null)
            {
                return new NotifyDelegationDeliveryWriteResult(false, path, readError);
            }

            if (current.ContainsKey(Key(record.TaskId, record.ResultNonce)))
            {
                return new NotifyDelegationDeliveryWriteResult(true, path, null);
            }

            var evidence = new NotifyDelegationDeliveryEvidence
            {
                Event = DeliveryEvent,
                Domain = record.Domain,
                Team = record.Team,
                TaskId = record.TaskId,
                ResultNonce = record.ResultNonce,
                DeliverySucceeded = true,
                DeliveredAt = deliveredAt.ToUniversalTime(),
            };
            var line = JsonSerializer.Serialize(evidence, JsonOptions);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine(line);
                return new NotifyDelegationDeliveryWriteResult(true, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NotifyDelegationDeliveryWriteResult(false, path, exception.Message);
            }
        }
    }

    public static NotifyDelegationDeliveryLookup Find(
        string routingRoot,
        string domain,
        string team,
        string taskId,
        string? resultNonce)
    {
        string path;
        try
        {
            path = ResolvePath(routingRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new NotifyDelegationDeliveryLookup
            {
                Resolved = false,
                Path = Path.Combine(routingRoot, RelativeDirectory, domain, team, FileName),
                Error = exception.Message,
            };
        }

        var current = ReadCurrent(path, out var error);
        return new NotifyDelegationDeliveryLookup
        {
            Resolved = error is null,
            Path = path,
            Evidence = error is null ? current.GetValueOrDefault(Key(taskId, resultNonce)) : null,
            Error = error,
        };
    }

    private static Dictionary<string, NotifyDelegationDeliveryEvidence> ReadCurrent(
        string path,
        out string? error)
    {
        error = null;
        var current = new Dictionary<string, NotifyDelegationDeliveryEvidence>(StringComparer.Ordinal);
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

                var evidence = JsonSerializer.Deserialize<NotifyDelegationDeliveryEvidence>(line, JsonOptions)
                    ?? throw new InvalidDataException("delivery evidence line was empty.");
                if (!string.Equals(evidence.Event, DeliveryEvent, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(evidence.Domain)
                    || string.IsNullOrWhiteSpace(evidence.Team)
                    || string.IsNullOrWhiteSpace(evidence.TaskId)
                    || !evidence.DeliverySucceeded
                    || evidence.DeliveredAt == default)
                {
                    throw new InvalidDataException("delivery evidence line has an unsupported event shape.");
                }

                current[Key(evidence.TaskId, evidence.ResultNonce)] = evidence;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = "Delegation delivery evidence store '" + path + "' could not be read: " + exception.Message;
        }

        return current;
    }

    private static string Key(string taskId, string? resultNonce) =>
        taskId + "|" + (resultNonce ?? string.Empty);

    private static string ValidateSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(name + " must be a safe path segment.", name);
        }

        return value;
    }
}
