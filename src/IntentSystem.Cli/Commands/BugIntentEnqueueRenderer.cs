namespace IntentSystem.Cli.Commands;

internal static class BugIntentEnqueueRenderer
{
    public static void WriteSummary(TextWriter writer, BugIntentEnqueueArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug intent-enqueue artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Allocated execution unit: {artifact.AllocatedExecutionUnit ?? "not-allocated"}");
        writer.WriteLine($"Linked issue URL: {artifact.LinkedIssueUrl ?? "not-linked"}");
        writer.WriteLine($"Linked issue number: {artifact.LinkedIssueNumber?.ToString() ?? "not-linked"}");
        writer.WriteLine($"Packet paths: {artifact.PacketPaths.Count}");
        writer.WriteLine($"Ready to enqueue: {artifact.ReadyToEnqueue.ToString().ToLowerInvariant()}");
    }
}
