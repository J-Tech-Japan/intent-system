namespace IntentSystem.Cli.Commands;

internal static class BugReportRenderer
{
    public static void WriteSummary(TextWriter writer, BugReportArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug report artifact generated for domain '{artifact.DomainSlug}'.");
        writer.WriteLine($"Bug ID: {artifact.BugId}");
        writer.WriteLine($"Title: {artifact.Title}");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Original instruction refs: {artifact.OriginalInstructionRefs.Count}");
        writer.WriteLine($"Linked execution units: {artifact.LinkedExecutionUnits.Count}");
        writer.WriteLine($"Linked issue refs: {artifact.LinkedIssueRefs.Count}");
        writer.WriteLine($"Linked PR refs: {artifact.LinkedPrRefs.Count}");
        writer.WriteLine($"Linked review refs: {artifact.LinkedReviewRefs.Count}");
    }
}
