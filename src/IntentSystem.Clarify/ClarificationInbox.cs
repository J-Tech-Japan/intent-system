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

        var sanitizedExecutionUnit = ValidateExecutionUnit(executionUnit);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (MatchesExecutionUnit(item, sanitizedExecutionUnit) && IsPending(item))
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

        var sanitizedExecutionUnit = ValidateExecutionUnit(executionUnit);
        return items.Where(item => MatchesExecutionUnit(item, sanitizedExecutionUnit)).ToArray();
    }

    private static bool IsPending(ClarificationItem item)
    {
        return item.State == ClarificationState.Open;
    }

    private static bool MatchesExecutionUnit(ClarificationItem item, string executionUnit)
    {
        return string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal);
    }

    private static string ValidateExecutionUnit(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        return executionUnit;
    }
}
