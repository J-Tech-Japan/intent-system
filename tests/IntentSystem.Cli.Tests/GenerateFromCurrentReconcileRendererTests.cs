using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentReconcileRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmedHandoff_WritesArtifactSummary()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentReconcileRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentReconcileResult
            {
                Domain = "auth",
                Route = "confirmed-handoff",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                ConfirmedItems = ["confirm: validate current auth boundary"],
                RejectedItems = ["reject: do not rewrite current auth ownership model"],
                DeferredItems = ["defer: return interface cleanup after clarification"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                ClarifyItems = [],
                ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current reconcile processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Confirmed reconstruction artifact path: .intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Confirmed items:", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenClarificationRoute_WritesClarificationSummary()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentReconcileRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentReconcileResult
            {
                Domain = "auth",
                Route = "clarification-return",
                SourceBundleArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                ReconstructedArtifactPaths =
                [
                    ".intent-cli/intake/auth.reconstructed-concept.yaml",
                    ".intent-cli/intake/auth.reconstructed-interview.md"
                ],
                ReviewArtifactPath = ".intent-cli/intake/auth.best-practice-review.md",
                DeveloperConfirmationArtifactPath = ".intent-cli/intake/auth.developer-confirmation.yaml",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                ConfirmedItems = ["confirm: validate current auth boundary"],
                RejectedItems = [],
                DeferredItems = [],
                BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                ClarifyItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                ReturnToIntentPaths = ["intents/intent-cli/specs/11-reconstruction-review-and-confirmation.md"],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Clarification-return artifact path: .intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Clarify items:", output, StringComparison.Ordinal);
        Assert.Contains("- clarify: resolve auth boundary before issue-cut-ready treatment.", output, StringComparison.Ordinal);
    }
}
