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
        Assert.Null(roundTripped.RepairExecutionUnit);
        Assert.Null(roundTripped.RepairIssueNumber);
        Assert.Null(roundTripped.RepairIssueUrl);
        Assert.Null(roundTripped.RecordedBy);
        Assert.Null(roundTripped.Note);
        Assert.Null(roundTripped.RecordedAt);
        Assert.DoesNotContain("repair_execution_unit", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("recorded_at", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeDeserialize_GivenRecordedRepairDetails_RoundTripsOptionalFields()
    {
        var recordedAt = new DateTimeOffset(2026, 9, 2, 12, 30, 0, TimeSpan.Zero);
        var artifact = new BugImplementationRepairArtifact
        {
            BugId = "BUG-1706",
            ExecutionRef = ".intent-cli/bugs/BUG-1706.plan.yaml",
            ImplementationTaskCandidates = ["G782"],
            ImplementationRepairTargets = [".intent-cli/issues/G782/packet.yaml"],
            SuggestedIssueTitle = "Implementation repair: packet links (BUG-1706)",
            SuggestedGoal = "Record the repair issue.",
            ReadyToIssueCut = true,
            RepairExecutionUnit = "G782",
            RepairIssueNumber = 1706,
            RepairIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1706",
            RecordedBy = "implementation",
            Note = "ready for review",
            RecordedAt = recordedAt
        };

        var yaml = BugImplementationRepairArtifactYaml.Serialize(artifact);
        var roundTripped = BugImplementationRepairArtifactYaml.Deserialize(yaml);

        Assert.Equal("G782", roundTripped.RepairExecutionUnit);
        Assert.Equal(1706, roundTripped.RepairIssueNumber);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/1706", roundTripped.RepairIssueUrl);
        Assert.Equal("implementation", roundTripped.RecordedBy);
        Assert.Equal("ready for review", roundTripped.Note);
        Assert.Equal(recordedAt, roundTripped.RecordedAt);
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
