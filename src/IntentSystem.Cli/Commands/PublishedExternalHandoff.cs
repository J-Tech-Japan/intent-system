using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G692: durable evidence that an authoring-only publish was handed to the
/// external issue surface. This record is the only condition that can silence
/// the authoring-only <c>published-not-delegated</c> observation.
/// </summary>
internal sealed record PublishedExternalHandoff
{
    [JsonPropertyName("record_kind")]
    public required string RecordKind { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("team")]
    public string? Team { get; init; }

    [JsonPropertyName("team_mode")]
    public required string TeamMode { get; init; }

    [JsonPropertyName("actor_role")]
    public required string ActorRole { get; init; }

    [JsonPropertyName("destination_ownership")]
    public required string DestinationOwnership { get; init; }

    [JsonPropertyName("target_repo")]
    public required string TargetRepo { get; init; }

    [JsonPropertyName("issue_number")]
    public int? IssueNumber { get; init; }

    [JsonPropertyName("issue_url")]
    public required string IssueUrl { get; init; }

    [JsonPropertyName("operator_acceptance_evidence")]
    public required string OperatorAcceptanceEvidence { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }
}

internal sealed record PublishedExternalHandoffReadResult
{
    public required string Path { get; init; }
    public PublishedExternalHandoff? Record { get; init; }
    public string? Error { get; init; }
}

internal sealed record PublishedExternalHandoffWriteResult
{
    public required bool Succeeded { get; init; }
    public required bool AlreadyRecorded { get; init; }
    public required string Path { get; init; }
    public string? Error { get; init; }
}

internal static class PublishedExternalHandoffStore
{
    public const string RecordKind = "published-external-handoff";
    public const string RecordRootRelativePath = ".intent-cli/published-external-handoff";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolveRelativePath(string executionUnit)
    {
        ValidateExecutionUnit(executionUnit);
        return $"{RecordRootRelativePath}/{executionUnit}.json";
    }

    public static string ResolvePath(string repoRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ValidateExecutionUnit(executionUnit);
        var root = Path.GetFullPath(Path.Combine(
            repoRoot,
            RecordRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var path = Path.GetFullPath(Path.Combine(root, executionUnit + ".json"));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("published external handoff path escaped its record root.");
        }

        return path;
    }

    public static PublishedExternalHandoffReadResult Read(string repoRoot, string executionUnit)
    {
        var path = ResolvePath(repoRoot, executionUnit);
        if (!File.Exists(path))
        {
            return new PublishedExternalHandoffReadResult { Path = path };
        }

        try
        {
            var record = JsonSerializer.Deserialize<PublishedExternalHandoff>(File.ReadAllText(path), JsonOptions);
            if (record is null)
            {
                return new PublishedExternalHandoffReadResult { Path = path, Error = "record deserialized to null" };
            }

            var error = Validate(record, executionUnit);
            return error is null
                ? new PublishedExternalHandoffReadResult { Path = path, Record = record }
                : new PublishedExternalHandoffReadResult { Path = path, Error = error };
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
        {
            return new PublishedExternalHandoffReadResult
            {
                Path = path,
                Error = $"record could not be read: {exception.Message}",
            };
        }
    }

    public static PublishedExternalHandoffWriteResult Write(
        string repoRoot,
        PublishedExternalHandoff record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var path = ResolvePath(repoRoot, record.ExecutionUnit);
        var validation = Validate(record, record.ExecutionUnit);
        if (validation is not null)
        {
            return new PublishedExternalHandoffWriteResult
            {
                Succeeded = false,
                AlreadyRecorded = false,
                Path = path,
                Error = validation,
            };
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(JsonSerializer.Serialize(record, JsonOptions));
            return new PublishedExternalHandoffWriteResult
            {
                Succeeded = true,
                AlreadyRecorded = false,
                Path = path,
            };
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = Read(repoRoot, record.ExecutionUnit);
            if (existing.Record is not null && SameRecord(existing.Record, record))
            {
                return new PublishedExternalHandoffWriteResult
                {
                    Succeeded = true,
                    AlreadyRecorded = true,
                    Path = path,
                };
            }

            return new PublishedExternalHandoffWriteResult
            {
                Succeeded = false,
                AlreadyRecorded = false,
                Path = path,
                Error = $"a conflicting published-external-handoff record already exists at '{path}'.",
            };
        }
        catch (IOException exception)
        {
            return new PublishedExternalHandoffWriteResult
            {
                Succeeded = false,
                AlreadyRecorded = false,
                Path = path,
                Error = $"could not write published-external-handoff record: {exception.Message}",
            };
        }
    }

    public static bool MatchesIssue(
        PublishedExternalHandoff record,
        string executionUnit,
        string targetRepo,
        int issueNumber,
        string issueUrl)
    {
        return string.Equals(record.RecordKind, RecordKind, StringComparison.Ordinal)
            && string.Equals(record.ExecutionUnit, executionUnit, StringComparison.Ordinal)
            && string.Equals(record.TeamMode, TeamMode.AuthoringOnly, StringComparison.Ordinal)
            && string.Equals(record.TargetRepo, targetRepo, StringComparison.OrdinalIgnoreCase)
            && record.IssueNumber == issueNumber
            && string.Equals(record.IssueUrl, issueUrl, StringComparison.Ordinal);
    }

    private static bool SameRecord(PublishedExternalHandoff left, PublishedExternalHandoff right) =>
        left == right;

    private static string? Validate(PublishedExternalHandoff record, string executionUnit)
    {
        if (!string.Equals(record.RecordKind, RecordKind, StringComparison.Ordinal))
        {
            return $"record_kind must be '{RecordKind}'.";
        }
        if (!string.Equals(record.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            return $"record execution_unit '{record.ExecutionUnit}' does not match '{executionUnit}'.";
        }
        if (!string.Equals(record.TeamMode, TeamMode.AuthoringOnly, StringComparison.Ordinal))
        {
            return "published-external-handoff is only valid for team_mode 'authoring-only'.";
        }
        if (!string.Equals(record.ActorRole, "design", StringComparison.Ordinal))
        {
            return "published-external-handoff actor_role must be 'design'.";
        }
        if (string.IsNullOrWhiteSpace(record.Domain)
            || string.IsNullOrWhiteSpace(record.DestinationOwnership)
            || string.IsNullOrWhiteSpace(record.TargetRepo)
            || string.IsNullOrWhiteSpace(record.IssueUrl)
            || string.IsNullOrWhiteSpace(record.OperatorAcceptanceEvidence)
            || record.RecordedAt == default)
        {
            return "published-external-handoff is missing required destination, issue, acceptance, or timestamp evidence.";
        }

        return null;
    }

    private static void ValidateExecutionUnit(string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }
}
