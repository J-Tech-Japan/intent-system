namespace IntentSystem.Cli.Commands;

internal static class BugIntentIssueRenderer
{
    public static void WriteSummary(TextWriter writer, BugIntentIssueArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug intent-issue artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Created issue title: {artifact.CreatedIssueTitle}");
        writer.WriteLine($"Created issue URL: {artifact.CreatedIssueUrl ?? "not-created"}");
        writer.WriteLine($"Parent repair targets: {artifact.ParentRepairTargets.Count}");
    }
}
