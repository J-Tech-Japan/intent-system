namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentConfirmResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current confirm processed for domain '{result.Domain}'.");
        writer.WriteLine($"Source bundle artifact path: {result.SourceBundleArtifactPath}");
        writer.WriteLine("Reconstructed artifact paths:");
        WriteList(writer, result.ReconstructedArtifactPaths);
        writer.WriteLine($"Best-practice review artifact path: {result.ReviewArtifactPath}");
        writer.WriteLine($"Prepared decision file path: {result.DecisionFilePath}");
        writer.WriteLine($"Developer confirmation artifact path: {result.ConfirmationArtifactPath}");
        writer.WriteLine("Confirmed items:");
        WriteList(writer, result.ConfirmedItems);
        writer.WriteLine("Rejected items:");
        WriteList(writer, result.RejectedItems);
        writer.WriteLine("Clarify items:");
        WriteList(writer, result.ClarifyItems);
        writer.WriteLine("Deferred items:");
        WriteList(writer, result.DeferredItems);
        writer.WriteLine("Blocked items:");
        WriteList(writer, result.BlockedItems);
        writer.WriteLine($"Downstream readiness: {result.DownstreamReadiness}");
        writer.WriteLine("Return-to-intent paths:");
        WriteList(writer, result.ReturnToIntentPaths);
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
