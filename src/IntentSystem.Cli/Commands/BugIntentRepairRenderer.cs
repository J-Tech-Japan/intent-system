namespace IntentSystem.Cli.Commands;

internal static class BugIntentRepairRenderer
{
    public static void WriteSummary(TextWriter writer, BugIntentRepairArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug intent-repair artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Ready to issue cut: {artifact.ReadyToIssueCut.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Intent task candidates: {artifact.IntentTaskCandidates.Count}");
        writer.WriteLine($"Parent repair targets: {artifact.ParentRepairTargets.Count}");
        writer.WriteLine($"Suggested issue title: {artifact.SuggestedIssueTitle}");
    }
}
