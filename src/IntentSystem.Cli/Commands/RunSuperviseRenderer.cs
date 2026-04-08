namespace IntentSystem.Cli.Commands;

internal static class RunSuperviseRenderer
{
    public static void WriteSummary(TextWriter writer, RunSuperviseResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Run supervision updated for {result.ExecutionUnit}.");
        writer.WriteLine($"Session artifact: {result.SessionArtifactPath}");
        writer.WriteLine($"Worker entry: {FormatWorkerEntry(result.WorkerEntry)}");
        writer.WriteLine($"Session status: {FormatSessionStatus(result.SessionStatus)}");
        writer.WriteLine($"Retry count: {result.RetryCount}/{result.RetryBudget}");
        writer.WriteLine($"Handoff artifact: {result.HandoffArtifactRef}");

        if (!string.IsNullOrWhiteSpace(result.NextRetryAt))
        {
            writer.WriteLine($"Next retry at: {result.NextRetryAt}");
        }

        if (result.RetryScheduled)
        {
            writer.WriteLine("Retry scheduled: yes");
        }

        if (result.AutoResumed)
        {
            writer.WriteLine("Auto-resumed: yes");
        }

        if (result.Blocked)
        {
            writer.WriteLine("Blocked transition applied: yes");
        }
    }

    private static string FormatWorkerEntry(RunSupervisionWorkerEntry workerEntry)
    {
        return workerEntry switch
        {
            RunSupervisionWorkerEntry.Implement => "run implement",
            RunSupervisionWorkerEntry.Fix => "run fix",
            _ => throw new InvalidOperationException($"Unsupported worker entry '{workerEntry}'.")
        };
    }

    private static string FormatSessionStatus(RunSupervisionSessionStatus status)
    {
        return status switch
        {
            RunSupervisionSessionStatus.Monitoring => "monitoring",
            RunSupervisionSessionStatus.RetryScheduled => "retry-scheduled",
            RunSupervisionSessionStatus.Blocked => "blocked",
            _ => throw new InvalidOperationException($"Unsupported session status '{status}'.")
        };
    }
}
