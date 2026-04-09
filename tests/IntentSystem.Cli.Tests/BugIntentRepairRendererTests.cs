using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentRepairRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugIntentRepairRenderer.WriteSummary(
            writer,
            new BugIntentRepairArtifact
            {
                BugId = "BUG-123",
                ExecutionRef = ".intent-cli/bugs/BUG-123.plan.yaml",
                IntentTaskCandidates = ["intents/intent-cli/means/auth.md"],
                ParentRepairTargets = ["intent:intents/intent-cli/means/auth.md"],
                SuggestedIssueTitle = "Intent repair: OAuth callback loop (BUG-123)",
                SuggestedGoal = "Repair parent intent targets for 'OAuth callback loop' (BUG-123) using .intent-cli/bugs/BUG-123.plan.yaml: intent:intents/intent-cli/means/auth.md",
                ReadyToIssueCut = true
            },
            ".intent-cli/bugs/BUG-123.intent-repair.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug intent-repair artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Ready to issue cut: true", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.intent-repair.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Intent task candidates: 1", output, StringComparison.Ordinal);
        Assert.Contains("Parent repair targets: 1", output, StringComparison.Ordinal);
        Assert.Contains("Suggested issue title: Intent repair: OAuth callback loop (BUG-123)", output, StringComparison.Ordinal);
    }
}
