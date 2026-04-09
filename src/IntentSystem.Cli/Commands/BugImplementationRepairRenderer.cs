namespace IntentSystem.Cli.Commands;

internal static class BugImplementationRepairRenderer
{
    public static void WriteSummary(TextWriter writer, BugImplementationRepairArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug implementation-repair artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Ready to issue cut: {artifact.ReadyToIssueCut.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Implementation task candidates: {artifact.ImplementationTaskCandidates.Count}");
        writer.WriteLine($"Implementation repair targets: {artifact.ImplementationRepairTargets.Count}");
        writer.WriteLine($"Suggested issue title: {artifact.SuggestedIssueTitle}");
    }
}
