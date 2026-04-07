using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentConfirmRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmResult_RendersDeterministicSections()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DecisionFilePath = "prepared/auth.decisions.md",
                ConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ConfirmedItems = ["confirm: validate the best-practice review suggestions for 'auth' against parent rules/specs before any canonical mutation."],
                RejectedItems = ["reject: explicitly reject any suggested intent addition that conflicts with project rules or specs."],
                ClarifyItems = [],
                DeferredItems = [],
                BlockedItems = [],
                DownstreamReadiness = "ready",
                ReturnToIntentPaths = ["README.md"]
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current confirm processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Prepared decision file path: prepared/auth.decisions.md", output, StringComparison.Ordinal);
        Assert.Contains("Developer confirmation artifact path: .intent-cli/intake/auth.developer-confirmation.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenEmptyLists_RendersNoneEntries()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmResult
            {
                Domain = "auth",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths = [],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DecisionFilePath = "prepared/auth.decisions.md",
                ConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ConfirmedItems = [],
                RejectedItems = [],
                ClarifyItems = [],
                DeferredItems = [],
                BlockedItems = ["clarify: resolve remaining auth boundary"],
                DownstreamReadiness = "not-ready",
                ReturnToIntentPaths = []
            });

        var output = writer.ToString();
        Assert.Contains("- none", output, StringComparison.Ordinal);
        Assert.Contains("Blocked items:", output, StringComparison.Ordinal);
    }
}
