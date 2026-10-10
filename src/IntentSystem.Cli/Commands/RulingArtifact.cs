using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>Canonical v1 local operator-ruling record and its bounded store.</summary>
internal sealed record RulingArtifact
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumInventory = 500;
    public const string RootRelativePath = ".intent-cli/rulings";

    public required string Id { get; init; }
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required string AuthorityRole { get; init; }
    public required string TargetRepo { get; init; }
    public required string ScopeKind { get; init; }
    public required IReadOnlyList<string> ExecutionUnits { get; init; }
    public required string Decision { get; init; }
    public required string Rationale { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required IReadOnlyList<string> Supersedes { get; init; }

    public string NormalizedRecordedAt => FormatTimestamp(RecordedAt);
    public string? NormalizedExpiresAt => ExpiresAt is null ? null : FormatTimestamp(ExpiresAt.Value);

    public static bool TryIdentifier(string? value, out string error)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(value, out error))
        {
            error = "identifier: " + error;
            return false;
        }

        return true;
    }

    public static bool TryTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value is null || !Regex.IsMatch(value,
                @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        var normalized = value.EndsWith('Z') ? value[..^1] + "+00:00" : value;
        var formats = new[]
        {
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        };
        return DateTimeOffset.TryParseExact(normalized, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out timestamp) && timestamp.Offset == TimeSpan.Zero;
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    public static byte[] Serialize(RulingArtifact record)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.Default,
            NewLine = "\n",
        }))
        {
            json.WriteStartObject();
            json.WriteString("schema_version", "1");
            json.WriteString("id", record.Id);
            json.WriteString("domain", record.Domain);
            json.WriteString("team", record.Team);
            json.WriteString("authority_role", record.AuthorityRole);
            json.WritePropertyName("scope");
            json.WriteStartObject();
            json.WriteString("target_repo", record.TargetRepo);
            json.WriteString("kind", record.ScopeKind);
            json.WritePropertyName("execution_units");
            WriteArray(json, record.ExecutionUnits);
            json.WriteEndObject();
            json.WriteString("decision", record.Decision);
            json.WriteString("rationale", record.Rationale);
            json.WritePropertyName("evidence_refs");
            WriteArray(json, record.EvidenceRefs);
            json.WriteString("recorded_at", FormatTimestamp(record.RecordedAt));
            if (record.ExpiresAt is null) json.WriteNull("expires_at");
            else json.WriteString("expires_at", FormatTimestamp(record.ExpiresAt.Value));
            json.WritePropertyName("supersedes");
            WriteArray(json, record.Supersedes.Order(StringComparer.Ordinal));
            json.WriteEndObject();
            json.Flush();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteArray(Utf8JsonWriter json, IEnumerable<string> values)
    {
        json.WriteStartArray();
        foreach (var value in values) json.WriteStringValue(value);
        json.WriteEndArray();
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, bool requireRecordedAt, out RulingArtifact? record,
        out string cause, out string detail)
    {
        record = null;
        cause = "ruling-invalid-input";
        detail = "invalid ruling JSON";
        if (bytes.Length > MaximumBytes)
        {
            cause = "ruling-size-limit";
            detail = "record exceeds the 1 MiB byte limit";
            return false;
        }

        try
        {
            // Validate byte encoding independently from JSON syntax so malformed UTF-8 has
            // one stable parser result across runtimes.
            _ = new UTF8Encoding(false, true).GetString(bytes);
            if (!RejectDuplicateKeysAndDepth(bytes))
            {
                cause = "ruling-depth-limit";
                detail = "JSON nesting exceeds the maximum depth of 16";
                return false;
            }
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid("root must be an object", out cause, out detail);
            if (!ExactKeys(root, ["schema_version", "id", "domain", "team", "authority_role", "scope", "decision", "rationale", "evidence_refs", "recorded_at", "expires_at", "supersedes"], ["recorded_at", "expires_at"], out var keyError))
                return Invalid(keyError, out cause, out detail);
            if (!String(root, "schema_version", out var schema) || schema != "1")
                return Invalid("schema_version must be the string '1'", out cause, out detail);
            if (!String(root, "id", out var id) || !TryIdentifier(id, out _)) return Invalid("id must be a canonical identifier", out cause, out detail);
            if (!String(root, "domain", out var domain) || !TryIdentifier(domain, out _)) return Invalid("domain must be a canonical identifier", out cause, out detail);
            if (!String(root, "team", out var team) || !TryIdentifier(team, out _)) return Invalid("team must be a canonical identifier", out cause, out detail);
            if (!String(root, "authority_role", out var authority)) return Invalid("authority_role must be a string", out cause, out detail);
            if (!root.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object)
                return Invalid("scope must be an object", out cause, out detail);
            if (!ExactKeys(scope, ["target_repo", "kind", "execution_units"], [], out keyError)) return Invalid("scope: " + keyError, out cause, out detail);
            if (!String(scope, "target_repo", out var targetRepo) || !CrossRuntimeReviewPaths.IsRepositoryName(targetRepo))
                return Invalid("scope.target_repo must be owner/repository", out cause, out detail);
            if (!String(scope, "kind", out var kind) || kind is not ("repository" or "execution-units"))
                return Invalid("scope.kind must be repository or execution-units", out cause, out detail);
            if (!Array(scope, "execution_units", 128, out var units)) return Invalid("scope.execution_units must be a string array of at most 128 entries", out cause, out detail);
            if (units.Any(x => !TryIdentifier(x, out _)) || units.Distinct(StringComparer.Ordinal).Count() != units.Count)
                return Invalid("scope.execution_units contains an invalid or duplicate identifier", out cause, out detail);
            if (kind == "repository" && units.Count != 0) return Invalid("repository scope requires execution_units=[]", out cause, out detail);
            if (kind == "execution-units" && units.Count == 0) return Invalid("execution-units scope requires at least one unit", out cause, out detail);
            if (!String(root, "decision", out var decision) || !ValidText(decision, 16384)) return Invalid("decision must be nonblank text up to 16384 UTF-16 code units", out cause, out detail);
            if (!String(root, "rationale", out var rationale) || !ValidText(rationale, 16384)) return Invalid("rationale must be nonblank text up to 16384 UTF-16 code units", out cause, out detail);
            if (!Array(root, "evidence_refs", 64, out var refs) || refs.Count == 0 || refs.Any(x => !ValidText(x, 2048)) || refs.Distinct(StringComparer.Ordinal).Count() != refs.Count)
                return Invalid("evidence_refs must contain 1–64 unique nonblank strings of at most 2048 UTF-16 code units", out cause, out detail);
            if (!Array(root, "supersedes", 500, out var supersedes) || supersedes.Any(x => !TryIdentifier(x, out _)) || supersedes.Distinct(StringComparer.Ordinal).Count() != supersedes.Count)
                return Invalid("supersedes must contain at most 500 unique canonical identifiers", out cause, out detail);
            var hasRecorded = root.TryGetProperty("recorded_at", out var recordedNode);
            DateTimeOffset recorded = default;
            if (!hasRecorded && requireRecordedAt) return Invalid("recorded_at is required in stored records", out cause, out detail);
            if (hasRecorded && (recordedNode.ValueKind != JsonValueKind.String || !TryTimestamp(GetString(recordedNode), out recorded)))
                return Invalid("recorded_at must be a full UTC timestamp", out cause, out detail);
            if (!hasRecorded) recorded = default;
            DateTimeOffset? expires = null;
            if (root.TryGetProperty("expires_at", out var expiresNode) && expiresNode.ValueKind != JsonValueKind.Null)
            {
                if (expiresNode.ValueKind != JsonValueKind.String || !TryTimestamp(GetString(expiresNode), out var parsedExpires))
                    return Invalid("expires_at must be null or a full UTC timestamp", out cause, out detail);
                expires = parsedExpires;
                if (hasRecorded && expires <= recorded) return Invalid("expires_at must be strictly later than recorded_at", out cause, out detail);
            }

            record = new RulingArtifact
            {
                Id = id,
                Domain = domain,
                Team = team,
                AuthorityRole = authority,
                TargetRepo = targetRepo,
                ScopeKind = kind,
                ExecutionUnits = units.Order(StringComparer.Ordinal).ToArray(),
                Decision = decision,
                Rationale = rationale,
                EvidenceRefs = refs,
                RecordedAt = recorded,
                ExpiresAt = expires,
                Supersedes = supersedes.Order(StringComparer.Ordinal).ToArray(),
            };
            cause = "";
            detail = "";
            return true;
        }
        catch (JsonException ex)
        {
            cause = "ruling-invalid-json";
            detail = ex.Message;
            return false;
        }
        catch (DecoderFallbackException ex)
        {
            cause = "ruling-invalid-utf8";
            detail = ex.Message;
            return false;
        }
    }

    private static bool Invalid(string message, out string cause, out string detail)
    { cause = "ruling-invalid-input"; detail = message; return false; }

    private static bool String(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = GetString(element);
        return !HasUnpairedSurrogate(value);
    }

    private static string GetString(JsonElement element)
    {
        try { return element.GetString() ?? ""; }
        catch (InvalidOperationException ex) { throw new DecoderFallbackException("JSON string contains invalid UTF-16", ex); }
    }

    private static string GetPropertyName(JsonProperty property)
    {
        try { return property.Name; }
        catch (InvalidOperationException ex) { throw new DecoderFallbackException("JSON property name contains invalid UTF-16", ex); }
    }

    private static string GetReaderString(ref Utf8JsonReader reader)
    {
        try { return reader.GetString() ?? ""; }
        catch (InvalidOperationException ex) { throw new DecoderFallbackException("JSON property name contains invalid UTF-16", ex); }
    }

    private static bool Array(JsonElement obj, string name, int maximum, out List<string> values)
    {
        values = [];
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > maximum) return false;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return false;
            var value = GetString(item);
            if (HasUnpairedSurrogate(value)) return false;
            values.Add(value);
        }
        return true;
    }

    private static bool ExactKeys(JsonElement obj, string[] allowed, string[] optional, out string error)
    {
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
        {
            var name = GetPropertyName(p);
            if (!set.Contains(name)) { error = $"unknown field '{name}'"; return false; }
        }
        foreach (var key in allowed)
        {
            if (!optional.Contains(key, StringComparer.Ordinal) && !obj.TryGetProperty(key, out _))
            { error = $"required field '{key}' is missing"; return false; }
        }
        error = "";
        return true;
    }

    private static bool ValidText(string text, int maximum) =>
        text.Length > 0 && !string.IsNullOrWhiteSpace(text) && text.Length <= maximum && !HasUnpairedSurrogate(text);

    private static bool HasUnpairedSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[++i])) return true;
            }
            else if (char.IsLowSurrogate(value[i])) return true;
        }
        return false;
    }

    private static bool RejectDuplicateKeysAndDepth(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 17 });
        var stack = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                if (stack.Count >= 16) return false;
                stack.Push(reader.TokenType == JsonTokenType.StartObject ? new HashSet<string>(StringComparer.Ordinal) : null!);
            }
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) stack.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = GetReaderString(ref reader);
                if (stack.Count == 0 || stack.Peek() is not { } properties || !properties.Add(name)) throw new JsonException("duplicate JSON object key");
            }
        }
        return true;
    }

    public bool SameApplicability(RulingArtifact other) =>
        TargetRepo == other.TargetRepo && ScopeKind == other.ScopeKind &&
        ExecutionUnits.SequenceEqual(other.ExecutionUnits, StringComparer.Ordinal);

    public string RelativePath => $"{RootRelativePath}/{Domain}/{Team}/{Id}.json";
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Serialize(this))).ToLowerInvariant();
}

