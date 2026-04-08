using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentConfirmedActivateRendererTests
{
    [Fact]
    public void WriteSummary_GivenConfirmedActivate_WritesStartedUnitsAndRefs()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedActivateRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedActivateResult
            {
                Domain = "auth",
                Route = "confirmed-activate",
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
                ConfirmedItems = ["confirm: validate current auth boundary"],
                BlockedItems = [],
                DownstreamReadiness = "ready"
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current confirmed-activate processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
        Assert.Contains("Created issue refs:", output, StringComparison.Ordinal);
        Assert.Contains("Worktree paths:", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: ready", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenClarificationReturnRoute_WritesStopReason()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentConfirmedActivateRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentConfirmedActivateResult
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
                ConfirmedItems = [],
                BlockedItems = ["clarify: resolve auth boundary before issue-cut-ready treatment."],
                DownstreamReadiness = "not-ready"
            });

        var output = writer.ToString();
        Assert.Contains("Clarification-return artifact path: .intent-cli/intake/auth.clarification-return.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Downstream readiness: not-ready", output, StringComparison.Ordinal);
    }
}
