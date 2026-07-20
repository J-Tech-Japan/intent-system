using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G543: <c>intent-cli queue priority-drift [--format json|markdown]</c> —
/// a read-only report of how many <c>queue-state.json</c> items hold each
/// distinct <c>priority</c> value, flagging any value outside the documented
/// <see cref="QueuePriorityClassification.DocumentedValues"/> enum (e.g. the
/// field-observed <c>"medium"</c>). Never mutates <c>queue-state.json</c> or
/// <c>runs.jsonl</c> — this exists purely so an operator can see the 59-item
/// (or any) drift shape without hand-writing a script.
/// </summary>
internal static class QueuePriorityDriftCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine = "Usage: intent-cli queue priority-drift [--format json|markdown]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var queueStatePath = context.GetQueueStatePath();

        // G543: group by the SAME normalization QueuePriorityClassification
        // uses for ordering/documented-membership, so this report and
        // candidate ordering can never disagree about which raw values are
        // "the same" priority. A missing queue-state.json is simply zero
        // items — the report shape (documented values always listed) stays
        // identical either way.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalItems = 0;
        if (File.Exists(queueStatePath))
        {
            var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            totalItems = queueState.Items.Count;
            foreach (var item in queueState.Items)
            {
                var normalized = QueuePriorityClassification.Normalize(item.Priority);
                counts[normalized] = counts.GetValueOrDefault(normalized) + 1;
            }
        }

        // Documented values are always listed, even at zero, so the report
        // shape is stable regardless of which values happen to be present.
        // Out-of-enum values are appended after, ordered by count
        // descending (the biggest drift first) then by value ascending for
        // a deterministic tiebreak.
        var groups = new List<QueuePriorityDriftGroup>();
        foreach (var documented in QueuePriorityClassification.DocumentedValues)
        {
            groups.Add(new QueuePriorityDriftGroup
            {
                Priority = documented,
                Count = counts.GetValueOrDefault(documented),
                Documented = true,
            });
        }

        var outOfEnum = counts
            .Where(pair => !QueuePriorityClassification.IsDocumented(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new QueuePriorityDriftGroup
            {
                Priority = string.IsNullOrEmpty(pair.Key) ? "(missing)" : pair.Key,
                Count = pair.Value,
                Documented = false,
            });
        groups.AddRange(outOfEnum);

        EmitResult(writer, format, BuildResult(queueStatePath, totalItems, groups));
        return 0;
    }

    private static QueuePriorityDriftResult BuildResult(string queueStatePath, int totalItems, IReadOnlyList<QueuePriorityDriftGroup> groups)
    {
        var outOfEnumGroups = groups.Where(group => !group.Documented && group.Count > 0).ToArray();
        return new QueuePriorityDriftResult
        {
            QueueStatePath = queueStatePath,
            TotalItems = totalItems,
            ByPriority = groups,
            HasDrift = outOfEnumGroups.Length > 0,
            OutOfEnumValues = outOfEnumGroups.Select(group => group.Priority).ToArray(),
        };
    }

    private static void EmitResult(TextWriter writer, string format, QueuePriorityDriftResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }

        writer.WriteLine("# Queue priority drift");
        writer.WriteLine();
        writer.WriteLine($"- queue state path: {result.QueueStatePath}");
        writer.WriteLine($"- total items: {result.TotalItems}");
        writer.WriteLine($"- drift: {(result.HasDrift ? "yes" : "no")}");
        writer.WriteLine();
        writer.WriteLine("| priority | count | documented |");
        writer.WriteLine("| --- | --- | --- |");
        foreach (var group in result.ByPriority)
        {
            writer.WriteLine($"| {group.Priority} | {group.Count} | {(group.Documented ? "yes" : "no")} |");
        }

        if (result.HasDrift)
        {
            writer.WriteLine();
            writer.WriteLine($"Out-of-enum values present: {string.Join(", ", result.OutOfEnumValues)}. These still order deterministically (same as an explicit \"normal\") — see `queue reprioritize` to migrate an item to a documented value.");
        }
    }

    private static bool TryParseArguments(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;

                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("queue priority-drift");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: reports item counts per queue-item priority value, flagging any value");
        writer.WriteLine("outside the documented high|normal|low enum (e.g. a legacy \"medium\" value). Never");
        writer.WriteLine("mutates queue-state.json or runs.jsonl.");
    }
}

internal sealed record QueuePriorityDriftResult
{
    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("total_items")]
    public required int TotalItems { get; init; }

    [JsonPropertyName("by_priority")]
    public required IReadOnlyList<QueuePriorityDriftGroup> ByPriority { get; init; }

    [JsonPropertyName("has_drift")]
    public required bool HasDrift { get; init; }

    [JsonPropertyName("out_of_enum_values")]
    public required IReadOnlyList<string> OutOfEnumValues { get; init; }
}

internal sealed record QueuePriorityDriftGroup
{
    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    [JsonPropertyName("count")]
    public required int Count { get; init; }

    [JsonPropertyName("documented")]
    public required bool Documented { get; init; }
}
