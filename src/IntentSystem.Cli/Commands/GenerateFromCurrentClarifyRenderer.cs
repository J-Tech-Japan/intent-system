namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentClarifyRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentClarifyResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current clarify processed for domain '{result.Domain}'.");
        writer.WriteLine($"Source bundle artifact path: {result.SourceBundleArtifactPath}");
        writer.WriteLine("Reconstructed artifact paths:");
        WriteList(writer, result.ReconstructedArtifactPaths);
        writer.WriteLine($"Best-practice review artifact path: {result.ReviewArtifactPath}");
        writer.WriteLine($"Developer confirmation artifact path: {result.DeveloperConfirmationArtifactPath}");
        writer.WriteLine($"Clarification-return artifact path: {result.ClarificationReturnArtifactPath}");
        writer.WriteLine("Clarify items:");
        WriteList(writer, result.ClarifyItems);
        writer.WriteLine("Affected parent refs:");
        WriteList(writer, result.AffectedParentRefs);
        writer.WriteLine("Reasons:");
        WriteList(writer, result.Reasons);
        writer.WriteLine("Blockingness:");
        WriteList(writer, result.Blockingness);
        writer.WriteLine("Return-to-intent paths:");
        WriteList(writer, result.ReturnToIntentPaths);
        writer.WriteLine($"Downstream readiness: {result.DownstreamReadiness}");
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
