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
            ImplementationTaskCandidates =
            [
                "execution_unit=G25;packet_ref=.intent-cli/issues/G25/packet.yaml;review_context_ref=.intent-cli/issues/G25/review-context.md"
            ],
            IntentTaskCandidates =
            [
                "intent_ref=intents/intent-cli/means/auth.md;source=intent",
                "intent_ref=intents/intent-cli/specs/12-bug-fix-and-intent-repair.md;source=rule-spec"
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
        implementation_task_candidates: []
        intent_task_candidates: []
        clarification_required: false
        ready_to_launch: true
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugExecutionArtifactYaml.Deserialize(yaml));

        Assert.Contains("triage_ref", exception.Message, StringComparison.Ordinal);
    }
}
