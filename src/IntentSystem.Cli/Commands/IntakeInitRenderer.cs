namespace IntentSystem.Cli.Commands;

internal static class IntakeInitRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeInitResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake init processed for domain '{result.Domain}'.");
        writer.WriteLine($"Work repo path: {result.WorkRepoPath}");
        writer.WriteLine($"Interview bootstrap: {(result.InterviewWasSkipped ? "skipped" : "generated")}");

        if (result.CreatedQuestionIds.Count > 0)
        {
            writer.WriteLine("Created question ids:");
            WriteList(writer, result.CreatedQuestionIds);
        }

        writer.WriteLine("Generated paths:");
        WriteList(writer, result.GeneratedPaths);
        writer.WriteLine("Skipped paths:");
        WriteList(writer, result.SkippedPaths);
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
