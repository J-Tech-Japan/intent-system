using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G812's completion-driven edge ledger. It records delivery, receipt and
/// consumption separately while using the same task/nonce identity as G811;
/// it is not a transport and cannot grant claim, review, merge or WIP power.
/// </summary>
internal sealed record ProgressHandoffEdge
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; init; } = ProgressSupervisionConstants.SchemaVersion;
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("unit")] public required string Unit { get; init; }
    [JsonPropertyName("head")] public required string Head { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public required string ResultNonce { get; init; }
    [JsonPropertyName("event_id")] public required string EventId { get; init; }
    [JsonPropertyName("sender")] public required string Sender { get; init; }
    [JsonPropertyName("recipient")] public required string Recipient { get; init; }
    [JsonPropertyName("recipient_identity")] public required string RecipientIdentity { get; init; }
    [JsonPropertyName("eligibility_at")] public required DateTimeOffset EligibilityAt { get; init; }
    [JsonPropertyName("deadline_at")] public required DateTimeOffset DeadlineAt { get; init; }
    [JsonPropertyName("delivery_attempts")] public int DeliveryAttempts { get; init; }
    [JsonPropertyName("delivery_result")] public string DeliveryResult { get; init; } = "pending";
    [JsonPropertyName("delivered_at")] public DateTimeOffset? DeliveredAt { get; init; }
    [JsonPropertyName("receipt_id")] public string? ReceiptId { get; init; }
    [JsonPropertyName("receipt_at")] public DateTimeOffset? ReceiptAt { get; init; }
    [JsonPropertyName("consumption_at")] public DateTimeOffset? ConsumptionAt { get; init; }
    [JsonPropertyName("consumption_result")] public string? ConsumptionResult { get; init; }
    [JsonPropertyName("successor_edge_ids")] public IReadOnlyList<string> SuccessorEdgeIds { get; init; } = [];
    [JsonPropertyName("failure")] public string? Failure { get; init; }
    [JsonPropertyName("recovery")] public string? Recovery { get; init; }

    public string IdempotencyKey() => string.Join("/", Domain, Team, Repo, Unit, Head, TaskId, ResultNonce, RecipientIdentity);
}

internal sealed record ProgressWorkState
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; init; } = ProgressSupervisionConstants.SchemaVersion;
    [JsonPropertyName("project")] public required string Project { get; init; }
    [JsonPropertyName("unit")] public required string Unit { get; init; }
    [JsonPropertyName("phase")] public required string Phase { get; init; }
    [JsonPropertyName("predecessor_evidence")] public IReadOnlyList<string> PredecessorEvidence { get; init; } = [];
    [JsonPropertyName("permitted_next_transition")] public required string PermittedNextTransition { get; init; }
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("eligible_at")] public required DateTimeOffset EligibleAt { get; init; }
    [JsonPropertyName("deadline_at")] public required DateTimeOffset DeadlineAt { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
}

internal sealed record ProgressHandoffWriteResult(bool Written, bool AlreadyConverged, string Path, string? Error);

internal static class ProgressHandoffStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string ResolvePath(string routingRoot, string domain, string team) =>
        Path.Combine(ProgressSupervisionStore.ResolveDirectory(routingRoot, domain, team), "handoffs.jsonl");

    public static ProgressHandoffWriteResult Emit(string routingRoot, ProgressHandoffEdge edge, bool write = true)
    {
        var path = ResolvePath(routingRoot, edge.Domain, edge.Team);
        if (!write) return new(false, false, path, null);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var current = Read(path, out var error);
        if (error is not null) return new(false, false, path, error);
        var existing = current.FirstOrDefault(candidate => candidate.IdempotencyKey() == edge.IdempotencyKey());
        if (existing is not null) return new(true, true, path, null);
        File.AppendAllText(path, JsonSerializer.Serialize(edge, JsonOptions) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new(true, false, path, null);
    }

    public static ProgressHandoffWriteResult RecordReceipt(string routingRoot, ProgressHandoffEdge edge, string receiptId, DateTimeOffset at, bool write = true)
    {
        if (string.IsNullOrWhiteSpace(receiptId)) return new(false, false, ResolvePath(routingRoot, edge.Domain, edge.Team), "receipt-id-required");
        var updated = edge with { ReceiptId = receiptId, ReceiptAt = at.ToUniversalTime(), DeliveryResult = "received" };
        return AppendTransition(routingRoot, updated, write);
    }

    public static ProgressHandoffWriteResult RecordConsumption(string routingRoot, ProgressHandoffEdge edge, string result, DateTimeOffset at, bool write = true)
    {
        if (string.IsNullOrWhiteSpace(result)) return new(false, false, ResolvePath(routingRoot, edge.Domain, edge.Team), "consumption-result-required");
        var updated = edge with { ConsumptionAt = at.ToUniversalTime(), ConsumptionResult = result };
        return AppendTransition(routingRoot, updated, write);
    }

    public static IReadOnlyList<ProgressHandoffEdge> Read(string path, out string? error)
    {
        var records = new List<ProgressHandoffEdge>();
        error = null;
        if (!File.Exists(path)) return records;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { records.Add(JsonSerializer.Deserialize<ProgressHandoffEdge>(line, JsonOptions)!); }
            catch (JsonException exception) { error = exception.Message; return records; }
        }
        return records;
    }

    private static ProgressHandoffWriteResult AppendTransition(string root, ProgressHandoffEdge edge, bool write)
    {
        var path = ResolvePath(root, edge.Domain, edge.Team);
        if (!write) return new(false, false, path, null);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var current = Read(path, out var error);
        if (error is not null) return new(false, false, path, error);
        var existing = current.LastOrDefault(candidate => candidate.IdempotencyKey() == edge.IdempotencyKey());
        if (existing is not null && existing.ReceiptId == edge.ReceiptId && existing.ConsumptionAt == edge.ConsumptionAt)
            return new(true, true, path, null);
        File.AppendAllText(path, JsonSerializer.Serialize(edge, JsonOptions) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new(true, false, path, null);
    }
}
