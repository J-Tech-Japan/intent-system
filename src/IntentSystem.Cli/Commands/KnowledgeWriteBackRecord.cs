using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G564: the canonical, machine-readable record that a packet-declared
/// knowledge write-back was PERFORMED, carrying the host commit as evidence.
///
/// The write-back itself is design's host-side act (G300 stands — nothing here
/// writes intent content). What was missing was any durable statement that it
/// happened: the evidence lived only in host commits the detection layer
/// cannot see, so nothing could say "not done" and the tree fell weeks behind
/// development with no structural signal.
///
/// One record per execution unit at
/// <c>.intent-cli/knowledge-writebacks/&lt;unit&gt;/record.json</c>, written
/// only by <see cref="AutomationKnowledgeWriteBackRecordCommand"/> — hand
/// editing is not the path, and a record whose evidence conflicts with an
/// existing one is refused rather than overwritten.
/// </summary>
internal sealed record KnowledgeWriteBackRecord
{
    public const string ArtifactKindValue = "knowledge-writeback-record";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    [JsonPropertyName("artifact_kind")]
    public required string ArtifactKind { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    /// <summary>The host commit the write-back landed in — the evidence.</summary>
    [JsonPropertyName("host_commit")]
    public required string HostCommit { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>
    /// Paths actually written, as reported by the recorder. Optional: the
    /// packet's declared targets remain the contract, and a recorder that
    /// names nothing still produces a valid record.
    /// </summary>
    [JsonPropertyName("targets")]
    public required IReadOnlyList<string> Targets { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>
    /// Repo-relative artifact path for <paramref name="executionUnit"/>,
    /// always in forward-slash form so it reads identically on every platform
    /// and in every report.
    /// </summary>
    public static string ResolveRelativePath(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        return $".intent-cli/knowledge-writebacks/{executionUnit}/record.json";
    }

    public static string ResolveFullPath(string repoRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        return Path.GetFullPath(Path.Combine(
            repoRoot,
            ResolveRelativePath(executionUnit).Replace('/', Path.DirectorySeparatorChar)));
    }

    public static string Serialize(KnowledgeWriteBackRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, SerializeOptions);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> on a payload that is not
    /// a well-formed record of this kind. A record on disk that cannot be read
    /// is never treated as absent: absence means "not written back yet", and
    /// silently downgrading unreadable evidence to that would re-open the
    /// false-clearance path this artifact closes.
    /// </summary>
    public static KnowledgeWriteBackRecord Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        KnowledgeWriteBackRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<KnowledgeWriteBackRecord>(json, SerializeOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Knowledge write-back record is not valid JSON: {exception.Message}");
        }

        if (record is null)
        {
            throw new InvalidOperationException("Knowledge write-back record payload deserialized to null.");
        }

        if (!string.Equals(record.ArtifactKind, ArtifactKindValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Knowledge write-back record declares artifact_kind '{record.ArtifactKind}', expected '{ArtifactKindValue}'.");
        }

        if (string.IsNullOrWhiteSpace(record.ExecutionUnit) || string.IsNullOrWhiteSpace(record.HostCommit))
        {
            throw new InvalidOperationException(
                "Knowledge write-back record is missing 'execution_unit' or 'host_commit' — a record without "
                + "evidence is not a record.");
        }

        // `targets: null` satisfies the required-member check but would hand
        // callers a null list; an unnamed target set is empty, not absent.
        return record.Targets is null
            ? record with { Targets = Array.Empty<string>() }
            : record;
    }
}
