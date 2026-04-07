namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentBestPracticeRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentBestPracticeResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current best-practice processed for domain '{result.Domain}'.");
        writer.WriteLine($"Source bundle artifact path: {result.SourceBundleArtifactPath}");
        writer.WriteLine("Reconstructed artifact paths:");
        WriteList(writer, result.ReconstructedArtifactPaths);
        writer.WriteLine($"Best-practice review artifact path: {result.ReviewArtifactPath}");
        writer.WriteLine("Reviewed dimensions:");
        WriteList(writer, result.ReviewedDimensions);
        writer.WriteLine("Model refs:");
        WriteList(writer, result.ModelRefs);
        writer.WriteLine("Knowledge refs:");
        WriteList(writer, result.KnowledgeRefs);
        writer.WriteLine("Recommended intent additions:");
        WriteList(writer, result.RecommendedIntentAdditions);
        writer.WriteLine("Recommended clarifications:");
        WriteList(writer, result.RecommendedClarifications);
        writer.WriteLine("Developer confirmation items:");
        WriteList(writer, result.DeveloperConfirmationItems);
        writer.WriteLine("Return-to-intent paths:");
        WriteList(writer, result.ReturnToIntentPaths);
        writer.WriteLine("Confidence deltas:");
        WriteList(writer, result.ConfidenceDeltas);
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
