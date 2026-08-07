using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G645: durable evidence that the guide routes a packet declared were
/// recorded in the host.  The guide content is written by design; this record
/// only makes the closeout obligation observable to stalled-work.
/// </summary>
internal sealed record GuideReachabilityRecord
{
    public const string ArtifactKindValue = "guide-reachability-record";
    public const string RecordRootRelativePath = ".intent-cli/guide-reachability";
    public const string PacketRootRelativePath = ".intent-cli/issues";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    [JsonPropertyName("artifact_kind")]
    public required string ArtifactKind { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("host_commit")]
    public required string HostCommit { get; init; }

    [JsonPropertyName("recorded_at")]
    public required DateTimeOffset RecordedAt { get; init; }

    [JsonPropertyName("guide_surfaces")]
    public required IReadOnlyList<string> GuideSurfaces { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<string> Roles { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    public static string ResolveRelativePath(string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return $"{RecordRootRelativePath}/{executionUnit}/record.json";
    }

    public static string ResolveFullPath(string repoRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }
        var root = Path.GetFullPath(Path.Combine(repoRoot, RecordRootRelativePath));
        var resolved = Path.GetFullPath(Path.Combine(root, executionUnit, "record.json"));
        return EnsureContained(root, resolved, executionUnit);
    }

    public static string ResolvePacketPath(string repoRoot, string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var root = Path.GetFullPath(Path.Combine(repoRoot, PacketRootRelativePath));
        var resolved = Path.GetFullPath(Path.Combine(root, executionUnit, "packet.yaml"));
        return EnsureContained(root, resolved, executionUnit);
    }

    private static string EnsureContained(string root, string resolved, string executionUnit)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"resolved path for execution unit '{executionUnit}' escapes the guide-reachability artifact root.");
        }

        return resolved;
    }

    public static string Serialize(GuideReachabilityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, SerializeOptions);
    }

    public static GuideReachabilityRecord Deserialize(string json, string expectedExecutionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedExecutionUnit);

        GuideReachabilityRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<GuideReachabilityRecord>(json, SerializeOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Guide reachability record is not valid JSON: {exception.Message}");
        }

        if (record is null)
        {
            throw new InvalidOperationException("Guide reachability record payload deserialized to null.");
        }

        if (!string.Equals(record.ArtifactKind, ArtifactKindValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Guide reachability record declares artifact_kind '{record.ArtifactKind}', expected '{ArtifactKindValue}'.");
        }

        if (!string.Equals(record.ExecutionUnit, expectedExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Guide reachability record declares execution_unit '{record.ExecutionUnit}' but is stored for "
                + $"'{expectedExecutionUnit}'.");
        }

        if (!KnowledgeWriteBackRecord.IsCommitShaped(record.HostCommit))
        {
            throw new InvalidOperationException(
                $"Guide reachability record for '{expectedExecutionUnit}' carries host_commit '{record.HostCommit}', "
                + "which is not hexadecimal commit evidence.");
        }

        return record with
        {
            GuideSurfaces = record.GuideSurfaces ?? Array.Empty<string>(),
            Roles = record.Roles ?? Array.Empty<string>(),
        };
    }
}
