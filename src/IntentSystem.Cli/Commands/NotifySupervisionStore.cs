using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Durable state for the measured supervision loop.  The loop is intentionally
/// append-only: a cycle, prompt audit, newly observed stall, and cleared stall
/// are facts that remain inspectable after the observing process exits.
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
                var cyclePath = Path.Combine(directory, CycleFileName);
                var cycles = ReadCycles(cyclePath);
                var promptAudits = ReadPromptAudits(cyclePath);
                var stalls = ReadStalls(Path.Combine(directory, StallFileName));
                return new NotifySupervisionReadResult
                {
                    Resolved = true,
                    Directory = directory,
                    Bound = bound,
                    LastCycle = cycles.LastOrDefault(),
                    LastIntervalCycle = cycles.LastOrDefault(cycle =>
                        string.IsNullOrWhiteSpace(cycle.Trigger)
                        || string.Equals(cycle.Trigger, "interval", StringComparison.Ordinal)),
                    ActiveStalls = stalls.Where(item => item.ClearedAt is null)
                        .ToDictionary(item => item.Key, StringComparer.Ordinal),
                    StallHistory = stalls,
                    PromptAudits = promptAudits,
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

    public static NotifySupervisionWriteResult RecordPromptAudit(
        string path,
        NotifyPromptAudit audit,
        bool write)
    {
        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        return Append(path, new NotifySupervisionEvent
        {
            Kind = "prompt-audit",
            PromptAudit = audit,
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

    private static IReadOnlyList<NotifyPromptAudit> ReadPromptAudits(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var audits = new List<NotifyPromptAudit>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions)
                ?? throw new InvalidDataException("A supervision prompt-audit event was empty.");
            if (string.Equals(entry.Kind, "prompt-audit", StringComparison.Ordinal)
                && entry.PromptAudit is not null)
            {
                audits.Add(entry.PromptAudit);
            }
        }
        return audits.OrderBy(item => item.Timestamp).ToArray();
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
        PromptAudits = [],
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
    // G676 is additive: cycles written before writer identity existed remain
    // readable and simply do not participate in duplicate detection.
    [JsonPropertyName("writer")] public NotifySupervisionWriterIdentity? Writer { get; init; }
    [JsonPropertyName("trigger")] public string Trigger { get; init; } = "interval";
    [JsonPropertyName("interval_seconds")] public required int IntervalSeconds { get; init; }
    [JsonPropertyName("cadence_interval_seconds")] public int? CadenceIntervalSeconds { get; init; }
    [JsonPropertyName("bound_seconds")] public int? BoundSeconds { get; init; }
    [JsonPropertyName("actual_interval_seconds")] public long? ActualIntervalSeconds { get; init; }
    [JsonPropertyName("bound_met")] public bool? BoundMet { get; init; }
    [JsonPropertyName("absence_threshold_seconds")] public int? AbsenceThresholdSeconds { get; init; }
    [JsonPropertyName("absence_threshold_kind")] public string? AbsenceThresholdKind { get; init; }
    [JsonPropertyName("absent_since_last_cycle")] public bool AbsentSinceLastCycle { get; init; }
    [JsonPropertyName("gap_seconds")] public long? GapSeconds { get; init; }
    [JsonPropertyName("bound_below_interval")] public bool BoundBelowInterval { get; init; }
    [JsonPropertyName("last_observed_state_change_sequences")] public IReadOnlyDictionary<string, long> LastObservedStateChangeSequences { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal);
    [JsonPropertyName("last_observed_state_change_times")] public IReadOnlyDictionary<string, DateTimeOffset> LastObservedStateChangeTimes { get; init; } = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
    [JsonPropertyName("last_observed_agent_statuses")] public IReadOnlyDictionary<string, string> LastObservedAgentStatuses { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    [JsonPropertyName("transitions")] public IReadOnlyList<NotifySupervisionTransition> Transitions { get; init; } = [];
    [JsonPropertyName("wait_events")] public IReadOnlyList<NotifySupervisionWaitEvent> WaitEvents { get; init; } = [];
}

internal sealed record NotifySupervisionWriterIdentity
{
    [JsonPropertyName("pid")] public required int Pid { get; init; }
    [JsonPropertyName("process_start_time")] public required DateTimeOffset ProcessStartTime { get; init; }
    [JsonPropertyName("host")] public required string Host { get; init; }

    public static NotifySupervisionWriterIdentity Current()
    {
        DateTimeOffset processStartTime;
        try
        {
            using var process = Process.GetCurrentProcess();
            processStartTime = new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The identity remains additive even on a platform that refuses
            // to expose process metadata. A current timestamp makes the
            // record explicit but cannot falsely match a later live process.
            processStartTime = DateTimeOffset.UtcNow;
        }

        return new NotifySupervisionWriterIdentity
        {
            Pid = Environment.ProcessId,
            ProcessStartTime = processStartTime,
            Host = Environment.MachineName,
        };
    }

    public bool IsSameWriter(NotifySupervisionWriterIdentity other) =>
        Pid == other.Pid
        && ProcessStartTime == other.ProcessStartTime
        && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);

    public bool IsLiveOn(NotifySupervisionWriterIdentity current)
    {
        if (!string.Equals(Host, current.Host, StringComparison.OrdinalIgnoreCase) || Pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(Pid);
            if (process.HasExited)
            {
                return false;
            }

            var actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime());
            return actualStart == ProcessStartTime;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            return false;
        }
    }
}

internal sealed record NotifySupervisionTransition
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("workspace_id")] public required string WorkspaceId { get; init; }
    [JsonPropertyName("pane_id")] public required string PaneId { get; init; }
    [JsonPropertyName("from_status")] public required string FromStatus { get; init; }
    [JsonPropertyName("to_status")] public required string ToStatus { get; init; }
    [JsonPropertyName("state_change_seq")] public required long StateChangeSequence { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("observed_at")] public required DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("latency_seconds")] public long? LatencySeconds { get; init; }
    [JsonPropertyName("wake_attempted")] public bool WakeAttempted { get; init; }
    [JsonPropertyName("wake_delivered")] public bool WakeDelivered { get; init; }
}

