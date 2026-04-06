namespace IntentSystem.Cli.Commands;

internal static class IntakeAdvanceRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeAdvanceResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake advance processed for domain '{result.Domain}'.");
        writer.WriteLine($"Readiness status: {result.ReadinessStatus}");
        writer.WriteLine("Updated source file paths:");
        WriteList(writer, result.UpdatedSourceFilePaths);
        writer.WriteLine("Updated execution file paths:");
        WriteList(writer, result.UpdatedExecutionFilePaths);
        writer.WriteLine("Regenerated artifact paths:");
        WriteList(writer, result.RegeneratedArtifactPaths);
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
