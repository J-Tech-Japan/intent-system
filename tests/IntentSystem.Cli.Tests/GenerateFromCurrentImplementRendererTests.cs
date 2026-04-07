using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentImplementRendererTests
{
    [Fact]
    public void WriteSummary_GivenImplementResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentImplementRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentImplementResult
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
                ImplementRequestArtifactPaths = [".intent-cli/implement/G124.request.md"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current implement processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Source bundle artifact path: .intent-cli/intake/auth.current-sources.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Implement request artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/implement/G124.request.md", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentImplementRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentImplementResult
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
                ReadinessStatus = "not-ready",
                SkippedStages = ["issue-generation", "launch", "implement-handoff"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- implement-handoff", output, StringComparison.Ordinal);
    }
}
