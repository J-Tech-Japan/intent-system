using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834: one recorded reviewer verdict, bound to the exact head it reviewed.
/// Stored as a new file under
/// <c>.intent-cli/cross-runtime-reviews/&lt;owner&gt;__&lt;repo&gt;/pr-&lt;n&gt;/</c>
/// with a byte copy of the raw verdict next to it. Records are append-only:
/// a later record supersedes an earlier one only in the gate's ordering, never
/// by overwriting it.
/// </summary>
internal sealed record CrossRuntimeReviewRecord
{
    public const string ArtifactKindValue = "cross-runtime-review-record";
    public const string KindImplementation = "implementation";
    public const string RelationSameRuntime = "same-runtime";
    public const string RelationCrossRuntime = "cross-runtime";

    [JsonPropertyName("artifact_kind")]
    public required string ArtifactKind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("head_sha")]
    public required string HeadSha { get; init; }

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

    /// <summary>
    /// Informational at write time. The gate recomputes the relation from the
    /// current declared conductor runtime and never reads this field.
    /// </summary>
    [JsonPropertyName("relation")]
    public required string Relation { get; init; }

    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    [JsonPropertyName("blocking_findings")]
    public required IReadOnlyList<CrossRuntimeReviewFinding> BlockingFindings { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Repo-relative, forward-slash path of the raw verdict copy.</summary>
    [JsonPropertyName("raw_verdict_file")]
    public required string RawVerdictFile { get; init; }

    [JsonPropertyName("raw_verdict_sha256")]
    public required string RawVerdictSha256 { get; init; }

    public static string RelationFor(string runtime, string conductorRuntime) =>
        string.Equals(runtime, conductorRuntime, StringComparison.Ordinal)
            ? RelationSameRuntime
            : RelationCrossRuntime;
}

/// <summary>A record read from disk with the file it came from.</summary>
internal sealed record CrossRuntimeReviewStoredRecord(string FileName, string RelativePath, CrossRuntimeReviewRecord Record);

internal sealed record CrossRuntimeReviewUnreadableRecord(
    [property: JsonPropertyName("file")] string RelativePath,
    [property: JsonPropertyName("error")] string Error);

internal sealed record CrossRuntimeReviewReadResult(
    IReadOnlyList<CrossRuntimeReviewStoredRecord> Records,
    IReadOnlyList<CrossRuntimeReviewUnreadableRecord> Unreadable);

internal sealed record CrossRuntimeReviewWriteResult(
    bool Written,
    string RecordRelativePath,
    string RawRelativePath,
    string? Error);

internal static class CrossRuntimeReviewStore
{
    public const string RecordExtension = ".json";
    public const string RawCopySuffix = ".raw-verdict";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary><c>&lt;recorded_at-utc-compact&gt;-&lt;runtime&gt;-&lt;head7&gt;</c>.</summary>
    public static string FileStem(CrossRuntimeReviewRecord record) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{record.RecordedAt.UtcDateTime:yyyyMMdd'T'HHmmssfffffff'Z'}-{record.Runtime}-{record.HeadSha[..7].ToLowerInvariant()}");

    public static string RawRelativePath(CrossRuntimeReviewRecord record) =>
        $"{CrossRuntimeReviewPaths.PrRelativeDirectory(record.Repo, record.Pr)}/{FileStem(record)}{RawCopySuffix}";

    public static string RecordRelativePath(CrossRuntimeReviewRecord record) =>
        $"{CrossRuntimeReviewPaths.PrRelativeDirectory(record.Repo, record.Pr)}/{FileStem(record)}{RecordExtension}";

    public static string Serialize(CrossRuntimeReviewRecord record) =>
        JsonSerializer.Serialize(record, JsonOptions) + "\n";

    /// <summary>
    /// Create-new semantics for both files: an existing record or raw copy is
    /// never overwritten, and the collision is reported. The raw copy is created
    /// first so a record never names a copy that does not exist; a raw copy this
    /// call created is removed again when its record cannot be created.
    /// </summary>
    public static CrossRuntimeReviewWriteResult Write(string repoRoot, CrossRuntimeReviewRecord record, byte[] rawVerdict)
    {
        var directory = CrossRuntimeReviewPaths.PrDirectory(repoRoot, record.Repo, record.Pr);
        var stem = FileStem(record);
        var recordPath = Path.Combine(directory, stem + RecordExtension);
        var rawPath = Path.Combine(directory, stem + RawCopySuffix);
        var recordRelative = RecordRelativePath(record);
        var rawRelative = RawRelativePath(record);

        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(recordPath) || File.Exists(rawPath))
            {
                return new CrossRuntimeReviewWriteResult(false, recordRelative, rawRelative,
                    $"{CrossRuntimeReviewCauses.RecordCollision}: '{recordRelative}' or its raw copy already exists; nothing was overwritten.");
            }

