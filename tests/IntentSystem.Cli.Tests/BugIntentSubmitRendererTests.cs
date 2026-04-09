using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentSubmitRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugIntentSubmitRenderer.WriteSummary(
            writer,
            new BugIntentSubmitArtifact
            {
                BugId = "BUG-123",
                IntentStartRef = ".intent-cli/bugs/BUG-123.intent-start.yaml",
                SubmittedExecutionUnit = "G41",
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/58",
                LinkedPrNumber = 58,
                ReadyToSubmit = true
            },
            ".intent-cli/bugs/BUG-123.intent-submit.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug intent-submit artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.intent-submit.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Submitted execution unit: G41", output, StringComparison.Ordinal);
        Assert.Contains("Linked PR URL: https://github.com/J-Tech-Japan/intent-system/pull/58", output, StringComparison.Ordinal);
        Assert.Contains("Linked PR number: 58", output, StringComparison.Ordinal);
        Assert.Contains("Ready to submit: true", output, StringComparison.Ordinal);
    }
}
