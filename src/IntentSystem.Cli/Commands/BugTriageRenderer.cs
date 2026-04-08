namespace IntentSystem.Cli.Commands;

internal static class BugTriageRenderer
{
    public static void WriteSummary(TextWriter writer, BugTriageArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug triage artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Classification: {artifact.Classification}");
        writer.WriteLine($"Downstream action: {artifact.DownstreamAction}");
        writer.WriteLine($"Clarification required: {artifact.ClarificationRequired.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Implementation repair candidates: {artifact.ImplementationRepairCandidates.Count}");
        writer.WriteLine($"Intent repair candidates: {artifact.IntentRepairCandidates.Count}");
        writer.WriteLine($"Resolved execution units: {artifact.ResolvedExecutionUnits.Count}");
        writer.WriteLine($"Unresolved execution units: {artifact.UnresolvedExecutionUnits.Count}");
        writer.WriteLine($"Linked review refs: {artifact.LinkedReviewRefs.Count}");
    }
}
