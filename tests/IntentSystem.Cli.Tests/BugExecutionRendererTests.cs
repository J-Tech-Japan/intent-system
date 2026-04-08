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
                ImplementationTaskCandidates =
                [
                    "execution_unit=G25;packet_ref=.intent-cli/issues/G25/packet.yaml;review_context_ref=.intent-cli/issues/G25/review-context.md"
                ],
                IntentTaskCandidates =
                [
                    "intent_ref=intents/intent-cli/means/auth.md;source=intent"
                ],
                ClarificationRequired = false,
                ReadyToLaunch = true
            },
            ".intent-cli/bugs/BUG-123.execution.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug execution artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Downstream action: dual-track", output, StringComparison.Ordinal);
        Assert.Contains("Clarification required: false", output, StringComparison.Ordinal);
        Assert.Contains("Ready to launch: true", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.execution.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Implementation task candidates: 1", output, StringComparison.Ordinal);
        Assert.Contains("Intent task candidates: 1", output, StringComparison.Ordinal);
    }
}
