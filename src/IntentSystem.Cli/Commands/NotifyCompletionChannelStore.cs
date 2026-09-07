using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G811 durable completion-channel receipts.  These stores deliberately live
/// beside the existing notify ledgers and are append-only snapshots keyed by
/// the completion identity; a reader acknowledgement is never inferred from
/// an event append alone.
/// </summary>
internal static class NotifyCompletionChannelStore
{
    private const string RelativeDirectory = ".intent-cli/notify";
    private const string ReceiptFileName = "consumption-receipts.jsonl";
    private const string AckFileName = "return-acks.jsonl";
    private const string ReceiptEvent = "consumption-receipt";
    private const string AckEvent = "return-ack";
    private const string AckConsumedEvent = "return-ack-consumed";
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string ResolveReceiptPath(string routingRoot, string domain, string team) =>
        ResolvePath(routingRoot, domain, team, ReceiptFileName);

    internal static string ResolveAckPath(string routingRoot, string domain, string team) =>
        ResolvePath(routingRoot, domain, team, AckFileName);

    internal static string CompletionIdentity(string taskId, string? resultNonce, string artifact) =>
        string.IsNullOrWhiteSpace(resultNonce)
            ? $"{taskId}:artifact:{artifact}"
            : $"{taskId}:{resultNonce}:{artifact}";

    internal static string BuildReceiptId(string taskId, string? resultNonce, string artifact, string role, string cursor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{taskId}|{resultNonce}|{artifact}|{role}"))).ToLowerInvariant();

    internal static NotifyConsumptionReceiptReadResult FindReceipt(
        string routingRoot,
        string domain,
        string team,
        string taskId,
        string? resultNonce,
        string artifact,
        string role,
        string cursor)
    {
        var path = ResolveReceiptPath(routingRoot, domain, team);
        lock (Sync)
        {
            var current = ReadReceipts(path, out var error);
            var receipt = current.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal)
                && string.Equals(candidate.ResultNonce, resultNonce, StringComparison.Ordinal)
                && string.Equals(candidate.Artifact, artifact, StringComparison.Ordinal)
                && string.Equals(candidate.Role, role, StringComparison.Ordinal)
                && string.Equals(candidate.Cursor, cursor, StringComparison.Ordinal));
            return new NotifyConsumptionReceiptReadResult(error is null, path, receipt, error);
        }
    }

    internal static NotifyCompletionWriteResult WriteReceipt(
        string routingRoot,
        NotifyConsumptionReceipt receipt,
        bool write)
    {
        var path = ResolveReceiptPath(routingRoot, receipt.Domain, receipt.Team);
        lock (Sync)
        {
            var current = ReadReceipts(path, out var error);
            if (error is not null)
            {
                return new NotifyCompletionWriteResult(false, false, path, error);
            }

            if (current.TryGetValue(ReceiptKey(receipt), out var existing))
            {
                // A replay is keyed by completion identity and role.  Cursor,
                // reader path, and observation time are evidence on that
                // identity, not a second identity; never reject a retry merely
                // because it was observed on a later canonical turn.
                return new NotifyCompletionWriteResult(false, true, path, null);
            }

            if (!write)
            {
                return new NotifyCompletionWriteResult(false, false, path, null);
            }

            return Append(path, new NotifyCompletionStoreEvent
            {
                Kind = ReceiptEvent,
                Receipt = receipt,
            });
        }
    }

    internal static NotifyReturnAckReadResult FindAck(
        string routingRoot,
        string domain,
        string team,
        string taskId,
        string? resultNonce,
        string artifact)
    {
        var path = ResolveAckPath(routingRoot, domain, team);
        lock (Sync)
        {
            var current = ReadAcks(path, out var error);
            var ack = current.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal)
                && string.Equals(candidate.ResultNonce, resultNonce, StringComparison.Ordinal)
                && string.Equals(candidate.Artifact, artifact, StringComparison.Ordinal));
            return new NotifyReturnAckReadResult(error is null, path, ack, error);
        }
    }

    internal static NotifyCompletionWriteResult WriteAck(
        string routingRoot,
        NotifyReturnAck ack,
        bool write)
    {
        var path = ResolveAckPath(routingRoot, ack.Domain, ack.Team);
        lock (Sync)
        {
            var current = ReadAcks(path, out var error);
            if (error is not null)
            {
                return new NotifyCompletionWriteResult(false, false, path, error);
            }

            if (current.TryGetValue(AckKey(ack), out var existing))
            {
                if (!string.Equals(existing.ConsumptionReceiptId, ack.ConsumptionReceiptId, StringComparison.Ordinal)
                    || !string.Equals(existing.ConsumptionCursor, ack.ConsumptionCursor, StringComparison.Ordinal))
                {
                    return new NotifyCompletionWriteResult(false, false, path,
                        "A different consumption receipt already exists for this completion identity.");
                }

                return new NotifyCompletionWriteResult(false, true, path,
                    existing.ConsumedAt is null || ack.ConsumedAt is null || existing.ConsumedAt == ack.ConsumedAt
                        ? null
                        : "A different return acknowledgement already exists for this completion identity.");
            }

            if (!write)
            {
                return new NotifyCompletionWriteResult(false, false, path, null);
            }

            return Append(path, new NotifyCompletionStoreEvent
            {
                Kind = AckEvent,
                Ack = ack,
            });
        }
    }

    internal static NotifyCompletionWriteResult ConsumeAck(
        string routingRoot,
        NotifyReturnAck ack,
        DateTimeOffset consumedAt,
        bool write)
    {
        var path = ResolveAckPath(routingRoot, ack.Domain, ack.Team);
        lock (Sync)
        {
            var current = ReadAcks(path, out var error);
            if (error is not null)
            {
                return new NotifyCompletionWriteResult(false, false, path, error);
            }

            if (!current.TryGetValue(AckKey(ack), out var existing))
            {
                return new NotifyCompletionWriteResult(false, false, path, "Return acknowledgement is not available.");
            }

            if (existing.ConsumedAt is not null)
            {
                return new NotifyCompletionWriteResult(false, true, path, null);
            }

            if (!write)
            {
                return new NotifyCompletionWriteResult(false, false, path, null);
            }

            return Append(path, new NotifyCompletionStoreEvent
            {
                Kind = AckConsumedEvent,
                Ack = existing with { ConsumedAt = consumedAt },
            });
        }
    }

    internal static IReadOnlyList<NotifyReturnAck> ReadAllAcks(
        string routingRoot,
        string domain,
        string team,
        out string? error)
    {
        var path = ResolveAckPath(routingRoot, domain, team);
        lock (Sync)
        {
            var current = ReadAcks(path, out error);
            return error is null ? current.Values.ToArray() : [];
        }
    }

    private static string ResolvePath(string routingRoot, string domain, string team, string fileName)
    {
        ValidateSegment(domain, "domain");
        ValidateSegment(team, "team");
        return Path.GetFullPath(Path.Combine(routingRoot, RelativeDirectory, domain, team, fileName));
    }

    private static NotifyCompletionWriteResult Append(string path, NotifyCompletionStoreEvent item)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(item, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
            return new NotifyCompletionWriteResult(true, false, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new NotifyCompletionWriteResult(false, false, path, exception.Message);
        }
    }

    private static Dictionary<string, NotifyConsumptionReceipt> ReadReceipts(string path, out string? error)
    {
        error = null;
        var current = new Dictionary<string, NotifyConsumptionReceipt>(StringComparer.Ordinal);
        if (!File.Exists(path)) return current;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonSerializer.Deserialize<NotifyCompletionStoreEvent>(line, JsonOptions)
                    ?? throw new InvalidDataException("completion receipt line was empty.");
                if (item.Receipt is not null) current[ReceiptKey(item.Receipt)] = item.Receipt;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = $"Completion receipt store '{path}' could not be read: {exception.Message}";
        }
        return current;
    }

    private static Dictionary<string, NotifyReturnAck> ReadAcks(string path, out string? error)
    {
        error = null;
        var current = new Dictionary<string, NotifyReturnAck>(StringComparer.Ordinal);
        if (!File.Exists(path)) return current;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonSerializer.Deserialize<NotifyCompletionStoreEvent>(line, JsonOptions)
                    ?? throw new InvalidDataException("return acknowledgement line was empty.");
                if (item.Ack is null) continue;
                current[AckKey(item.Ack)] = item.Ack;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = $"Return acknowledgement store '{path}' could not be read: {exception.Message}";
        }
        return current;
    }

    private static string ReceiptKey(NotifyConsumptionReceipt receipt) =>
        $"{CompletionIdentity(receipt.TaskId, receipt.ResultNonce, receipt.Artifact)}:{receipt.Role}";

    private static string AckKey(NotifyReturnAck ack) =>
        CompletionIdentity(ack.TaskId, ack.ResultNonce, ack.Artifact);

    private static void ValidateSegment(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Contains('/') || value.Contains('\\') || value.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"{label} must be a safe path segment.", label);
    }
}

