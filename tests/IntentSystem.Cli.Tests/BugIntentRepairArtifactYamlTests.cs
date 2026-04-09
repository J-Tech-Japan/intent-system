using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentRepairArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsBugIntentRepairArtifact()
    {
        var artifact = new BugIntentRepairArtifact
        {
            BugId = "BUG-123",
            ExecutionRef = ".intent-cli/bugs/BUG-123.plan.yaml",
            IntentTaskCandidates =
            [
                "intents/intent-cli/means/auth.md",
                "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ],
            ParentRepairTargets =
            [
                "intent:intents/intent-cli/means/auth.md",
                "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ],
            SuggestedIssueTitle = "Intent repair: OAuth callback loop (BUG-123)",
            SuggestedGoal = "Repair parent intent targets for 'OAuth callback loop' (BUG-123) using .intent-cli/bugs/BUG-123.plan.yaml: intent:intents/intent-cli/means/auth.md, rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
            ReadyToIssueCut = true
        };

        var yaml = BugIntentRepairArtifactYaml.Serialize(artifact);
        var roundTripped = BugIntentRepairArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.ExecutionRef, roundTripped.ExecutionRef);
        Assert.Equal(artifact.IntentTaskCandidates, roundTripped.IntentTaskCandidates);
        Assert.Equal(artifact.ParentRepairTargets, roundTripped.ParentRepairTargets);
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
        intent_task_candidates: []
        parent_repair_targets: []
        suggested_issue_title: "Intent repair: OAuth callback loop (BUG-123)"
        ready_to_issue_cut: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentRepairArtifactYaml.Deserialize(yaml));

        Assert.Contains("suggested_goal", exception.Message, StringComparison.Ordinal);
    }
}
