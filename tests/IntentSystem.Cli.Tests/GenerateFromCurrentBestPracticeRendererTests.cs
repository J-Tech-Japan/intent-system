using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentBestPracticeRendererTests
{
    [Fact]
    public void WriteSummary_GivenBestPracticeResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentBestPracticeRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentBestPracticeResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                ReviewedDimensions = ["architecture: needs-confirmation", "security: needs-confirmation"],
                ModelRefs = [".intent/model-registry/auth-model.md"],
                KnowledgeRefs = [".intent/best-practices/security.md"],
                RecommendedIntentAdditions = ["Promote reconstructed intent candidates for 'auth' into explicit parent intent additions after confirmation."],
                RecommendedClarifications = ["Clarify the authn/authz model and trust boundary for 'auth'."],
                DeveloperConfirmationItems = ["confirm: validate the best-practice review suggestions for 'auth' against parent rules/specs before any canonical mutation."],
                ReturnToIntentPaths = ["README.md", "AGENTS.md"],
                ConfidenceDeltas = ["purpose: medium -> high", "execution: medium -> high"],
                ReadinessStatus = "ready",
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current best-practice processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Best-practice review artifact path: .intent-cli/intake/auth.best-practice-review.md", output, StringComparison.Ordinal);
        Assert.Contains("Reviewed dimensions:", output, StringComparison.Ordinal);
        Assert.Contains("Confidence deltas:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentBestPracticeRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentBestPracticeResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths = [],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                ReviewedDimensions = [],
                ModelRefs = [],
                KnowledgeRefs = [],
                RecommendedIntentAdditions = [],
                RecommendedClarifications = [],
                DeveloperConfirmationItems = [],
                ReturnToIntentPaths = [],
                ConfidenceDeltas = [],
                ReadinessStatus = "not-ready",
                SkippedStages = ["model-registry-review", "best-practice-knowledge-review"]
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("- model-registry-review", output, StringComparison.Ordinal);
    }
}
