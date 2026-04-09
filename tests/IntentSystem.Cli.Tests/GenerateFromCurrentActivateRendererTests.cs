using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentActivateRendererTests
{
    [Fact]
    public void WriteSummary_GivenActivateResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentActivateRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentActivateResult
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/G124/packet.yaml"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/124"],
                WorktreePaths = [".intent-cli/worktrees/G124"],
                StartedExecutionUnits = ["G124"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current activate processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Source bundle artifact path: .intent-cli/intake/auth.current-sources.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Generated issue artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Created issue refs:", output, StringComparison.Ordinal);
        Assert.Contains("Worktree paths:", output, StringComparison.Ordinal);
        Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentActivateRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentActivateResult
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
                ReadinessStatus = "not-ready",
                SkippedStages = ["issue-generation", "launch"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- issue-generation", output, StringComparison.Ordinal);
        Assert.Contains("- launch", output, StringComparison.Ordinal);
    }
}
