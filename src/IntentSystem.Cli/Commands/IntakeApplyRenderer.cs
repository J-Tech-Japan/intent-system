namespace IntentSystem.Cli.Commands;

internal static class IntakeApplyRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake apply completed for domain '{result.Domain}'.");
        writer.WriteLine($"Applied edit count: {result.AppliedEditCount}");
        writer.WriteLine("Changed file paths:");

        if (result.ChangedFilePaths.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var path in result.ChangedFilePaths)
            {
                writer.WriteLine($"- {path}");
            }
        }

        writer.WriteLine("Source concept refs:");
        if (result.SourceConceptRefs.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (var path in result.SourceConceptRefs)
        {
            writer.WriteLine($"- {path}");
        }
    }
}
