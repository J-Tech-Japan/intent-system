using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentIssueRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugIntentIssueRenderer.WriteSummary(
            writer,
            new BugIntentIssueArtifact
            {
                BugId = "BUG-123",
                IntentRepairRef = ".intent-cli/bugs/BUG-123.intent-repair.yaml",
                CreatedIssueTitle = "Intent repair: OAuth callback loop (BUG-123)",
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53",
                CreatedIssueNumber = 53,
                ParentRepairTargets =
                [
                    "intent:intents/intent-cli/means/auth.md",
                    "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
                ]
            },
            ".intent-cli/bugs/BUG-123.intent-issue.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug intent-issue artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.intent-issue.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Created issue title: Intent repair: OAuth callback loop (BUG-123)", output, StringComparison.Ordinal);
        Assert.Contains("Created issue URL: https://github.com/J-Tech-Japan/MyIntentHost/issues/53", output, StringComparison.Ordinal);
        Assert.Contains("Parent repair targets: 2", output, StringComparison.Ordinal);
    }
}
