using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentAcceptRendererTests
{
    [Fact]
    public void WriteSummary_GivenAcceptResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentAcceptRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentAcceptResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G130/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/130"],
                WorktreePaths = [".intent-cli/worktrees/G130"],
                StartedExecutionUnits = ["G130"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G130.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/130"],
                ReviewExecutionUnits = ["G130"],
                ReviewRequestArtifactPaths = [".intent-cli/reviews/G130.request.json"],
                MergedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/130"],
                ClosedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/130"],
                CompletedExecutionUnits = ["G130"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current accept processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Merged PR refs:", output, StringComparison.Ordinal);
        Assert.Contains("Closed issue refs:", output, StringComparison.Ordinal);
        Assert.Contains("Completed execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- G130", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentAcceptRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentAcceptResult
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
                MergedPrRefs = [],
                ClosedIssueRefs = [],
                CompletedExecutionUnits = [],
                ReadinessStatus = "not-ready",
                SkippedStages = ["review-request", "accepted-closeout"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- accepted-closeout", output, StringComparison.Ordinal);
    }
}
