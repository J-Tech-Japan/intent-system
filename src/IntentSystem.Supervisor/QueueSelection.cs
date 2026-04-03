using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor;

public static class QueueSelection
{
    public static QueueItem? SelectNext(QueueState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var completedUnits = state.Items
            .Where(item => item.State == QueueItemState.Completed)
            .Select(item => item.ExecutionUnit)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var item in state.Items)
        {
            if (item.State != QueueItemState.Queued)
            {
                continue;
            }

            if (item.BlockedBy.Count > 0)
            {
                continue;
            }

            if (item.Dependencies.Any(dependency => !completedUnits.Contains(dependency)))
            {
                continue;
            }

            return item;
        }

        return null;
    }
}
