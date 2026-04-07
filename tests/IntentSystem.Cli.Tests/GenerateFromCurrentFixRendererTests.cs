using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentFixRendererTests
{
    [Fact]
    public void WriteSummary_GivenFixResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentFixRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentFixResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G134/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/134"],
                WorktreePaths = [".intent-cli/worktrees/G134"],
                StartedExecutionUnits = ["G134"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G134.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/134"],
                ReviewExecutionUnits = ["G134"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G134.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/G134.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/134#issuecomment-1"],
                FixingExecutionUnits = ["G134"],
                FixRequestArtifactPaths = [".intent-cli/fix/G134.request.md"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current fix processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Fix request artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/fix/G134.request.md", output, StringComparison.Ordinal);
        Assert.Contains("Fixing execution units:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentFixRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentFixResult
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
                FixRequestArtifactPaths = [],
                ReadinessStatus = "not-ready",
                SkippedStages = ["review-comment", "fix-handoff"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- fix-handoff", output, StringComparison.Ordinal);
    }
}
