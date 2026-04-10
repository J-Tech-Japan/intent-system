using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeInitRendererTests
{
    [Fact]
    public void WriteSummary_GivenGeneratedResult_RendersQuestionIdsAndPaths()
    {
        var result = new IntakeInitResult
        {
            Domain = "auth",
            WorkRepoPath = "/tmp/work-repo",
            InterviewWasSkipped = false,
            CreatedQuestionIds = ["iq-goal", "iq-constraints"],
            GeneratedPaths =
            [
                ".intent-cli/config.toml",
                ".intent-cli/interviews/auth/iq-goal.yaml"
            ],
            SkippedPaths = []
        };
        using var writer = new StringWriter();

        IntakeInitRenderer.WriteSummary(writer, result);

        var output = writer.ToString();
        Assert.Contains("Intake init processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Interview bootstrap: generated", output, StringComparison.Ordinal);
        Assert.Contains("- iq-goal", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/config.toml", output, StringComparison.Ordinal);
        Assert.Contains("Skipped paths:", output, StringComparison.Ordinal);
        Assert.Contains("- none", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenSkippedArtifacts_RendersSkippedPaths()
    {
        var result = new IntakeInitResult
        {
            Domain = "auth",
            WorkRepoPath = "/tmp/work-repo",
            InterviewWasSkipped = true,
            CreatedQuestionIds = [],
            GeneratedPaths = [],
            SkippedPaths =
            [
                ".intent-cli/config.toml",
                ".intent-cli/interviews/auth/iq-existing.yaml"
            ]
        };
        using var writer = new StringWriter();

        IntakeInitRenderer.WriteSummary(writer, result);

        var output = writer.ToString();
        Assert.Contains("Interview bootstrap: skipped", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/interviews/auth/iq-existing.yaml", output, StringComparison.Ordinal);
    }
}
