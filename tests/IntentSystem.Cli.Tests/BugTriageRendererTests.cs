using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugTriageRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugTriageRenderer.WriteSummary(
            writer,
            new BugTriageArtifact
            {
                BugId = "BUG-123",
                ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
                Classification = "implementation-impact",
                DownstreamAction = "implementation-repair",
                ClarificationRequired = true,
                ClarificationReasons = ["execution unit roots could not be fully resolved for: G77"],
                OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
                LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"],
                ResolvedExecutionUnits = ["G25"],
                ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
                ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
                ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
                UnresolvedExecutionUnits = ["G77"],
                ImplementationRepairCandidates = ["G25"],
                IntentRepairCandidates = []
            },
            ".intent-cli/bugs/BUG-123.triage.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug triage artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Classification: implementation-impact", output, StringComparison.Ordinal);
        Assert.Contains("Downstream action: implementation-repair", output, StringComparison.Ordinal);
        Assert.Contains("Clarification required: true", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.triage.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Implementation repair candidates: 1", output, StringComparison.Ordinal);
        Assert.Contains("Intent repair candidates: 0", output, StringComparison.Ordinal);
        Assert.Contains("Resolved execution units: 1", output, StringComparison.Ordinal);
        Assert.Contains("Unresolved execution units: 1", output, StringComparison.Ordinal);
        Assert.Contains("Linked review refs: 1", output, StringComparison.Ordinal);
    }
}
