namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedActivateRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentConfirmedActivateResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current confirmed-activate processed for domain '{result.Domain}'.");

        if (string.Equals(result.Route, "clarification-return", StringComparison.Ordinal))
        {
            writer.WriteLine($"Clarification-return artifact path: {result.ClarificationReturnArtifactPath}");
        }
        else if (string.Equals(result.Route, "reconciliation-required", StringComparison.Ordinal))
        {
            writer.WriteLine($"Confirmed reconstruction artifact path: {result.ConfirmedReconstructionArtifactPath}");
            writer.WriteLine("Confirmed activate did not run because reconciliation is not ready.");
        }

        writer.WriteLine("Updated source file paths:");
        WriteList(writer, result.UpdatedSourceFilePaths);
        writer.WriteLine("Updated execution file paths:");
        WriteList(writer, result.UpdatedExecutionFilePaths);
        writer.WriteLine("Regenerated artifact paths:");
        WriteList(writer, result.RegeneratedArtifactPaths);
        writer.WriteLine("Started execution units:");
        WriteList(writer, result.StartedExecutionUnits);
        writer.WriteLine("Created issue refs:");
        WriteList(writer, result.CreatedIssueRefs);
        writer.WriteLine("Worktree paths:");
        WriteList(writer, result.WorktreePaths);
        writer.WriteLine("Confirmed items:");
        WriteList(writer, result.ConfirmedItems);
        writer.WriteLine("Blocked items:");
        WriteList(writer, result.BlockedItems);
        writer.WriteLine($"Downstream readiness: {result.DownstreamReadiness}");
    }

    private static void WriteList(TextWriter writer, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (var value in values)
        {
            writer.WriteLine($"- {value}");
        }
    }
}
