using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentSubmitArtifactYamlTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTripsDeterministically()
    {
        var artifact = new BugIntentSubmitArtifact
        {
            BugId = "BUG-123",
            IntentStartRef = ".intent-cli/bugs/BUG-123.intent-start.yaml",
            SubmittedExecutionUnit = "G41",
            LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/58",
            LinkedPrNumber = 58,
            ReadyToSubmit = true
        };

        var roundTripped = BugIntentSubmitArtifactYaml.Deserialize(BugIntentSubmitArtifactYaml.Serialize(artifact));

        Assert.Equal(artifact, roundTripped);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_Throws()
    {
        var yaml = """
        bug_id: BUG-123
        intent_start_ref: ".intent-cli/bugs/BUG-123.intent-start.yaml"
        submitted_execution_unit: null
        linked_pr_url: null
        ready_to_submit: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentSubmitArtifactYaml.Deserialize(yaml));

        Assert.Contains("linked_pr_number", exception.Message, StringComparison.Ordinal);
    }
}
