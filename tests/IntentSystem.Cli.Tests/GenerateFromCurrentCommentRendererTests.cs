using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentCommentRendererTests
{
    [Fact]
    public void WriteSummary_GivenCommentResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentCommentRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentCommentResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                StandardIntakeArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/interviews/auth/iq-1.yaml"
                ],
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G132/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/132"],
                WorktreePaths = [".intent-cli/worktrees/G132"],
                StartedExecutionUnits = ["G132"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G132.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/132"],
                ReviewExecutionUnits = ["G132"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G132.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/G132.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/132#issuecomment-1"],
                FixingExecutionUnits = ["G132"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current comment processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Posted comment artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Comment refs:", output, StringComparison.Ordinal);
        Assert.Contains("Fixing execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- G132", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentCommentRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentCommentResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths = [],
                StandardIntakeArtifactPaths = [],
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                GeneratedIssueArtifactPaths = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                StartedExecutionUnits = [],
                ImplementRequestArtifactPaths = [],
                CreatedPrRefs = [],
                ReviewExecutionUnits = [],
                ReviewRequestArtifactPaths = [],
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = [],
                ReadinessStatus = "not-ready",
                SkippedStages = ["review-request", "review-comment"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- review-comment", output, StringComparison.Ordinal);
    }
}
