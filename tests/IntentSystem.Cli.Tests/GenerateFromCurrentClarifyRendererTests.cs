using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentClarifyRendererTests
{
    [Fact]
    public void WriteSummary_GivenClarifyResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentClarifyRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentClarifyResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ClarifyItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                AffectedParentRefs = ["README.md"],
                Reasons = ["Clarify the authn/authz model and trust boundary for 'auth'."],
                Blockingness = ["clarify: resolve auth boundary before issue-cut-ready treatment. => blocking"],
                ReturnToIntentPaths = ["README.md"],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current clarify processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Clarification-return artifact path: .intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Affected parent refs:", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentClarifyRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentClarifyResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths = [],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ClarifyItems = [],
                AffectedParentRefs = [],
                Reasons = [],
                Blockingness = [],
                ReturnToIntentPaths = [],
                DownstreamReadiness = "ready"
            });

        Assert.Contains("- none", writer.ToString(), StringComparison.Ordinal);
    }
}
