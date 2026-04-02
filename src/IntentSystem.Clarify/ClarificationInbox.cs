using IntentSystem.Clarify.Models;

namespace IntentSystem.Clarify;

public static class ClarificationInbox
{
    public static IReadOnlyList<ClarificationItem> GetPendingForUnit(
        IReadOnlyList<ClarificationItem> items,
        string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(items);

        var linkedItems = FindLinkedClarifications(items, executionUnit);
        return linkedItems.Where(IsPending).ToArray();
    }

    public static bool HasPendingClarifications(
        IReadOnlyList<ClarificationItem> items,
        string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(items);

        var validatedUnit = ValidateExecutionUnit(executionUnit);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (AffectsExecutionUnit(item, validatedUnit) && IsPending(item))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<ClarificationItem> FindLinkedClarifications(
        IReadOnlyList<ClarificationItem> items,
        string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(items);

        var validatedUnit = ValidateExecutionUnit(executionUnit);
        return items.Where(item => AffectsExecutionUnit(item, validatedUnit)).ToArray();
    }

    private static bool IsPending(ClarificationItem item)
    {
        return item.State == ClarificationState.Open;
    }

    private static bool AffectsExecutionUnit(ClarificationItem item, string executionUnit)
    {
        return item.AffectedExecutionUnits.Contains(executionUnit, StringComparer.Ordinal);
    }

    private static string ValidateExecutionUnit(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        return executionUnit;
    }
}
