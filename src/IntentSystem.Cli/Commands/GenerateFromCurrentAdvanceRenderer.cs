namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentAdvanceRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentAdvanceResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current advance processed for domain '{result.Domain}'.");
        writer.WriteLine($"Source bundle artifact path: {result.SourceBundleArtifactPath}");
        writer.WriteLine("Reconstructed artifact paths:");
        WriteList(writer, result.ReconstructedArtifactPaths);
        writer.WriteLine("Regenerated standard intake artifact paths:");
        WriteList(writer, result.StandardIntakeArtifactPaths);
        writer.WriteLine("Updated source file paths:");
        WriteList(writer, result.UpdatedSourceFilePaths);
        writer.WriteLine("Updated execution file paths:");
        WriteList(writer, result.UpdatedExecutionFilePaths);
        writer.WriteLine($"Readiness status: {result.ReadinessStatus}");
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
