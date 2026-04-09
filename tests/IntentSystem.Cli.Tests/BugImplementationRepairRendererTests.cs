using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugImplementationRepairRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugImplementationRepairRenderer.WriteSummary(
            writer,
            new BugImplementationRepairArtifact
            {
                BugId = "BUG-123",
                ExecutionRef = ".intent-cli/bugs/BUG-123.execution.yaml",
                ImplementationTaskCandidates = ["G25"],
                ImplementationRepairTargets = [".intent-cli/issues/G25/packet.yaml"],
                SuggestedIssueTitle = "Implementation repair: OAuth callback loop (BUG-123)",
                SuggestedGoal = "Repair child implementation targets for 'OAuth callback loop' (BUG-123) using .intent-cli/bugs/BUG-123.execution.yaml: .intent-cli/issues/G25/packet.yaml",
                ReadyToIssueCut = true
            },
            ".intent-cli/bugs/BUG-123.implementation-repair.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug implementation-repair artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Ready to issue cut: true", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.implementation-repair.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Implementation task candidates: 1", output, StringComparison.Ordinal);
        Assert.Contains("Implementation repair targets: 1", output, StringComparison.Ordinal);
        Assert.Contains("Suggested issue title: Implementation repair: OAuth callback loop (BUG-123)", output, StringComparison.Ordinal);
    }
}
