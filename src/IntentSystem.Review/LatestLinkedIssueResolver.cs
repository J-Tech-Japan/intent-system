using IntentSystem.Supervisor.Models;

namespace IntentSystem.Review;

public static class LatestLinkedIssueResolver
{
    public static string Resolve(IReadOnlyList<RunEvent> runEvents, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(runEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        string? latestLinkedIssue = null;

        foreach (var runEvent in runEvents)
        {
            if (!string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(runEvent.LinkedIssue))
            {
                latestLinkedIssue = runEvent.LinkedIssue;
            }
        }

        return latestLinkedIssue
            ?? throw new InvalidOperationException(
                $"No linked issue found for execution unit '{executionUnit}' in run log.");
    }
}
