namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentBridgeRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentBridgeResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current bridge processed for domain '{result.Domain}'.");
        writer.WriteLine($"Generated concept artifact: {result.ConceptArtifactPath}");
        writer.WriteLine("Generated interview artifacts:");
        WriteList(writer, result.InterviewArtifactPaths);
        writer.WriteLine("Recommended updates:");
        WriteList(writer, result.RecommendedUpdates);
        writer.WriteLine("Return-to-intent paths:");
        WriteList(writer, result.ReturnToIntentPaths);
        writer.WriteLine("Gaps:");
        WriteList(writer, result.Gaps);
        writer.WriteLine("Skipped bridge steps:");
        WriteList(writer, result.SkippedBridgeSteps);
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
