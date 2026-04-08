using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentConfirmedBridgeRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmedBridge_WritesRegeneratedArtifacts()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedBridgeRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedBridgeResult
            {
                Domain = "auth",
                Route = "confirmed-bridge",
                ConceptArtifactPath = ".intent-cli/intake/auth.concept.yaml",
                InterviewArtifactPaths =
                [
                    ".intent-cli/interviews/auth/iq-root.yaml",
                    ".intent-cli/interviews/auth/iq-root.md"
                ],
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/interviews/auth/iq-root.yaml",
                    ".intent-cli/interviews/auth/iq-root.md"
                ],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current confirmed-bridge processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Generated concept artifact: .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Regenerated artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenClarificationReturnRoute_WritesClarificationGuidance()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedBridgeRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedBridgeResult
            {
                Domain = "auth",
                Route = "clarification-return",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                InterviewArtifactPaths = [],
                RegeneratedArtifactPaths = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Clarification-return artifact path: .intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
    }
}
