namespace IntentSystem.Cli.Commands;

internal static class IntakeLaunchRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeLaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake launch processed for domain '{result.Domain}'.");
        writer.WriteLine("Launched execution units:");
        WriteList(writer, result.LaunchedExecutionUnits);
        writer.WriteLine("Created issue refs:");
        WriteList(writer, result.CreatedIssueRefs);
        writer.WriteLine("Worktree paths:");
        WriteList(writer, result.WorktreePaths);
        writer.WriteLine("Skipped units:");
        WriteList(writer, result.SkippedUnits);
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
