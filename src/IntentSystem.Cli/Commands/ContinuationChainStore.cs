using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G695: append-only evidence for the continuation owed by a completion
/// signal. The chain records observations only; it never performs a lifecycle
/// transition. A later wake may add the missing link, which keeps the exact
/// point of a silent stop queryable after the observing process exits.
/// </summary>
internal static class ContinuationChainStore
{
    public const string ReportReceived = "report-received";
    public const string OrchestrationWakeAttempted = "orchestration-wake-attempted";
    public const string WakeDeliveredOrObserved = "wake-delivered-or-observed";
    public const string CanonicalStateClassified = "canonical-state-classified";
    public const string RequiredContinuationStarted = "required-continuation-started";
    public const string NamedBlockerRecorded = "named-blocker-recorded";

    public const string RelativeDirectory = ".intent-cli/continuation-chains";
    public const string FileName = "chains.jsonl";

    private static readonly string[] RequiredLinks =
    [
        ReportReceived,
        OrchestrationWakeAttempted,
        WakeDeliveredOrObserved,
        CanonicalStateClassified,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Sync = new();

    /// <summary>Test seam for exercising a durable-write failure.</summary>
    internal static Func<string, string, ContinuationChainWriteResult>? WriteOverride { get; set; }

    public static string ResolvePath(string routingRoot, string domain, string team) => Path.GetFullPath(Path.Combine(
        routingRoot,
        RelativeDirectory,
        ValidateSegment(domain, "domain"),
        ValidateSegment(team, "team"),
        FileName));

    public static string BuildCompletionSignalId(string taskId, string? resultNonce) =>
        string.IsNullOrWhiteSpace(resultNonce)
            ? $"{taskId}:report"
            : $"{taskId}:{resultNonce}";

    public static string BuildChainId(string completionSignalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completionSignalId);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(completionSignalId));
        return $"continuation-{Convert.ToHexString(digest)[..20].ToLowerInvariant()}";
    }

    public static ContinuationChainReadResult Read(
        string routingRoot,
        string domain,
        string team,
        string? taskId = null,
        string? completionSignalId = null,
        string? chainId = null)
    {
        string path;
        try
        {
            path = ResolvePath(routingRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ContinuationChainReadResult
            {
                Resolved = false,
                Path = routingRoot,
                Records = [],
                Error = exception.Message,
            };
        }

        lock (Sync)
        {
            var current = ReadCurrent(path, out var error);
            if (error is not null)
            {
                return new ContinuationChainReadResult
                {
                    Resolved = false,
                    Path = path,
                    Records = [],
                    Error = error,
                };
            }

            var records = current.Values
                .Where(record => string.IsNullOrWhiteSpace(taskId)
                    || string.Equals(record.TaskId, taskId, StringComparison.Ordinal))
                .Where(record => string.IsNullOrWhiteSpace(completionSignalId)
                    || string.Equals(record.CompletionSignalId, completionSignalId, StringComparison.Ordinal))
                .Where(record => string.IsNullOrWhiteSpace(chainId)
                    || string.Equals(record.ChainId, chainId, StringComparison.Ordinal))
                .OrderBy(record => record.CreatedAt)
                .ThenBy(record => record.ChainId, StringComparer.Ordinal)
                .ToArray();
            return new ContinuationChainReadResult
            {
                Resolved = true,
                Path = path,
                Records = records,
            };
        }
    }

    public static ContinuationChainWriteResult RecordReportReceived(
        string routingRoot,
        string domain,
        string team,
        string taskId,
        string? resultNonce,
        string status,
        string artifact,
        string summary,
        DateTimeOffset? timestamp = null,
        bool write = true,
        string source = "notify-report")
    {
        var signalId = BuildCompletionSignalId(taskId, resultNonce);
        var chainId = BuildChainId(signalId);
        return RecordLink(
            routingRoot,
            domain,
            team,
            signalId,
            taskId,
            chainId,
            ReportReceived,
            source,
            [
                $"status:{status}",
                $"artifact:{artifact}",
                $"summary:{NormalizeEvidence(summary)}",
            ],
            classification: status,
            blocker: null,
            timestamp,
            write,
            status,
            artifact,
            summary,
            resultNonce);
    }

    public static ContinuationChainWriteResult RecordLink(
        string routingRoot,
        string domain,
        string team,
        string completionSignalId,
        string? taskId,
        string? chainId,
        string link,
        string source,
        IReadOnlyList<string> evidence,
        string? classification = null,
        string? blocker = null,
        DateTimeOffset? timestamp = null,
        bool write = true,
        string? status = null,
        string? artifact = null,
        string? summary = null,
        string? resultNonce = null)
    {
        if (string.IsNullOrWhiteSpace(completionSignalId))
        {
            return new ContinuationChainWriteResult(false, false, string.Empty, null, null,
                "completion signal id is required.");
        }

        string path;
        try
        {
            path = ResolvePath(routingRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ContinuationChainWriteResult(false, false, routingRoot, null, null, exception.Message);
        }

        lock (Sync)
        {
            var current = ReadCurrent(path, out var error);
            if (error is not null)
            {
                return new ContinuationChainWriteResult(false, false, path, null, null, error);
            }

            var resolvedChainId = string.IsNullOrWhiteSpace(chainId)
                ? BuildChainId(completionSignalId)
                : chainId;
            var existing = current.Values.FirstOrDefault(record =>
                string.Equals(record.ChainId, resolvedChainId, StringComparison.Ordinal)
                || string.Equals(record.CompletionSignalId, completionSignalId, StringComparison.Ordinal));
            if (existing is not null
                && existing.Links.Any(existingLink => string.Equals(existingLink.Name, link, StringComparison.Ordinal)))
            {
                return new ContinuationChainWriteResult(false, true, path, existing, existing, null);
            }

            var now = (timestamp ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var record = existing ?? new ContinuationChainRecord
            {
                ChainId = resolvedChainId,
                CompletionSignalId = completionSignalId,
                TaskId = taskId ?? completionSignalId,
                Domain = domain,
                Team = team,
                ResultNonce = resultNonce,
                Status = status,
                Artifact = artifact,
                Summary = summary,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var updated = record with
            {
                ResultNonce = record.ResultNonce ?? resultNonce,
                Status = record.Status ?? status,
                Artifact = record.Artifact ?? artifact,
                Summary = record.Summary ?? summary,
                UpdatedAt = now,
                Links =
                [
                    .. record.Links,
                    new ContinuationChainLink
                    {
                        Name = link,
                        Timestamp = now,
                        Source = source,
                        Evidence = evidence.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
                        Classification = classification,
                        Blocker = blocker,
                    },
                ],
            };

            if (!write)
            {
                return new ContinuationChainWriteResult(false, false, path, updated, existing, null);
            }

            var append = Append(path, updated);
            return append with
            {
                Record = append.Applied ? updated : existing,
                Preview = updated,
            };
        }
    }

    private static ContinuationChainWriteResult Append(string path, ContinuationChainRecord record)
    {
        var line = JsonSerializer.Serialize(new ContinuationChainEvent
        {
            Kind = "chain-updated",
            Record = record,
        }, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new ContinuationChainWriteResult(true, false, path, record, record, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ContinuationChainWriteResult(false, false, path, null, record, exception.Message);
        }
    }

    private static Dictionary<string, ContinuationChainRecord> ReadCurrent(string path, out string? error)
    {
        error = null;
        var current = new Dictionary<string, ContinuationChainRecord>(StringComparer.Ordinal);
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

                var entry = JsonSerializer.Deserialize<ContinuationChainEvent>(line, JsonOptions)
                    ?? throw new InvalidDataException("A continuation-chain event was empty.");
                if (entry.Record is not null)
                {
                    current[entry.Record.ChainId] = entry.Record;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            error = $"Continuation-chain state '{path}' could not be read: {exception.Message}";
        }

        return current;
    }

    private static string ValidateSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || value is "." or "..")
        {
            throw new ArgumentException($"Continuation-chain {name} '{value}' is not a safe path segment.", name);
        }

        return value;
    }

    private static string NormalizeEvidence(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string? ComputeNextMissing(IReadOnlyList<ContinuationChainLink> links)
    {
        if (links.Any(link => string.Equals(link.Name, RequiredContinuationStarted, StringComparison.Ordinal)
                || string.Equals(link.Name, NamedBlockerRecorded, StringComparison.Ordinal)))
        {
            return null;
        }

        return RequiredLinks.FirstOrDefault(required =>
            !links.Any(link => string.Equals(link.Name, required, StringComparison.Ordinal)));
    }

    private static bool IsComplete(IReadOnlyList<ContinuationChainLink> links) =>
        links.Any(link => string.Equals(link.Name, RequiredContinuationStarted, StringComparison.Ordinal)
            || string.Equals(link.Name, NamedBlockerRecorded, StringComparison.Ordinal));

    internal static string? NextMissingLink(IReadOnlyList<ContinuationChainLink> links) => ComputeNextMissing(links);

    internal static bool IsChainComplete(IReadOnlyList<ContinuationChainLink> links) => IsComplete(links);
}

internal sealed record ContinuationChainRecord
{
    [JsonPropertyName("chain_id")] public required string ChainId { get; init; }
    [JsonPropertyName("completion_signal_id")] public required string CompletionSignalId { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("artifact")] public string? Artifact { get; init; }
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public required DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("links")] public IReadOnlyList<ContinuationChainLink> Links { get; init; } = [];

    [JsonPropertyName("next_missing_link")]
    public string? NextMissingLink => ContinuationChainStore.NextMissingLink(Links);

    [JsonPropertyName("complete")]
    public bool Complete => ContinuationChainStore.IsChainComplete(Links);
}

internal sealed record ContinuationChainLink
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("timestamp")] public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("evidence")] public IReadOnlyList<string> Evidence { get; init; } = [];
    [JsonPropertyName("classification")] public string? Classification { get; init; }
    [JsonPropertyName("blocker")] public string? Blocker { get; init; }
}

internal sealed record ContinuationChainEvent
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("record")] public required ContinuationChainRecord Record { get; init; }
}

internal sealed record ContinuationChainWriteResult(
    bool Applied,
    bool AlreadyConverged,
    string Path,
    ContinuationChainRecord? Record,
    ContinuationChainRecord? Preview,
    string? Error);

internal sealed record ContinuationChainReadResult
{
    public required bool Resolved { get; init; }
    public required string Path { get; init; }
    public required IReadOnlyList<ContinuationChainRecord> Records { get; init; }
    public string? Error { get; init; }
}
