namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentConfirmedBridgeRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentConfirmedBridgeResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current confirmed-bridge processed for domain '{result.Domain}'.");

        if (string.Equals(result.Route, "clarification-return", StringComparison.Ordinal))
        {
            writer.WriteLine($"Clarification-return artifact path: {result.ClarificationReturnArtifactPath}");
        }
        else if (string.Equals(result.Route, "reconciliation-required", StringComparison.Ordinal))
        {
            writer.WriteLine($"Confirmed reconstruction artifact path: {result.ConfirmedReconstructionArtifactPath}");
            writer.WriteLine("Confirmed bridge did not run because reconciliation is not ready.");
        }
        else
        {
            writer.WriteLine($"Generated concept artifact: {result.ConceptArtifactPath}");
            writer.WriteLine("Generated interview artifacts:");
            WriteList(writer, result.InterviewArtifactPaths);
        }

        writer.WriteLine("Regenerated artifact paths:");
        WriteList(writer, result.RegeneratedArtifactPaths);
        writer.WriteLine("Confirmed items:");
        WriteList(writer, result.ConfirmedItems);
        writer.WriteLine("Blocked items:");
        WriteList(writer, result.BlockedItems);
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
