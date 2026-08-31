using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
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
    public const string EmissionPolicyFileName = "emission-policy.json";
    public const string InstalledSupervisorFileName = "installed-supervisor.json";
    public const string CycleFileName = "cycles.jsonl";
    public const string StallFileName = "stalls.jsonl";
    public const string EvidenceDefinitionsFileName = "evidence-definitions.json";
    public const string ShrinkAuditFileName = "shrink-audit.jsonl";
    public const string ShrinkTransactionFileName = "shrink-transaction.json";
    public const string CycleArchiveDirectoryName = "cycles-archive";
    public const string ArchiveTransactionFileName = "archive-transaction.json";
    public const string LocalIgnoreFileName = ".gitignore";
    public static readonly IReadOnlyList<string> CycleHistoryIgnoreLines =
    [
        "**/cycles.jsonl",
        "**/cycles-archive/",
    ];
    public const int DefaultLiveWindowDays = 7;
    private const string LockFileName = ".supervision.lock";
    private const string ShrinkTransactionSchema = "intent-cli.supervision-shrink-transaction/v1";
    private const string ShrinkTransactionStagePrefix = ".shrink-transaction-";
    private const string ArchiveTransactionSchema = "intent-cli.supervision-archive-transaction/v1";
    private const string ArchiveTransactionStagePrefix = ".archive-transaction-";
    internal const string EvidenceSchema = "intent-cli.supervision-evidence/v1";
    internal const string HerdrRegistrationEvidenceKey = "recorded-herdr-seat-registration";
    internal const string HerdrRegistrationDefinition =
        "a recorded herdr seat is registered only when the matching agent-list entry is running at the recorded workspace and pane";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonOptions)
    {
        WriteIndented = true,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly object Sync = new();

    internal static Func<string, string, NotifySupervisionWriteResult>? WriteOverride { get; set; }
    internal static Action<NotifySupervisionShrinkFaultPoint>? ShrinkFaultInjector { get; set; }
    internal static Action<NotifySupervisionArchiveFaultPoint>? ArchiveFaultInjector { get; set; }

    public static string ResolveDirectory(string artifactRoot, string domain, string team)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ValidateSegment(domain, "domain");
        ValidateSegment(team, "team");
        return Path.GetFullPath(Path.Combine(artifactRoot, domain, team));
    }

    public static string ResolveBoundPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), BoundFileName);

    public static string ResolveEmissionPolicyPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), EmissionPolicyFileName);

    public static string ResolveInstalledSupervisorPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), InstalledSupervisorFileName);

    public static string ResolveCyclePath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), CycleFileName);

    public static string ResolveCycleHistoryIgnorePath(string artifactRoot) =>
        Path.Combine(Path.GetFullPath(artifactRoot), LocalIgnoreFileName);

    public static string ResolveCycleArchiveDirectoryPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), CycleArchiveDirectoryName);

    public static string ResolveCycleArchivePath(
        string artifactRoot,
        string domain,
        string team,
        DateTimeOffset period) =>
        Path.Combine(
            ResolveCycleArchiveDirectoryPath(artifactRoot, domain, team),
            GetCycleArchiveFileName(period));

    public static string ResolveStallPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), StallFileName);

    public static string ResolveEvidenceDefinitionsPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), EvidenceDefinitionsFileName);

    public static string ResolveShrinkAuditPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), ShrinkAuditFileName);

    public static string ResolveShrinkTransactionPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), ShrinkTransactionFileName);

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
                using var directoryLock = Directory.Exists(directory)
                    ? AcquireDirectoryLock(directory, createDirectory: false)
                    : null;
                if (File.Exists(Path.Combine(directory, ArchiveTransactionFileName)))
                {
                    RecoverPendingArchiveTransaction(directory);
                }
                var definitions = ReadEvidenceDefinitions(Path.Combine(directory, EvidenceDefinitionsFileName));
                var bound = ReadBound(Path.Combine(directory, BoundFileName));
                var emissionPolicy = ReadEmissionPolicy(Path.Combine(directory, EmissionPolicyFileName));
                var installedSupervisor = ReadInstalledSupervisor(Path.Combine(directory, InstalledSupervisorFileName));
                var cyclePaths = ResolveCycleHistoryPaths(directory);
                var unreadableRecords = new List<NotifySupervisionUnreadableRecord>();
                var cycles = ReadCycles(cyclePaths, directory, unreadableRecords);
                var promptAudits = ReadPromptAudits(cyclePaths, directory, unreadableRecords);
                var stalls = ReadStalls(
                    Path.Combine(directory, StallFileName),
                    directory,
                    definitions,
                    unreadableRecords);
                return new NotifySupervisionReadResult
                {
                    Resolved = true,
                    Directory = directory,
                    Bound = bound,
                    EmissionPolicy = emissionPolicy,
                    InstalledSupervisor = installedSupervisor,
                    LastCycle = cycles.LastOrDefault(),
                    LastIntervalCycle = cycles.LastOrDefault(cycle =>
                        string.IsNullOrWhiteSpace(cycle.Trigger)
                        || string.Equals(cycle.Trigger, "interval", StringComparison.Ordinal)),
                    CycleHistory = cycles,
                    ActiveStalls = stalls.Where(item => item.ClearedAt is null)
                        .ToDictionary(item => item.Key, StringComparer.Ordinal),
                    StallHistory = stalls,
                    PromptAudits = promptAudits,
                    UnreadableRecords = unreadableRecords
                        .OrderBy(item => item.File, StringComparer.Ordinal)
                        .ThenBy(item => item.Line)
                        .ToArray(),
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
                using var directoryLock = AcquireDirectoryLock(Path.GetDirectoryName(path)!, createDirectory: true);
                File.WriteAllText(path, line, Utf8NoBom);
                return new NotifySupervisionWriteResult(true, false, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NotifySupervisionWriteResult(false, false, path, exception.Message);
            }
        }
    }

    public static NotifySupervisionWriteResult RecordEmissionPolicy(
        string artifactRoot,
        NotifySupervisionEmissionPolicy policy,
        bool write)
    {
        string path;
        try
        {
            path = ResolveEmissionPolicyPath(artifactRoot, policy.Domain, policy.Team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new NotifySupervisionWriteResult(false, false, artifactRoot, exception.Message);
        }

        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        var line = JsonSerializer.Serialize(policy, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        lock (Sync)
        {
            try
            {
                using var directoryLock = AcquireDirectoryLock(Path.GetDirectoryName(path)!, createDirectory: true);
                File.WriteAllText(path, line, Utf8NoBom);
                return new NotifySupervisionWriteResult(true, false, path, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NotifySupervisionWriteResult(false, false, path, exception.Message);
            }
        }
    }

    public static NotifySupervisionWriteResult RecordInstalledSupervisor(
        string artifactRoot,
        NotifySupervisionInstalledSupervisor installedSupervisor,
        bool write)
    {
        string path;
        try
        {
            path = ResolveInstalledSupervisorPath(
                artifactRoot,
                installedSupervisor.Domain,
                installedSupervisor.Team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new NotifySupervisionWriteResult(false, false, artifactRoot, exception.Message);
        }

        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        var line = JsonSerializer.Serialize(installedSupervisor, JsonOptions) + Environment.NewLine;
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, line);
        }

        lock (Sync)
        {
            try
            {
                using var directoryLock = AcquireDirectoryLock(Path.GetDirectoryName(path)!, createDirectory: true);
                File.WriteAllText(path, line, Utf8NoBom);
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
        }, ensureCycleHistoryIgnore: true);
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
        }, ensureCycleHistoryIgnore: true);
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

    internal static NotifySupervisionIgnoreResult EnsureCycleHistoryIgnore(
        string artifactRoot,
        bool write)
    {
        string ignorePath;
        try
        {
            ignorePath = ResolveCycleHistoryIgnorePath(artifactRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return NotifySupervisionIgnoreResult.Failure(artifactRoot, exception.Message);
        }

        lock (Sync)
        {
            try
            {
                var existingText = File.Exists(ignorePath)
                    ? File.ReadAllText(ignorePath, Utf8NoBom)
                    : string.Empty;
                var existingLines = existingText
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .ToHashSet(StringComparer.Ordinal);
                var missing = CycleHistoryIgnoreLines
                    .Where(line => !existingLines.Contains(line))
                    .ToArray();
                if (missing.Length == 0 || !write)
                {
                    return new NotifySupervisionIgnoreResult(
                        Applied: false,
                        WouldChange: missing.Length > 0,
                        ignorePath,
                        MissingLines: missing,
                        Error: null);
                }

                var directory = Path.GetDirectoryName(ignorePath)!;
                Directory.CreateDirectory(directory);
                var prefix = existingText.Length == 0 || existingText.EndsWith('\n')
                    ? string.Empty
                    : Environment.NewLine;
                File.AppendAllText(
                    ignorePath,
                    prefix + string.Join(Environment.NewLine, missing) + Environment.NewLine,
                    Utf8NoBom);
                return new NotifySupervisionIgnoreResult(
                    Applied: true,
                    WouldChange: true,
                    ignorePath,
                    MissingLines: [],
                    Error: null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return NotifySupervisionIgnoreResult.Failure(ignorePath, exception.Message);
            }
        }
    }

    private static NotifySupervisionWriteResult Append(
        string path,
        NotifySupervisionEvent entry,
        bool ensureCycleHistoryIgnore = false)
    {
        if (WriteOverride is { } writeOverride)
        {
            var overriddenLine = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            return writeOverride(path, overriddenLine);
        }

        lock (Sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(path)!;
                if (ensureCycleHistoryIgnore && IsInsideGitRepository(directory))
                {
                    var supervisionRoot = ResolveSupervisionRoot(directory);
                    if (supervisionRoot is not null)
                    {
                        var ignoreResult = EnsureCycleHistoryIgnore(supervisionRoot, write: true);
                        if (ignoreResult.Error is not null)
                        {
                            return new NotifySupervisionWriteResult(
                                Applied: false,
                                AlreadyConverged: false,
                                Path: path,
                                Error: ignoreResult.Error);
                        }
                    }
                }
                using var directoryLock = AcquireDirectoryLock(directory, createDirectory: true);
                RecoverPendingArchiveTransaction(directory);
                RecoverPendingShrinkTransaction(directory);
                var storedEntry = PrepareEventForStorage(entry, directory, ensureDefinitions: true);
                var line = JsonSerializer.Serialize(storedEntry, JsonOptions) + Environment.NewLine;
                AtomicAppendWriter.Append(path, Utf8NoBom.GetBytes(line));
                return new NotifySupervisionWriteResult(true, false, path, null);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or InvalidDataException)
            {
                return new NotifySupervisionWriteResult(false, false, path, exception.Message);
            }
        }
    }

    private static string? ResolveSupervisionRoot(string teamDirectory)
    {
        var team = new DirectoryInfo(teamDirectory);
        var domain = team.Parent;
        return domain?.Parent?.FullName;
    }

    private static bool IsInsideGitRepository(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
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

    private static NotifySupervisionEmissionPolicy? ReadEmissionPolicy(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<NotifySupervisionEmissionPolicy>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The supervision emission policy file was empty.");
    }

    private static NotifySupervisionInstalledSupervisor? ReadInstalledSupervisor(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<NotifySupervisionInstalledSupervisor>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The installed supervisor record was empty.");
    }

    private static IReadOnlyList<NotifySupervisionCycle> ReadCycles(
        IEnumerable<string> paths,
        string directory,
        ICollection<NotifySupervisionUnreadableRecord> unreadableRecords)
    {
        var cycles = new List<NotifySupervisionCycle>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryReadEvent(
                        path,
                        directory,
                        lineNumber,
                        "cycles",
                        line,
                        unreadableRecords,
                        out var entry))
                {
                    continue;
                }

                if (!string.Equals(entry!.Kind, "cycle", StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Cycle is null)
                {
                    AddUnreadableRecord(
                        path,
                        directory,
                        lineNumber,
                        "cycles",
                        "missing-cycle-payload",
                        unreadableRecords);
                    continue;
                }

                cycles.Add(entry.Cycle);
            }
        }

        return cycles.OrderBy(item => item.CompletedAt).ToArray();
    }

    private static IReadOnlyList<NotifyPromptAudit> ReadPromptAudits(
        IEnumerable<string> paths,
        string directory,
        ICollection<NotifySupervisionUnreadableRecord> unreadableRecords)
    {
        var audits = new List<NotifyPromptAudit>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryReadEvent(
                        path,
                        directory,
                        lineNumber,
                        "prompt-audits",
                        line,
                        unreadableRecords,
                        out var entry))
                {
                    continue;
                }

                if (!string.Equals(entry!.Kind, "prompt-audit", StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.PromptAudit is null)
                {
                    AddUnreadableRecord(
                        path,
                        directory,
                        lineNumber,
                        "prompt-audits",
                        "missing-prompt-audit-payload",
                        unreadableRecords);
                    continue;
                }

                audits.Add(entry.PromptAudit);
            }
        }
        return audits.OrderBy(item => item.Timestamp).ToArray();
    }

    private static IReadOnlyList<string> ResolveCycleHistoryPaths(string directory)
    {
        var paths = new List<string>();
        var archiveDirectory = Path.Combine(directory, CycleArchiveDirectoryName);
        if (Directory.Exists(archiveDirectory))
        {
            paths.AddRange(
                Directory.EnumerateFiles(archiveDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal));
        }

        var livePath = Path.Combine(directory, CycleFileName);
        if (File.Exists(livePath))
        {
            paths.Add(livePath);
        }

        return paths;
    }

    private static IReadOnlyList<NotifySupervisionStallRecord> ReadStalls(
        string path,
        string directory,
        IReadOnlyDictionary<string, string> definitions,
        ICollection<NotifySupervisionUnreadableRecord> unreadableRecords)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var current = new Dictionary<string, NotifySupervisionStallRecord>(StringComparer.Ordinal);
        var history = new List<NotifySupervisionStallRecord>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryReadEvent(
                    path,
                    directory,
                    lineNumber,
                    "stalls",
                    line,
                    unreadableRecords,
                    out var entry))
            {
                continue;
            }

            if (string.Equals(entry!.Kind, "open", StringComparison.Ordinal))
            {
                if (entry.Stall is null)
                {
                    AddUnreadableRecord(
                        path,
                        directory,
                        lineNumber,
                        "stalls",
                        "missing-stall-payload",
                        unreadableRecords);
                    continue;
                }

                current[entry.Stall.Key] = ResolveStoredStall(entry.Stall, definitions);
                continue;
            }

            if (string.Equals(entry.Kind, "clear", StringComparison.Ordinal))
            {
                if (entry.Key is null)
                {
                    AddUnreadableRecord(
                        path,
                        directory,
                        lineNumber,
                        "stalls",
                        "missing-stall-key",
                        unreadableRecords);
                    continue;
                }

                if (!current.Remove(entry.Key, out var open))
                {
                    continue;
                }

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

    private static bool TryReadEvent(
        string path,
        string directory,
        int lineNumber,
        string component,
        string line,
        ICollection<NotifySupervisionUnreadableRecord> unreadableRecords,
        out NotifySupervisionEvent? entry)
    {
        try
        {
            entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions);
            if (entry is null)
            {
                AddUnreadableRecord(
                    path,
                    directory,
                    lineNumber,
                    component,
                    "empty-event",
                    unreadableRecords);
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.Kind))
            {
                AddUnreadableRecord(
                    path,
                    directory,
                    lineNumber,
                    component,
                    "missing-event-kind",
                    unreadableRecords);
                entry = null;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            // Match shrink's degrade-and-report shape: preserve the rest of
            // the append-only file and expose the exact unreadable line.
            entry = null;
            AddUnreadableRecord(
                path,
                directory,
                lineNumber,
                component,
                "invalid-json",
                unreadableRecords);
            return false;
        }
    }

    private static void AddUnreadableRecord(
        string path,
        string directory,
        int lineNumber,
        string component,
        string reason,
        ICollection<NotifySupervisionUnreadableRecord> unreadableRecords)
    {
        var file = Path.GetRelativePath(directory, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (unreadableRecords.Any(item =>
                item.File == file
                && item.Line == lineNumber))
        {
            return;
        }

        unreadableRecords.Add(new NotifySupervisionUnreadableRecord
        {
            Component = component,
            File = file,
            Line = lineNumber,
            Reason = reason,
        });
    }

    private static IReadOnlyDictionary<string, string> ReadEvidenceDefinitions(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var document = JsonSerializer.Deserialize<NotifySupervisionEvidenceDefinitions>(
            File.ReadAllText(path),
            JsonOptions)
            ?? throw new InvalidDataException("The supervision evidence definition file was empty.");
        if (!string.Equals(document.Schema, EvidenceSchema, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported supervision evidence definition schema '{document.Schema}'.");
        }

        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in document.Definitions)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
            {
                throw new InvalidDataException(
                    "The supervision evidence definition file contains an empty key or definition.");
            }

            definitions.Add(item.Key, item.Value);
        }

        return definitions;
    }

    private static NotifySupervisionStallRecord ResolveStoredStall(
        NotifySupervisionStallRecord record,
        IReadOnlyDictionary<string, string> definitions)
    {
        if (record.EvidenceReference is null)
        {
            return record;
        }

        if (!definitions.TryGetValue(record.EvidenceReference, out var definition))
        {
            throw new InvalidDataException(
                $"Stall '{record.Key}' references unknown supervision evidence '{record.EvidenceReference}'.");
        }

        var evidence = record.Evidence;
        if (record.EvidenceReferenceIncludesEvidence == true)
        {
            evidence = [
                $"registration_definition:{definition}",
                .. (evidence ?? []),
            ];
        }

        return record with
        {
            RegistrationDefinition = record.RegistrationDefinition ?? definition,
            Evidence = evidence,
        };
    }

    private static NotifySupervisionEvent PrepareEventForStorage(
        NotifySupervisionEvent entry,
        string directory,
        bool ensureDefinitions)
    {
        if (entry.Stall is null)
        {
            return entry;
        }

        var compacted = CompactStall(entry.Stall, out var changed);
        if (!changed)
        {
            return entry;
        }

        if (ensureDefinitions)
        {
            EnsureEvidenceDefinitions(directory);
        }
        return entry with { Stall = compacted };
    }

    private static NotifySupervisionStallRecord CompactStall(
        NotifySupervisionStallRecord record,
        out bool changed)
    {
        var definitionMatches = string.Equals(
            record.RegistrationDefinition,
            HerdrRegistrationDefinition,
            StringComparison.Ordinal);
        var evidence = record.Evidence;
        var definitionEvidence = $"registration_definition:{HerdrRegistrationDefinition}";
        var evidenceContainsDefinition = evidence?.Contains(definitionEvidence, StringComparer.Ordinal) == true;
        if (!definitionMatches && !evidenceContainsDefinition)
        {
            changed = false;
            return record;
        }

        var remainingEvidence = evidenceContainsDefinition
            ? evidence!.Where(item => !string.Equals(item, definitionEvidence, StringComparison.Ordinal)).ToArray()
            : evidence;
        var compacted = record with
        {
            RegistrationDefinition = null,
            Evidence = remainingEvidence,
            EvidenceReference = HerdrRegistrationEvidenceKey,
            EvidenceReferenceIncludesEvidence = evidenceContainsDefinition,
        };
        changed = !string.Equals(record.EvidenceReference, compacted.EvidenceReference, StringComparison.Ordinal)
            || record.EvidenceReferenceIncludesEvidence != compacted.EvidenceReferenceIncludesEvidence
            || record.RegistrationDefinition is not null
            || !ReferenceEquals(record.Evidence, compacted.Evidence);
        return compacted;
    }

    private static void EnsureEvidenceDefinitions(string directory)
    {
        var path = Path.Combine(directory, EvidenceDefinitionsFileName);
        var definitions = ReadEvidenceDefinitions(path);
        var content = BuildEvidenceDefinitionsContent(definitions, out var changed);
        if (changed)
        {
            File.WriteAllText(path, content, Utf8NoBom);
        }
    }

    private static string BuildEvidenceDefinitionsContent(
        IReadOnlyDictionary<string, string> definitions,
        out bool changed)
    {
        if (definitions.TryGetValue(HerdrRegistrationEvidenceKey, out var existing)
            && string.Equals(existing, HerdrRegistrationDefinition, StringComparison.Ordinal))
        {
            changed = false;
            return string.Empty;
        }

        var merged = new Dictionary<string, string>(definitions, StringComparer.Ordinal)
        {
            [HerdrRegistrationEvidenceKey] = HerdrRegistrationDefinition,
        };
        var document = new NotifySupervisionEvidenceDefinitions
        {
            Schema = EvidenceSchema,
            Definitions = merged,
        };
        changed = true;
        return JsonSerializer.Serialize(document, ManifestJsonOptions) + Environment.NewLine;
    }

    internal static NotifySupervisionArchiveResult Archive(
        string artifactRoot,
        string domain,
        string team,
        bool write,
        DateTimeOffset occurredAt,
        int liveWindowDays)
    {
        string directory;
        try
        {
            directory = ResolveDirectory(artifactRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return NotifySupervisionArchiveResult.Failure(artifactRoot, liveWindowDays, exception.Message);
        }

        if (liveWindowDays <= 0)
        {
            return NotifySupervisionArchiveResult.Failure(
                directory,
                liveWindowDays,
                "the live window must be greater than zero days.");
        }

        if (write)
        {
            var ignore = EnsureCycleHistoryIgnore(artifactRoot, write: true);
            if (ignore.Error is not null)
            {
                return NotifySupervisionArchiveResult.Failure(directory, liveWindowDays, ignore.Error);
            }
        }

        if (!Directory.Exists(directory) && !write)
        {
            return NotifySupervisionArchiveResult.Empty(directory, liveWindowDays);
        }

        lock (Sync)
        {
            try
            {
                using var directoryLock = AcquireDirectoryLock(directory, createDirectory: write);
                var transactionPath = Path.Combine(directory, ArchiveTransactionFileName);
                if (File.Exists(transactionPath))
                {
                    if (!write)
                    {
                        return NotifySupervisionArchiveResult.Failure(
                            directory,
                            liveWindowDays,
                            "archive-recovery-pending: a prior archive transaction requires --write to recover safely.");
                    }

                    RecoverPendingArchiveTransaction(directory);
                }

                RecoverPendingShrinkTransaction(directory);
                var cutoff = occurredAt.ToUniversalTime().Subtract(TimeSpan.FromDays(liveWindowDays));
                var plan = PlanCycleArchive(directory, cutoff);
                if (write && plan.WouldChange)
                {
                    ExecuteCycleArchiveTransaction(directory, plan);
                }

                return new NotifySupervisionArchiveResult
                {
                    Applied = write && plan.WouldChange,
                    WouldChange = plan.WouldChange,
                    Directory = directory,
                    LivePath = plan.LivePath,
                    ArchiveDirectory = plan.ArchiveDirectory,
                    Cutoff = cutoff,
                    LiveWindowDays = liveWindowDays,
                    BeforeLiveBytes = plan.BeforeLiveBytes,
                    AfterLiveBytes = plan.AfterLiveBytes,
                    BeforeLiveRecordCount = plan.BeforeLiveRecordCount,
                    AfterLiveRecordCount = plan.AfterLiveRecordCount,
                    RecordsMoved = plan.RecordsMoved,
                    RecordsRetained = plan.RecordsRetained,
                    RecordsDiscarded = 0,
                    Archives = plan.Archives,
                    Error = null,
                };
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                return NotifySupervisionArchiveResult.Failure(
                    directory,
                    liveWindowDays,
                    $"archive-validation-failed: {exception.Message}");
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                return NotifySupervisionArchiveResult.Failure(directory, liveWindowDays, exception.Message);
            }
        }
    }

    private static NotifySupervisionArchivePlan PlanCycleArchive(
        string directory,
        DateTimeOffset cutoff)
    {
        var livePath = Path.Combine(directory, CycleFileName);
        var archiveDirectory = Path.Combine(directory, CycleArchiveDirectoryName);
        if (!File.Exists(livePath))
        {
            return NotifySupervisionArchivePlan.Empty(livePath, archiveDirectory);
        }

        var original = File.ReadAllText(livePath, Utf8NoBom);
        var lines = SplitCycleLines(original, out var newline, out var trailingNewline);
        var retained = new List<string>(lines.Count);
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var beforeRecordCount = 0;
        var recordsMoved = 0;

        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                beforeRecordCount++;
            }

            if (TryGetCycleArchiveTimestamp(line, out var timestamp)
                && timestamp < cutoff)
            {
                var period = timestamp.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                if (!grouped.TryGetValue(period, out var periodLines))
                {
                    periodLines = [];
                    grouped.Add(period, periodLines);
                }

                periodLines.Add(line);
                recordsMoved++;
            }
            else
            {
                retained.Add(line);
            }
        }

        var beforeBytes = new FileInfo(livePath).Length;
        if (recordsMoved == 0)
        {
            return new NotifySupervisionArchivePlan
            {
                WouldChange = false,
                LivePath = livePath,
                ArchiveDirectory = archiveDirectory,
                LiveContent = original,
                BeforeLiveBytes = beforeBytes,
                AfterLiveBytes = beforeBytes,
                BeforeLiveRecordCount = beforeRecordCount,
                AfterLiveRecordCount = beforeRecordCount,
                RecordsMoved = 0,
                RecordsRetained = beforeRecordCount,
                Replacements = [],
                Archives = [],
            };
        }

        var liveContent = JoinCycleLines(retained, newline, trailingNewline);
        var replacements = new List<NotifySupervisionArchiveReplacement>();
        var archives = new List<NotifySupervisionArchiveFileMeasurement>();
        foreach (var group in grouped.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var fileName = group.Key + ".jsonl";
            var targetPath = Path.Combine(archiveDirectory, fileName);
            var existingContent = File.Exists(targetPath)
                ? File.ReadAllText(targetPath, Utf8NoBom)
                : string.Empty;
            var archiveContent = AppendArchiveLines(existingContent, group.Value, newline);
            var beforeArchiveBytes = File.Exists(targetPath)
                ? new FileInfo(targetPath).Length
                : 0L;
            var beforeArchiveRecords = CountNonBlankLines(
                SplitCycleLines(existingContent, out _, out _));
            archives.Add(new NotifySupervisionArchiveFileMeasurement
            {
                Period = group.Key,
                Path = targetPath,
                BeforeBytes = beforeArchiveBytes,
                AfterBytes = Utf8NoBom.GetByteCount(archiveContent),
                BeforeRecordCount = beforeArchiveRecords,
                AfterRecordCount = beforeArchiveRecords + group.Value.Count,
                MovedRecordCount = group.Value.Count,
            });
            replacements.Add(new NotifySupervisionArchiveReplacement
            {
                TargetName = $"{CycleArchiveDirectoryName}/{fileName}",
                TargetPath = targetPath,
                Content = archiveContent,
            });
        }

        replacements.Add(new NotifySupervisionArchiveReplacement
        {
            TargetName = CycleFileName,
            TargetPath = livePath,
            Content = liveContent,
        });

        return new NotifySupervisionArchivePlan
        {
            WouldChange = true,
            LivePath = livePath,
            ArchiveDirectory = archiveDirectory,
            LiveContent = liveContent,
            BeforeLiveBytes = beforeBytes,
            AfterLiveBytes = Utf8NoBom.GetByteCount(liveContent),
            BeforeLiveRecordCount = beforeRecordCount,
            AfterLiveRecordCount = CountNonBlankLines(retained),
            RecordsMoved = recordsMoved,
            RecordsRetained = CountNonBlankLines(retained),
            Replacements = replacements,
            Archives = archives,
        };
    }

    private static List<string> SplitCycleLines(
        string content,
        out string newline,
        out bool trailingNewline)
    {
        newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        trailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (trailingNewline)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static string JoinCycleLines(
        IReadOnlyList<string> lines,
        string newline,
        bool trailingNewline) =>
        lines.Count == 0
            ? string.Empty
            : string.Join(newline, lines) + (trailingNewline ? newline : string.Empty);

    private static string AppendArchiveLines(
        string existing,
        IReadOnlyList<string> additions,
        string newline)
    {
        var builder = new StringBuilder(existing);
        if (builder.Length > 0 && !existing.EndsWith(newline, StringComparison.Ordinal))
        {
            builder.Append(newline);
        }

        builder.Append(string.Join(newline, additions));
        builder.Append(newline);
        return builder.ToString();
    }

    private static bool TryGetCycleArchiveTimestamp(
        string line,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            var entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions);
            if (entry is null)
            {
                return false;
            }

            if (string.Equals(entry.Kind, "cycle", StringComparison.Ordinal)
                && entry.Cycle is not null)
            {
                timestamp = entry.Cycle.CompletedAt.ToUniversalTime();
                return true;
            }

            if (string.Equals(entry.Kind, "prompt-audit", StringComparison.Ordinal)
                && entry.PromptAudit is not null)
            {
                timestamp = entry.PromptAudit.Timestamp.ToUniversalTime();
                return true;
            }
        }
        catch (JsonException)
        {
            // Preserve malformed or unknown lines in the live file. Archiving
            // is fail-closed so this command never discards an unreadable line.
        }

        return false;
    }

    private static string GetCycleArchiveFileName(DateTimeOffset period) =>
        period.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl";

    private static void ExecuteCycleArchiveTransaction(
        string directory,
        NotifySupervisionArchivePlan plan)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var stageDirectoryName = ArchiveTransactionStagePrefix + transactionId;
        var stageDirectory = Path.Combine(directory, stageDirectoryName);
        Directory.CreateDirectory(stageDirectory);

        var transactionFiles = new List<NotifySupervisionArchiveTransactionFile>(
            plan.Replacements.Count);
        for (var index = 0; index < plan.Replacements.Count; index++)
        {
            var replacement = plan.Replacements[index];
            var stageName = $"replacement-{index}.jsonl";
            var stagePath = ResolveArchiveStagePath(stageDirectory, stageName);
            ReplaceAtomically(stagePath, replacement.Content);
            transactionFiles.Add(new NotifySupervisionArchiveTransactionFile
            {
                TargetName = replacement.TargetName,
                StageName = stageName,
                BeforeSha256 = HashFileOrNull(replacement.TargetPath),
                AfterSha256 = HashContent(replacement.Content),
            });
        }

        var transaction = new NotifySupervisionArchiveTransaction
        {
            Schema = ArchiveTransactionSchema,
            TransactionId = transactionId,
            Phase = "prepared",
            StageDirectory = stageDirectoryName,
            Files = transactionFiles,
        };
        PersistArchiveTransaction(directory, transaction);
        ArchiveFaultInjector?.Invoke(NotifySupervisionArchiveFaultPoint.BeforeReplacement);

        foreach (var transactionFile in transaction.Files)
        {
            var targetPath = ResolveArchiveTransactionTargetPath(directory, transactionFile.TargetName);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var stagePath = ResolveArchiveStagePath(stageDirectory, transactionFile.StageName);
            ReplaceAtomically(targetPath, File.ReadAllText(stagePath, Utf8NoBom));
            transaction = transaction with { Phase = $"replaced:{transactionFile.TargetName}" };
            PersistArchiveTransaction(directory, transaction);
            ArchiveFaultInjector?.Invoke(ResolveArchiveFaultPoint(transactionFile.TargetName));
        }

        DeleteArchiveTransactionArtifacts(directory, transaction);
    }

    private static void RecoverPendingArchiveTransaction(string directory)
    {
        var transactionPath = Path.Combine(directory, ArchiveTransactionFileName);
        if (!File.Exists(transactionPath))
        {
            return;
        }

        var transaction = JsonSerializer.Deserialize<NotifySupervisionArchiveTransaction>(
            File.ReadAllText(transactionPath, Utf8NoBom),
            JsonOptions)
            ?? throw new InvalidDataException("archive-recovery-invalid: the transaction journal was empty.");
        if (!string.Equals(transaction.Schema, ArchiveTransactionSchema, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"archive-recovery-invalid: unsupported transaction schema '{transaction.Schema}'.");
        }

        var stageDirectory = ResolveArchiveStageDirectory(directory, transaction.StageDirectory);
        foreach (var transactionFile in transaction.Files)
        {
            var targetPath = ResolveArchiveTransactionTargetPath(directory, transactionFile.TargetName);
            var currentHash = HashFileOrNull(targetPath);
            if (string.Equals(currentHash, transactionFile.AfterSha256, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(currentHash, transactionFile.BeforeSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"archive-recovery-aborted: target '{transactionFile.TargetName}' changed outside the transaction.");
            }

            var stagePath = ResolveArchiveStagePath(stageDirectory, transactionFile.StageName);
            if (!File.Exists(stagePath))
            {
                throw new InvalidDataException(
                    $"archive-recovery-invalid: staged replacement for '{transactionFile.TargetName}' is missing.");
            }

            var content = File.ReadAllText(stagePath, Utf8NoBom);
            if (!string.Equals(HashContent(content), transactionFile.AfterSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"archive-recovery-invalid: staged replacement for '{transactionFile.TargetName}' is corrupt.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            ReplaceAtomically(targetPath, content);
            if (!string.Equals(HashFileOrNull(targetPath), transactionFile.AfterSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"archive-recovery-aborted: recovered replacement for '{transactionFile.TargetName}' did not verify.");
            }

            transaction = transaction with { Phase = $"recovered:{transactionFile.TargetName}" };
            PersistArchiveTransaction(directory, transaction);
        }

        DeleteArchiveTransactionArtifacts(directory, transaction);
    }

    private static void PersistArchiveTransaction(
        string directory,
        NotifySupervisionArchiveTransaction transaction) =>
        ReplaceAtomically(
            Path.Combine(directory, ArchiveTransactionFileName),
            JsonSerializer.Serialize(transaction, JsonOptions) + Environment.NewLine);

    private static string ResolveArchiveStageDirectory(
        string directory,
        string stageDirectoryName)
    {
        if (!stageDirectoryName.StartsWith(ArchiveTransactionStagePrefix, StringComparison.Ordinal)
            || Path.GetFileName(stageDirectoryName) != stageDirectoryName)
        {
            throw new InvalidDataException("archive-recovery-invalid: the transaction stage directory is unsafe.");
        }

        return Path.Combine(directory, stageDirectoryName);
    }

    private static string ResolveArchiveStagePath(
        string stageDirectory,
        string stageName)
    {
        if (string.IsNullOrWhiteSpace(stageName)
            || Path.GetFileName(stageName) != stageName
            || stageName is "." or "..")
        {
            throw new InvalidDataException("archive-recovery-invalid: a transaction stage path is unsafe.");
        }

        return Path.Combine(stageDirectory, stageName);
    }

    private static string ResolveArchiveTransactionTargetPath(
        string directory,
        string targetName)
    {
        if (string.Equals(targetName, CycleFileName, StringComparison.Ordinal))
        {
            return Path.Combine(directory, CycleFileName);
        }

        var prefix = CycleArchiveDirectoryName + "/";
        if (!targetName.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"archive-recovery-invalid: unsupported transaction target '{targetName}'.");
        }

        var fileName = targetName[prefix.Length..];
        if (Path.GetFileName(fileName) != fileName
            || !fileName.EndsWith(".jsonl", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"archive-recovery-invalid: unsafe transaction target '{targetName}'.");
        }

        return Path.Combine(directory, CycleArchiveDirectoryName, fileName);
    }

    private static void DeleteArchiveTransactionArtifacts(
        string directory,
        NotifySupervisionArchiveTransaction transaction)
    {
        var stageDirectory = ResolveArchiveStageDirectory(directory, transaction.StageDirectory);
        if (Directory.Exists(stageDirectory))
        {
            Directory.Delete(stageDirectory, recursive: true);
        }

        var transactionPath = Path.Combine(directory, ArchiveTransactionFileName);
        if (File.Exists(transactionPath))
        {
            File.Delete(transactionPath);
        }
    }

    private static NotifySupervisionArchiveFaultPoint ResolveArchiveFaultPoint(
        string targetName) =>
        string.Equals(targetName, CycleFileName, StringComparison.Ordinal)
            ? NotifySupervisionArchiveFaultPoint.AfterLiveReplacement
            : NotifySupervisionArchiveFaultPoint.AfterArchiveReplacement;

    internal static NotifySupervisionShrinkResult Shrink(
        string artifactRoot,
        string domain,
        string team,
        bool write,
        DateTimeOffset occurredAt,
        string supervisorState,
        NotifySupervisionWriterIdentity? supervisorWriter)
    {
        string directory;
        try
        {
            directory = ResolveDirectory(artifactRoot, domain, team);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return NotifySupervisionShrinkResult.Failure(artifactRoot, exception.Message);
        }

        if (!Directory.Exists(directory) && !write)
        {
            return NotifySupervisionShrinkResult.Empty(directory, write);
        }

        if (write)
        {
            var ignore = EnsureCycleHistoryIgnore(artifactRoot, write: true);
            if (ignore.Error is not null)
            {
                return NotifySupervisionShrinkResult.Failure(directory, ignore.Error);
            }
        }

        lock (Sync)
        {
            try
            {
                using var directoryLock = AcquireDirectoryLock(directory, createDirectory: write);
                var definitionsPath = Path.Combine(directory, EvidenceDefinitionsFileName);
                var definitions = ReadEvidenceDefinitions(definitionsPath);
                var stalls = PlanFile(
                    Path.Combine(directory, StallFileName),
                    transformStalls: true,
                    definitions);
                var cycles = PlanFile(
                    Path.Combine(directory, CycleFileName),
                    transformStalls: false,
                    definitions);

                // This first complete read/plan is the fail-closed boundary:
                // every retained evidence_ref and its manifest dependency is
                // resolved under the shared lock before recovery or a new
                // transaction can write anything.
                if (File.Exists(Path.Combine(directory, ShrinkTransactionFileName)))
                {
                    if (!write)
                    {
                        return NotifySupervisionShrinkResult.Failure(
                            directory,
                            "shrink-recovery-pending: a prior shrink transaction requires --write to recover safely.");
                    }

                    RecoverPendingShrinkTransaction(directory);
                    definitions = ReadEvidenceDefinitions(definitionsPath);
                    stalls = PlanFile(
                        Path.Combine(directory, StallFileName),
                        transformStalls: true,
                        definitions);
                    cycles = PlanFile(
                        Path.Combine(directory, CycleFileName),
                        transformStalls: false,
                        definitions);
                }

                var plans = new[] { stalls, cycles };
                var beforeBytes = plans.Sum(item => item.BeforeBytes);
                var afterBytes = plans.Sum(item => item.AfterBytes);
                var beforeRecords = plans.Sum(item => item.BeforeRecords);
                var afterRecords = plans.Sum(item => item.AfterRecords);
                var invariantLiteralBytesRemoved = plans.Sum(item =>
                    item.InvariantLiteralBytesBefore - item.InvariantLiteralBytesAfter);
                var invariantReferenceBytesAdded = plans.Sum(item =>
                    item.InvariantReferenceBytesAfter - item.InvariantReferenceBytesBefore);
                var invariantBytesSavedInRecords = plans.Sum(item => item.InvariantBytesSavedInChangedLines);
                var otherBytesSaved = beforeBytes - afterBytes - invariantBytesSavedInRecords;
                var referencesNeeded = plans.Any(item => item.EvidenceReferencesAfter > 0);
                var manifestChanged = false;
                var manifestContent = referencesNeeded
                    ? BuildEvidenceDefinitionsContent(definitions, out manifestChanged)
                    : null;
                var transactionId = write && (manifestChanged || plans.Any(ShouldReplace))
                    ? Guid.NewGuid().ToString("N")
                    : null;

                var audit = new NotifySupervisionShrinkAudit
                {
                    Schema = "intent-cli.supervision-shrink/v1",
                    Outcome = "completed",
                    TransactionId = transactionId,
                    OccurredAt = occurredAt,
                    SupervisorState = supervisorState,
                    SupervisorWriter = supervisorWriter,
                    BeforeBytes = beforeBytes,
                    AfterBytes = afterBytes,
                    BeforeRecordCount = beforeRecords,
                    AfterRecordCount = afterRecords,
                    BeforeAverageBytesPerRecord = Average(beforeBytes, beforeRecords),
                    AfterAverageBytesPerRecord = Average(afterBytes, afterRecords),
                    InvariantLiteralBytesRemoved = invariantLiteralBytesRemoved,
                    InvariantReferenceBytesAdded = invariantReferenceBytesAdded,
                    InvariantBytesSavedInRecords = invariantBytesSavedInRecords,
                    OtherBytesSaved = otherBytesSaved,
                    RecordsArchived = 0,
                    RecordsDiscarded = 0,
                    RecordsCompacted = afterRecords,
                    RecordsRotated = 0,
                    Files = plans.ToDictionary(
                        item => item.Name,
                        item => new NotifySupervisionFileShrinkAudit
                        {
                            BeforeBytes = item.BeforeBytes,
                            AfterBytes = item.AfterBytes,
                            BeforeRecordCount = item.BeforeRecords,
                            AfterRecordCount = item.AfterRecords,
                            Action = item.Exists
                                ? item.TransformStalls
                                    ? "atomically compacted; every stall event retained"
                                    : "atomically rewritten; every cycle and prompt-audit event retained"
                                : "absent; no rotation or discard",
                        },
                        StringComparer.Ordinal),
                    EvidenceReference = referencesNeeded
                        ? $"{EvidenceDefinitionsFileName}#{HerdrRegistrationEvidenceKey}"
                        : null,
                    AuditSummary = "No records were archived, discarded, or rotated. Existing stalls and cycles were retained in place; invariant registration prose was moved once to the readable definition manifest and records now reference it.",
                };

                if (write)
                {
                    var replacements = new List<NotifySupervisionShrinkReplacement>();
                    if (manifestContent is not null && manifestChanged)
                    {
                        replacements.Add(new NotifySupervisionShrinkReplacement
                        {
                            TargetName = EvidenceDefinitionsFileName,
                            TargetPath = definitionsPath,
                            Content = manifestContent,
                        });
                    }

                    replacements.AddRange(
                        plans
                            .Where(ShouldReplace)
                            .Select(plan => new NotifySupervisionShrinkReplacement
                            {
                                TargetName = plan.Name,
                                TargetPath = plan.Path,
                                Content = plan.Content,
                            }));

                    if (replacements.Count == 0)
                    {
                        AppendShrinkAudit(directory, audit);
                    }
                    else
                    {
                        ExecuteShrinkTransaction(directory, transactionId!, replacements, audit);
                    }
                }

                return new NotifySupervisionShrinkResult
                {
                    Applied = write,
                    WouldChange = stalls.Changed || cycles.Exists || manifestChanged,
                    Directory = directory,
                    StallFile = stalls.ToMeasurement(),
                    CycleFile = cycles.ToMeasurement(),
                    BeforeBytes = beforeBytes,
                    AfterBytes = afterBytes,
                    BeforeRecordCount = beforeRecords,
                    AfterRecordCount = afterRecords,
                    BeforeAverageBytesPerRecord = Average(beforeBytes, beforeRecords),
                    AfterAverageBytesPerRecord = Average(afterBytes, afterRecords),
                    InvariantLiteralBytesRemoved = invariantLiteralBytesRemoved,
                    InvariantReferenceBytesAdded = invariantReferenceBytesAdded,
                    InvariantBytesSavedInRecords = invariantBytesSavedInRecords,
                    OtherBytesSaved = otherBytesSaved,
                    EvidenceDefinitionsPath = referencesNeeded
                        ? definitionsPath
                        : File.Exists(definitionsPath) ? definitionsPath : null,
                    AuditPath = write ? Path.Combine(directory, ShrinkAuditFileName) : null,
                    Audit = audit,
                    Error = null,
                };
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                return NotifySupervisionShrinkResult.Failure(
                    directory,
                    $"shrink-validation-failed: {exception.Message}");
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                return NotifySupervisionShrinkResult.Failure(directory, exception.Message);
            }
        }
    }

    private static FileCompactionPlan PlanFile(
        string path,
        bool transformStalls,
        IReadOnlyDictionary<string, string> definitions)
    {
        if (!File.Exists(path))
        {
            return FileCompactionPlan.Absent(path, transformStalls);
        }

        var original = File.ReadAllText(path);
        var lines = original
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .ToList();
        var trailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (trailingNewline)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var transformed = new List<string>(lines.Count);
        var changed = false;
        var invariantBytesSavedInChangedLines = 0L;
        foreach (var line in lines)
        {
            if (!transformStalls || string.IsNullOrWhiteSpace(line))
            {
                transformed.Add(line);
                continue;
            }

            var entry = JsonSerializer.Deserialize<NotifySupervisionEvent>(line, JsonOptions)
                ?? throw new InvalidDataException($"The supervision event in '{path}' was empty.");
            if (entry.Stall?.EvidenceReference is not null)
            {
                // Resolve every retained reference while the shared lock is
                // held, before any manifest, JSONL file, or audit write.
                _ = ResolveStoredStall(entry.Stall, definitions);
            }

            var compacted = PrepareEventForStorage(
                entry,
                Path.GetDirectoryName(path)!,
                ensureDefinitions: false);
            var compactLine = JsonSerializer.Serialize(compacted, JsonOptions);
            transformed.Add(compactLine);
            if (!string.Equals(line, compactLine, StringComparison.Ordinal))
            {
                changed = true;
                invariantBytesSavedInChangedLines +=
                    Utf8NoBom.GetByteCount(line) - Utf8NoBom.GetByteCount(compactLine);
            }
        }

        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var content = string.Join(newline, transformed) + (trailingNewline ? newline : string.Empty);
        var beforeBytes = new FileInfo(path).Length;
        var beforeRecords = lines.Count(line => !string.IsNullOrWhiteSpace(line));
        var afterRecords = transformed.Count(line => !string.IsNullOrWhiteSpace(line));
        return new FileCompactionPlan
        {
            Name = Path.GetFileName(path),
            Path = path,
            Exists = true,
            TransformStalls = transformStalls,
            Changed = changed,
            Content = content,
            BeforeBytes = beforeBytes,
            AfterBytes = Utf8NoBom.GetByteCount(content),
            BeforeRecords = beforeRecords,
            AfterRecords = afterRecords,
            InvariantLiteralBytesBefore = CountOccurrences(original, HerdrRegistrationDefinition)
                * Utf8NoBom.GetByteCount(HerdrRegistrationDefinition),
            InvariantLiteralBytesAfter = CountOccurrences(content, HerdrRegistrationDefinition)
                * Utf8NoBom.GetByteCount(HerdrRegistrationDefinition),
            InvariantReferenceBytesBefore = CountOccurrences(original, HerdrRegistrationEvidenceKey)
                * Utf8NoBom.GetByteCount(HerdrRegistrationEvidenceKey),
            InvariantReferenceBytesAfter = CountOccurrences(content, HerdrRegistrationEvidenceKey)
                * Utf8NoBom.GetByteCount(HerdrRegistrationEvidenceKey),
            InvariantBytesSavedInChangedLines = invariantBytesSavedInChangedLines,
            EvidenceReferencesAfter = CountOccurrences(content, HerdrRegistrationEvidenceKey),
        };
    }

    private static bool ShouldReplace(FileCompactionPlan plan) =>
        plan.Exists && (!plan.TransformStalls || plan.Changed);

    private static void ExecuteShrinkTransaction(
        string directory,
        string transactionId,
        IReadOnlyList<NotifySupervisionShrinkReplacement> replacements,
        NotifySupervisionShrinkAudit audit)
    {
        var stageDirectoryName = ShrinkTransactionStagePrefix + transactionId;
        var stageDirectory = Path.Combine(directory, stageDirectoryName);
        Directory.CreateDirectory(stageDirectory);

        var transactionFiles = new List<NotifySupervisionShrinkTransactionFile>(replacements.Count);
        foreach (var replacement in replacements)
        {
            var stagePath = ResolveTransactionChildPath(stageDirectory, replacement.TargetName);
            ReplaceAtomically(stagePath, replacement.Content);
            transactionFiles.Add(new NotifySupervisionShrinkTransactionFile
            {
                TargetName = replacement.TargetName,
                StageName = replacement.TargetName,
                BeforeSha256 = HashFileOrNull(replacement.TargetPath),
                AfterSha256 = HashContent(replacement.Content),
            });
        }

        var transaction = new NotifySupervisionShrinkTransaction
        {
            Schema = ShrinkTransactionSchema,
            TransactionId = transactionId,
            OccurredAt = audit.OccurredAt,
            Phase = "prepared",
            StageDirectory = stageDirectoryName,
            Files = transactionFiles,
            Audit = audit,
        };
        PersistShrinkTransaction(directory, transaction);

        foreach (var replacement in replacements)
        {
            ReplaceAtomically(replacement.TargetPath, replacement.Content);
            transaction = transaction with { Phase = $"replaced:{replacement.TargetName}" };
            PersistShrinkTransaction(directory, transaction);
            ShrinkFaultInjector?.Invoke(ResolveFaultPoint(replacement.TargetName));
        }

        transaction = transaction with { Phase = "audit-pending" };
        PersistShrinkTransaction(directory, transaction);
        ShrinkFaultInjector?.Invoke(NotifySupervisionShrinkFaultPoint.BeforeAuditAppend);
        AppendShrinkAudit(directory, audit);
        DeleteShrinkTransactionArtifacts(directory, transaction);
    }

    private static void RecoverPendingShrinkTransaction(string directory)
    {
        var transactionPath = Path.Combine(directory, ShrinkTransactionFileName);
        if (!File.Exists(transactionPath))
        {
            return;
        }

        var transaction = JsonSerializer.Deserialize<NotifySupervisionShrinkTransaction>(
            File.ReadAllText(transactionPath),
            JsonOptions)
            ?? throw new InvalidDataException("shrink-recovery-invalid: the transaction journal was empty.");
        if (!string.Equals(transaction.Schema, ShrinkTransactionSchema, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"shrink-recovery-invalid: unsupported transaction schema '{transaction.Schema}'.");
        }

        var stageDirectory = ResolveTransactionStageDirectory(directory, transaction);
        if (ShrinkAuditContainsTransaction(directory, transaction.TransactionId))
        {
            DeleteShrinkTransactionArtifacts(directory, transaction);
            return;
        }

        foreach (var transactionFile in transaction.Files)
        {
            var targetPath = ResolveTransactionTargetPath(directory, transactionFile.TargetName);
            var stagePath = ResolveTransactionChildPath(stageDirectory, transactionFile.StageName);
            var currentHash = HashFileOrNull(targetPath);
            if (string.Equals(currentHash, transactionFile.AfterSha256, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(currentHash, transactionFile.BeforeSha256, StringComparison.Ordinal))
            {
                AbortShrinkTransaction(
                    directory,
                    transaction,
                    $"target '{transactionFile.TargetName}' changed outside the transaction");
                return;
            }

            if (!File.Exists(stagePath))
            {
                AbortShrinkTransaction(
                    directory,
                    transaction,
                    $"staged replacement for '{transactionFile.TargetName}' is missing");
                return;
            }

            var stagedContent = File.ReadAllText(stagePath);
            if (!string.Equals(HashContent(stagedContent), transactionFile.AfterSha256, StringComparison.Ordinal))
            {
                AbortShrinkTransaction(
                    directory,
                    transaction,
                    $"staged replacement for '{transactionFile.TargetName}' is unreadable or corrupt");
                return;
            }

            ReplaceAtomically(targetPath, stagedContent);
            if (!string.Equals(HashFileOrNull(targetPath), transactionFile.AfterSha256, StringComparison.Ordinal))
            {
                AbortShrinkTransaction(
                    directory,
                    transaction,
                    $"recovered replacement for '{transactionFile.TargetName}' did not verify");
                return;
            }

            transaction = transaction with { Phase = $"recovered:{transactionFile.TargetName}" };
            PersistShrinkTransaction(directory, transaction);
        }

        var recoveredAudit = transaction.Audit with
        {
            Outcome = "recovered-completed",
            RecoveryDetail = $"Recovered transaction after phase '{transaction.Phase}'; every staged replacement was verified before audit append.",
        };
        AppendShrinkAudit(directory, recoveredAudit);
        DeleteShrinkTransactionArtifacts(directory, transaction);
    }

    private static void AbortShrinkTransaction(
        string directory,
        NotifySupervisionShrinkTransaction transaction,
        string reason)
    {
        var abortedAudit = transaction.Audit with
        {
            Outcome = "aborted",
            RecoveryDetail = reason,
        };
        AppendShrinkAudit(directory, abortedAudit);
        DeleteShrinkTransactionArtifacts(directory, transaction);
        throw new InvalidDataException($"shrink-recovery-aborted: {reason}");
    }

    private static void PersistShrinkTransaction(
        string directory,
        NotifySupervisionShrinkTransaction transaction)
    {
        ReplaceAtomically(
            Path.Combine(directory, ShrinkTransactionFileName),
            JsonSerializer.Serialize(transaction, JsonOptions) + Environment.NewLine);
    }

    private static void AppendShrinkAudit(
        string directory,
        NotifySupervisionShrinkAudit audit)
    {
        var auditPath = Path.Combine(directory, ShrinkAuditFileName);
        var bytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(audit, JsonOptions) + Environment.NewLine);
        using var stream = new FileStream(
            auditPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static bool ShrinkAuditContainsTransaction(string directory, string transactionId)
    {
        var auditPath = Path.Combine(directory, ShrinkAuditFileName);
        if (!File.Exists(auditPath))
        {
            return false;
        }

        foreach (var line in File.ReadLines(auditPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("transaction_id", out var value)
                    && string.Equals(value.GetString(), transactionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // A prior process may have stopped while appending a JSONL
                // line. The transaction journal remains authoritative and a
                // valid recovery outcome is appended below.
            }
        }

        return false;
    }

    private static string ResolveTransactionStageDirectory(
        string directory,
        NotifySupervisionShrinkTransaction transaction)
    {
        if (!transaction.StageDirectory.StartsWith(ShrinkTransactionStagePrefix, StringComparison.Ordinal)
            || transaction.StageDirectory.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidDataException("shrink-recovery-invalid: the transaction stage directory is unsafe.");
        }

        return ResolveTransactionChildPath(directory, transaction.StageDirectory);
    }

    private static string ResolveTransactionTargetPath(string directory, string targetName) =>
        targetName switch
        {
            EvidenceDefinitionsFileName => Path.Combine(directory, EvidenceDefinitionsFileName),
            StallFileName => Path.Combine(directory, StallFileName),
            CycleFileName => Path.Combine(directory, CycleFileName),
            _ => throw new InvalidDataException(
                $"shrink-recovery-invalid: unsupported transaction target '{targetName}'."),
        };

    private static string ResolveTransactionChildPath(string directory, string childName)
    {
        if (string.IsNullOrWhiteSpace(childName)
            || Path.GetFileName(childName) != childName
            || childName is "." or "..")
        {
            throw new InvalidDataException("shrink-recovery-invalid: a transaction path is unsafe.");
        }

        return Path.Combine(directory, childName);
    }

    private static NotifySupervisionShrinkFaultPoint ResolveFaultPoint(string targetName) =>
        targetName switch
        {
            EvidenceDefinitionsFileName => NotifySupervisionShrinkFaultPoint.AfterManifestReplacement,
            StallFileName => NotifySupervisionShrinkFaultPoint.AfterStallsReplacement,
            CycleFileName => NotifySupervisionShrinkFaultPoint.AfterCyclesReplacement,
            _ => throw new InvalidDataException(
                $"shrink-transaction-invalid: unsupported replacement target '{targetName}'."),
        };

    private static string HashContent(string content) =>
        Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(content)));

    private static string? HashFileOrNull(string path) =>
        File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            : null;

    private static void DeleteShrinkTransactionArtifacts(
        string directory,
        NotifySupervisionShrinkTransaction transaction)
    {
        var stageDirectory = ResolveTransactionStageDirectory(directory, transaction);
        if (Directory.Exists(stageDirectory))
        {
            Directory.Delete(stageDirectory, recursive: true);
        }

        var transactionPath = Path.Combine(directory, ShrinkTransactionFileName);
        if (File.Exists(transactionPath))
        {
            File.Delete(transactionPath);
        }
    }

    private static void ReplaceAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8NoBom, 4096, leaveOpen: true))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static FileStream AcquireDirectoryLock(string directory, bool createDirectory)
    {
        if (createDirectory)
        {
            Directory.CreateDirectory(directory);
        }

        var lockPath = Path.Combine(directory, LockFileName);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
        }
    }

    private static int CountOccurrences(string value, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }

    private static int CountNonBlankLines(IEnumerable<string> lines) =>
        lines.Count(line => !string.IsNullOrWhiteSpace(line));

    private static double? Average(long bytes, int records) =>
        records == 0 ? null : (double)bytes / records;

    private static NotifySupervisionReadResult Failure(string directory, string error) => new()
    {
        Resolved = false,
        CycleHistory = [],
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

internal enum NotifySupervisionArchiveFaultPoint
{
    BeforeReplacement,
    AfterArchiveReplacement,
    AfterLiveReplacement,
}

internal sealed record NotifySupervisionArchiveReplacement
{
    public required string TargetName { get; init; }
    public required string TargetPath { get; init; }
    public required string Content { get; init; }
}

internal sealed record NotifySupervisionArchivePlan
{
    public required bool WouldChange { get; init; }
    public required string LivePath { get; init; }
    public required string ArchiveDirectory { get; init; }
    public required string LiveContent { get; init; }
    public required long BeforeLiveBytes { get; init; }
    public required long AfterLiveBytes { get; init; }
    public required int BeforeLiveRecordCount { get; init; }
    public required int AfterLiveRecordCount { get; init; }
    public required int RecordsMoved { get; init; }
    public required int RecordsRetained { get; init; }
    public required IReadOnlyList<NotifySupervisionArchiveReplacement> Replacements { get; init; }
    public required IReadOnlyList<NotifySupervisionArchiveFileMeasurement> Archives { get; init; }

    public static NotifySupervisionArchivePlan Empty(string livePath, string archiveDirectory) => new()
    {
        WouldChange = false,
        LivePath = livePath,
        ArchiveDirectory = archiveDirectory,
        LiveContent = string.Empty,
        BeforeLiveBytes = 0,
        AfterLiveBytes = 0,
        BeforeLiveRecordCount = 0,
        AfterLiveRecordCount = 0,
        RecordsMoved = 0,
        RecordsRetained = 0,
        Replacements = [],
        Archives = [],
    };
}

internal sealed record NotifySupervisionArchiveFileMeasurement
{
    public required string Period { get; init; }
    public required string Path { get; init; }
    public required long BeforeBytes { get; init; }
    public required long AfterBytes { get; init; }
    public required int BeforeRecordCount { get; init; }
    public required int AfterRecordCount { get; init; }
    public required int MovedRecordCount { get; init; }
}

internal sealed record NotifySupervisionArchiveTransaction
{
    [JsonPropertyName("schema")] public required string Schema { get; init; }
    [JsonPropertyName("transaction_id")] public required string TransactionId { get; init; }
    [JsonPropertyName("phase")] public required string Phase { get; init; }
    [JsonPropertyName("stage_directory")] public required string StageDirectory { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyList<NotifySupervisionArchiveTransactionFile> Files { get; init; }
}

internal sealed record NotifySupervisionArchiveTransactionFile
{
    [JsonPropertyName("target")] public required string TargetName { get; init; }
    [JsonPropertyName("stage")] public required string StageName { get; init; }
    [JsonPropertyName("before_sha256")] public string? BeforeSha256 { get; init; }
    [JsonPropertyName("after_sha256")] public required string AfterSha256 { get; init; }
}

internal sealed record NotifySupervisionArchiveResult
{
    public required bool Applied { get; init; }
    public required bool WouldChange { get; init; }
    public required string Directory { get; init; }
    public required string LivePath { get; init; }
    public required string ArchiveDirectory { get; init; }
    public required DateTimeOffset Cutoff { get; init; }
    public required int LiveWindowDays { get; init; }
    public required long BeforeLiveBytes { get; init; }
    public required long AfterLiveBytes { get; init; }
    public required int BeforeLiveRecordCount { get; init; }
    public required int AfterLiveRecordCount { get; init; }
    public required int RecordsMoved { get; init; }
    public required int RecordsRetained { get; init; }
    public required int RecordsDiscarded { get; init; }
    public required IReadOnlyList<NotifySupervisionArchiveFileMeasurement> Archives { get; init; }
    public string? Error { get; init; }

    public static NotifySupervisionArchiveResult Empty(string directory, int liveWindowDays) => new()
    {
        Applied = false,
        WouldChange = false,
        Directory = directory,
        LivePath = Path.Combine(directory, NotifySupervisionStore.CycleFileName),
        ArchiveDirectory = Path.Combine(directory, NotifySupervisionStore.CycleArchiveDirectoryName),
        Cutoff = DateTimeOffset.MinValue,
        LiveWindowDays = liveWindowDays,
        BeforeLiveBytes = 0,
        AfterLiveBytes = 0,
        BeforeLiveRecordCount = 0,
        AfterLiveRecordCount = 0,
        RecordsMoved = 0,
        RecordsRetained = 0,
        RecordsDiscarded = 0,
        Archives = [],
    };

    public static NotifySupervisionArchiveResult Failure(
        string directory,
        int liveWindowDays,
        string error) => new()
    {
        Applied = false,
        WouldChange = false,
        Directory = directory,
        LivePath = Path.Combine(directory, NotifySupervisionStore.CycleFileName),
        ArchiveDirectory = Path.Combine(directory, NotifySupervisionStore.CycleArchiveDirectoryName),
        Cutoff = DateTimeOffset.MinValue,
        LiveWindowDays = liveWindowDays,
        BeforeLiveBytes = 0,
        AfterLiveBytes = 0,
        BeforeLiveRecordCount = 0,
        AfterLiveRecordCount = 0,
        RecordsMoved = 0,
        RecordsRetained = 0,
        RecordsDiscarded = 0,
        Archives = [],
        Error = error,
    };
}

internal enum NotifySupervisionShrinkFaultPoint
{
    AfterManifestReplacement,
    AfterStallsReplacement,
    AfterCyclesReplacement,
    BeforeAuditAppend,
}

internal sealed record NotifySupervisionShrinkReplacement
{
    public required string TargetName { get; init; }
    public required string TargetPath { get; init; }
    public required string Content { get; init; }
}

internal sealed record NotifySupervisionEvidenceDefinitions
{
    [JsonPropertyName("schema")] public required string Schema { get; init; }
    [JsonPropertyName("definitions")] public required IReadOnlyDictionary<string, string> Definitions { get; init; }
}

internal sealed record NotifySupervisionFileShrinkAudit
{
    [JsonPropertyName("before_bytes")] public required long BeforeBytes { get; init; }
    [JsonPropertyName("after_bytes")] public required long AfterBytes { get; init; }
    [JsonPropertyName("before_record_count")] public required int BeforeRecordCount { get; init; }
    [JsonPropertyName("after_record_count")] public required int AfterRecordCount { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
}

internal sealed record NotifySupervisionShrinkAudit
{
    [JsonPropertyName("schema")] public required string Schema { get; init; }
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("transaction_id")] public string? TransactionId { get; init; }
    [JsonPropertyName("recovery_detail")] public string? RecoveryDetail { get; init; }
    [JsonPropertyName("occurred_at")] public required DateTimeOffset OccurredAt { get; init; }
    [JsonPropertyName("supervisor_state")] public required string SupervisorState { get; init; }
    [JsonPropertyName("supervisor_writer")] public NotifySupervisionWriterIdentity? SupervisorWriter { get; init; }
    [JsonPropertyName("before_bytes")] public required long BeforeBytes { get; init; }
    [JsonPropertyName("after_bytes")] public required long AfterBytes { get; init; }
    [JsonPropertyName("before_record_count")] public required int BeforeRecordCount { get; init; }
    [JsonPropertyName("after_record_count")] public required int AfterRecordCount { get; init; }
    [JsonPropertyName("before_average_bytes_per_record")] public double? BeforeAverageBytesPerRecord { get; init; }
    [JsonPropertyName("after_average_bytes_per_record")] public double? AfterAverageBytesPerRecord { get; init; }
    [JsonPropertyName("invariant_literal_bytes_removed")] public required long InvariantLiteralBytesRemoved { get; init; }
    [JsonPropertyName("invariant_reference_bytes_added")] public required long InvariantReferenceBytesAdded { get; init; }
    [JsonPropertyName("invariant_bytes_saved_in_records")] public required long InvariantBytesSavedInRecords { get; init; }
    [JsonPropertyName("other_bytes_saved")] public required long OtherBytesSaved { get; init; }
    [JsonPropertyName("records_archived")] public required int RecordsArchived { get; init; }
    [JsonPropertyName("records_discarded")] public required int RecordsDiscarded { get; init; }
    [JsonPropertyName("records_compacted")] public required int RecordsCompacted { get; init; }
    [JsonPropertyName("records_rotated")] public required int RecordsRotated { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyDictionary<string, NotifySupervisionFileShrinkAudit> Files { get; init; }
    [JsonPropertyName("evidence_reference")] public string? EvidenceReference { get; init; }
    [JsonPropertyName("audit_summary")] public required string AuditSummary { get; init; }
}

internal sealed record NotifySupervisionShrinkTransaction
{
    [JsonPropertyName("schema")] public required string Schema { get; init; }
    [JsonPropertyName("transaction_id")] public required string TransactionId { get; init; }
    [JsonPropertyName("occurred_at")] public required DateTimeOffset OccurredAt { get; init; }
    [JsonPropertyName("phase")] public required string Phase { get; init; }
    [JsonPropertyName("stage_directory")] public required string StageDirectory { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyList<NotifySupervisionShrinkTransactionFile> Files { get; init; }
    [JsonPropertyName("audit")] public required NotifySupervisionShrinkAudit Audit { get; init; }
}

internal sealed record NotifySupervisionShrinkTransactionFile
{
    [JsonPropertyName("target")] public required string TargetName { get; init; }
    [JsonPropertyName("stage")] public required string StageName { get; init; }
    [JsonPropertyName("before_sha256")] public string? BeforeSha256 { get; init; }
    [JsonPropertyName("after_sha256")] public required string AfterSha256 { get; init; }
}

internal sealed record NotifySupervisionFileShrinkMeasurement
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("exists")] public required bool Exists { get; init; }
    [JsonPropertyName("before_bytes")] public required long BeforeBytes { get; init; }
    [JsonPropertyName("after_bytes")] public required long AfterBytes { get; init; }
    [JsonPropertyName("before_record_count")] public required int BeforeRecords { get; init; }
    [JsonPropertyName("after_record_count")] public required int AfterRecords { get; init; }
    [JsonPropertyName("changed")] public required bool Changed { get; init; }

    public static NotifySupervisionFileShrinkMeasurement Empty(string name) => new()
    {
        Name = name,
        Exists = false,
        BeforeBytes = 0,
        AfterBytes = 0,
        BeforeRecords = 0,
        AfterRecords = 0,
        Changed = false,
    };
}

internal sealed record NotifySupervisionShrinkResult
{
    public required bool Applied { get; init; }
    public required bool WouldChange { get; init; }
    public required string Directory { get; init; }
    public required NotifySupervisionFileShrinkMeasurement StallFile { get; init; }
    public required NotifySupervisionFileShrinkMeasurement CycleFile { get; init; }
    public required long BeforeBytes { get; init; }
    public required long AfterBytes { get; init; }
    public required int BeforeRecordCount { get; init; }
    public required int AfterRecordCount { get; init; }
    public double? BeforeAverageBytesPerRecord { get; init; }
    public double? AfterAverageBytesPerRecord { get; init; }
    public required long InvariantLiteralBytesRemoved { get; init; }
    public required long InvariantReferenceBytesAdded { get; init; }
    public required long InvariantBytesSavedInRecords { get; init; }
    public required long OtherBytesSaved { get; init; }
    public string? EvidenceDefinitionsPath { get; init; }
    public string? AuditPath { get; init; }
    public NotifySupervisionShrinkAudit? Audit { get; init; }
    public string? Error { get; init; }

    public static NotifySupervisionShrinkResult Empty(string directory, bool write) => new()
    {
        Applied = false,
        WouldChange = false,
        Directory = directory,
        StallFile = NotifySupervisionFileShrinkMeasurement.Empty(NotifySupervisionStore.StallFileName),
        CycleFile = NotifySupervisionFileShrinkMeasurement.Empty(NotifySupervisionStore.CycleFileName),
        BeforeBytes = 0,
        AfterBytes = 0,
        BeforeRecordCount = 0,
        AfterRecordCount = 0,
        InvariantLiteralBytesRemoved = 0,
        InvariantReferenceBytesAdded = 0,
        InvariantBytesSavedInRecords = 0,
        OtherBytesSaved = 0,
        Error = null,
    };

    public static NotifySupervisionShrinkResult Failure(string directory, string error) => new()
    {
        Applied = false,
        WouldChange = false,
        Directory = directory,
        StallFile = NotifySupervisionFileShrinkMeasurement.Empty(NotifySupervisionStore.StallFileName),
        CycleFile = NotifySupervisionFileShrinkMeasurement.Empty(NotifySupervisionStore.CycleFileName),
        BeforeBytes = 0,
        AfterBytes = 0,
        BeforeRecordCount = 0,
        AfterRecordCount = 0,
        InvariantLiteralBytesRemoved = 0,
        InvariantReferenceBytesAdded = 0,
        InvariantBytesSavedInRecords = 0,
        OtherBytesSaved = 0,
        Error = error,
    };
}

internal sealed record FileCompactionPlan
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool Exists { get; init; }
    public required bool TransformStalls { get; init; }
    public required bool Changed { get; init; }
    public required string Content { get; init; }
    public required long BeforeBytes { get; init; }
    public required long AfterBytes { get; init; }
    public required int BeforeRecords { get; init; }
    public required int AfterRecords { get; init; }
    public required long InvariantLiteralBytesBefore { get; init; }
    public required long InvariantLiteralBytesAfter { get; init; }
    public required long InvariantReferenceBytesBefore { get; init; }
    public required long InvariantReferenceBytesAfter { get; init; }
    public required long InvariantBytesSavedInChangedLines { get; init; }
    public required int EvidenceReferencesAfter { get; init; }

    public static FileCompactionPlan Absent(string path, bool transformStalls) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        Path = path,
        Exists = false,
        TransformStalls = transformStalls,
        Changed = false,
        Content = string.Empty,
        BeforeBytes = 0,
        AfterBytes = 0,
        BeforeRecords = 0,
        AfterRecords = 0,
        InvariantLiteralBytesBefore = 0,
        InvariantLiteralBytesAfter = 0,
        InvariantReferenceBytesBefore = 0,
        InvariantReferenceBytesAfter = 0,
        InvariantBytesSavedInChangedLines = 0,
        EvidenceReferencesAfter = 0,
    };

    public NotifySupervisionFileShrinkMeasurement ToMeasurement() => new()
    {
        Name = Name,
        Exists = Exists,
        BeforeBytes = BeforeBytes,
        AfterBytes = AfterBytes,
        BeforeRecords = BeforeRecords,
        AfterRecords = AfterRecords,
        Changed = Changed,
    };
}

