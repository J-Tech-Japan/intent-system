using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentRereviewRendererTests
{
    [Fact]
    public void WriteSummary_GivenRereviewResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentRereviewRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentRereviewResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G138/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/138"],
                WorktreePaths = [".intent-cli/worktrees/G138"],
                StartedExecutionUnits = ["G138"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G138.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/138"],
                ReviewExecutionUnits = ["G138"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G138.request.json"],
                PostedCommentArtifactPaths = [".intent-cli/reviews/G138.comment.json"],
                CommentRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/138#issuecomment-1"],
                FixingExecutionUnits = ["G138"],
                FixRequestArtifactPaths = [".intent-cli/fix/G138.request.md"],
                ResubmittedExecutionUnits = ["G138"],
                ResubmittedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/138"],
                RereviewedExecutionUnits = ["G138"],
                RereviewedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/138"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current rereview processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Rereviewed execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- G138", output, StringComparison.Ordinal);
        Assert.Contains("Rereviewed PR refs:", output, StringComparison.Ordinal);
        Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/138", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentRereviewRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentRereviewResult
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
                ReadinessStatus = "not-ready",
                SkippedStages = ["resubmit-trace", "rereview-entry"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- rereview-entry", output, StringComparison.Ordinal);
    }
}
