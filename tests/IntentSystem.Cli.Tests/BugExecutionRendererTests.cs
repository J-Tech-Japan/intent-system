using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugExecutionRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugExecutionRenderer.WriteSummary(
            writer,
            new BugExecutionArtifact
            {
                BugId = "BUG-123",
                ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
                TriageRef = ".intent-cli/bugs/BUG-123.triage.yaml",
                DownstreamAction = "dual-track",
                ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
                ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
                ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
                ImplementationTaskCandidates = ["G25"],
                IntentTaskCandidates = ["intents/intent-cli/means/auth.md"],
                ClarificationRequired = false,
                ReadyToLaunch = true
            },
            ".intent-cli/bugs/BUG-123.plan.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug plan artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Downstream action: dual-track", output, StringComparison.Ordinal);
        Assert.Contains("Clarification required: false", output, StringComparison.Ordinal);
        Assert.Contains("Ready to launch: true", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.plan.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Implementation task candidates: 1", output, StringComparison.Ordinal);
        Assert.Contains("Intent task candidates: 1", output, StringComparison.Ordinal);
    }
}
