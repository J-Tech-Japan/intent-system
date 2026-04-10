namespace IntentSystem.Cli.Commands;

internal static class IntakeInterviewRenderer
{
    public static void WriteSummary(TextWriter writer, IntakeInterviewResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Intake interview bootstrap processed for domain '{result.Domain}'.");
        writer.WriteLine($"Concept artifact: {result.ConceptArtifactPath}");

        if (result.WasSkipped)
        {
            writer.WriteLine("Bootstrap status: skipped");
            writer.WriteLine("Existing interview artifacts:");
            WriteList(writer, result.ExistingArtifactPaths);
            return;
        }

        writer.WriteLine("Bootstrap status: generated");
        writer.WriteLine("Created question ids:");
        WriteList(writer, result.CreatedQuestionIds);
        writer.WriteLine("Generated interview artifacts:");
        WriteList(writer, result.GeneratedArtifactPaths);
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
