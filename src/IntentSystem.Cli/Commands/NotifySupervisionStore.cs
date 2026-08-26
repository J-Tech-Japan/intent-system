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
    public const string EmissionPolicyFileName = "emission-policy.json";
    public const string InstalledSupervisorFileName = "installed-supervisor.json";
    public const string CycleFileName = "cycles.jsonl";
    public const string StallFileName = "stalls.jsonl";
    public const string EvidenceDefinitionsFileName = "evidence-definitions.json";
    public const string ShrinkAuditFileName = "shrink-audit.jsonl";
    private const string LockFileName = ".supervision.lock";
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

    public static string ResolveStallPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), StallFileName);

    public static string ResolveEvidenceDefinitionsPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), EvidenceDefinitionsFileName);

    public static string ResolveShrinkAuditPath(string artifactRoot, string domain, string team) =>
        Path.Combine(ResolveDirectory(artifactRoot, domain, team), ShrinkAuditFileName);

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
                var definitions = ReadEvidenceDefinitions(Path.Combine(directory, EvidenceDefinitionsFileName));
                var bound = ReadBound(Path.Combine(directory, BoundFileName));
                var emissionPolicy = ReadEmissionPolicy(Path.Combine(directory, EmissionPolicyFileName));
                var installedSupervisor = ReadInstalledSupervisor(Path.Combine(directory, InstalledSupervisorFileName));
                var cyclePath = Path.Combine(directory, CycleFileName);
                var cycles = ReadCycles(cyclePath);
                var promptAudits = ReadPromptAudits(cyclePath);
                var stalls = ReadStalls(Path.Combine(directory, StallFileName), definitions);
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
                using var directoryLock = AcquireDirectoryLock(directory, createDirectory: true);
                var storedEntry = PrepareEventForStorage(entry, directory, ensureDefinitions: true);
                var line = JsonSerializer.Serialize(storedEntry, JsonOptions) + Environment.NewLine;
                File.AppendAllText(path, line, Utf8NoBom);
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

    private static IReadOnlyList<NotifySupervisionStallRecord> ReadStalls(
        string path,
        IReadOnlyDictionary<string, string> definitions)
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
                current[entry.Stall.Key] = ResolveStoredStall(entry.Stall, definitions);
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

        return document.Definitions.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal);
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
        NotifySupervisionEvidenceDefinitions document;
        if (File.Exists(path))
        {
            document = JsonSerializer.Deserialize<NotifySupervisionEvidenceDefinitions>(
                File.ReadAllText(path),
                JsonOptions)
                ?? throw new InvalidDataException("The supervision evidence definition file was empty.");
            if (!string.Equals(document.Schema, EvidenceSchema, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported supervision evidence definition schema '{document.Schema}'.");
            }

            if (document.Definitions.TryGetValue(HerdrRegistrationEvidenceKey, out var existing)
                && string.Equals(existing, HerdrRegistrationDefinition, StringComparison.Ordinal))
            {
                return;
            }

            var definitions = new Dictionary<string, string>(document.Definitions, StringComparer.Ordinal)
            {
                [HerdrRegistrationEvidenceKey] = HerdrRegistrationDefinition,
            };
            document = document with { Definitions = definitions };
        }
        else
        {
            document = new NotifySupervisionEvidenceDefinitions
            {
                Schema = EvidenceSchema,
                Definitions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [HerdrRegistrationEvidenceKey] = HerdrRegistrationDefinition,
                },
            };
        }

        File.WriteAllText(path, JsonSerializer.Serialize(document, ManifestJsonOptions) + Environment.NewLine, Utf8NoBom);
    }

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

        lock (Sync)
        {
            try
            {
                using var directoryLock = AcquireDirectoryLock(directory, createDirectory: write);
                var definitionsPath = Path.Combine(directory, EvidenceDefinitionsFileName);
                _ = ReadEvidenceDefinitions(definitionsPath);
                var stalls = PlanFile(Path.Combine(directory, StallFileName), transformStalls: true);
                var cycles = PlanFile(Path.Combine(directory, CycleFileName), transformStalls: false);
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

                var audit = new NotifySupervisionShrinkAudit
                {
                    Schema = "intent-cli.supervision-shrink/v1",
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
                    if (referencesNeeded)
                    {
                        EnsureEvidenceDefinitions(directory);
                    }

                    foreach (var plan in plans)
                    {
                        if (!plan.Exists)
                        {
                            continue;
                        }

                        // Rewriting cycles through the same atomic path is
                        // deliberate: cycles.jsonl has the same append-only
                        // shape and must not be left outside the shrink
                        // boundary merely because its current records do not
                        // carry the repeated stall definition.
                        if (plan.TransformStalls && !plan.Changed)
                        {
                            continue;
                        }

                        ReplaceAtomically(plan.Path, plan.Content);
                    }

                    var auditPath = Path.Combine(directory, ShrinkAuditFileName);
                    File.AppendAllText(
                        auditPath,
                        JsonSerializer.Serialize(audit, JsonOptions) + Environment.NewLine,
                        Utf8NoBom);
                }

                return new NotifySupervisionShrinkResult
                {
                    Applied = write,
                    WouldChange = stalls.Changed || cycles.Exists,
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
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or InvalidDataException)
            {
                return NotifySupervisionShrinkResult.Failure(directory, exception.Message);
            }
        }
    }

    private static FileCompactionPlan PlanFile(string path, bool transformStalls)
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

    private static double? Average(long bytes, int records) =>
        records == 0 ? null : (double)bytes / records;

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

    private static bool IsStartTimeUnverified(NotifySupervisionWriterIdentity identity) =>
        string.Equals(identity.ProcessStartTimeSource, "clock-fallback", StringComparison.Ordinal);
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
