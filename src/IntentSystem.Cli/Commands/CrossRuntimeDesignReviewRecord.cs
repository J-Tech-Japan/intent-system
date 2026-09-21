using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G835: one recorded design-review verdict, bound to the exact packet digest.
/// </summary>
internal sealed record CrossRuntimeDesignReviewRecord
{
    public const string ArtifactKindValue = "cross-runtime-review-record";

    [JsonPropertyName("artifact_kind")]
    public required string ArtifactKind { get; init; }

    [JsonPropertyName("packet_digest")]
    public required string PacketDigest { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("runtime")]
    public required string Runtime { get; init; }

    [JsonPropertyName("runtime_version")]
    public required string RuntimeVersion { get; init; }

    [JsonPropertyName("conductor_runtime")]
    public required string ConductorRuntime { get; init; }

    [JsonPropertyName("relation")]
    public required string Relation { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Effort { get; init; }

    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    [JsonPropertyName("blocking_findings")]
    public required IReadOnlyList<CrossRuntimeReviewFinding> BlockingFindings { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }

    [JsonPropertyName("raw_verdict_file")]
    public required string RawVerdictFile { get; init; }

    [JsonPropertyName("raw_verdict_sha256")]
    public required string RawVerdictSha256 { get; init; }
}

internal sealed record CrossRuntimeDesignReviewStoredRecord(
    string FileName,
    string RelativePath,
    CrossRuntimeDesignReviewRecord Record);

internal sealed record CrossRuntimeDesignReviewReadResult(
    IReadOnlyList<CrossRuntimeDesignReviewStoredRecord> Records,
    IReadOnlyList<CrossRuntimeReviewUnreadableRecord> Unreadable);

internal static class CrossRuntimeDesignReviewStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FileStem(CrossRuntimeDesignReviewRecord record) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{record.RecordedAt.UtcDateTime:yyyyMMdd'T'HHmmssfffffff'Z'}-{record.Runtime}-{record.PacketDigest[..7].ToLowerInvariant()}");

    public static string RawRelativePath(CrossRuntimeDesignReviewRecord record) =>
        $"{CrossRuntimeReviewPaths.DesignRelativeDirectory(record.ExecutionUnit)}/{FileStem(record)}{CrossRuntimeReviewStore.RawCopySuffix}";

    public static string RecordRelativePath(CrossRuntimeDesignReviewRecord record) =>
        $"{CrossRuntimeReviewPaths.DesignRelativeDirectory(record.ExecutionUnit)}/{FileStem(record)}{CrossRuntimeReviewStore.RecordExtension}";

    public static string Serialize(CrossRuntimeDesignReviewRecord record) =>
        JsonSerializer.Serialize(record, JsonOptions) + "\n";

