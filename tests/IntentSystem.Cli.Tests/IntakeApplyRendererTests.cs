using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeApplyRendererTests
{
    [Fact]
    public void WriteSummary_GivenApplyResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeApplyRenderer.WriteSummary(
            writer,
            new IntakeApplyResult
            {
                Domain = "auth",
                ChangedFilePaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
                AppliedEditCount = 2,
                SourceConceptRefs = ["intents/intent-cli/concepts/auth-oauth2.md"]
            });

        var output = writer.ToString();
        Assert.Contains("Intake apply completed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Applied edit count: 2", output, StringComparison.Ordinal);
        Assert.Contains("Changed file paths:", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/intent-tree/means/auth-oauth2.md", output, StringComparison.Ordinal);
        Assert.Contains("Source concept refs:", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", output, StringComparison.Ordinal);
    }
}
