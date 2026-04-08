using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentConfirmedAdvanceRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmedAdvance_WritesUpdatedPaths()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedAdvanceRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedAdvanceResult
            {
                Domain = "auth",
                Route = "confirmed-advance",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/intake/auth.compile.md",
                    ".intent-cli/intake/auth.execution.md"
                ],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current confirmed-advance processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Updated source file paths:", output, StringComparison.Ordinal);
        Assert.Contains("Updated execution file paths:", output, StringComparison.Ordinal);
        Assert.Contains("Regenerated artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenReconciliationRequired_WritesStopReason()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedAdvanceRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedAdvanceResult
            {
                Domain = "auth",
                Route = "reconciliation-required",
                ConfirmedReconstructionArtifactPath = ".intent-cli/intake/auth.confirmed-reconstruction.yaml",
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = [".intent-cli/intake/auth.confirmed-reconstruction.yaml"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = ["defer: return interface cleanup after clarification"],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Confirmed reconstruction artifact path: .intent-cli/intake/auth.confirmed-reconstruction.yaml", output, StringComparison.Ordinal);
        Assert.Contains("reconciliation is not ready", output, StringComparison.OrdinalIgnoreCase);
    }
}
