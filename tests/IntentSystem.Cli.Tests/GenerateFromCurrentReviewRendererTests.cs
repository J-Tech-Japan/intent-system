using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentReviewRendererTests
{
    [Fact]
    public void WriteSummary_GivenReviewResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentReviewRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentReviewResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G128/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/128"],
                WorktreePaths = [".intent-cli/worktrees/G128"],
                StartedExecutionUnits = ["G128"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G128.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/128"],
                ReviewExecutionUnits = ["G128"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G128.request.json"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current review processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Review request artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/reviews/G128.request.json", output, StringComparison.Ordinal);
        Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/128", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentReviewRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentReviewResult
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
                ReadinessStatus = "not-ready",
                SkippedStages = ["submit-review", "review-request"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- review-request", output, StringComparison.Ordinal);
    }
}
