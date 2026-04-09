using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentEnqueueArtifactYamlTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTripsDeterministically()
    {
        var artifact = new BugIntentEnqueueArtifact
        {
            BugId = "BUG-123",
            IntentIssueRef = ".intent-cli/bugs/BUG-123.intent-issue.yaml",
            AllocatedExecutionUnit = "G41",
            LinkedIssueUrl = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53",
            LinkedIssueNumber = 53,
            PacketPaths =
            [
                ".intent-cli/issues/G41/implementation.md",
                ".intent-cli/issues/G41/review-context.md",
                ".intent-cli/issues/G41/packet.yaml"
            ],
            ReadyToEnqueue = true
        };

        var roundTripped = BugIntentEnqueueArtifactYaml.Deserialize(BugIntentEnqueueArtifactYaml.Serialize(artifact));

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.IntentIssueRef, roundTripped.IntentIssueRef);
        Assert.Equal(artifact.AllocatedExecutionUnit, roundTripped.AllocatedExecutionUnit);
        Assert.Equal(artifact.LinkedIssueUrl, roundTripped.LinkedIssueUrl);
        Assert.Equal(artifact.LinkedIssueNumber, roundTripped.LinkedIssueNumber);
        Assert.Equal(artifact.PacketPaths, roundTripped.PacketPaths);
        Assert.Equal(artifact.ReadyToEnqueue, roundTripped.ReadyToEnqueue);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_Throws()
    {
        var yaml = """
        bug_id: BUG-123
        intent_issue_ref: ".intent-cli/bugs/BUG-123.intent-issue.yaml"
        allocated_execution_unit: null
        linked_issue_url: null
        linked_issue_number: null
        ready_to_enqueue: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentEnqueueArtifactYaml.Deserialize(yaml));

        Assert.Contains("packet_paths", exception.Message, StringComparison.Ordinal);
    }
}
