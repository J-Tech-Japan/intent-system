namespace IntentSystem.Cli.Commands;

internal static class BugIntentReviewRenderer
{
    public static void WriteSummary(TextWriter writer, BugIntentReviewArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug intent-review artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Reviewed execution unit: {artifact.ReviewedExecutionUnit ?? "not-reviewed"}");
        writer.WriteLine($"Review request ref: {artifact.ReviewRequestRef ?? "not-reviewed"}");
        writer.WriteLine($"Linked PR URL: {artifact.LinkedPrUrl ?? "not-reviewed"}");
        writer.WriteLine($"Ready to review: {artifact.ReadyToReview.ToString().ToLowerInvariant()}");
    }
}
