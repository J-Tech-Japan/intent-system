namespace IntentSystem.Cli.Commands;

internal static class BugIntentCommentRenderer
{
    public static void WriteSummary(TextWriter writer, BugIntentCommentArtifact artifact, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Bug intent-comment artifact generated for '{artifact.BugId}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Commented execution unit: {artifact.CommentedExecutionUnit ?? "not-commented"}");
        writer.WriteLine($"Review comment ref: {artifact.ReviewCommentRef ?? "not-commented"}");
        writer.WriteLine($"Comment ref: {artifact.CommentRef ?? "not-commented"}");
        writer.WriteLine($"Linked PR URL: {artifact.LinkedPrUrl ?? "not-commented"}");
        writer.WriteLine($"Ready to comment: {artifact.ReadyToComment.ToString().ToLowerInvariant()}");
    }
}
