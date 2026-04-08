using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugTriageArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsBugTriageArtifact()
    {
        var artifact = new BugTriageArtifact
        {
            BugId = "BUG-123",
            ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
            Classification = "implementation-and-intent-impact",
            DownstreamAction = "implementation-and-intent-repair",
            ClarificationRequired = true,
            ClarificationReasons = ["execution unit roots could not be fully resolved for: G77"],
            OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"],
            ResolvedExecutionUnits = ["G25"],
            ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
            ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
            ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
            UnresolvedExecutionUnits = ["G77"],
            ImplementationRepairCandidates = ["G25"],
            IntentRepairCandidates = ["intents/intent-cli/means/auth.md"]
        };

        var yaml = BugTriageArtifactYaml.Serialize(artifact);
        var roundTripped = BugTriageArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.ReportRef, roundTripped.ReportRef);
        Assert.Equal(artifact.Classification, roundTripped.Classification);
        Assert.Equal(artifact.DownstreamAction, roundTripped.DownstreamAction);
        Assert.Equal(artifact.ClarificationRequired, roundTripped.ClarificationRequired);
        Assert.Equal(artifact.ClarificationReasons, roundTripped.ClarificationReasons);
        Assert.Equal(artifact.OriginalInstructionRootRefs, roundTripped.OriginalInstructionRootRefs);
        Assert.Equal(artifact.LinkedReviewRefs, roundTripped.LinkedReviewRefs);
        Assert.Equal(artifact.ResolvedExecutionUnits, roundTripped.ResolvedExecutionUnits);
        Assert.Equal(artifact.ResolvedImplementationRefs, roundTripped.ResolvedImplementationRefs);
        Assert.Equal(artifact.ResolvedReviewContextRefs, roundTripped.ResolvedReviewContextRefs);
        Assert.Equal(artifact.ResolvedPacketRefs, roundTripped.ResolvedPacketRefs);
        Assert.Equal(artifact.UnresolvedExecutionUnits, roundTripped.UnresolvedExecutionUnits);
        Assert.Equal(artifact.ImplementationRepairCandidates, roundTripped.ImplementationRepairCandidates);
        Assert.Equal(artifact.IntentRepairCandidates, roundTripped.IntentRepairCandidates);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var yaml = """
        bug_id: BUG-123
        report_ref: ".intent-cli/bugs/BUG-123.report.yaml"
        classification: implementation-impact
        downstream_action: implementation-repair
        clarification_required: false
        clarification_reasons: []
        original_instruction_root_refs: []
        linked_review_refs: []
        resolved_execution_units: []
        resolved_implementation_refs: []
        resolved_review_context_refs: []
        unresolved_execution_units: []
        implementation_repair_candidates: []
        intent_repair_candidates: []
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugTriageArtifactYaml.Deserialize(yaml));

        Assert.Contains("resolved_packet_refs", exception.Message, StringComparison.Ordinal);
    }
}