internal sealed record NotifySupervisionBound
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("bound_seconds")] public required int BoundSeconds { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
}

internal sealed record NotifySupervisionEmissionPolicy
{
    public const int DefaultRepeatBackoffSeconds = 1_800;
    public const int DefaultDebounceConsecutiveObservations = 3;
    public const int MaximumDebounceConsecutiveObservations = 100;

    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("full_cadence_seconds")] public required int FullCadenceSeconds { get; init; }
    [JsonPropertyName("repeat_backoff_seconds")] public required int RepeatBackoffSeconds { get; init; }
    [JsonPropertyName("debounce_consecutive_observations")] public required int DebounceConsecutiveObservations { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
}

internal sealed record NotifySupervisionInstalledSupervisor
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("artifact_path")] public required string ArtifactPath { get; init; }
    [JsonPropertyName("writer")] public required NotifySupervisionWriterIdentity Writer { get; init; }
    [JsonPropertyName("startup_bound_seconds")] public required int StartupBoundSeconds { get; init; }
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
    [JsonPropertyName("repeat_backoff_seconds")] public int? RepeatBackoffSeconds { get; init; }
    [JsonPropertyName("debounce_consecutive_observations")] public int? DebounceConsecutiveObservations { get; init; }
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
    [JsonPropertyName("last_observed_agent_status_consecutive_counts")] public IReadOnlyDictionary<string, int> LastObservedAgentStatusConsecutiveCounts { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    [JsonPropertyName("last_observed_agent_status_run_from")] public IReadOnlyDictionary<string, string> LastObservedAgentStatusRunFrom { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    [JsonPropertyName("transitions")] public IReadOnlyList<NotifySupervisionTransition> Transitions { get; init; } = [];
    [JsonPropertyName("wait_events")] public IReadOnlyList<NotifySupervisionWaitEvent> WaitEvents { get; init; } = [];
}

internal sealed record NotifySupervisionWriterIdentity
{
    private static readonly TimeSpan ProcessStartTimeResolution = TimeSpan.FromMilliseconds(100);

    [JsonPropertyName("pid")] public required int Pid { get; init; }
    [JsonPropertyName("process_start_time")] public required DateTimeOffset ProcessStartTime { get; init; }
    [JsonPropertyName("process_start_time_source")] public string? ProcessStartTimeSource { get; init; }
    [JsonPropertyName("host")] public required string Host { get; init; }

    public static NotifySupervisionWriterIdentity Current()
    {
        DateTimeOffset processStartTime;
        var processStartTimeSource = "process";
        try
        {
            using var process = Process.GetCurrentProcess();
            processStartTime = new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            // The identity remains additive even on a platform that refuses
            // to expose process metadata. A current timestamp makes the
            // record explicit; liveness falls back to same-host PID evidence
            // only when this source is named, never silently.
            processStartTime = DateTimeOffset.UtcNow;
            processStartTimeSource = "clock-fallback";
        }

        return new NotifySupervisionWriterIdentity
        {
            Pid = Environment.ProcessId,
            ProcessStartTime = processStartTime,
            ProcessStartTimeSource = processStartTimeSource,
            Host = Environment.MachineName,
        };
    }

    public bool IsSameWriter(NotifySupervisionWriterIdentity other) =>
        Pid == other.Pid
        && (ProcessStartTime == other.ProcessStartTime
            || IsStartTimeUnverified(this)
            || IsStartTimeUnverified(other))
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

            if (IsStartTimeUnverified(this) || IsStartTimeUnverified(current))
            {
                return true;
            }

            var actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime());
            return StartTimesMatch(actualStart, ProcessStartTime);
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

    private static bool IsStartTimeUnverified(NotifySupervisionWriterIdentity identity) =>
        string.Equals(identity.ProcessStartTimeSource, "clock-fallback", StringComparison.Ordinal);

    private static bool StartTimesMatch(DateTimeOffset actual, DateTimeOffset expected) =>
        (actual - expected).Duration() <= ProcessStartTimeResolution;
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
    [JsonPropertyName("cycle_id")] public string? CycleId { get; init; }
    [JsonPropertyName("attempt_id")] public string? AttemptId { get; init; }
    [JsonPropertyName("prompt_key")] public required string PromptKey { get; init; }
    [JsonPropertyName("seat")] public required string Seat { get; init; }
    [JsonPropertyName("pane")] public required string Pane { get; init; }
    [JsonPropertyName("agent_kind")] public required string AgentKind { get; init; }
    [JsonPropertyName("prompt_class")] public required string PromptClass { get; init; }
    [JsonPropertyName("rule")] public required string Rule { get; init; }
    [JsonPropertyName("actor")] public required string Actor { get; init; }
    [JsonPropertyName("decision_actor_role")] public string? DecisionActorRole { get; init; }
    [JsonPropertyName("mechanical_executor")] public string? MechanicalExecutor { get; init; }
    [JsonPropertyName("scope_or_rule_id")] public string? ScopeOrRuleId { get; init; }
    [JsonPropertyName("state_change_sequence")] public long? StateChangeSequence { get; init; }
    [JsonPropertyName("observed_text_hash")] public string? ObservedTextHash { get; init; }
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
    [JsonPropertyName("evidence")] public IReadOnlyList<string>? Evidence { get; init; }
    [JsonPropertyName("owed_transition")] public string? OwedTransition { get; init; }
    [JsonPropertyName("registration_definition")] public string? RegistrationDefinition { get; init; }
    [JsonPropertyName("registration_lookup")] public string? RegistrationLookup { get; init; }
    [JsonPropertyName("registration_result")] public string? RegistrationResult { get; init; }
    [JsonPropertyName("consulted_observations")] public IReadOnlyList<string>? ConsultedObservations { get; init; }
    [JsonPropertyName("evidence_ref")] public string? EvidenceReference { get; init; }
    [JsonPropertyName("evidence_ref_in_evidence")] public bool? EvidenceReferenceIncludesEvidence { get; init; }
    [JsonPropertyName("observed_prompt")] public NotifyObservedPrompt? Prompt { get; init; }
    [JsonPropertyName("first_seen")] public DateTimeOffset? FirstSeenAt { get; init; }
    [JsonPropertyName("last_seen")] public DateTimeOffset? LastSeenAt { get; init; }
    [JsonPropertyName("repeat_count")] public int RepeatCount { get; init; }
    [JsonPropertyName("last_emitted_at")] public DateTimeOffset? LastEmittedAt { get; init; }
    [JsonPropertyName("parked")] public bool Parked { get; init; }
    [JsonPropertyName("park_reason")] public string? ParkReason { get; init; }
    [JsonPropertyName("emission_cadence_seconds")] public int? EmissionCadenceSeconds { get; init; }
    [JsonPropertyName("state_fingerprint")] public string? StateFingerprint { get; init; }
}

