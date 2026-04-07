namespace IntentSystem.Cli.Commands;

internal static class GenerateFromCurrentRenderer
{
    public static void WriteSummary(TextWriter writer, GenerateFromCurrentResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Generate-from-current processed for domain '{result.Domain}'.");
        writer.WriteLine($"Artifact path: {result.ArtifactPath}");
        writer.WriteLine($"Source root: {result.SourceRoot}");
        writer.WriteLine($"Selected issue scope: {result.SelectedIssueScope}");
        writer.WriteLine($"Selected PR scope: {result.SelectedPrScope}");
        writer.WriteLine("Selected altitudes:");
        WriteList(writer, result.SelectedAltitudes);
        writer.WriteLine("Selected paths:");
        WriteList(writer, result.SelectedPaths);
        writer.WriteLine("Source refs:");
        WriteList(writer, result.SourceRefs);
        writer.WriteLine("Sampling notes:");
        WriteList(writer, result.SamplingNotes);
        writer.WriteLine("Gaps:");
        WriteList(writer, result.Gaps);
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
