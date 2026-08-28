using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Supervisor;

/// <summary>
/// Shared duplicate guard for execution-unit-keyed queue-state indexes.
/// Duplicate rows are a durable-state finding, not an ordering decision:
/// callers must fail closed and route the operator to the canonical repair
/// surface instead of silently choosing or discarding a row.
/// </summary>
public static class QueueStateExecutionUnitIndex
{
    /// <summary>
    /// Builds an execution-unit index after checking all input rows. The
    /// returned dictionary is therefore safe by construction.
    /// </summary>
    public static IReadOnlyDictionary<string, QueueItem> BuildUnique(
        IReadOnlyList<QueueItem> items,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        EnsureUnique(items, operation);

        var index = new Dictionary<string, QueueItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            // EnsureUnique above makes assignment safe by construction while
            // avoiding Dictionary.Add's raw duplicate-key exception.
            index[item.ExecutionUnit] = item;
        }

        return index;
    }

    /// <summary>
    /// Ensures model-backed queue rows are unique before a stale delta or
    /// execution-unit dictionary is built.
    /// </summary>
    internal static void EnsureUnique(
        IReadOnlyList<QueueItem> items,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        ThrowIfDuplicate(
            items.Select((item, index) => new DuplicateEntry(
                item.ExecutionUnit,
                index,
                System.Text.Json.JsonSerializer.Serialize(item, SupervisorJsonOptions.Indented))),
            operation);
    }

    /// <summary>
    /// Ensures raw queue-state items are unique before the raw stale-delta
    /// map is built. Partial/legacy documents without an items array remain
    /// accepted by the raw writer.
    /// </summary>
    internal static void EnsureRawUnique(string rawText, string operation)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        ThrowIfDuplicate(ReadRawItems(rawText), operation);
    }

    private static void ThrowIfDuplicate(
        IEnumerable<DuplicateEntry> entries,
        string operation)
    {
        var duplicateGroups = entries
            .GroupBy(entry => entry.ExecutionUnit, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (duplicateGroups.Length == 0)
        {
            return;
        }

        var units = string.Join(
            ", ",
            duplicateGroups.Select(group => $"'{group.Key}'"));
        var competingEntries = string.Join(
            Environment.NewLine,
            duplicateGroups.SelectMany(group => new[]
            {
                $"execution unit '{group.Key}':",
                string.Join(
                    Environment.NewLine,
                    group.OrderBy(entry => entry.Index)
                        .Select(entry => $"entry[{entry.Index}]: {entry.FullEntryJson}")),
            }));

        throw new QueueStateDuplicateExecutionUnitException(
            $"duplicate-queue-item: refusing {operation} because queue-state contains duplicate "
            + $"execution_unit entries for {units}; the operation failed closed, no index was built, "
            + "and no mutation was performed. Canonical recovery: run "
            + "`intent-cli automation state-doctor --write` when a strict-dominance repair is available; "
            + "incomparable or equivalent entries require operator reconciliation. Competing full entries:"
            + Environment.NewLine
            + competingEntries);
    }

    private static IReadOnlyList<DuplicateEntry> ReadRawItems(string rawText)
    {
        using var document = System.Text.Json.JsonDocument.Parse(rawText);
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return Array.Empty<DuplicateEntry>();
        }

        var entries = new List<DuplicateEntry>();
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == System.Text.Json.JsonValueKind.Object
                && item.TryGetProperty("execution_unit", out var unit)
                && unit.ValueKind == System.Text.Json.JsonValueKind.String
                && unit.GetString() is { Length: > 0 } value)
            {
                entries.Add(new DuplicateEntry(value, index, item.GetRawText()));
            }

            index++;
        }

        return entries;
    }

    private sealed record DuplicateEntry(
        string ExecutionUnit,
        int Index,
        string FullEntryJson);
}
