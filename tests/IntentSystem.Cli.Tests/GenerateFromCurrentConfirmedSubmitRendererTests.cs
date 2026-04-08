using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentConfirmedSubmitRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmedSubmit_WritesCreatedPrRefs()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedSubmitRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedSubmitResult
            {
                Domain = "auth",
                Route = "confirmed-submit",
                UpdatedSourceFilePaths = ["intents/intent-cli/concepts/auth-oauth2.md"],
                UpdatedExecutionFilePaths = ["intents/intent-cli/execution/05-post-mvp-sub-slices.md"],
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.concept.yaml",
                    ".intent-cli/intake/auth.execution.md",
                    ".intent-cli/issues/AUTH-01/packet.yaml"
                ],
                StartedExecutionUnits = ["AUTH-01"],
                CreatedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/501"],
                WorktreePaths = ["/tmp/worktrees/AUTH-01"],
                ImplementRequestArtifactPaths = [".intent-cli/implement/AUTH-01.request.md"],
                CreatedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/501"],
                ReviewExecutionUnits = ["AUTH-01"],
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current confirmed-submit processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Created PR refs:", output, StringComparison.Ordinal);
        Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/pull/501", output, StringComparison.Ordinal);
        Assert.Contains("Review execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenClarificationReturnRoute_WritesStopReason()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedSubmitRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedSubmitResult
            {
                Domain = "auth",
                Route = "clarification-return",
                ClarificationReturnArtifactPath = ".intent-cli/intake/auth.clarification-return.yaml",
                UpdatedSourceFilePaths = [],
                UpdatedExecutionFilePaths = [],
                RegeneratedArtifactPaths = [],
                StartedExecutionUnits = [],
                CreatedIssueRefs = [],
                WorktreePaths = [],
                ImplementRequestArtifactPaths = [],
                CreatedPrRefs = [],
                ReviewExecutionUnits = [],
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before review-entry treatment."],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Clarification-return artifact path: .intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
    }
}
