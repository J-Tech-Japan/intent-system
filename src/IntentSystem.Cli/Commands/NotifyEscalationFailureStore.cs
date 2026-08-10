using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// A failed reader append cannot be written to the reader that rejected it.
/// Preserve that genuine dead letter separately so measured supervision does
/// not confuse it with an appended historical event or clear it as a migrated
/// false positive.
/// </summary>
internal static class NotifyEscalationFailureStore
{
    public const string FileName = "escalation-append-failures.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Sync = new();

    public static string ResolvePath(string routingRoot, string domain, string team) =>
        Path.Combine(
            Path.GetDirectoryName(NotifyReportOutboxStore.ResolvePath(routingRoot, domain, team))!,
            FileName);

    public static NotifyEscalationFailureWriteResult Append(
        string routingRoot,
        NotifyEscalationAppendFailure failure)
    {
        var path = ResolvePath(routingRoot, failure.Domain, failure.Team);
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(
                    path,
                    JsonSerializer.Serialize(failure, JsonOptions) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return new NotifyEscalationFailureWriteResult(true, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NotifyEscalationFailureWriteResult(false, path, exception.Message);
            }
        }
    }

    public static IReadOnlyList<NotifyEscalationAppendFailure> Read(
        string routingRoot,
        string domain,
        string team,
        out string? error)
    {
        var path = ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            error = null;
            return [];
        }

        try
        {
            var failures = File.ReadLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<NotifyEscalationAppendFailure>(line, JsonOptions)
                    ?? throw new InvalidDataException("An escalation append failure record was empty."))
                .ToArray();
            error = null;
            return failures;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = $"Escalation append failure store '{path}' could not be read: {exception.Message}";
            return [];
        }
    }
}

internal sealed record NotifyEscalationAppendFailure
{
    [JsonPropertyName("timestamp")] public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("from_role")] public required string FromRole { get; init; }
    [JsonPropertyName("artifact")] public required string Artifact { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("reader_path")] public required string ReaderPath { get; init; }
    [JsonPropertyName("delivery_basis")] public required string DeliveryBasis { get; init; }
    [JsonPropertyName("error")] public required string Error { get; init; }
}

internal sealed record NotifyEscalationFailureWriteResult(bool Written, string Path, string? Error);
