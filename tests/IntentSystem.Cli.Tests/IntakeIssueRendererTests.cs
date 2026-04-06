using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeIssueRendererTests
{
    [Fact]
    public void WriteSummary_GivenIssueResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeIssueRenderer.WriteSummary(
            writer,
            new IntakeIssueResult
            {
                Domain = "auth",
                GeneratedExecutionUnits = ["AUTH-01", "AUTH-02"],
                ArtifactPaths =
                [
                    ".intent-cli/issues/AUTH-01/implementation.md",
                    ".intent-cli/issues/AUTH-01/review-context.md",
                    ".intent-cli/issues/AUTH-01/packet.yaml",
                    ".intent-cli/issues/AUTH-01/github-body.md"
                ],
                SkippedUnits = ["AUTH-03"]
            });

        var output = writer.ToString();
        Assert.Contains("Intake issue artifacts generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Generated execution units:", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
        Assert.Contains("Artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/issues/AUTH-01/github-body.md", output, StringComparison.Ordinal);
        Assert.Contains("Skipped units:", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-03", output, StringComparison.Ordinal);
    }
}