    public static CrossRuntimeReviewWriteResult Write(string repoRoot, CrossRuntimeDesignReviewRecord record, byte[] rawVerdict) =>
        CrossRuntimeReviewStore.WriteCreateNew(
            CrossRuntimeReviewPaths.DesignDirectory(repoRoot, record.ExecutionUnit),
            CrossRuntimeReviewPaths.DesignRelativeDirectory(record.ExecutionUnit),
            FileStem(record),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Serialize(record)),
            rawVerdict,
            RecordRelativePath(record),
            RawRelativePath(record));

    public static CrossRuntimeDesignReviewReadResult Read(string repoRoot, string executionUnit)
    {
        var directory = CrossRuntimeReviewPaths.DesignDirectory(repoRoot, executionUnit);
        var relativeDirectory = CrossRuntimeReviewPaths.DesignRelativeDirectory(executionUnit);
        if (!Directory.Exists(directory))
        {
            return new CrossRuntimeDesignReviewReadResult([], []);
        }

        var records = new List<CrossRuntimeDesignReviewStoredRecord>();
        var unreadable = new List<CrossRuntimeReviewUnreadableRecord>();
        foreach (var path in Directory.EnumerateFiles(directory, "*" + CrossRuntimeReviewStore.RecordExtension, SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(path);
            var relative = $"{relativeDirectory}/{fileName}";
            try
            {
                var record = Deserialize(File.ReadAllText(path), repoRoot, executionUnit, relative);
                records.Add(new CrossRuntimeDesignReviewStoredRecord(fileName, relative, record));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                or NotSupportedException or ArgumentException or FormatException or DecoderFallbackException)
            {
                unreadable.Add(new CrossRuntimeReviewUnreadableRecord(relative, exception.Message));
            }
        }

        return new CrossRuntimeDesignReviewReadResult(
            records
                .OrderBy(item => item.Record.RecordedAt)
                .ThenBy(item => item.FileName, StringComparer.Ordinal)
                .ToArray(),
            unreadable);
    }

    public static CrossRuntimeDesignReviewRecord Deserialize(string json, string repoRoot, string executionUnit, string relativePath)
    {
        CrossRuntimeDesignReviewRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<CrossRuntimeDesignReviewRecord>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"record is not a valid {CrossRuntimeDesignReviewRecord.ArtifactKindValue}: {exception.Message}");
        }

        if (record is null)
        {
            throw new InvalidOperationException("record payload deserialized to null.");
        }

        void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        Require(string.Equals(record.ArtifactKind, CrossRuntimeDesignReviewRecord.ArtifactKindValue, StringComparison.Ordinal),
            $"artifact_kind '{record.ArtifactKind}' is not '{CrossRuntimeDesignReviewRecord.ArtifactKindValue}'.");
        Require(string.Equals(record.ExecutionUnit, executionUnit, StringComparison.Ordinal),
            $"record names execution unit '{record.ExecutionUnit}' but is stored for '{executionUnit}'.");
        Require(CrossRuntimeReviewPaths.IsRepositoryName(record.TargetRepo), $"target_repo '{record.TargetRepo}' is not '<owner>/<repo>'.");
        Require(record.PacketDigest is { Length: 64 } && record.PacketDigest.All(Uri.IsHexDigit),
            $"packet_digest '{record.PacketDigest}' is not a 64-character hexadecimal SHA-256.");
        Require(KnowledgeWriteBackRecord.TryValidateExecutionUnit(record.ExecutionUnit, out var unitError), $"execution_unit is invalid: {unitError}");
        Require(!string.IsNullOrWhiteSpace(record.Domain) && !string.IsNullOrWhiteSpace(record.Team), "domain and team are required.");
        Require(string.Equals(record.Kind, CrossRuntimeReviewRecord.KindDesign, StringComparison.Ordinal),
            $"kind '{record.Kind}' is not '{CrossRuntimeReviewRecord.KindDesign}'.");
        Require(CrossRuntimeReviewRuntimes.IsSupported(record.Runtime), $"runtime '{record.Runtime}' is not supported.");
        Require(CrossRuntimeReviewRuntimes.IsSupported(record.ConductorRuntime), $"conductor_runtime '{record.ConductorRuntime}' is not supported.");
        Require(record.Relation is CrossRuntimeReviewRecord.RelationSameRuntime or CrossRuntimeReviewRecord.RelationCrossRuntime,
            $"relation '{record.Relation}' is not recognized.");
        Require(!string.IsNullOrWhiteSpace(record.RuntimeVersion), "runtime_version is required.");
        Require(record.RecordedAt != default, "recorded_at is required.");
        Require(record.BlockingFindings is not null && record.Notes is not null, "blocking_findings and notes are required.");

        using (var verdictDocument = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
               {
                   [CrossRuntimeReviewVerdict.FieldVerdict] = record.Verdict,
                   [CrossRuntimeReviewVerdict.FieldPacketDigest] = record.PacketDigest,
                   [CrossRuntimeReviewVerdict.FieldBlockingFindings] = record.BlockingFindings,
                   [CrossRuntimeReviewVerdict.FieldNotes] = record.Notes,
               })))
        {
            Require(CrossRuntimeReviewVerdict.TryValidateDesign(verdictDocument.RootElement, out _, out var verdictError),
                $"verdict is invalid: {verdictError}");
        }

        var expectedRaw = $"{CrossRuntimeReviewPaths.DesignRelativeDirectory(executionUnit)}/{Path.GetFileNameWithoutExtension(relativePath)}{CrossRuntimeReviewStore.RawCopySuffix}";
        Require(string.Equals(record.RawVerdictFile, expectedRaw, StringComparison.Ordinal),
            $"raw_verdict_file '{record.RawVerdictFile}' is not the copy next to this record ('{expectedRaw}').");
        var rawPath = Path.Combine(repoRoot, expectedRaw.Replace('/', Path.DirectorySeparatorChar));
        Require(File.Exists(rawPath), $"raw verdict copy '{expectedRaw}' is missing.");
        Require(string.Equals(CrossRuntimeReviewStore.Sha256Hex(File.ReadAllBytes(rawPath)), record.RawVerdictSha256, StringComparison.Ordinal),
            $"raw verdict copy '{expectedRaw}' does not match raw_verdict_sha256.");

        return record;
    }
}
