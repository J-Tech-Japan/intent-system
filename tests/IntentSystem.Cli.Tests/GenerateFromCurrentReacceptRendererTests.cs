using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentReacceptRendererTests
{
    [Fact]
    public void WriteSummary_GivenReacceptResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentReacceptRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentReacceptResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G140/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/140"],
                WorktreePaths = [".intent-cli/worktrees/G140"],
                StartedExecutionUnits = ["G140"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G140.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/140"],
                ReviewExecutionUnits = ["G140"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G140.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/G140.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/140#issuecomment-1"],
                FixingExecutionUnits = ["G140"],
                FixRequestArtifactPaths = [".intent-cli/fix/G140.request.md"],
                ResubmittedExecutionUnits = ["G140"],
                ResubmittedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/140"],
                RereviewedExecutionUnits = ["G140"],
                RereviewedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/140"],
                CompletedExecutionUnits = ["G140"],
                ClosedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/140"],
                MergedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/140"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current reaccept processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Completed execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- G140", output, StringComparison.Ordinal);
        Assert.Contains("Closed issue refs:", output, StringComparison.Ordinal);
        Assert.Contains("Merged PR refs:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentReacceptRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentReacceptResult
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
                ResubmittedExecutionUnits = [],
                ResubmittedPrRefs = [],
                RereviewedExecutionUnits = [],
                RereviewedPrRefs = [],
                CompletedExecutionUnits = [],
                ClosedIssueRefs = [],
                MergedPrRefs = [],
                ReadinessStatus = "not-ready",
                SkippedStages = ["rereview-entry", "accepted-closeout"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- accepted-closeout", output, StringComparison.Ordinal);
    }
}
