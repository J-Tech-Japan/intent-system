using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugImplementationIssueRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugImplementationIssueRenderer.WriteSummary(
            writer,
            new BugImplementationIssueArtifact
            {
                BugId = "BUG-123",
                ImplementationRepairRef = ".intent-cli/bugs/BUG-123.implementation-repair.yaml",
                CreatedIssueTitle = "Implementation repair: OAuth callback loop (BUG-123)",
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/53",
                CreatedIssueNumber = 53,
                ImplementationRepairTargets = [".intent-cli/issues/G25/packet.yaml"]
            },
            ".intent-cli/bugs/BUG-123.implementation-issue.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug implementation-issue artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.implementation-issue.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Created issue title: Implementation repair: OAuth callback loop (BUG-123)", output, StringComparison.Ordinal);
        Assert.Contains("Created issue URL: https://github.com/J-Tech-Japan/intent-system/issues/53", output, StringComparison.Ordinal);
        Assert.Contains("Implementation repair targets: 1", output, StringComparison.Ordinal);
    }
}
