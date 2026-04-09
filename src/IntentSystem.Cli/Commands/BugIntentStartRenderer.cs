namespace IntentSystem.Cli.Commands;

internal static class BugIntentStartRenderer
{
    public static void WriteSummary(TextWriter writer, BugIntentStartArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug intent-start artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Allocated execution unit: {artifact.AllocatedExecutionUnit ?? "not-allocated"}");
        writer.WriteLine($"Worktree path: {artifact.WorktreePath ?? "not-started"}");
        writer.WriteLine($"Branch name: {artifact.BranchName ?? "not-started"}");
        writer.WriteLine($"Ready to start: {artifact.ReadyToStart.ToString().ToLowerInvariant()}");
    }
}