internal sealed record NotifyConsumptionReceipt
{
    [JsonPropertyName("receipt_id")] public required string ReceiptId { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("artifact")] public required string Artifact { get; init; }
    [JsonPropertyName("cursor")] public required string Cursor { get; init; }
    [JsonPropertyName("reader_path")] public required string ReaderPath { get; init; }
    [JsonPropertyName("consumed_at")] public required DateTimeOffset ConsumedAt { get; init; }
}

internal sealed record NotifyReturnAck
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("artifact")] public required string Artifact { get; init; }
    [JsonPropertyName("steward_role")] public required string StewardRole { get; init; }
    [JsonPropertyName("recipient_role")] public required string RecipientRole { get; init; }
    [JsonPropertyName("recipient_identity")] public required string RecipientIdentity { get; init; }
    [JsonPropertyName("resident")] public required string Resident { get; init; }
    [JsonPropertyName("workspace_id")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("pane_id")] public string? PaneId { get; init; }
    [JsonPropertyName("consumption_receipt_id")] public required string ConsumptionReceiptId { get; init; }
    [JsonPropertyName("consumption_cursor")] public required string ConsumptionCursor { get; init; }
    [JsonPropertyName("available_at")] public required DateTimeOffset AvailableAt { get; init; }
    [JsonPropertyName("consumed_at")] public DateTimeOffset? ConsumedAt { get; init; }
}

internal sealed record NotifyCompletionStoreEvent
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("receipt")] public NotifyConsumptionReceipt? Receipt { get; init; }
    [JsonPropertyName("ack")] public NotifyReturnAck? Ack { get; init; }
}

internal sealed record NotifyCompletionWriteResult(bool Written, bool AlreadyConverged, string Path, string? Error);
internal sealed record NotifyConsumptionReceiptReadResult(bool Resolved, string Path, NotifyConsumptionReceipt? Receipt, string? Error);
internal sealed record NotifyReturnAckReadResult(bool Resolved, string Path, NotifyReturnAck? Ack, string? Error);
