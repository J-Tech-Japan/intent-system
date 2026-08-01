using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G568: <c>intent-cli automation queue-dependency-reconcile
/// [--execution-unit &lt;u&gt;] [--write] [--format json|markdown]</c> —
/// diagnoses and repairs queue items whose <c>dependencies</c> drifted from
/// what their packet declares.
///
/// G568 fixes the seed path going forward, but items already in the queue were
/// seeded by the lossy one: a block-style <c>dependencies:</c> was dropped
/// entirely, so a dependent unit sits in the queue looking like it has no
/// prerequisites. Dependency-aware selection reads exactly that field, so those
/// units can be picked while their root is still open — and hand-editing
/// <c>queue-state.json</c> to fix it is forbidden (G548). Hence a canonical
/// surface.
///
/// Contract:
/// <list type="bullet">
///   <item>read-only by default: reports the per-unit delta between what the
///         packet declares and what the queue holds, and mutates nothing;</item>
///   <item><c>--write</c> re-derives <c>dependencies</c> FROM THE PACKET — it
///         never merges, never guesses, and touches no other field of the
///         item;</item>
///   <item>idempotent: a second run reports <c>in-sync</c> and applies
///         nothing;</item>
///   <item>fail-closed: an unknown unit, a missing packet, or a packet that
///         does not parse is REPORTED and SKIPPED, never repaired from a
///         partial read — and with <c>--execution-unit</c> naming such a unit,
///         the command exits non-zero without writing at all;</item>
///   <item>the write goes through <see cref="QueueStatePersistence.Persist"/>,
///         so G548's no-item-loss and stale-base invariants hold.</item>
/// </list>
///
/// It is never run automatically. The operator or orchestrator invokes it.
/// </summary>
internal static class AutomationQueueDependencyReconcileCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string StatusInSync = "in-sync";
    public const string StatusDrifted = "dependencies-drifted";
    public const string StatusRepaired = "dependencies-repaired";
    public const string StatusPacketMissing = "packet-missing";
    public const string StatusPacketUnparseable = "packet-unparseable";

    public const string ReconcileEventName = "queue_dependencies_reconciled";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation queue-dependency-reconcile [--execution-unit <unit>] [--dry-run|--write] [--format json|markdown]";

    /// <summary>Test seam: deterministic runs-log timestamps.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var executionUnit, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            writer.WriteLine($"queue-state.json not found at `{queueStatePath}`; nothing to reconcile.");
            return 1;
        }

        QueueState baseState;
        try
        {
            baseState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            writer.WriteLine($"queue-state.json at `{queueStatePath}` is unparseable; refusing to reconcile. {exception.Message}");
            return 1;
        }

        var targets = executionUnit is null
            ? baseState.Items
            : baseState.Items.Where(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)).ToArray();

        if (executionUnit is not null && targets.Count == 0)
        {
            writer.WriteLine(
                $"execution unit '{executionUnit}' is not in queue-state; refusing to reconcile a unit this queue does not hold.");
            return 1;
        }

        var findings = new List<QueueDependencyFinding>();
        var repairs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var item in targets.OrderBy(item => item.ExecutionUnit, StringComparer.Ordinal))
        {
            findings.Add(Inspect(context, item, repairs));
        }

        // Fail closed on an explicitly named unit whose packet cannot be read:
        // the caller asked about THAT unit, and "I could not tell" is an error
        // for a single-unit question even though it is a skip in a sweep.
        var namedFailure = executionUnit is not null
            && findings.Any(finding => finding.Status is StatusPacketMissing or StatusPacketUnparseable);

        var applied = false;
        if (write && repairs.Count > 0 && !namedFailure)
        {
            applied = Apply(context, queueStatePath, baseState, repairs);
        }

        var result = new QueueDependencyReconcileResult
        {
            Mode = write ? "write" : "dry-run",
            Applied = applied,
            ExecutionUnit = executionUnit,
            Inspected = findings.Count,
            Drifted = findings.Count(finding => finding.Status is StatusDrifted),
            Skipped = findings.Count(finding => finding.Status is StatusPacketMissing or StatusPacketUnparseable),
            Items = applied
                ? findings.Select(finding => finding.Status == StatusDrifted ? finding with { Status = StatusRepaired } : finding).ToArray()
                : findings,
            Summary = BuildSummary(write, applied, findings, executionUnit),
        };

        Emit(writer, format, result);
        return namedFailure ? 1 : 0;
    }

    private static QueueDependencyFinding Inspect(
        CliContext context,
        QueueItem item,
        Dictionary<string, IReadOnlyList<string>> repairs)
    {
        var packetPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", item.ExecutionUnit, "packet.yaml");
        if (!File.Exists(packetPath))
        {
            return new QueueDependencyFinding
            {
                ExecutionUnit = item.ExecutionUnit,
                Status = StatusPacketMissing,
                QueueDependencies = item.Dependencies,
                PacketDependencies = Array.Empty<string>(),
                Detail = $"no packet at `.intent-cli/issues/{item.ExecutionUnit}/packet.yaml`; the queue's dependencies "
                    + "cannot be checked against a declaration that is not there, so this item is reported and skipped.",
            };
        }

        string content;
        try
        {
            content = File.ReadAllText(packetPath);
        }
        catch (IOException exception)
        {
            return new QueueDependencyFinding
            {
                ExecutionUnit = item.ExecutionUnit,
                Status = StatusPacketUnparseable,
                QueueDependencies = item.Dependencies,
                PacketDependencies = Array.Empty<string>(),
                Detail = $"packet `.intent-cli/issues/{item.ExecutionUnit}/packet.yaml` could not be read: {exception.Message}",
            };
        }

        if (!PacketYamlDocument.TryParse(content, out var document, out var parseError))
        {
            return new QueueDependencyFinding
            {
                ExecutionUnit = item.ExecutionUnit,
                Status = StatusPacketUnparseable,
                QueueDependencies = item.Dependencies,
                PacketDependencies = Array.Empty<string>(),
                Detail = $"packet `.intent-cli/issues/{item.ExecutionUnit}/packet.yaml` does not parse: {parseError} "
                    + "Repairing from a partial read would replace one wrong answer with another, so this item is skipped.",
            };
        }

        var declared = document!.LookupSequence(
            "implementation_issue_packet.dependencies",
            "implementation_issue.dependencies",
            "dependencies");

        if (declared.SequenceEqual(item.Dependencies, StringComparer.Ordinal))
        {
            return new QueueDependencyFinding
            {
                ExecutionUnit = item.ExecutionUnit,
                Status = StatusInSync,
                QueueDependencies = item.Dependencies,
                PacketDependencies = declared,
                Detail = "queue dependencies match the packet declaration.",
            };
        }

        repairs[item.ExecutionUnit] = declared;
        return new QueueDependencyFinding
        {
            ExecutionUnit = item.ExecutionUnit,
            Status = StatusDrifted,
            QueueDependencies = item.Dependencies,
            PacketDependencies = declared,
            Detail = $"packet declares [{string.Join(", ", declared)}]; queue holds [{string.Join(", ", item.Dependencies)}]. "
                + "`--write` re-derives the queue's dependencies FROM the packet — it never merges the two.",
        };
    }

    /// <summary>
    /// Rewrites ONLY <c>dependencies</c>, on only the drifted items, through
    /// the guarded persistence path so G548's no-item-loss and stale-base
    /// re-application invariants hold.
    /// </summary>
    private static bool Apply(
        CliContext context,
        string queueStatePath,
        QueueState baseState,
        IReadOnlyDictionary<string, IReadOnlyList<string>> repairs)
    {
        var updated = baseState with
        {
            UpdatedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Items = baseState.Items
                .Select(item => repairs.TryGetValue(item.ExecutionUnit, out var dependencies)
                    ? item with { Dependencies = dependencies }
                    : item)
                .ToArray(),
        };

        QueueStatePersistence.Persist(queueStatePath, baseState, updated);

        var runLogPath = context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runLogPath)!);
        foreach (var (unit, dependencies) in repairs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            File.AppendAllText(
                runLogPath,
                RunLogSerializer.SerializeLine(new RunEvent
                {
                    Ts = updated.UpdatedAt,
                    ExecutionUnit = unit,
                    Event = ReconcileEventName,
                    By = "intent-cli automation queue-dependency-reconcile",
                    Reason = $"dependencies re-derived from packet: [{string.Join(", ", dependencies)}]",
                }) + Environment.NewLine);
        }

        return true;
    }

    private static string BuildSummary(
        bool write,
        bool applied,
        IReadOnlyList<QueueDependencyFinding> findings,
        string? executionUnit)
    {
        var scope = executionUnit is null ? "every queue item" : $"`{executionUnit}`";
        var drifted = findings.Count(finding => finding.Status == StatusDrifted);
        var skipped = findings.Count(finding => finding.Status is StatusPacketMissing or StatusPacketUnparseable);

        if (drifted == 0 && skipped == 0)
        {
            return $"{scope}: dependencies match the packet declarations; nothing to reconcile.";
        }

        var applyNote = applied
            ? "Re-derived them from the packets and persisted the queue through the guarded write."
            : write
                ? "No write was applied — resolve the skipped item(s) first."
                : $"Re-run with `--write` to re-derive them from the packets{(executionUnit is null ? string.Empty : $" for `{executionUnit}`")}.";

        return $"{scope}: {drifted} item(s) with dependencies drifted from their packet, {skipped} skipped. {applyNote}";
    }

    private static void Emit(TextWriter writer, string format, QueueDependencyReconcileResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine("# automation queue-dependency-reconcile");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- applied: {(result.Applied ? "true" : "false")}");
        writer.WriteLine($"- inspected: {result.Inspected}");
        writer.WriteLine($"- drifted: {result.Drifted}");
        writer.WriteLine($"- skipped: {result.Skipped}");
        writer.WriteLine();
        foreach (var finding in result.Items.Where(finding => finding.Status != StatusInSync))
        {
            writer.WriteLine($"## `{finding.ExecutionUnit}` — {finding.Status}");
            writer.WriteLine($"- packet: [{string.Join(", ", finding.PacketDependencies)}]");
            writer.WriteLine($"- queue: [{string.Join(", ", finding.QueueDependencies)}]");
            writer.WriteLine($"- detail: {finding.Detail}");
            writer.WriteLine();
        }

        writer.WriteLine(result.Summary);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--execution-unit":
                case "--unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"{args[index]} requires a value.";
                        return false;
                    }
                    executionUnit = args[++index].Trim();
                    break;

                case "--write":
                    write = true;
                    break;

                case "--dry-run":
                    write = false;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    format = args[++index].Trim();
                    if (!string.Equals(format, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{format}').";
                        return false;
                    }
                    break;

                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        return true;
    }
}

internal sealed record QueueDependencyFinding
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("packet_dependencies")]
    public required IReadOnlyList<string> PacketDependencies { get; init; }

    [JsonPropertyName("queue_dependencies")]
    public required IReadOnlyList<string> QueueDependencies { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}

internal sealed record QueueDependencyReconcileResult
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string? ExecutionUnit { get; init; }

    [JsonPropertyName("inspected")]
    public required int Inspected { get; init; }

    [JsonPropertyName("drifted")]
    public required int Drifted { get; init; }

    [JsonPropertyName("skipped")]
    public required int Skipped { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<QueueDependencyFinding> Items { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