            CreateNew(rawPath, rawVerdict);
            try
            {
                CreateNew(recordPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Serialize(record)));
            }
            catch
            {
                File.Delete(rawPath);
                throw;
            }

            return new CrossRuntimeReviewWriteResult(true, recordRelative, rawRelative, null);
        }
        catch (IOException exception) when (File.Exists(recordPath))
        {
            return new CrossRuntimeReviewWriteResult(false, recordRelative, rawRelative,
                $"{CrossRuntimeReviewCauses.RecordCollision}: '{recordRelative}' already exists; nothing was overwritten ({exception.Message}).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CrossRuntimeReviewWriteResult(false, recordRelative, rawRelative, exception.Message);
        }
    }

    private static void CreateNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    /// <summary>
    /// Reads every record file of the PR. A file that fails validation is
    /// reported by name and never counted; the gate turns it into a fail-closed
    /// <c>blocked</c> decision.
    /// </summary>
    public static CrossRuntimeReviewReadResult Read(string repoRoot, string repo, int pr)
    {
        var directory = CrossRuntimeReviewPaths.PrDirectory(repoRoot, repo, pr);
        var relativeDirectory = CrossRuntimeReviewPaths.PrRelativeDirectory(repo, pr);
        if (!Directory.Exists(directory))
        {
            return new CrossRuntimeReviewReadResult([], []);
        }

        var records = new List<CrossRuntimeReviewStoredRecord>();
        var unreadable = new List<CrossRuntimeReviewUnreadableRecord>();
        foreach (var path in Directory.EnumerateFiles(directory, "*" + RecordExtension, SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(path);
            var relative = $"{relativeDirectory}/{fileName}";
            try
            {
                var record = Deserialize(File.ReadAllText(path), repoRoot, repo, pr, relative);
                records.Add(new CrossRuntimeReviewStoredRecord(fileName, relative, record));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                unreadable.Add(new CrossRuntimeReviewUnreadableRecord(relative, exception.Message));
            }
        }

        return new CrossRuntimeReviewReadResult(
            records
                .OrderBy(item => item.Record.RecordedAt)
                .ThenBy(item => item.FileName, StringComparer.Ordinal)
                .ToArray(),
            unreadable);
    }

    /// <summary>
    /// Validates a stored record against the folder it lives in: artifact kind,
    /// repository and PR, a full head SHA, a canonical execution unit, supported
    /// runtimes and kind, a verdict that passes the verdict rules, and a raw copy
    /// whose sha256 still matches.
    /// </summary>
    public static CrossRuntimeReviewRecord Deserialize(string json, string repoRoot, string repo, int pr, string relativePath)
    {
        CrossRuntimeReviewRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<CrossRuntimeReviewRecord>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"record is not a valid {CrossRuntimeReviewRecord.ArtifactKindValue}: {exception.Message}");
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

        Require(string.Equals(record.ArtifactKind, CrossRuntimeReviewRecord.ArtifactKindValue, StringComparison.Ordinal),
            $"artifact_kind '{record.ArtifactKind}' is not '{CrossRuntimeReviewRecord.ArtifactKindValue}'.");
        Require(string.Equals(record.Repo, repo, StringComparison.OrdinalIgnoreCase) && record.Pr == pr,
            $"record names {record.Repo}#{record.Pr} but is stored for {repo}#{pr}.");
        Require(CrossRuntimeReviewPaths.IsFullHeadSha(record.HeadSha), $"head_sha '{record.HeadSha}' is not a 40-character hexadecimal SHA.");
        Require(KnowledgeWriteBackRecord.TryValidateExecutionUnit(record.ExecutionUnit, out var unitError), $"execution_unit is invalid: {unitError}");
        Require(!string.IsNullOrWhiteSpace(record.Domain) && !string.IsNullOrWhiteSpace(record.Team), "domain and team are required.");
        Require(string.Equals(record.Kind, CrossRuntimeReviewRecord.KindImplementation, StringComparison.Ordinal),
            $"kind '{record.Kind}' is not '{CrossRuntimeReviewRecord.KindImplementation}'.");
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
                   [CrossRuntimeReviewVerdict.FieldHeadSha] = record.HeadSha,
                   [CrossRuntimeReviewVerdict.FieldBlockingFindings] = record.BlockingFindings,
                   [CrossRuntimeReviewVerdict.FieldNotes] = record.Notes,
               })))
        {
            Require(CrossRuntimeReviewVerdict.TryValidate(verdictDocument.RootElement, out _, out var verdictError),
                $"verdict is invalid: {verdictError}");
        }

        var expectedRaw = $"{CrossRuntimeReviewPaths.PrRelativeDirectory(repo, pr)}/{Path.GetFileNameWithoutExtension(relativePath)}{RawCopySuffix}";
        Require(string.Equals(record.RawVerdictFile, expectedRaw, StringComparison.Ordinal),
            $"raw_verdict_file '{record.RawVerdictFile}' is not the copy next to this record ('{expectedRaw}').");
        var rawPath = Path.Combine(repoRoot, expectedRaw.Replace('/', Path.DirectorySeparatorChar));
        Require(File.Exists(rawPath), $"raw verdict copy '{expectedRaw}' is missing.");
        Require(string.Equals(Sha256Hex(File.ReadAllBytes(rawPath)), record.RawVerdictSha256, StringComparison.Ordinal),
            $"raw verdict copy '{expectedRaw}' does not match raw_verdict_sha256.");

        return record;
    }
}
