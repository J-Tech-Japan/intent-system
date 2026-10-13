using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record VerificationEvidenceCriterion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("criterion_index")] int CriterionIndex,
    [property: JsonPropertyName("text")] string Text);

internal sealed record VerificationEvidenceBinding(
    [property: JsonPropertyName("proof_id")] string ProofId,
    [property: JsonPropertyName("acceptance_criteria")] IReadOnlyList<VerificationEvidenceCriterion> AcceptanceCriteria,
    [property: JsonPropertyName("lane_id")] string LaneId,
    [property: JsonPropertyName("lane_weight")] string LaneWeight,
    [property: JsonPropertyName("lane_affected")] bool LaneAffected,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("evidence")] string Evidence);

internal sealed record VerificationEvidenceRecord(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("execution_unit")] string ExecutionUnit,
    [property: JsonPropertyName("target_repo")] string TargetRepo,
    [property: JsonPropertyName("child_head_sha")] string ChildHeadSha,
    [property: JsonPropertyName("packet_sha256")] string PacketSha256,
    [property: JsonPropertyName("binding")] VerificationEvidenceBinding Binding,
    [property: JsonPropertyName("trx_sha256")] string TrxSha256,
    [property: JsonPropertyName("trx_base64")] string TrxBase64,
    [property: JsonPropertyName("report")] TrxEvidenceReader.Summary Report);

internal sealed record VerificationEvidenceDiagnostic(
    [property: JsonPropertyName("cause")] string Cause,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("detail")] string Detail);

internal sealed record VerificationEvidenceStoreRead(
    string State,
    string Cause,
    string Detail,
    string? Path,
    VerificationEvidenceRecord? Record,
    byte[]? Bytes,
    string? Sha256,
    IReadOnlyList<VerificationEvidenceDiagnostic> Diagnostics);

internal sealed record VerificationEvidenceStoreWrite(
    bool Success,
    string State,
    string Cause,
    string Detail,
    bool Wrote,
    bool Idempotent,
    string? Path,
    string? Sha256,
    IReadOnlyList<string> ResidualOwnedPaths,
    IReadOnlyList<VerificationEvidenceDiagnostic> Diagnostics);

