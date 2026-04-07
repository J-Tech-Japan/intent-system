using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentAdvanceRendererTests
{
    [Fact]
    public void WriteSummary_GivenResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentAdvanceRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentAdvanceResult
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
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current advance processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Source bundle artifact path: .intent-cli/intake/auth.current-sources.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Reconstructed artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Regenerated standard intake artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Updated source file paths:", output, StringComparison.Ordinal);
        Assert.Contains("Updated execution file paths:", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
        Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
    }
}
