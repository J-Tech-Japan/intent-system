namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentReviewRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current review processed for domain '{result.Domain}'.");
        writer.WriteLine($"Source bundle artifact path: {result.SourceBundleArtifactPath}");
        writer.WriteLine("Reconstructed artifact paths:");
        WriteList(writer, result.ReconstructedArtifactPaths);
        writer.WriteLine("Regenerated standard intake artifact paths:");
        WriteList(writer, result.StandardIntakeArtifactPaths);
        writer.WriteLine("Updated source file paths:");
        WriteList(writer, result.UpdatedSourceFilePaths);
        writer.WriteLine("Updated execution file paths:");
        WriteList(writer, result.UpdatedExecutionFilePaths);
        writer.WriteLine("Generated issue artifact paths:");
        WriteList(writer, result.GeneratedIssueArtifactPaths);
        writer.WriteLine("Created issue refs:");
        WriteList(writer, result.CreatedIssueRefs);
        writer.WriteLine("Worktree paths:");
        WriteList(writer, result.WorktreePaths);
        writer.WriteLine("Started execution units:");
        WriteList(writer, result.StartedExecutionUnits);
        writer.WriteLine("Implement request artifact paths:");
        WriteList(writer, result.ImplementRequestArtifactPaths);
        writer.WriteLine("Created PR refs:");
        WriteList(writer, result.CreatedPrRefs);
        writer.WriteLine("Review execution units:");
        WriteList(writer, result.ReviewExecutionUnits);
        writer.WriteLine("Review request artifact paths:");
        WriteList(writer, result.ReviewRequestArtifactPaths);
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
