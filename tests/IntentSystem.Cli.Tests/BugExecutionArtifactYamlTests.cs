using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugExecutionArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsBugExecutionArtifact()
    {
        var artifact = new BugExecutionArtifact
        {
            BugId = "BUG-123",
            ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
            TriageRef = ".intent-cli/bugs/BUG-123.triage.yaml",
            DownstreamAction = "dual-track",
            ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
            ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
            ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
            ImplementationTaskCandidates = ["G25"],
            IntentTaskCandidates =
            [
                "intents/intent-cli/means/auth.md",
                "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ],
            ClarificationRequired = false,
            ReadyToLaunch = true
        };

        var yaml = BugExecutionArtifactYaml.Serialize(artifact);
        var roundTripped = BugExecutionArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.ReportRef, roundTripped.ReportRef);
        Assert.Equal(artifact.TriageRef, roundTripped.TriageRef);
        Assert.Equal(artifact.DownstreamAction, roundTripped.DownstreamAction);
        Assert.Equal(artifact.ResolvedImplementationRefs, roundTripped.ResolvedImplementationRefs);
        Assert.Equal(artifact.ResolvedReviewContextRefs, roundTripped.ResolvedReviewContextRefs);
        Assert.Equal(artifact.ResolvedPacketRefs, roundTripped.ResolvedPacketRefs);
        Assert.Equal(artifact.ImplementationTaskCandidates, roundTripped.ImplementationTaskCandidates);
        Assert.Equal(artifact.IntentTaskCandidates, roundTripped.IntentTaskCandidates);
        Assert.Equal(artifact.ClarificationRequired, roundTripped.ClarificationRequired);
        Assert.Equal(artifact.ReadyToLaunch, roundTripped.ReadyToLaunch);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var yaml = """
        bug_id: BUG-123
        report_ref: ".intent-cli/bugs/BUG-123.report.yaml"
        downstream_action: dual-track
        resolved_implementation_refs: []
        resolved_review_context_refs: []
        resolved_packet_refs: []
        implementation_task_candidates: []
        intent_task_candidates: []
        clarification_required: false
        ready_to_launch: true
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugExecutionArtifactYaml.Deserialize(yaml));

        Assert.Contains("triage_ref", exception.Message, StringComparison.Ordinal);
    }
}
