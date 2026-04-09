namespace IntentSystem.Cli.Commands;

internal static class BugIntentSubmitRenderer
{
    public static void WriteSummary(TextWriter writer, BugIntentSubmitArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug intent-submit artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Submitted execution unit: {artifact.SubmittedExecutionUnit ?? "not-submitted"}");
        writer.WriteLine($"Linked PR: {artifact.LinkedPr ?? "not-submitted"}");
        writer.WriteLine($"Ready to submit: {artifact.ReadyToSubmit.ToString().ToLowerInvariant()}");
    }
}
