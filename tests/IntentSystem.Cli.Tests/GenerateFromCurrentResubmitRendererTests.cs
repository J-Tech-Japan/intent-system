using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentResubmitRendererTests
{
    [Fact]
    public void WriteSummary_GivenResubmitResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentResubmitRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentResubmitResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G136/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/136"],
                WorktreePaths = [".intent-cli/worktrees/G136"],
                StartedExecutionUnits = ["G136"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G136.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/136"],
                ReviewExecutionUnits = ["G136"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G136.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/G136.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/136#issuecomment-1"],
                FixingExecutionUnits = ["G136"],
                FixRequestArtifactPaths = [".intent-cli/fix/G136.request.md"],
                ResubmittedExecutionUnits = ["G136"],
                ResubmittedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/136"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current resubmit processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Resubmitted execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- G136", output, StringComparison.Ordinal);
        Assert.Contains("Resubmitted PR refs:", output, StringComparison.Ordinal);
        Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/136", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentResubmitRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentResubmitResult
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
                ReadinessStatus = "not-ready",
                SkippedStages = ["fix-handoff", "resubmit-trace"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- resubmit-trace", output, StringComparison.Ordinal);
    }
}
