using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentCloseoutRendererTests
{
    [Fact]
    public void WriteSummary_GivenAcceptedCloseoutResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentCloseoutRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentCloseoutResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G142/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/142"],
                WorktreePaths = [".intent-cli/worktrees/G142"],
                StartedExecutionUnits = ["G142"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G142.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/142"],
                ReviewExecutionUnits = ["G142"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G142.request.json"],
                PostedCommentArtifactPaths = [],
                CommentRefs = [],
                FixingExecutionUnits = [],
                FixRequestArtifactPaths = [],
                ResubmittedExecutionUnits = [],
                ResubmittedPrRefs = [],
                RereviewedExecutionUnits = [],
                CompletedExecutionUnits = ["G142"],
                ClosedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/142"],
                MergedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/142"],
                ReadinessStatus = "ready",
                SelectedCloseoutPath = "accepted-closeout",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current closeout processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Selected closeout path: accepted-closeout", output, StringComparison.Ordinal);
        Assert.Contains("Completed execution units:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenRepairCloseoutResult_RendersRepairPath()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentCloseoutRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentCloseoutResult
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
                PostedCommentArtifactPaths = [".intent-cli/reviews/G142.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/142#issuecomment-1"],
                FixingExecutionUnits = ["G142"],
                FixRequestArtifactPaths = [".intent-cli/fix/G142.request.md"],
                ResubmittedExecutionUnits = ["G142"],
                ResubmittedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/142"],
                RereviewedExecutionUnits = ["G142"],
                CompletedExecutionUnits = ["G142"],
                ClosedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/142"],
                MergedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/142"],
                ReadinessStatus = "ready",
                SelectedCloseoutPath = "repair-in-place-accepted-closeout",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Selected closeout path: repair-in-place-accepted-closeout", output, StringComparison.Ordinal);
        Assert.Contains("Comment refs:", output, StringComparison.Ordinal);
    }
}
