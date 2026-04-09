using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugImplementationIssueArtifactYamlTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsBugImplementationIssueArtifact()
    {
        var artifact = new BugImplementationIssueArtifact
        {
            BugId = "BUG-123",
            ImplementationRepairRef = ".intent-cli/bugs/BUG-123.implementation-repair.yaml",
            CreatedIssueTitle = "Implementation repair: OAuth callback loop (BUG-123)",
            CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/53",
            CreatedIssueNumber = 53,
            ImplementationRepairTargets = [".intent-cli/issues/G25/packet.yaml"]
        };

        var yaml = BugImplementationIssueArtifactYaml.Serialize(artifact);
        var roundTripped = BugImplementationIssueArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.BugId, roundTripped.BugId);
        Assert.Equal(artifact.ImplementationRepairRef, roundTripped.ImplementationRepairRef);
        Assert.Equal(artifact.CreatedIssueTitle, roundTripped.CreatedIssueTitle);
        Assert.Equal(artifact.CreatedIssueUrl, roundTripped.CreatedIssueUrl);
        Assert.Equal(artifact.CreatedIssueNumber, roundTripped.CreatedIssueNumber);
        Assert.Equal(artifact.ImplementationRepairTargets, roundTripped.ImplementationRepairTargets);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var yaml = """
        bug_id: BUG-123
        implementation_repair_ref: ".intent-cli/bugs/BUG-123.implementation-repair.yaml"
        created_issue_title: "Implementation repair: OAuth callback loop (BUG-123)"
        created_issue_url: null
        implementation_repair_targets: []
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugImplementationIssueArtifactYaml.Deserialize(yaml));

        Assert.Contains("created_issue_number", exception.Message, StringComparison.Ordinal);
    }
}
