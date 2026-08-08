using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Durable state for the measured supervision loop.  The loop is intentionally
/// append-only: a cycle, a newly observed stall, and a cleared stall are facts
/// that should remain inspectable after the process that observed them exits.
/// </summary>
internal static class NotifySupervisionStore
{
    public const string BoundFileName = "bound.json";
    public const string CycleFileName = "cycles.jsonl";
    public const string StallFileName = "stalls.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Sync = new();

    internal static Func<string, string, NotifySupervisionWriteResult>? WriteOverride { get; set; }

    public static string ResolveDirectory(string artifactRoot, string domain, string team)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ValidateSegment(domain, "domain");
        ValidateSegment(team, "team");
        return Path.GetFullPath(Path.Combine(artifactRoot, domain, team));
    }

    public static string ResolveBoundPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), BoundFileName);

    public static string ResolveCyclePath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), CycleFileName);

    public static string ResolveStallPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), StallFileName);

    public static NotifySupervisionReadResult Read(string artifactRoot, string domain, string team)
    {
        string directory;
        try
        {
            directory = ResolveDirectory(artifactRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure(artifactRoot, exception.Message);
        }

        lock (Sync)
        {
            try
            {
                var bound = ReadBound(Path.Combine(directory, BoundFileName));
                var cycles = ReadCycles(Path.Combine(directory, CycleFileName));
                var stalls = ReadStalls(Path.Combine(directory, StallFileName));
                return new NotifySupervisionReadResult
                {
                    Resolved = true,
                    Directory = directory,
                    Bound = bound,
                    LastCycle = cycles.LastOrDefault(),
                    ActiveStalls = stalls.Where(item => item.ClearedAt is null)
                        .ToDictionary(item => item.Key, StringComparer.Ordinal),
                    StallHistory = stalls,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return Failure(directory, $"Supervision state at '{directory}' could not be read: {exception.Message}");
            }
        }
    }

    public static NotifySupervisionWriteResult RecordBound(
        string artifactRoot,
        NotifySupervisionBound bound,
        bool write)
    {
        string path;
        try
        {
            path = ResolveBoundPath(artifactRoot, bound.Domain, bound.Team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new NotifySupervisionWriteResult(false, false, artifactRoot, exception.Message);
        }

        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        var line = JsonSerializer.Serialize(bound, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return new NotifySupervisionWriteResult(true, false, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NotifySupervisionWriteResult(false, false, path, exception.Message);
            }
        }
    }

    public static NotifySupervisionWriteResult RecordCycle(
        string path,
        NotifySupervisionCycle cycle,
        bool write)
    {
        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        return Append(path, new NotifySupervisionEvent
        {
            Kind = "cycle",
            Cycle = cycle,
        });
    }

    public static NotifySupervisionWriteResult OpenStall(
        string path,
        NotifySupervisionStallRecord stall,
        bool write)
    {
        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        return Append(path, new NotifySupervisionEvent
        {
            Kind = "open",
            Stall = stall,
        });
    }

    public static NotifySupervisionWriteResult ClearStall(
        string path,
        string key,
        DateTimeOffset clearedAt,
        bool write)
    {
        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        return Append(path, new NotifySupervisionEvent
        {
            Kind = "clear",
            Key = key,
            ClearedAt = clearedAt,
        });
    }

    private static NotifySupervisionWriteResult Append(string path, NotifySupervisionEvent entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return new NotifySupervisionWriteResult(true, false, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NotifySupervisionWriteResult(false, false, path, exception.Message);
            }
        }
    }

    private static NotifySupervisionBound? ReadBound(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<NotifySupervisionBound>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The supervision bound file was empty.");
    }

    private static IReadOnlyList<NotifySupervisionCycle> ReadCycles(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var cycles = new List<NotifySupervisionCycle>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions)
                ?? throw new InvalidDataException("A supervision cycle event was empty.");
            if (!string.Equals(entry.Kind, "cycle", StringComparison.Ordinal) || entry.Cycle is null)
            {
                continue;
            }

            cycles.Add(entry.Cycle);
        }

        return cycles.OrderBy(item => item.CompletedAt).ToArray();
    }

    private static IReadOnlyList<NotifySupervisionStallRecord> ReadStalls(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var current = new Dictionary<string, NotifySupervisionStallRecord>(StringComparer.Ordinal);
        var history = new List<NotifySupervisionStallRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions)
                ?? throw new InvalidDataException("A supervision stall event was empty.");
            if (string.Equals(entry.Kind, "open", StringComparison.Ordinal) && entry.Stall is not null)
            {
                current[entry.Stall.Key] = entry.Stall;
                continue;
            }

            if (string.Equals(entry.Kind, "clear", StringComparison.Ordinal)
                && entry.Key is not null
                && current.Remove(entry.Key, out var open))
            {
                var cleared = open with
                {
                    ClearedAt = entry.ClearedAt,
                    DurationSeconds = open.DetectableAt is { } detectableAt && entry.ClearedAt is { } closedAt
                        ? Math.Max(0, (long)(closedAt - detectableAt).TotalSeconds)
                        : null,
                };
                history.Add(cleared);
            }
        }

        history.AddRange(current.Values);
        return history
            .OrderBy(item => item.SurfacedAt)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static NotifySupervisionReadResult Failure(string directory, string error) => new()
    {
        Resolved = false,
        Directory = directory,
        Error = error,
        ActiveStalls = new Dictionary<string, NotifySupervisionStallRecord>(StringComparer.Ordinal),
        StallHistory = [],
    };

    private static void ValidateSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || value is "." or "..")
        {
            throw new ArgumentException($"Supervision {name} '{value}' is not a safe path segment.", name);
        }
    }
}