/// <summary>Canonical JSON and create-only local store for one TRX evidence record.</summary>
internal sealed class VerificationEvidenceStore(string repoRoot, Action<string, string>? beforeOperation = null)
{
    internal const int MaximumArtifactBytes = 24 * 1024 * 1024;
    internal const int MaximumSiblingEntries = 4096;
    private const string RootRelativePath = ".intent-cli/verification-evidence";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static byte[] Serialize(VerificationEvidenceRecord record)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        if (json.Length == 0 || json[^1] != (byte)'\n') return [.. json, (byte)'\n'];
        return json;
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, out VerificationEvidenceRecord? record, out string detail)
    {
        record = null;
        detail = "stored evidence record is invalid";
        if (bytes.Length is 0 or > MaximumArtifactBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var parsed = JsonSerializer.Deserialize<VerificationEvidenceRecord>(bytes, JsonOptions);
            if (parsed is null || !ValidRecord(parsed, out detail)) return false;
            if (!bytes.SequenceEqual(Serialize(parsed)))
            {
                detail = "stored evidence bytes are not canonical JSON";
                return false;
            }
            byte[] raw;
            try { raw = Convert.FromBase64String(parsed.TrxBase64); }
            catch (FormatException)
            {
                detail = "stored TRX base64 is invalid";
                return false;
            }
            if (!string.Equals(Convert.ToBase64String(raw), parsed.TrxBase64, StringComparison.Ordinal)
                || raw.Length > TrxEvidenceReader.MaximumBytes
                || !string.Equals(Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(), parsed.TrxSha256, StringComparison.Ordinal))
            {
                detail = "stored TRX base64, length, or digest is invalid";
                return false;
            }
            var report = TrxEvidenceReader.Read(raw);
            if (!report.Success || report.Report is null
                || !JsonSerializer.SerializeToUtf8Bytes(report.Report, JsonOptions)
                    .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(parsed.Report, JsonOptions)))
            {
                detail = "stored TRX report does not match its embedded bytes";
                return false;
            }
            record = parsed;
            detail = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            detail = "stored evidence JSON is malformed or has an unsupported shape";
            return false;
        }
    }

    private static bool ValidRecord(VerificationEvidenceRecord record, out string detail)
    {
        detail = "stored evidence record has invalid required fields";
        if (record.SchemaVersion != "1"
            || !RulingArtifact.TryIdentifier(record.Id, out _)
            || !RulingArtifact.TryIdentifier(record.Domain, out _)
            || !RulingArtifact.TryIdentifier(record.Team, out _)
            || !RulingArtifact.TryIdentifier(record.ExecutionUnit, out _)
            || !ValidRepo(record.TargetRepo)
            || !LowerHex(record.ChildHeadSha, 40)
            || !LowerHex(record.PacketSha256, 64)
            || !LowerHex(record.TrxSha256, 64)
            || string.IsNullOrWhiteSpace(record.TrxBase64)
            || record.TrxBase64.Length > (TrxEvidenceReader.MaximumBytes + 2) / 3 * 4)
            return false;

        var binding = record.Binding;
        if (binding is null
            || !ValidProofId(binding.ProofId)
            || !ValidProofId(binding.LaneId)
            || binding.LaneWeight is not ("ordinary" or "heavy")
            || !binding.LaneAffected
            || binding.Command is null || string.IsNullOrWhiteSpace(binding.Command) || binding.Command.Length > 16384
            || binding.Evidence != "local-required"
            || binding.AcceptanceCriteria is null || binding.AcceptanceCriteria.Count is < 1 or > 128)
            return false;

        var priorId = string.Empty;
        var indexes = new HashSet<int>();
        foreach (var criterion in binding.AcceptanceCriteria)
        {
            if (criterion is null || !ValidProofId(criterion.Id)
                || (priorId.Length > 0 && StringComparer.Ordinal.Compare(priorId, criterion.Id) >= 0)
                || criterion.CriterionIndex is < 1 or > 128 || !indexes.Add(criterion.CriterionIndex)
                || string.IsNullOrWhiteSpace(criterion.Text) || criterion.Text.Length > 16384)
                return false;
            priorId = criterion.Id;
        }

        detail = string.Empty;
        return true;
    }

    private static bool ValidProofId(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 64
        && value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
        && value.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
            or >= '0' and <= '9' or '_' or '-');

    private static bool ValidRepo(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part is not ("." or "..")
            && part.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '_' or '.' or '-'));
    }

    private static bool LowerHex(string value, int length) =>
        value is not null && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public string RelativePath(string domain, string team, string unit, string id) =>
        $"{RootRelativePath}/{domain}/{team}/{unit}/{id}.json";

    public VerificationEvidenceStoreRead Read(string domain, string team, string unit, string id)
    {
        var relative = RelativePath(domain, team, unit, id);
        if (!TryResolveFile(relative, allowMissingDirectories: false, requireLeaf: false,
                out var fullPath, out var exists, out var failure, out var detail))
        {
            return new(failure == "missing" ? "missing" : "unavailable",
                failure == "missing" ? "verification-evidence-missing" : "verification-evidence-read-unavailable",
                detail, relative, null, null, null,
                [new(failure == "missing" ? "missing" : failure, relative, detail)]);
        }
        if (!exists)
            return new("missing", "verification-evidence-missing", "requested evidence record is absent", relative,
                null, null, null, [new("missing", relative, "requested evidence record is absent")]);

        beforeOperation?.Invoke("stored-read", fullPath);
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(fullPath, MaximumArtifactBytes,
                out var bytes, out var readFailure, out _))
        {
            var cause = readFailure == CrossRuntimeReviewFileReadFailure.TooLarge
                ? "verification-evidence-resource-limit" : "verification-evidence-read-unavailable";
            var safeDetail = $"stored evidence could not be read ({readFailure})";
            return new("unavailable", cause, safeDetail, relative,
                null, null, null, [new(cause, relative, safeDetail)]);
        }
        if (!TryParse(bytes, out var record, out var parseDetail) || record is null)
            return new("unavailable", "verification-evidence-record-invalid", parseDetail, relative,
                null, null, null, [new("record-invalid", relative, parseDetail)]);

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (record.Id != id || record.Domain != domain || record.Team != team || record.ExecutionUnit != unit)
            return new("conflict", "verification-evidence-identity-conflict", "stored embedded identity does not match its requested scope and filename",
                relative, record, bytes, sha, [new("identity-conflict", relative, "embedded identity differs from request")]);
        return new("current", string.Empty, string.Empty, relative, record, bytes, sha, []);
    }

    public VerificationEvidenceStoreWrite Write(
        VerificationEvidenceRecord record,
        byte[] bytes,
        Func<(bool Matches, string Cause, string Detail)> recheck)
    {
        var relative = RelativePath(record.Domain, record.Team, record.ExecutionUnit, record.Id);
        if (bytes.Length > MaximumArtifactBytes)
            return Failure("unavailable", "verification-evidence-resource-limit", "serialized evidence exceeds the 24 MiB limit", relative);
        var expected = Serialize(record);
        if (!expected.AsSpan().SequenceEqual(bytes))
            return Failure("invalid", "verification-evidence-record-invalid", "writer bytes differ from canonical serialization", relative);

        var createdDirectories = new List<string>();
        var fullDirectory = string.Empty;
        var pathCause = "verification-evidence-read-unavailable";
        var pathDetail = "evidence directory could not be safely inspected";
        bool prepared;
        try
        {
            prepared = TryPrepareDirectories(record, out fullDirectory, out createdDirectories,
                out pathCause, out pathDetail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            var cleanupDiagnostics = new List<VerificationEvidenceDiagnostic>();
            var residual = new List<string>();
            CleanupDirectories(createdDirectories, relative, cleanupDiagnostics, residual);
            pathDetail = "evidence directory setup failed at a bounded file operation";
            cleanupDiagnostics.Insert(0, new(pathCause, relative, pathDetail));
            return new(false, "unavailable", pathCause, pathDetail, false, false, relative, null,
                residual, cleanupDiagnostics);
        }

        if (!prepared)
        {
            var cleanupDiagnostics = new List<VerificationEvidenceDiagnostic>();
            var residual = new List<string>();
            CleanupDirectories(createdDirectories, relative, cleanupDiagnostics, residual);
            cleanupDiagnostics.Insert(0, new(pathCause, relative, pathDetail));
            return new(false, pathCause == "verification-evidence-identity-conflict" ? "conflict" : "unavailable",
                pathCause, pathDetail, false, false, relative, null, residual, cleanupDiagnostics);
        }

        var fullPath = Path.Combine(fullDirectory, record.Id + ".json");
        var initial = InspectLeaf(fullPath, out var exists, out var leafFailure, out var leafDetail);
        if (!initial)
        {
            var cleanupDiagnostics = new List<VerificationEvidenceDiagnostic>();
            var residual = new List<string>();
            CleanupDirectories(createdDirectories, relative, cleanupDiagnostics, residual);
            cleanupDiagnostics.Insert(0, new(leafFailure, relative, leafDetail));
            return new(false, "unavailable", leafFailure, leafDetail, false, false, relative, null, residual, cleanupDiagnostics);
        }
        if (exists)
        {
            var current = Read(record.Domain, record.Team, record.ExecutionUnit, record.Id);
            if (current.Record is not null && current.Bytes is not null
                && current.Bytes.AsSpan().SequenceEqual(bytes))
            {
                var check = recheck();
                if (check.Matches)
                    return new(true, "current", "", "identical canonical record already exists", false, true,
                        relative, current.Sha256, [], []);
                return Failure("stale", check.Cause, check.Detail, relative);
            }
            var cleanupDiagnostics = new List<VerificationEvidenceDiagnostic>();
            var residual = new List<string>();
            CleanupDirectories(createdDirectories, relative, cleanupDiagnostics, residual);
            cleanupDiagnostics.Insert(0, new("verification-evidence-record-conflict", relative,
                "target already exists with different, malformed, or mismatched content"));
            return new(false, "conflict", "verification-evidence-record-conflict",
                "target already exists with different, malformed, or mismatched content", false, false, relative,
                current.State == "current" ? current.Sha256 : null,
                residual, cleanupDiagnostics);
        }

        string? tempPath = null;
        var wrote = false;
        var idempotent = false;
        var diagnostics = new List<VerificationEvidenceDiagnostic>();
        var residualPaths = new List<string>();
        var phase = "temp-create";
        try
        {
            tempPath = Path.Combine(fullDirectory, "." + record.Id + "." + Guid.NewGuid().ToString("N") + ".tmp");
            beforeOperation?.Invoke("temp-create", tempPath);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            phase = "before-publish";
            beforeOperation?.Invoke("before-publish", fullPath);
            var check = recheck();
            if (!check.Matches)
            {
                CleanupFailureArtifacts(tempPath, createdDirectories, relative, diagnostics, residualPaths);
                return new(false, "stale", check.Cause, check.Detail, false, false, null, null,
                    residualPaths, diagnostics);
            }

            if (!InspectLeaf(fullPath, out exists, out leafFailure, out leafDetail))
            {
                CleanupFailureArtifacts(tempPath, createdDirectories, relative, diagnostics, residualPaths);
                return new(false, "unavailable", leafFailure, leafDetail, false, false, null, null,
                    residualPaths, diagnostics);
            }
            if (exists)
            {
                var raced = Read(record.Domain, record.Team, record.ExecutionUnit, record.Id);
                if (raced.State == "current" && raced.Record is not null && raced.Bytes is not null
                    && raced.Bytes.AsSpan().SequenceEqual(bytes))
                {
                    idempotent = true;
                    CleanupOwnedTemp(tempPath, relative, diagnostics, residualPaths, publicationCompleted: true);
                    CleanupDirectories(createdDirectories, relative, diagnostics, residualPaths);
                    return new(true, "current", "", "identical canonical record won the publication race", false, true,
                        relative, raced.Sha256, residualPaths, diagnostics);
                }
                CleanupFailureArtifacts(tempPath, createdDirectories, relative, diagnostics, residualPaths);
                diagnostics.Insert(0, new("verification-evidence-record-conflict", relative,
                    "a different or invalid record appeared before publication"));
                return new(false, "conflict", "verification-evidence-record-conflict",
                    "a different or invalid record appeared before publication", false, false, relative,
                    raced.State == "current" ? raced.Sha256 : null,
                    residualPaths, diagnostics);
            }

            phase = "atomic-create";
            beforeOperation?.Invoke("atomic-create", fullPath);
            var link = TryCreateAtomicHardLink(tempPath, fullPath);
            if (link.Success)
            {
                // Publication is the truth boundary. Keep this assignment before temp cleanup.
                wrote = true;
                CleanupOwnedTemp(tempPath, relative, diagnostics, residualPaths, publicationCompleted: true);
                var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var warning = diagnostics.Count == 0 ? "canonical evidence record was atomically created" : "record created; owned temporary cleanup needs attention";
                return new(true, "current", "", warning, wrote, false, relative, sha, residualPaths, diagnostics);
            }

            var racedTarget = Read(record.Domain, record.Team, record.ExecutionUnit, record.Id);
            if (racedTarget.State == "current" && racedTarget.Record is not null && racedTarget.Bytes is not null
                && racedTarget.Bytes.AsSpan().SequenceEqual(bytes))
            {
                idempotent = true;
                CleanupOwnedTemp(tempPath, relative, diagnostics, residualPaths, publicationCompleted: true);
                CleanupDirectories(createdDirectories, relative, diagnostics, residualPaths);
                return new(true, "current", "", "identical canonical record won the publication race", false, idempotent,
                    relative, racedTarget.Sha256, residualPaths, diagnostics);
            }
            CleanupFailureArtifacts(tempPath, createdDirectories, relative, diagnostics, residualPaths);
            var targetInspected = InspectLeaf(fullPath, out var targetPresent, out var targetFailure, out var targetDetail);
            var destinationExists = targetInspected && targetPresent
                || racedTarget.State is "current" or "invalid" or "conflict";
            if (!targetInspected && targetFailure == "verification-evidence-identity-conflict")
            {
                var detail = "an unsafe entry appeared at the evidence target before publication";
                diagnostics.Insert(0, new(targetFailure, relative, detail));
                return new(false, "conflict", targetFailure, detail, false, false, relative, null,
                    residualPaths, diagnostics);
            }
            var atomicCause = destinationExists ? "verification-evidence-record-conflict" : "verification-evidence-read-unavailable";
            var atomicDetail = destinationExists
                ? "a destination appeared before the create-only publication completed"
                : !targetInspected ? targetDetail : link.Detail;
            diagnostics.Insert(0, new(atomicCause, destinationExists ? relative : null, atomicDetail));
            return new(false, destinationExists ? "conflict" : "unavailable", atomicCause, atomicDetail,
                false, false, destinationExists ? relative : null,
                racedTarget.State == "current" ? racedTarget.Sha256 : null, residualPaths, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            CleanupFailureArtifacts(tempPath, createdDirectories, relative, diagnostics, residualPaths);
            var cause = phase == "atomic-create" && exception is DllNotFoundException or EntryPointNotFoundException
                ? "verification-evidence-read-unavailable" : "verification-evidence-read-unavailable";
            return new(false, "unavailable", cause, "local evidence write failed at a bounded file operation",
                wrote, idempotent, wrote ? relative : null,
                wrote ? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() : null,
                residualPaths, diagnostics);
        }
    }

    private bool TryPrepareDirectories(VerificationEvidenceRecord record, out string finalDirectory,
        out List<string> created, out string cause, out string detail)
    {
        created = [];
        cause = "verification-evidence-read-unavailable";
        detail = "evidence directory could not be safely inspected";
        finalDirectory = string.Empty;
        if (!TryFullRoot(out var root, out cause, out detail)) return false;
        var segments = new[] { ".intent-cli", "verification-evidence", record.Domain, record.Team, record.ExecutionUnit };
        var current = root;
        foreach (var segment in segments)
        {
            if (!TryReadDirectoryEntries(current, segment, out var exists, out var unsafeAlias, out detail))
            {
                cause = unsafeAlias ? "verification-evidence-identity-conflict" : "verification-evidence-read-unavailable";
                return false;
            }
            var child = Path.Combine(current, segment);
            if (exists)
            {
                if (!InspectDirectory(child, out var present, out var childFailure, out detail))
                {
                    cause = childFailure;
                    return false;
                }
                if (!present)
                {
                    cause = "verification-evidence-read-unavailable";
                    detail = "evidence directory disappeared during path inspection";
                    return false;
                }
            }
            else
            {
                beforeOperation?.Invoke("directory-create", child);
                if (TryCreateDirectoryExclusive(child, out var createdHere, out detail))
                {
                    if (createdHere) created.Add(child);
                }
                else
                {
                    cause = "verification-evidence-read-unavailable";
                    return false;
                }
            }
            if (!InspectDirectory(child, out var childExists, out var childCause, out detail) || !childExists)
            {
                cause = childCause.Length == 0 ? "verification-evidence-read-unavailable" : childCause;
                return false;
            }
            current = child;
        }
        finalDirectory = current;
        cause = string.Empty;
        detail = string.Empty;
        return true;
    }

    private bool TryResolveFile(string relative, bool allowMissingDirectories, bool requireLeaf,
        out string fullPath, out bool exists, out string failure, out string detail)
    {
        fullPath = string.Empty;
        exists = false;
        failure = "verification-evidence-read-unavailable";
        detail = "path could not be inspected";
        if (!TryFullRoot(out var root, out failure, out detail)) return false;
        var segments = relative.Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            failure = "verification-evidence-identity-conflict";
            detail = "relative path contains an unsafe segment";
            return false;
        }
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var last = index == segments.Length - 1;
            var segment = segments[index];
            if (!TryReadDirectoryEntries(current, segment, out var siblingExists, out var alias, out detail))
            {
                failure = alias ? "verification-evidence-identity-conflict" : "verification-evidence-read-unavailable";
                return false;
            }
            var child = Path.Combine(current, segment);
            if (!InspectEntry(child, out var state, out detail))
            {
                failure = "verification-evidence-read-unavailable";
                return false;
            }
            if (state == EntryState.Missing)
            {
                fullPath = child;
                if (!last && !allowMissingDirectories)
                {
                    failure = "missing";
                    detail = "evidence scope directory is absent";
                    return false;
                }
                if (last && requireLeaf)
                {
                    failure = "missing";
                    detail = "requested file is absent";
                    return true;
                }
                return true;
            }
            if (state == EntryState.Link)
            {
                failure = "verification-evidence-identity-conflict";
                detail = "symlink or reparse point refused in evidence path";
                return false;
            }
            if (last)
            {
                fullPath = child;
                if (state == EntryState.Directory)
                {
                    failure = "verification-evidence-identity-conflict";
                    detail = "directory occupies evidence file path";
                    return false;
                }
                exists = true;
                return true;
            }
            if (state != EntryState.Directory)
            {
                failure = "verification-evidence-identity-conflict";
                detail = "file blocks evidence directory path";
                return false;
            }
            current = child;
        }
        return true;
    }

    private bool TryFullRoot(out string root, out string cause, out string detail)
    {
        root = string.Empty;
        cause = "verification-evidence-read-unavailable";
        detail = "repository root is unavailable";
        try { root = Path.GetFullPath(repoRoot); }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        { detail = "repository root path is invalid"; return false; }
        if (!InspectEntry(root, out var state, out detail)) return false;
        if (state != EntryState.Directory)
        {
            cause = "verification-evidence-identity-conflict";
            detail = state == EntryState.Link ? "repository root is a symlink or reparse point" : "repository root is not a directory";
            return false;
        }
        return true;
    }

    private bool TryReadDirectoryEntries(string directory, string desiredName, out bool exists,
        out bool unsafeAlias, out string detail)
    {
        exists = false;
        unsafeAlias = false;
        detail = string.Empty;
        if (!InspectEntry(directory, out var state, out detail)) return false;
        if (state != EntryState.Directory)
        {
            detail = state == EntryState.Missing ? "parent directory is missing" : "parent path is not a safe directory";
            return false;
        }
        try
        {
            var count = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > MaximumSiblingEntries)
                {
                    detail = $"directory exceeds the bounded {MaximumSiblingEntries}-entry sibling inspection limit";
                    return false;
                }
                var name = Path.GetFileName(entry);
                if (string.Equals(name, desiredName, StringComparison.Ordinal)) exists = true;
                else if (string.Equals(name, desiredName, StringComparison.OrdinalIgnoreCase))
                {
                    unsafeAlias = true;
                    detail = $"case-alias sibling '{name}' conflicts with requested segment";
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            detail = "directory cannot be safely enumerated";
            return false;
        }
    }

    private bool InspectDirectory(string path, out bool exists, out string cause, out string detail)
    {
        exists = false;
        cause = "verification-evidence-read-unavailable";
        if (!InspectEntry(path, out var state, out detail)) return false;
        if (state == EntryState.Missing) return true;
        if (state == EntryState.Link || state != EntryState.Directory)
        {
            cause = "verification-evidence-identity-conflict";
            detail = state == EntryState.Link ? "symlink or reparse point refused in evidence path" : "file blocks evidence directory path";
            return false;
        }
        if (!TryReadDirectoryEntries(path, string.Empty, out _, out _, out detail)) return false;
        exists = true;
        cause = string.Empty;
        return true;
    }

    private static bool InspectLeaf(string path, out bool exists, out string cause, out string detail)
    {
        exists = false;
        cause = "verification-evidence-read-unavailable";
        if (!InspectEntry(path, out var state, out detail)) return false;
        if (state == EntryState.Missing) return true;
        if (state == EntryState.Link || state == EntryState.Directory)
        {
            cause = "verification-evidence-identity-conflict";
            detail = state == EntryState.Link ? "symlink or reparse point refused at evidence target" : "directory occupies evidence target";
            return false;
        }
        exists = true;
        cause = string.Empty;
        return true;
    }

    private enum EntryState { Missing, File, Directory, Link }

    private static bool InspectEntry(string path, out EntryState state, out string detail)
    {
        state = EntryState.Missing;
        detail = string.Empty;
        try
        {
            var file = new FileInfo(path);
            if (file.LinkTarget is not null) { state = EntryState.Link; return true; }
            var directory = new DirectoryInfo(path);
            if (directory.LinkTarget is not null) { state = EntryState.Link; return true; }
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) state = EntryState.Link;
            else if ((attributes & FileAttributes.Directory) != 0) state = EntryState.Directory;
            else state = EntryState.File;
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            state = EntryState.Missing;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            detail = "filesystem entry cannot be inspected safely";
            return false;
        }
    }

    private static bool TryCreateDirectoryExclusive(string path, out bool created, out string detail)
    {
        created = false;
        detail = "directory creation failed";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (CreateWindowsDirectory(path, IntPtr.Zero)) { created = true; return true; }
                var code = Marshal.GetLastPInvokeError();
                if (code is 80 or 183) return true;
                detail = $"CreateDirectoryW failed with error {code}";
                return false;
            }
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (CreateUnixDirectory(path, Convert.ToUInt32("700", 8)) == 0) { created = true; return true; }
                var code = Marshal.GetLastPInvokeError();
                if (code == 17) return true;
                detail = $"mkdir failed with errno {code}";
                return false;
            }
            detail = "exclusive directory creation is unsupported on this operating system";
            return false;
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            detail = "exclusive directory creation binding is unavailable";
            return false;
        }
    }

    private static AtomicLinkResult TryCreateAtomicHardLink(string existingTemp, string final)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (CreateUnixHardLink(existingTemp, final) == 0) return new(true, false, "atomic hard-link create succeeded");
                var code = Marshal.GetLastPInvokeError();
                var unsupported = OperatingSystem.IsLinux() ? code is 38 or 95 : code is 78 or 45;
                return new(false, unsupported, $"atomic hard-link create failed with errno {code}");
            }
            if (OperatingSystem.IsWindows())
            {
                if (CreateWindowsHardLink(final, existingTemp, IntPtr.Zero)) return new(true, false, "atomic hard-link create succeeded");
                var code = Marshal.GetLastPInvokeError();
                return new(false, code is 1 or 50 or 120, $"atomic hard-link create failed with error {code}");
            }
            return new(false, true, "atomic hard-link create is unsupported on this operating system");
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            return new(false, true, "atomic hard-link binding is unavailable");
        }
    }

    private sealed record AtomicLinkResult(bool Success, bool Unsupported, string Detail);

    private void CleanupOwnedTemp(string tempPath, string relative,
        List<VerificationEvidenceDiagnostic> diagnostics, List<string> residual, bool publicationCompleted = false)
    {
        try
        {
            beforeOperation?.Invoke("temp-cleanup", tempPath);
            File.Delete(tempPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            var path = ToRelative(tempPath);
            residual.Add(path);
            diagnostics.Add(new("owned-temp-cleanup", path, publicationCompleted
                ? "canonical final record is complete but its owned temporary path remains"
                : "publication did not complete and its owned temporary path remains"));
        }
    }

    private void CleanupDirectories(List<string> directories, string relative,
        List<VerificationEvidenceDiagnostic> diagnostics, List<string> residual)
    {
        foreach (var directory in directories.AsEnumerable().Reverse())
        {
            try { Directory.Delete(directory, recursive: false); }
            catch (DirectoryNotFoundException) { }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                var path = ToRelative(directory);
                residual.Add(path);
                diagnostics.Add(new("owned-directory-cleanup", path, "owned directory was preserved because it was no longer empty or could not be removed"));
            }
        }
    }

    private void CleanupFailureArtifacts(string? tempPath, List<string> directories, string relative,
        List<VerificationEvidenceDiagnostic> diagnostics, List<string> residual)
    {
        if (tempPath is not null) CleanupOwnedTemp(tempPath, relative, diagnostics, residual);
        CleanupDirectories(directories, relative, diagnostics, residual);
    }

    private static VerificationEvidenceStoreWrite Failure(string state, string cause, string detail, string? path,
        IReadOnlyList<string>? residual = null) =>
        new(false, state, cause, detail, false, false, path, null, residual ?? [],
            [new(cause, path, detail)]);

    private string ToRelative(string absolute)
    {
        var root = Path.GetFullPath(repoRoot);
        var relative = Path.GetRelativePath(root, absolute);
        return relative == "." ? "." : relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int CreateUnixDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateUnixHardLink([MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateWindowsDirectory(string path, IntPtr securityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateWindowsHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