internal sealed record NotifySupervisionWaitEvent
{
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("workspace_id")] public required string WorkspaceId { get; init; }
    [JsonPropertyName("pane_id")] public required string PaneId { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("detail")] public required string Detail { get; init; }
    [JsonPropertyName("observed_at")] public required DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("rearm_attempted")] public bool RearmAttempted { get; init; }
}

internal sealed record NotifyPromptAudit
{
    [JsonPropertyName("attempt_id")] public string? AttemptId { get; init; }
    [JsonPropertyName("prompt_key")] public required string PromptKey { get; init; }
    [JsonPropertyName("seat")] public required string Seat { get; init; }
    [JsonPropertyName("pane")] public required string Pane { get; init; }
    [JsonPropertyName("agent_kind")] public required string AgentKind { get; init; }
    [JsonPropertyName("prompt_class")] public required string PromptClass { get; init; }
    [JsonPropertyName("rule")] public required string Rule { get; init; }
    [JsonPropertyName("actor")] public required string Actor { get; init; }
    [JsonPropertyName("timestamp")] public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("exact_answer_scope")] public string? ExactAnswerScope { get; init; }
    [JsonPropertyName("matched_scopes")] public IReadOnlyList<string> MatchedScopes { get; init; } = [];
    [JsonPropertyName("command_digest")] public string? CommandDigest { get; init; }
    [JsonPropertyName("dialog_hash")] public string? DialogHash { get; init; }
}

internal sealed record NotifySupervisionStallRecord
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("owner_role")] public required string OwnerRole { get; init; }
    [JsonPropertyName("subject_role")] public string? SubjectRole { get; init; }
    [JsonPropertyName("wake_target_role")] public string? WakeTargetRole { get; init; }
    [JsonPropertyName("wake_class")] public string? WakeClass { get; init; }
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
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("observed_prompt")] public NotifyObservedPrompt? Prompt { get; init; }
}

internal sealed record NotifySupervisionReadResult
{
    public required bool Resolved { get; init; }
    public required string Directory { get; init; }
    public NotifySupervisionBound? Bound { get; init; }
    public NotifySupervisionCycle? LastCycle { get; init; }
    public NotifySupervisionCycle? LastIntervalCycle { get; init; }
    public required IReadOnlyDictionary<string, NotifySupervisionStallRecord> ActiveStalls { get; init; }
    public required IReadOnlyList<NotifySupervisionStallRecord> StallHistory { get; init; }
    public required IReadOnlyList<NotifyPromptAudit> PromptAudits { get; init; }
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
    [JsonPropertyName("prompt_audit")] public NotifyPromptAudit? PromptAudit { get; init; }
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("cleared_at")] public DateTimeOffset? ClearedAt { get; init; }
}