internal sealed record NotifySupervisionBound
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("bound_seconds")] public required int BoundSeconds { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
}

internal sealed record NotifySupervisionCycle
{
    [JsonPropertyName("cycle_id")] public required string CycleId { get; init; }
    [JsonPropertyName("started_at")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("completed_at")] public required DateTimeOffset CompletedAt { get; init; }
    [JsonPropertyName("interval_seconds")] public required int IntervalSeconds { get; init; }
    [JsonPropertyName("cadence_interval_seconds")] public int? CadenceIntervalSeconds { get; init; }
    [JsonPropertyName("bound_seconds")] public int? BoundSeconds { get; init; }
    [JsonPropertyName("actual_interval_seconds")] public long? ActualIntervalSeconds { get; init; }
    [JsonPropertyName("bound_met")] public bool? BoundMet { get; init; }
    [JsonPropertyName("absence_threshold_seconds")] public int? AbsenceThresholdSeconds { get; init; }
    [JsonPropertyName("absence_threshold_kind")] public string? AbsenceThresholdKind { get; init; }
    [JsonPropertyName("absent_since_last_cycle")] public bool AbsentSinceLastCycle { get; init; }
    [JsonPropertyName("gap_seconds")] public long? GapSeconds { get; init; }
}

internal sealed record NotifySupervisionStallRecord
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("owner_role")] public required string OwnerRole { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("detectable_at")] public DateTimeOffset? DetectableAt { get; init; }
    [JsonPropertyName("surfaced_at")] public required DateTimeOffset SurfacedAt { get; init; }
    [JsonPropertyName("cleared_at")] public DateTimeOffset? ClearedAt { get; init; }
    [JsonPropertyName("duration_seconds")] public long? DurationSeconds { get; init; }
    [JsonPropertyName("detectable_at_unknown")] public bool DetectableAtUnknown { get; init; }
    [JsonPropertyName("wake_attempted")] public bool WakeAttempted { get; init; }
    [JsonPropertyName("wake_delivered")] public bool WakeDelivered { get; init; }
    [JsonPropertyName("resend_permitted")] public bool? ResendPermitted { get; init; }
    [JsonPropertyName("wake_cause")] public string? WakeCause { get; init; }
}

internal sealed record NotifySupervisionReadResult
{
    public required bool Resolved { get; init; }
    public required string Directory { get; init; }
    public NotifySupervisionBound? Bound { get; init; }
    public NotifySupervisionCycle? LastCycle { get; init; }
    public required IReadOnlyDictionary<string, NotifySupervisionStallRecord> ActiveStalls { get; init; }
    public required IReadOnlyList<NotifySupervisionStallRecord> StallHistory { get; init; }
    public string? Error { get; init; }
}

internal sealed record NotifySupervisionWriteResult(
    bool Applied,
    bool AlreadyConverged,
    string Path,
    string? Error);

internal sealed record NotifySupervisionEvent
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("cycle")] public NotifySupervisionCycle? Cycle { get; init; }
    [JsonPropertyName("stall")] public NotifySupervisionStallRecord? Stall { get; init; }
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("cleared_at")] public DateTimeOffset? ClearedAt { get; init; }
}
