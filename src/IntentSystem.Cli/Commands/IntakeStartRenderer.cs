namespace IntentSystem.Cli.Commands;

internal static class IntakeStartRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeStartResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake start processed for domain '{result.Domain}'.");
        writer.WriteLine("Started execution units:");
        WriteList(writer, result.StartedExecutionUnits);
        writer.WriteLine("Generated artifact paths:");
        WriteList(writer, result.GeneratedArtifactPaths);
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
