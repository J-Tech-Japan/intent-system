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
            IntentRepairRef = ".intent-cli/bugs/BUG-123.intent-repair.yaml",
            AllocatedExecutionUnit = "G41",
            LinkedIssueUrl = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53",
            ParentRepairTargets =
            [
                "intent:intents/intent-cli/means/auth.md",
                "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ],
            GeneratedPacketPaths =
            [
                ".intent-cli/issues/G41/implementation.md",
                ".intent-cli/issues/G41/review-context.md",
                ".intent-cli/issues/G41/packet.yaml"
            ],
            WasEnqueued = true
        };

        var roundTripped = BugIntentEnqueueArtifactYaml.Deserialize(BugIntentEnqueueArtifactYaml.Serialize(artifact));

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.IntentIssueRef, roundTripped.IntentIssueRef);
        Assert.Equal(artifact.IntentRepairRef, roundTripped.IntentRepairRef);
        Assert.Equal(artifact.AllocatedExecutionUnit, roundTripped.AllocatedExecutionUnit);
        Assert.Equal(artifact.LinkedIssueUrl, roundTripped.LinkedIssueUrl);
        Assert.Equal(artifact.ParentRepairTargets, roundTripped.ParentRepairTargets);
        Assert.Equal(artifact.GeneratedPacketPaths, roundTripped.GeneratedPacketPaths);
        Assert.Equal(artifact.WasEnqueued, roundTripped.WasEnqueued);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_Throws()
    {
        var yaml = """
        bug_id: BUG-123
        intent_issue_ref: ".intent-cli/bugs/BUG-123.intent-issue.yaml"
        intent_repair_ref: ".intent-cli/bugs/BUG-123.intent-repair.yaml"
        allocated_execution_unit: null
        linked_issue_url: null
        parent_repair_targets: []
        was_enqueued: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentEnqueueArtifactYaml.Deserialize(yaml));

        Assert.Contains("generated_packet_paths", exception.Message, StringComparison.Ordinal);
    }
}
