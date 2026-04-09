using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentStartArtifactYamlTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTripsDeterministically()
    {
        var artifact = new BugIntentStartArtifact
        {
            BugId = "BUG-123",
            IntentEnqueueRef = ".intent-cli/bugs/BUG-123.intent-enqueue.yaml",
            StartedExecutionUnit = "G41",
            WorktreePath = "/tmp/repo/.intent-cli/worktrees/G41",
            BranchName = "issue-53-g41",
            ReadyToStart = true
        };

        var roundTripped = BugIntentStartArtifactYaml.Deserialize(BugIntentStartArtifactYaml.Serialize(artifact));

        Assert.Equal(artifact, roundTripped);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_Throws()
    {
        var yaml = """
        bug_id: BUG-123
        intent_enqueue_ref: ".intent-cli/bugs/BUG-123.intent-enqueue.yaml"
        started_execution_unit: null
        worktree_path: null
        ready_to_start: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentStartArtifactYaml.Deserialize(yaml));

        Assert.Contains("branch_name", exception.Message, StringComparison.Ordinal);
    }
}