internal sealed record NotifySupervisionReadResult
{
    public required bool Resolved { get; init; }
    public required string Directory { get; init; }
    public NotifySupervisionBound? Bound { get; init; }
    public NotifySupervisionEmissionPolicy? EmissionPolicy { get; init; }
    public NotifySupervisionInstalledSupervisor? InstalledSupervisor { get; init; }
    public NotifySupervisionCycle? LastCycle { get; init; }
    public IReadOnlyList<NotifySupervisionCycle> CycleHistory { get; init; } = [];
    /// <summary>
    /// The only cycle identity a command-side adjudication may trust. It is
    /// derived from the latest recorded supervision cycle, never from a CLI
    /// argument or a policy payload.
    /// </summary>
    public string? TrustedCycleId => LastCycle?.CycleId;
    public NotifySupervisionCycle? LastIntervalCycle { get; init; }
    public required IReadOnlyDictionary<string, NotifySupervisionStallRecord> ActiveStalls { get; init; }
    public required IReadOnlyList<NotifySupervisionStallRecord> StallHistory { get; init; }
    public required IReadOnlyList<NotifyPromptAudit> PromptAudits { get; init; }
    public IReadOnlyList<NotifySupervisionUnreadableRecord> UnreadableRecords { get; init; } = [];
    public string? Error { get; init; }
}

internal sealed record NotifySupervisionUnreadableRecord
{
    [JsonPropertyName("component")] public required string Component { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("line")] public required int Line { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
}

internal sealed record NotifySupervisionWriteResult(
    bool Applied,
    bool AlreadyConverged,
    string Path,
    string? Error);

internal sealed record NotifySupervisionIgnoreResult(
    bool Applied,
    bool WouldChange,
    string Path,
    IReadOnlyList<string> MissingLines,
    string? Error)
{
    public static NotifySupervisionIgnoreResult Failure(string path, string error) =>
        new(false, false, path, [], error);
}

internal sealed record NotifySupervisionEvent
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("cycle")] public NotifySupervisionCycle? Cycle { get; init; }
    [JsonPropertyName("stall")] public NotifySupervisionStallRecord? Stall { get; init; }
    [JsonPropertyName("prompt_audit")] public NotifyPromptAudit? PromptAudit { get; init; }
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("cleared_at")] public DateTimeOffset? ClearedAt { get; init; }
}
