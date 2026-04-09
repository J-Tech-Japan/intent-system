using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentIssueArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsBugIntentIssueArtifact()
    {
        var artifact = new BugIntentIssueArtifact
        {
            BugId = "BUG-123",
            IntentRepairRef = ".intent-cli/bugs/BUG-123.intent-repair.yaml",
            CreatedIssueTitle = "Intent repair: OAuth callback loop (BUG-123)",
            CreatedIssueUrl = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53",
            CreatedIssueNumber = 53,
            ParentRepairTargets =
            [
                "intent:intents/intent-cli/means/auth.md",
                "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ]
        };

        var yaml = BugIntentIssueArtifactYaml.Serialize(artifact);
        var roundTripped = BugIntentIssueArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.IntentRepairRef, roundTripped.IntentRepairRef);
        Assert.Equal(artifact.CreatedIssueTitle, roundTripped.CreatedIssueTitle);
        Assert.Equal(artifact.CreatedIssueUrl, roundTripped.CreatedIssueUrl);
        Assert.Equal(artifact.CreatedIssueNumber, roundTripped.CreatedIssueNumber);
        Assert.Equal(artifact.ParentRepairTargets, roundTripped.ParentRepairTargets);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var yaml = """
        bug_id: BUG-123
        intent_repair_ref: ".intent-cli/bugs/BUG-123.intent-repair.yaml"
        created_issue_title: "Intent repair: OAuth callback loop (BUG-123)"
        created_issue_url: null
        parent_repair_targets: []
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentIssueArtifactYaml.Deserialize(yaml));

        Assert.Contains("created_issue_number", exception.Message, StringComparison.Ordinal);
    }
}
