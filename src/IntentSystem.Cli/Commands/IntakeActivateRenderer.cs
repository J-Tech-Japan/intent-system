namespace IntentSystem.Cli.Commands;

internal static class IntakeActivateRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeActivateResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake activate processed for domain '{result.Domain}'.");
        writer.WriteLine($"Readiness status: {result.ReadinessStatus}");
        writer.WriteLine("Updated source file paths:");
        WriteList(writer, result.UpdatedSourceFilePaths);
        writer.WriteLine("Updated execution file paths:");
        WriteList(writer, result.UpdatedExecutionFilePaths);
        writer.WriteLine("Regenerated artifact paths:");
        WriteList(writer, result.RegeneratedArtifactPaths);
        writer.WriteLine("Started execution units:");
        WriteList(writer, result.StartedExecutionUnits);
        writer.WriteLine("Generated issue artifact paths:");
        WriteList(writer, result.GeneratedIssueArtifactPaths);
        writer.WriteLine("Created issue refs:");
        WriteList(writer, result.CreatedIssueRefs);
        writer.WriteLine("Worktree paths:");
        WriteList(writer, result.WorktreePaths);
        writer.WriteLine("Skipped stages:");
        WriteList(writer, result.SkippedStages);
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
