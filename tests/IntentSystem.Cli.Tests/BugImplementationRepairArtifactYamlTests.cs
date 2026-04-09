using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugImplementationRepairArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsBugImplementationRepairArtifact()
    {
        var artifact = new BugImplementationRepairArtifact
        {
            BugId = "BUG-123",
            ExecutionRef = ".intent-cli/bugs/BUG-123.plan.yaml",
            ImplementationTaskCandidates = ["G25"],
            ImplementationRepairTargets = [".intent-cli/issues/G25/packet.yaml"],
            SuggestedIssueTitle = "Implementation repair: OAuth callback loop (BUG-123)",
            SuggestedGoal = "Repair child implementation targets for 'OAuth callback loop' (BUG-123) using .intent-cli/bugs/BUG-123.plan.yaml: .intent-cli/issues/G25/packet.yaml",
            ReadyToIssueCut = true
        };

        var yaml = BugImplementationRepairArtifactYaml.Serialize(artifact);
        var roundTripped = BugImplementationRepairArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.ExecutionRef, roundTripped.ExecutionRef);
        Assert.Equal(artifact.ImplementationTaskCandidates, roundTripped.ImplementationTaskCandidates);
        Assert.Equal(artifact.ImplementationRepairTargets, roundTripped.ImplementationRepairTargets);
        Assert.Equal(artifact.SuggestedIssueTitle, roundTripped.SuggestedIssueTitle);
        Assert.Equal(artifact.SuggestedGoal, roundTripped.SuggestedGoal);
        Assert.Equal(artifact.ReadyToIssueCut, roundTripped.ReadyToIssueCut);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var yaml = """
        bug_id: BUG-123
        execution_ref: ".intent-cli/bugs/BUG-123.plan.yaml"
        implementation_task_candidates: []
        implementation_repair_targets: []
        suggested_issue_title: "Implementation repair: OAuth callback loop (BUG-123)"
        ready_to_issue_cut: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugImplementationRepairArtifactYaml.Deserialize(yaml));

        Assert.Contains("suggested_goal", exception.Message, StringComparison.Ordinal);
    }
}
