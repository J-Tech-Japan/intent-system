using IntentSystem.Supervisor.Models;

namespace IntentSystem.Review;

public static class LatestLinkedPrResolver
{
    public static string Resolve(IReadOnlyList<RunEvent> runEvents, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(runEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        string? latestLinkedPr = null;

        foreach (var runEvent in runEvents)
        {
            if (!string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(runEvent.LinkedPr))
            {
                latestLinkedPr = runEvent.LinkedPr;
            }
        }

        return latestLinkedPr
            ?? throw new InvalidOperationException(
                $"No linked PR found for execution unit '{executionUnit}' in run log.");
    }
}
