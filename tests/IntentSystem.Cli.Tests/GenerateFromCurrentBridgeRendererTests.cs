using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentBridgeRendererTests
{
    [Fact]
    public void WriteSummary_GivenResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentBridgeRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentBridgeResult
            {
                Domain = "auth",
                ConceptArtifactPath = ".intent-cli/intake/auth.concept.yaml",
                InterviewArtifactPaths =
                [
                    ".intent-cli/interviews/auth/iq-1.yaml",
                    ".intent-cli/interviews/auth/iq-1.md"
                ],
                RecommendedUpdates = ["Clarify auth goal."],
                ReturnToIntentPaths = ["README.md"],
                Gaps = ["Need stronger auth purpose signal."],
                SkippedBridgeSteps = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current bridge processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Generated concept artifact: .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Generated interview artifacts:", output, StringComparison.Ordinal);
        Assert.Contains("Recommended updates:", output, StringComparison.Ordinal);
        Assert.Contains("Skipped bridge steps:", output, StringComparison.Ordinal);
    }
}
