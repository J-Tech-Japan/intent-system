namespace IntentSystem.Cli.Commands;

internal static class BugImplementationIssueRenderer
{
    public static void WriteSummary(TextWriter writer, BugImplementationIssueArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug implementation-issue artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Created issue title: {artifact.CreatedIssueTitle}");
        writer.WriteLine($"Created issue URL: {artifact.CreatedIssueUrl ?? "not-created"}");
        writer.WriteLine($"Implementation repair targets: {artifact.ImplementationRepairTargets.Count}");
    }
}
