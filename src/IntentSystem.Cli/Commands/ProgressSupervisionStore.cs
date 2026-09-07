using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Append-only, routing-root scoped evidence for the G812 controller.  The
/// store deliberately has no knowledge of a host queue, a pane, or a model;
/// callers supply project-neutral evidence and the controller only observes
/// the resulting records.
/// </summary>
internal static class ProgressSupervisionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolveDirectory(string routingRoot, string domain, string team)
    {
        var root = Path.GetFullPath(routingRoot);
        return Path.Combine(root, ".intent-cli", "progress", Segment(domain), Segment(team));
    }

    public static string ResolveEvidencePath(string routingRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(routingRoot, domain, team), "evidence.jsonl");

    public static string ResolveIncidentPath(string routingRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(routingRoot, domain, team), "incidents.jsonl");

    public static void WriteEvidence(string routingRoot, ProgressSupervisionEvidence evidence)
    {
        var path = ResolveEvidencePath(routingRoot, evidence.Domain, evidence.Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(evidence with
        {
            SemanticFingerprint = evidence.SemanticFingerprint is "" or "unknown"
                ? evidence.StableFingerprint()
                : evidence.SemanticFingerprint,
        }, JsonOptions) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static IReadOnlyList<ProgressSupervisionEvidence> ReadEvidence(
        string routingRoot,
        string domain,
        string team,
        out IReadOnlyList<string> failures)
    {
        var path = ResolveEvidencePath(routingRoot, domain, team);
        var records = new List<ProgressSupervisionEvidence>();
        var errors = new List<string>();
        if (!File.Exists(path))
        {
            failures = errors;
            return records;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<ProgressSupervisionEvidence>(line, JsonOptions);
                if (record is null) errors.Add("empty-evidence-record");
                else records.Add(record);
            }
            catch (JsonException exception)
            {
                errors.Add($"invalid-evidence-record:{exception.Message}");
            }
        }

        failures = errors;
        return records;
    }

    public static ProgressSupervisionEvidence? ReadLatest(
        string routingRoot,
        string domain,
        string team,
        string unit,
        out IReadOnlyList<string> failures)
    {
        var records = ReadEvidence(routingRoot, domain, team, out failures);
        return records.LastOrDefault(record => string.Equals(record.Unit, unit, StringComparison.Ordinal));
    }

    public static void RecordIncident(string routingRoot, ProgressIncidentRecord incident)
    {
        var path = ResolveIncidentPath(routingRoot, incident.Domain, incident.Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(incident, JsonOptions) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void RecordWriteBack(string routingRoot, ProgressLearningWriteBack writeBack)
    {
        var path = ResolveIncidentPath(routingRoot, writeBack.Domain, writeBack.Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(writeBack, JsonOptions) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static bool IsLearningComplete(ProgressIncidentRecord incident, ProgressLearningWriteBack? writeBack)
    {
        if (writeBack is { Verified: true, Commit: not null, Path: not null, ContentDigest: not null }
            && !string.IsNullOrWhiteSpace(writeBack.Commit)
            && !string.IsNullOrWhiteSpace(writeBack.Path)
            && !string.IsNullOrWhiteSpace(writeBack.ContentDigest))
        {
            return true;
        }

        return incident.LinkedLearningTask is { } task
            && !string.IsNullOrWhiteSpace(task.Owner)
            && task.Deadline > incident.OpenedAt
            && !string.Equals(task.Status, "resolved", StringComparison.OrdinalIgnoreCase);
    }

    public static ProgressLearningVerification VerifyWriteBack(
        string routingRoot,
        ProgressLearningWriteBack writeBack)
    {
        ArgumentNullException.ThrowIfNull(writeBack);
        if (!writeBack.Verified || string.IsNullOrWhiteSpace(writeBack.Commit)
            || string.IsNullOrWhiteSpace(writeBack.Path)
            || string.IsNullOrWhiteSpace(writeBack.ContentDigest))
        {
            return new ProgressLearningVerification { Verified = false, Reason = "write-back-record-is-not-verified" };
        }

        var root = Path.GetFullPath(routingRoot);
        var relative = writeBack.Path.Trim();
        if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(segment => segment is "." or ".."))
        {
            return new ProgressLearningVerification { Verified = false, Reason = "write-back-path-is-not-relative" };
        }

        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return new ProgressLearningVerification { Verified = false, Reason = "write-back-path-escapes-routing-root" };
        }
        if (!File.Exists(path))
        {
            return new ProgressLearningVerification { Verified = false, Reason = "write-back-content-missing", Path = path };
        }

        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var matches = string.Equals(digest, writeBack.ContentDigest.Trim(), StringComparison.OrdinalIgnoreCase);
        return new ProgressLearningVerification
        {
            Verified = matches,
            Reason = matches ? "knowledge-writeback-record-verified" : "write-back-content-digest-mismatch",
            Path = path,
            ActualDigest = digest,
        };
    }

    public static bool IsLearningComplete(
        ProgressIncidentRecord incident,
        ProgressLearningWriteBack? writeBack,
        string routingRoot)
    {
        if (writeBack is not null && VerifyWriteBack(routingRoot, writeBack).Verified)
        {
            return true;
        }

        return incident.LinkedLearningTask is { } task
            && !string.IsNullOrWhiteSpace(task.Owner)
            && task.Deadline > incident.OpenedAt
            && !string.Equals(task.Status, "resolved", StringComparison.OrdinalIgnoreCase);
    }

    private static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Routing identity is required.", nameof(value));
        var normalized = value.Trim();
        if (normalized is "." or ".." || normalized.Any(character => character is '/' or '\\' or '\0'))
            throw new ArgumentException("Routing identity contains a path separator.", nameof(value));
        return normalized;
    }
}

internal sealed record ProgressLearningTask
{
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("deadline")] public required DateTimeOffset Deadline { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "open";
}

internal sealed record ProgressIncidentRecord
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; init; } = ProgressSupervisionConstants.SchemaVersion;
    [JsonPropertyName("incident_id")] public required string IncidentId { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("unit")] public required string Unit { get; init; }
    [JsonPropertyName("phase")] public required string Phase { get; init; }
    [JsonPropertyName("opened_at")] public required DateTimeOffset OpenedAt { get; init; }
    [JsonPropertyName("evidence")] public IReadOnlyList<string> Evidence { get; init; } = [];
    [JsonPropertyName("known_unknowns")] public IReadOnlyList<string> KnownUnknowns { get; init; } = [];
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("correction")] public required string Correction { get; init; }
    [JsonPropertyName("verification")] public required string Verification { get; init; }
    [JsonPropertyName("lesson")] public required string Lesson { get; init; }
    [JsonPropertyName("linked_learning_task")] public ProgressLearningTask? LinkedLearningTask { get; init; }
    [JsonPropertyName("closed_at")] public DateTimeOffset? ClosedAt { get; init; }
}

internal sealed record ProgressLearningWriteBack
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "knowledge-writeback";
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("incident_id")] public required string IncidentId { get; init; }
    [JsonPropertyName("commit")] public string? Commit { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("content_digest")] public string? ContentDigest { get; init; }
    [JsonPropertyName("verified")] public bool Verified { get; init; }
    [JsonPropertyName("verified_at")] public DateTimeOffset? VerifiedAt { get; init; }
}

internal sealed record ProgressLearningVerification
{
    public bool Verified { get; init; }
    public required string Reason { get; init; }
    public string? Path { get; init; }
    public string? ActualDigest { get; init; }
}