internal sealed record RulingEvaluation(
    string Status,
    string Cause,
    string Detail,
    IReadOnlyList<string> SupersededBy,
    IReadOnlyList<string> ReplacementIds,
    IReadOnlyList<RulingArtifact> Records,
    IReadOnlyList<RulingDiagnostic> Diagnostics);

internal sealed record RulingDiagnostic(
    [property: JsonPropertyName("cause")] string Cause,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("detail")] string Detail);

internal enum RulingNativePlatform { Linux, MacOS, Windows }

internal sealed record RulingAtomicLinkResult(bool Success, bool Unsupported, int NativeError, string Detail);

/// <summary>Finite exact-scope reader/evaluator. Fault callback is instance-local and absent in production.</summary>
internal sealed class RulingArtifactStore(string repoRoot, Action<string, string>? beforeOperation = null)
{
    private const string RulingsRoot = RulingArtifact.RootRelativePath;
    public string ScopeDirectory(string domain, string team) => Path.Combine(repoRoot, RulingsRoot, domain, team);

    internal static bool HasCaseInsensitiveIdentifierCollision(IEnumerable<string> identifiers)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var identifier in identifiers)
        {
            if (seen.TryGetValue(identifier, out var prior) && !string.Equals(prior, identifier, StringComparison.Ordinal)) return true;
            seen[identifier] = identifier;
        }
        return false;
    }

    public RulingEvaluation? CheckInventoryCapacity(string domain, string team, string candidateId)
    {
        var directory = ScopeDirectory(domain, team);
        if (!TryValidatePath(directory, domain, team, candidateId, out var pathCause, out var pathDetail))
            return pathCause == "ruling-identity-conflict"
                ? Conflict(pathCause, pathDetail, [], [], directory)
                : Unavailable(pathCause, pathDetail, [], [], directory);
        try
        {
            var state = Inspect(directory);
            if (state.Error is not null) return Unavailable("ruling-artifact-unavailable", state.Error, [], [], directory);
            if (!state.Exists) return null;
            if (state.IsSymlink || !state.IsDirectory)
                return Unavailable("ruling-unsafe-path", state.IsSymlink ? "scoped directory is a symlink" : "scoped path is not a directory", [], [], directory);

            var artifacts = Directory.EnumerateFileSystemEntries(directory)
                .Where(entry => Path.GetFileName(entry).EndsWith(".json", StringComparison.Ordinal))
                .ToArray();
            var artifactIds = artifacts.Select(entry => Path.GetFileName(entry)[..^5]);
            if (HasCaseInsensitiveIdentifierCollision(artifactIds))
                return Conflict("ruling-identity-conflict", "scoped inventory contains case-insensitive ruling ID aliases", [], [], directory);

            var count = artifacts.Length;
            if (!artifacts.Any(entry => string.Equals(Path.GetFileName(entry), candidateId + ".json", StringComparison.Ordinal))) count++;
            return count > RulingArtifact.MaximumInventory
                ? Unavailable("ruling-inventory-limit", "exact scope has more than 500 artifact entries including the candidate", [], [], directory)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Unavailable("ruling-artifact-unavailable", ex.Message, [], [], directory);
        }
    }

    public RulingEvaluation Evaluate(string domain, string team, string id, DateTimeOffset now,
        RulingArtifact? candidate = null, bool forAdmission = false)
    {
        var records = new List<RulingArtifact>();
        var diagnostics = new List<RulingDiagnostic>();
        var directory = ScopeDirectory(domain, team);
        if (!TryValidatePath(directory, domain, team, id, out var pathCause, out var pathDetail))
            return pathCause == "ruling-identity-conflict"
                ? Conflict(pathCause, pathDetail, records, diagnostics, directory)
                : Unavailable(pathCause, pathDetail, records, diagnostics, directory);
        try
        {
            var scopeState = Inspect(directory);
            if (scopeState.Error is not null)
                return Unavailable("ruling-artifact-unavailable", scopeState.Error, records, diagnostics, directory);
            if (!scopeState.Exists)
            {
                if (candidate is null) return new("missing", "ruling-not-found", "scope folder is absent", [], [], records, diagnostics);
            }
            else if (scopeState.IsSymlink || !scopeState.IsDirectory)
                return Unavailable("ruling-unsafe-path", scopeState.IsSymlink ? "scoped directory is a symlink" : "scoped path is not a directory", records, diagnostics, directory);
            else
            {
                var entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
                var artifactEntries = entries
                    .Where(entry => Path.GetFileName(entry).EndsWith(".json", StringComparison.Ordinal))
                    .ToArray();
                var artifactIds = artifactEntries.Select(entry => Path.GetFileName(entry)[..^5]);
                if (HasCaseInsensitiveIdentifierCollision(artifactIds))
                    return Conflict("ruling-identity-conflict", "scoped inventory contains case-insensitive ruling ID aliases", records, diagnostics, directory);
                var inventoryCount = artifactEntries.Length;
                if (candidate is not null && !artifactEntries.Any(entry => string.Equals(
                        Path.GetFileName(entry), candidate.Id + ".json", StringComparison.Ordinal)))
                    inventoryCount++;
                if (inventoryCount > RulingArtifact.MaximumInventory)
                    return Unavailable("ruling-inventory-limit", "exact scope has more than 500 artifact entries including the candidate", records, diagnostics, directory);
                foreach (var entry in entries.Order(StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(entry);
                    beforeOperation?.Invoke("inventory-inspect", entry);
                    var entryState = Inspect(entry);
                    if (entryState.Error is not null) return Unavailable("ruling-artifact-unavailable", entryState.Error, records, diagnostics, entry);
                    if (!entryState.Exists)
                    {
                        if (name.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                        return Unavailable("ruling-artifact-unavailable", "inventory entry disappeared during scan", records, diagnostics, entry);
                    }
                    if (entryState.IsSymlink) return Unavailable("ruling-unsafe-path", "symlink in scoped inventory", records, diagnostics, entry);
                    if (entryState.IsDirectory) return Unavailable("ruling-invalid-layout", "nested directory in scoped inventory", records, diagnostics, entry);
                    if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;
                    var artifactId = name[..^5];
                    var path = entry;
                    byte[] bytes;
                    try
                    {
                        beforeOperation?.Invoke("read", path);
                        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(path, RulingArtifact.MaximumBytes, out bytes, out var failure, out var error))
                            return Unavailable("ruling-artifact-unavailable", $"{failure}: {error}", records, diagnostics, path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        return Unavailable("ruling-artifact-unavailable", ex.Message, records, diagnostics, path);
                    }
                    var parseBytes = bytes.AsSpan();
                    if (parseBytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) parseBytes = parseBytes[3..];
                    if (!RulingArtifact.TryParse(parseBytes, true, out var parsed, out var cause, out var detail) || parsed is null)
                        return Unavailable(cause, detail, records, diagnostics, path);
                    if (parsed.AuthorityRole != "operator")
                        return Unavailable("ruling-invalid-authority", "stored authority_role is not the literal operator assertion", records, diagnostics, path);
                    if (parsed.Id != artifactId || parsed.Domain != domain || parsed.Team != team)
                        return Conflict("ruling-identity-conflict", "filename and embedded domain/team/id disagree", records, diagnostics, path);
                    var canonical = RulingArtifact.Serialize(parsed);
                    if (!bytes.AsSpan().SequenceEqual(canonical))
                        return Unavailable("ruling-noncanonical-artifact", "stored artifact bytes differ from canonical serialization", records, diagnostics, path);
                    records.Add(parsed);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Unavailable("ruling-artifact-unavailable", ex.Message, records, diagnostics, directory);
        }

        if (!ValidateGraph(records, out var graphError)) return Unavailable("ruling-graph-conflict", graphError, records, diagnostics, directory);
        if (candidate is not null)
        {
            if (records.Any(x => x.Id == candidate.Id))
            {
                var existing = records.First(x => x.Id == candidate.Id);
                return Status(existing, records, now, diagnostics);
            }
            if (records.Count + 1 > RulingArtifact.MaximumInventory)
                return Unavailable("ruling-inventory-limit", "candidate would exceed 500 exact-scope records", records, diagnostics, directory);
            if (candidate.RecordedAt > now) return new("unavailable", "ruling-future-recorded-at", "new candidate recorded_at is later than evaluated_at", [], [], records, diagnostics);
            if (!ValidateCandidateAdmission(candidate, records, out var admissionCause, out graphError))
                return new("conflict", admissionCause, graphError, [], [], records, diagnostics);
            var combined = records.Append(candidate).ToArray();
            return Status(candidate, combined, now, diagnostics);
        }

        var target = records.FirstOrDefault(x => x.Id == id);
        return target is null
            ? new("missing", "ruling-not-found", "ruling id is absent from exact scope", [], [], records, diagnostics)
            : Status(target, records, now, diagnostics);
    }

    public bool TryWrite(RulingArtifact record, byte[] bytes, DateTimeOffset now, out bool wrote, out bool idempotent,
        out string cause, out string detail, out IReadOnlyList<string> createdDirectories)
    {
        wrote = false; idempotent = false; cause = "ruling-write-failed"; detail = "write failed";
        var created = new List<string>();
        string? ownedTemp = null;
        var cleanupStarted = false;
        var phase = "preflight";
        var directory = ScopeDirectory(record.Domain, record.Team);
        if (!TryValidatePath(directory, record.Domain, record.Team, record.Id, out cause, out detail))
        { createdDirectories = created; return false; }
        try
        {
            EnsureDirectoryParents(directory, created);
            var final = Path.Combine(directory, record.Id + ".json");
            var finalState = Inspect(final);
            if (finalState.Error is not null) { cause = "ruling-target-unavailable"; detail = finalState.Error; createdDirectories = created; return false; }
            if (finalState.Exists)
            {
                if (finalState.IsSymlink || finalState.IsDirectory) { cause = "ruling-unsafe-path"; detail = "existing target is a symlink or directory"; createdDirectories = created; return false; }
                var current = Evaluate(record.Domain, record.Team, record.Id, now);
                var existing = current.Records.FirstOrDefault(x => x.Id == record.Id);
                if (current.Status == "active" && existing is not null && RulingArtifact.Serialize(existing).AsSpan().SequenceEqual(bytes))
                { idempotent = true; cause = ""; detail = ""; createdDirectories = created; return true; }
                if (existing is not null && RulingArtifact.Serialize(existing).AsSpan().SequenceEqual(bytes))
                { cause = current.Cause.Length == 0 ? "ruling-inactive-target" : current.Cause; detail = current.Detail; createdDirectories = created; return false; }
                cause = "ruling-content-conflict"; detail = "target already exists with different content"; createdDirectories = created; return false;
            }
            ownedTemp = Path.Combine(directory, "." + record.Id + "." + Guid.NewGuid().ToString("N") + ".tmp");
            phase = "temp-write";
            beforeOperation?.Invoke("temp-write", ownedTemp);
            using (var stream = new FileStream(ownedTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (!TryValidatePath(directory, record.Domain, record.Team, record.Id, out cause, out detail))
            { phase = "owned-temp-cleanup"; detail = CleanupOwnedTempPreservingFailure(detail); createdDirectories = created; return false; }
            var preCommit = Evaluate(record.Domain, record.Team, record.Id, now, record, forAdmission: true);
            if (preCommit.Status != "active")
            {
                if (TryResolveFailedEvaluationTarget(record, bytes, now, final, out var targetIdempotent, out var targetCause, out var targetDetail))
                {
                    if (targetIdempotent)
                    {
                        phase = "owned-temp-cleanup";
                        CleanupOwnedTemp();
                        idempotent = true;
                        cause = "";
                        detail = "identical canonical artifact is active";
                        createdDirectories = created;
                        return true;
                    }
                    cause = targetCause;
                    detail = targetDetail;
                }
                else
                {
                    cause = preCommit.Cause.Length == 0 ? "ruling-supersession-conflict" : preCommit.Cause;
                    detail = preCommit.Detail;
                }
                phase = "owned-temp-cleanup";
                detail = CleanupOwnedTempPreservingFailure(detail); createdDirectories = created; return false;
            }
            beforeOperation?.Invoke("before-rename-inspect", final);
            var beforeRename = Inspect(final);
            if (beforeRename.Error is not null || beforeRename.Exists)
            {
                if (beforeRename.Exists && !beforeRename.IsSymlink && !beforeRename.IsDirectory
                    && TryResolveConcurrentTarget(record, bytes, now, final, out cause, out detail))
                {
                    phase = "owned-temp-cleanup";
                    CleanupOwnedTemp();
                    idempotent = true;
                    createdDirectories = created;
                    return true;
                }
                phase = "owned-temp-cleanup";
                if (beforeRename.Error is not null) { cause = "ruling-target-unavailable"; detail = beforeRename.Error; }
                else if (beforeRename.Exists && !beforeRename.IsSymlink && !beforeRename.IsDirectory && detail.Length > 0) { }
                else { cause = beforeRename.IsSymlink || beforeRename.IsDirectory ? "ruling-unsafe-path" : "ruling-content-conflict"; detail = beforeRename.IsSymlink || beforeRename.IsDirectory ? "target is a symlink or directory" : "target appeared before atomic create"; }
                detail = CleanupOwnedTempPreservingFailure(detail); createdDirectories = created; return false;
            }
            phase = "rename";
            beforeOperation?.Invoke("rename", final);
            var immediatelyBeforeCreate = Evaluate(record.Domain, record.Team, record.Id, now, record, forAdmission: true);
            if (immediatelyBeforeCreate.Status != "active")
            {
                if (TryResolveFailedEvaluationTarget(record, bytes, now, final, out var targetIdempotent, out var targetCause, out var targetDetail))
                {
                    if (targetIdempotent)
                    {
                        phase = "owned-temp-cleanup";
                        CleanupOwnedTemp();
                        idempotent = true;
                        cause = "";
                        detail = "identical canonical artifact is active";
                        createdDirectories = created;
                        return true;
                    }
                    cause = targetCause;
                    detail = targetDetail;
                }
                else
                {
                    cause = immediatelyBeforeCreate.Cause.Length == 0 ? "ruling-supersession-conflict" : immediatelyBeforeCreate.Cause;
                    detail = immediatelyBeforeCreate.Detail;
                }
                phase = "owned-temp-cleanup";
                detail = CleanupOwnedTempPreservingFailure(detail);
                createdDirectories = created;
                return false;
            }
            var link = TryCreateAtomicHardLink(ownedTemp, final);
            if (link.Success)
            {
                wrote = true;
                phase = "owned-temp-cleanup";
                CleanupOwnedTemp();
            }
            else
            {
                var racedState = Inspect(final);
                if (racedState.Error is not null)
                { phase = "owned-temp-cleanup"; cause = "ruling-target-unavailable"; detail = CleanupOwnedTempPreservingFailure(racedState.Error); createdDirectories = created; return false; }
                if (racedState.Exists)
                {
                    if (racedState.IsSymlink || racedState.IsDirectory)
                    { phase = "owned-temp-cleanup"; cause = "ruling-unsafe-path"; detail = CleanupOwnedTempPreservingFailure("concurrent destination is a symlink or directory"); createdDirectories = created; return false; }
                    if (TryResolveConcurrentTarget(record, bytes, now, final, out cause, out detail))
                    {
                        phase = "owned-temp-cleanup"; CleanupOwnedTemp();
                        idempotent = true; cause = ""; detail = "identical canonical artifact is active"; createdDirectories = created; return true;
                    }
                    phase = "owned-temp-cleanup"; detail = CleanupOwnedTempPreservingFailure(detail); createdDirectories = created; return false;
                }
                cause = link.Unsupported ? "ruling-atomic-create-unavailable" : "ruling-write-failed";
                detail = link.Detail;
                phase = "owned-temp-cleanup"; detail = CleanupOwnedTempPreservingFailure(detail);
                createdDirectories = created; return false;
            }
            phase = "readback";
            beforeOperation?.Invoke("readback", final);
            if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(final, RulingArtifact.MaximumBytes, out var readback, out _, out var readError)
                || !readback.AsSpan().SequenceEqual(bytes))
            { cause = "ruling-readback-failed"; detail = readError; createdDirectories = created; return false; }
            cause = ""; detail = ""; createdDirectories = created; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            if (ownedTemp is not null && !cleanupStarted)
            {
                try { CleanupOwnedTemp(); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException) { }
            }
            cause = phase switch
            {
                "rename" when ex is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException => "ruling-atomic-create-unavailable",
                "rename" => "ruling-write-failed",
                "temp-write" => "ruling-write-failed",
                "owned-temp-cleanup" => "ruling-temp-cleanup-failed",
                "readback" when wrote => "ruling-readback-failed",
                "read" => "ruling-artifact-unavailable",
                _ when wrote => "ruling-readback-failed",
                _ => "ruling-path-unavailable",
            };
            detail = ex.Message; createdDirectories = created; return false;
        }

        void CleanupOwnedTemp()
        {
            if (ownedTemp is null) return;
            cleanupStarted = true;
            beforeOperation?.Invoke("owned-temp-cleanup", ownedTemp);
            File.Delete(ownedTemp);
            ownedTemp = null;
        }

        string CleanupOwnedTempPreservingFailure(string priorDetail)
        {
            try { CleanupOwnedTemp(); return priorDetail; }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
            {
                return priorDetail.Length == 0 ? $"owned temporary cleanup also failed: {cleanup.Message}" : $"{priorDetail}; owned temporary cleanup also failed: {cleanup.Message}";
            }
        }
    }

    private bool TryResolveConcurrentTarget(RulingArtifact record, byte[] bytes, DateTimeOffset now, string path,
        out string cause, out string detail)
    {
        cause = "ruling-content-conflict";
        detail = "concurrent destination has different or noncanonical content";
        try
        {
            beforeOperation?.Invoke("read", path);
            if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(path, RulingArtifact.MaximumBytes, out var currentBytes, out _, out var readError))
            { cause = "ruling-target-unavailable"; detail = $"concurrent destination is not a readable regular file: {readError}"; return false; }
            if (!currentBytes.AsSpan().SequenceEqual(bytes)) return false;
            var evaluation = Evaluate(record.Domain, record.Team, record.Id, now);
            var existing = evaluation.Records.FirstOrDefault(x => x.Id == record.Id);
            if (evaluation.Status == "active" && existing is not null
                && RulingArtifact.Serialize(existing).AsSpan().SequenceEqual(currentBytes))
            { cause = ""; detail = "identical canonical artifact is active"; return true; }
            cause = evaluation.Cause.Length == 0 ? "ruling-inactive-target" : evaluation.Cause;
            detail = evaluation.Detail.Length == 0 ? "identical concurrent artifact is not active" : evaluation.Detail;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { cause = "ruling-target-unavailable"; detail = ex.Message; return false; }
    }

    private bool TryResolveFailedEvaluationTarget(RulingArtifact record, byte[] bytes, DateTimeOffset now, string path,
        out bool idempotent, out string cause, out string detail)
    {
        idempotent = false;
        cause = "";
        detail = "";
        var state = Inspect(path);
        if (state.Error is not null)
        {
            cause = "ruling-target-unavailable";
            detail = state.Error;
            return true;
        }
        if (!state.Exists) return false;
        if (state.IsSymlink || state.IsDirectory)
        {
            cause = "ruling-unsafe-path";
            detail = "raced target is a symlink or directory";
            return true;
        }
        idempotent = TryResolveConcurrentTarget(record, bytes, now, path, out cause, out detail);
        return true;
    }

    internal static string ClassifyNativeAtomicCreateError(RulingNativePlatform platform, int errorCode) => platform switch
    {
        RulingNativePlatform.Linux when errorCode is 38 or 95 => "ruling-atomic-create-unavailable",
        RulingNativePlatform.MacOS when errorCode is 78 or 45 => "ruling-atomic-create-unavailable",
        RulingNativePlatform.Windows when errorCode is 1 or 50 or 120 => "ruling-atomic-create-unavailable",
        _ => "ruling-write-failed",
    };

    private static RulingAtomicLinkResult TryCreateAtomicHardLink(string existingTemp, string final)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                if (CreateUnixHardLink(existingTemp, final) == 0) return new(true, false, 0, "atomic hard-link create succeeded");
                var code = Marshal.GetLastPInvokeError();
                return new(false, ClassifyNativeAtomicCreateError(RulingNativePlatform.Linux, code) == "ruling-atomic-create-unavailable", code, $"libc link failed with errno {code}");
            }
            if (OperatingSystem.IsMacOS())
            {
                if (CreateUnixHardLink(existingTemp, final) == 0) return new(true, false, 0, "atomic hard-link create succeeded");
                var code = Marshal.GetLastPInvokeError();
                return new(false, ClassifyNativeAtomicCreateError(RulingNativePlatform.MacOS, code) == "ruling-atomic-create-unavailable", code, $"libc link failed with errno {code}");
            }
            if (OperatingSystem.IsWindows())
            {
                if (CreateWindowsHardLink(final, existingTemp, IntPtr.Zero)) return new(true, false, 0, "atomic hard-link create succeeded");
                var code = Marshal.GetLastPInvokeError();
                return new(false, ClassifyNativeAtomicCreateError(RulingNativePlatform.Windows, code) == "ruling-atomic-create-unavailable", code, $"CreateHardLinkW failed with error {code}");
            }
            return new(false, true, 0, "atomic hard-link create is unsupported on this operating system");
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        { return new(false, true, 0, $"atomic hard-link binding is unavailable: {ex.Message}"); }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateUnixHardLink([MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateWindowsHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    private bool TryValidatePath(string directory, string domain, string team, string? id, out string cause, out string detail)
    {
        cause = "ruling-path-unavailable"; detail = "ruling path could not be inspected";
        try
        {
            var root = Path.GetFullPath(repoRoot);
            if (!TryValidateRepositoryRootAncestors(root, out cause, out detail)) return false;
            var segments = new[] { ".intent-cli", "rulings", domain, team };
            if (id is not null) segments = segments.Append(id + ".json").ToArray();
            var current = root;
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                var isArtifactSegment = id is not null && index == segments.Length - 1;
                var parentState = Inspect(current);
                if (parentState.Error is not null) { detail = parentState.Error; return false; }
                if (!parentState.Exists) break;
                if (parentState.IsSymlink || !parentState.IsDirectory) { cause = "ruling-unsafe-path"; detail = "parent path is not a safe directory"; return false; }
                beforeOperation?.Invoke("list-directory", current);
                foreach (var existing in Directory.EnumerateFileSystemEntries(current))
                {
                    var name = Path.GetFileName(existing);
                    if (string.Equals(name, segment, StringComparison.OrdinalIgnoreCase) && name != segment)
                    { cause = "ruling-identity-conflict"; detail = $"case-insensitive alias '{name}' conflicts with '{segment}'"; return false; }
                }
                current = Path.Combine(current, segment);
                var state = Inspect(current);
                if (state.Error is not null) { detail = state.Error; return false; }
                if (state.Exists && state.IsSymlink) { cause = "ruling-unsafe-path"; detail = "symlink path component refused"; return false; }
                if (state.Exists && !isArtifactSegment && !state.IsDirectory) { cause = "ruling-unsafe-path"; detail = "file blocks ruling directory path"; return false; }
                if (state.Exists && isArtifactSegment && state.IsDirectory) { cause = "ruling-unsafe-path"; detail = "directory occupies ruling target"; return false; }
            }
            var full = Path.GetFullPath(directory);
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.Ordinal)) { cause = "ruling-unsafe-path"; detail = "resolved path escapes repository"; return false; }
            cause = ""; detail = ""; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { cause = "ruling-path-unavailable"; detail = ex.Message; return false; }
    }

    private static bool TryValidateRepositoryRootAncestors(string root, out string cause, out string detail)
    {
        cause = "ruling-path-unavailable";
        detail = "repository root ancestor could not be inspected";
        var filesystemRoot = Path.GetPathRoot(root);
        if (string.IsNullOrEmpty(filesystemRoot))
        {
            detail = "repository root has no filesystem root";
            return false;
        }

        var current = filesystemRoot;
        var state = Inspect(current);
        if (state.Error is not null) { detail = state.Error; return false; }
        if (!state.Exists) { detail = "filesystem root is missing"; return false; }
        if (state.IsSymlink || !state.IsDirectory)
        {
            cause = "ruling-unsafe-path";
            detail = "filesystem root is not a safe directory";
            return false;
        }

        var relative = Path.GetRelativePath(filesystemRoot, root);
        if (relative == ".") { cause = ""; detail = ""; return true; }
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            state = Inspect(current);
            if (state.Error is not null) { detail = state.Error; return false; }
            if (!state.Exists) { detail = "repository root ancestor is missing"; return false; }
            if (state.IsSymlink || !state.IsDirectory)
            {
                cause = "ruling-unsafe-path";
                detail = "repository root ancestor is not a safe directory";
                return false;
            }
        }

        cause = "";
        detail = "";
        return true;
    }

    private void EnsureDirectoryParents(string directory, List<string> created)
    {
        var root = Path.GetFullPath(repoRoot);
        var relative = Path.GetRelativePath(root, directory);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            var state = Inspect(current);
            if (state.Error is not null) throw new IOException(state.Error);
            if (state.Exists)
            {
                if (state.IsSymlink || !state.IsDirectory) throw new IOException("unsafe parent blocks ruling directory creation");
                continue;
            }
            Directory.CreateDirectory(current);
            created.Add(Path.GetRelativePath(root, current).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private static bool ValidateGraph(IReadOnlyList<RulingArtifact> records, out string error)
    {
        var byId = records.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var edges = records.ToDictionary(x => x.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var successor in records)
        foreach (var predecessorId in successor.Supersedes)
        {
            if (!byId.TryGetValue(predecessorId, out var predecessor)) { error = $"{successor.Id} references missing predecessor {predecessorId}"; return false; }
            if (!predecessor.SameApplicability(successor)) { error = $"{successor.Id} changes predecessor applicability"; return false; }
            if (predecessor.RecordedAt >= successor.RecordedAt) { error = $"{successor.Id} is not later than predecessor {predecessorId}"; return false; }
            edges[predecessorId].Add(successor.Id);
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (!visiting.Add(id)) return false;
            if (visited.Contains(id)) { visiting.Remove(id); return true; }
            foreach (var next in edges[id]) if (!Visit(next)) return false;
            visiting.Remove(id); visited.Add(id); return true;
        }
        foreach (var id in byId.Keys) if (!Visit(id)) { error = "supersession graph contains a cycle"; return false; }
        error = ""; return true;
    }

    private static bool ValidateCandidateAdmission(RulingArtifact candidate, IReadOnlyList<RulingArtifact> records, out string cause, out string error)
    {
        cause = "ruling-supersession-conflict";
        var byId = records.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var predecessorId in candidate.Supersedes)
        {
            if (StringComparer.Ordinal.Equals(predecessorId, candidate.Id))
            { error = $"candidate {candidate.Id} cannot supersede itself"; return false; }
            if (!byId.TryGetValue(predecessorId, out var predecessor)) { error = $"missing predecessor {predecessorId}"; return false; }
            if (!candidate.SameApplicability(predecessor)) { error = $"predecessor {predecessorId} has different applicability"; return false; }
            if (predecessor.RecordedAt >= candidate.RecordedAt) { error = $"candidate is not later than {predecessorId}"; return false; }
        }
        if (candidate.Supersedes.Count == 0) { error = ""; return true; }
        var undirected = records.ToDictionary(x => x.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var item in records) foreach (var pred in item.Supersedes) { undirected[item.Id].Add(pred); undirected[pred].Add(item.Id); }
        var components = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<string>(candidate.Supersedes);
        while (todo.Count > 0) { var id = todo.Pop(); if (!components.Add(id)) continue; foreach (var n in undirected[id]) todo.Push(n); }
        var hasSuccessor = records.SelectMany(x => x.Supersedes).ToHashSet(StringComparer.Ordinal);
        var tips = components.Where(x => !hasSuccessor.Contains(x)).ToHashSet(StringComparer.Ordinal);
        if (!tips.SetEquals(candidate.Supersedes))
        {
            if (candidate.Supersedes.All(tips.Contains)) cause = "ruling-incomplete-merge";
            error = "candidate must explicitly supersede every current terminal tip in the touched weak component(s)";
            return false;
        }
        error = ""; return true;
    }

    private static RulingEvaluation Status(RulingArtifact target, IReadOnlyList<RulingArtifact> records, DateTimeOffset now, IReadOnlyList<RulingDiagnostic> diagnostics)
    {
        var children = records.ToDictionary(x => x.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var item in records) foreach (var p in item.Supersedes) children[p].Add(item.Id);
        var direct = children[target.Id].Order(StringComparer.Ordinal).ToArray();
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(children[target.Id]);
        while (queue.Count > 0) { var id = queue.Dequeue(); if (!reached.Add(id)) continue; foreach (var c in children[id]) queue.Enqueue(c); }
        var terminals = reached.Where(x => children[x].Count == 0).Order(StringComparer.Ordinal).ToArray();
        if (terminals.Length > 1) return new("conflict", "ruling-successor-conflict", "multiple terminal successors remain", direct, terminals, records, diagnostics);
        if (terminals.Length == 1) return new("superseded", "", "ruling has a terminal successor", direct, terminals, records, diagnostics);
        if (target.ExpiresAt is not null && now >= target.ExpiresAt.Value) return new("expired", "", "ruling expired at or before evaluated_at", [], [], records, diagnostics);
        return new("active", "", "ruling is an active terminal record", [], [], records, diagnostics);
    }

    private RulingEvaluation Unavailable(string cause, string detail, IReadOnlyList<RulingArtifact> records,
        IReadOnlyList<RulingDiagnostic> diagnostics, string path) =>
        new("unavailable", cause, detail, [], [], records, diagnostics.Append(new(cause, RelativeDiagnosticPath(path), detail)).ToArray());

    private RulingEvaluation Conflict(string cause, string detail, IReadOnlyList<RulingArtifact> records,
        IReadOnlyList<RulingDiagnostic> diagnostics, string path) =>
        new("conflict", cause, detail, [], [], records, diagnostics.Append(new(cause, RelativeDiagnosticPath(path), detail)).ToArray());

    private string RelativeDiagnosticPath(string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    private static (bool Exists, bool IsDirectory, bool IsSymlink, string? Error) Inspect(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var directory = new DirectoryInfo(path);
            var target = file.LinkTarget ?? directory.LinkTarget;
            if (target is not null) return (true, false, true, null);
            var attributes = File.GetAttributes(path);
            return (true, (attributes & FileAttributes.Directory) != 0, target is not null || (attributes & FileAttributes.ReparsePoint) != 0, null);
        }
        catch (FileNotFoundException) { return (false, false, false, null); }
        catch (DirectoryNotFoundException) { return (false, false, false, null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return (false, false, false, ex.Message); }
    }
}
