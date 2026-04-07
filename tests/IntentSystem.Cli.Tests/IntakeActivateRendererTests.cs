using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeActivateRendererTests
{
    [Fact]
    public void WriteSummary_GivenActivateResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeActivateRenderer.WriteSummary(
            writer,
            new IntakeActivateResult
            {
                Domain = "auth",
                ReadinessStatus = "ready",
                UpdatedSourceFilePaths =
                [
                    "intents/intent-cli/concepts/auth-oauth2.md"
                ],
                UpdatedExecutionFilePaths =
                [
                    "intents/intent-cli/execution/05-post-mvp-sub-slices.md"
                ],
                RegeneratedArtifactPaths =
                [
                    ".intent-cli/intake/auth.compile.md",
                    ".intent-cli/intake/auth.execution.md"
                ],
                StartedExecutionUnits =
                [
                    "AUTH-01"
                ],
                GeneratedIssueArtifactPaths =
                [
                    ".intent-cli/issues/AUTH-01/packet.yaml",
                    ".intent-cli/issues/AUTH-01/github-body.md"
                ],
                CreatedIssueRefs =
                [
                    "https://github.com/J-Tech-Japan/intent-system/issues/501"
                ],
                WorktreePaths =
                [
                    "/repo/.intent-cli/worktrees/AUTH-01"
                ],
                SkippedStages = []
            });

        var output = writer.ToString();
        Assert.Contains("Intake activate processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
        Assert.Contains("Updated source file paths:", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", output, StringComparison.Ordinal);
        Assert.Contains("Generated issue artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/issues/AUTH-01/github-body.md", output, StringComparison.Ordinal);
        Assert.Contains("Created issue refs:", output, StringComparison.Ordinal);
        Assert.Contains("Worktree paths:", output, StringComparison.Ordinal);
        Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
    }
}
