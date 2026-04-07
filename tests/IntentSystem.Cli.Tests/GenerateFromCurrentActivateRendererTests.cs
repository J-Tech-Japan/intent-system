using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentActivateRendererTests
{
    [Fact]
    public void WriteSummary_GivenResult_WritesDeterministicSummary()
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
                GeneratedIssueArtifactPaths = [".intent-cli/issues/AUTH-01/packet.yaml"],
                CreatedIssueRefs = ["issue:201"],
                WorktreePaths = [".intent-cli/worktrees/issue-201-auth-01"],
                StartedExecutionUnits = ["AUTH-01"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current activate processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Generated issue artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Created issue refs:", output, StringComparison.Ordinal);
        Assert.Contains("Worktree paths:", output, StringComparison.Ordinal);
        Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
    }
}
