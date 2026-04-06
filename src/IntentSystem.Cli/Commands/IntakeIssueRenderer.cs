namespace IntentSystem.Cli.Commands;

internal static class IntakeIssueRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeIssueResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake issue artifacts generated for domain '{result.Domain}'.");
        writer.WriteLine("Generated execution units:");
        WriteList(writer, result.GeneratedExecutionUnits);
        writer.WriteLine("Artifact paths:");
        WriteList(writer, result.ArtifactPaths);
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
