using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentSubmitRendererTests
{
    [Fact]
    public void WriteSummary_GivenSubmitResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentSubmitRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentSubmitResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G126/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/126"],
                WorktreePaths = [".intent-cli/worktrees/G126"],
                StartedExecutionUnits = ["G126"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/G126.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/126"],
                ReviewExecutionUnits = ["G126"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current submit processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Created PR refs:", output, StringComparison.Ordinal);
        Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/126", output, StringComparison.Ordinal);
        Assert.Contains("Review execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- G126", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentSubmitRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentSubmitResult
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
                ReadinessStatus = "not-ready",
                SkippedStages = ["issue-generation", "launch", "implement-handoff", "submit-review"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- submit-review", output, StringComparison.Ordinal);
    }
}
