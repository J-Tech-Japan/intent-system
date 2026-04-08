namespace IntentSystem.Cli.Commands;

internal static class BugExecutionRenderer
{
    public static void WriteSummary(TextWriter writer, BugExecutionArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug execution artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Downstream action: {artifact.DownstreamAction}");
        writer.WriteLine($"Clarification required: {artifact.ClarificationRequired.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Ready to launch: {artifact.ReadyToLaunch.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Implementation task candidates: {artifact.ImplementationTaskCandidates.Count}");
        writer.WriteLine($"Intent task candidates: {artifact.IntentTaskCandidates.Count}");
    }
}
