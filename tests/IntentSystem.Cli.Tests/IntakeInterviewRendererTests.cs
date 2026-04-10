using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeInterviewRendererTests
{
    [Fact]
    public void WriteSummary_GivenGeneratedResult_RendersCreatedQuestionIdsAndArtifacts()
    {
        var result = new IntakeInterviewResult
        {
            Domain = "auth",
            ConceptArtifactPath = ".intent-cli/intake/auth.concept.yaml",
            WasSkipped = false,
            GeneratedArtifactPaths =
            [
                ".intent-cli/interviews/auth/iq-goal.yaml",
                ".intent-cli/interviews/auth/iq-goal.md"
            ],
            ExistingArtifactPaths = [],
            CreatedQuestionIds = ["iq-goal", "iq-constraints", "iq-unknowns"]
        };
        using var writer = new StringWriter();

        IntakeInterviewRenderer.WriteSummary(writer, result);

        var output = writer.ToString();
        Assert.Contains("Intake interview bootstrap processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Bootstrap status: generated", output, StringComparison.Ordinal);
        Assert.Contains("- iq-goal", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/interviews/auth/iq-goal.yaml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenSkippedResult_RendersExistingArtifacts()
    {
        var result = new IntakeInterviewResult
        {
            Domain = "auth",
            ConceptArtifactPath = ".intent-cli/intake/auth.concept.yaml",
            WasSkipped = true,
            GeneratedArtifactPaths = [],
            ExistingArtifactPaths = [".intent-cli/interviews/auth/iq-goal.yaml"],
            CreatedQuestionIds = []
        };
        using var writer = new StringWriter();

        IntakeInterviewRenderer.WriteSummary(writer, result);

        var output = writer.ToString();
        Assert.Contains("Bootstrap status: skipped", output, StringComparison.Ordinal);
        Assert.Contains("Existing interview artifacts:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/interviews/auth/iq-goal.yaml", output, StringComparison.Ordinal);
    }
}
