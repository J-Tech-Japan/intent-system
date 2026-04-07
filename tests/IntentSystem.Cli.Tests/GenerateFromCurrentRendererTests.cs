using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class GenerateFromCurrentRendererTests
{
    [Fact]
    public void WriteSummary_GivenResult_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        GenerateFromCurrentRenderer.WriteSummary(
            writer,
            new GenerateFromCurrentResult
            {
                Domain = "auth",
                ArtifactPath = ".intent-cli/intake/auth.current-sources.yaml",
                SourceRoot = "src/IntentSystem.Cli",
                SelectedIssueScope = "114",
                SelectedPrScope = "113",
                SelectedAltitudes = ["means", "execution"],
                SelectedPaths = ["src/IntentSystem.Cli/Program.cs"],
                SourceRefs = ["code:src/IntentSystem.Cli/Program.cs"],
                SamplingNotes = ["code:src/IntentSystem.Cli/Program.cs summary=namespace IntentSystem.Cli;"],
                Gaps = ["Issue 114 has sparse signal."]
            });

        var output = writer.ToString();
        Assert.Contains("Generate-from-current processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/intake/auth.current-sources.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Selected issue scope: 114", output, StringComparison.Ordinal);
        Assert.Contains("Selected altitudes:", output, StringComparison.Ordinal);
        Assert.Contains("Source refs:", output, StringComparison.Ordinal);
        Assert.Contains("Gaps:", output, StringComparison.Ordinal);
    }
}
