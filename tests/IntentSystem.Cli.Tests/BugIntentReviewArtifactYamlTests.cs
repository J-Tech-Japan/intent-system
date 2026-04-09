using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentReviewArtifactYamlTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTripsDeterministically()
    {
        var artifact = new BugIntentReviewArtifact
        {
            BugId = "BUG-123",
            IntentSubmitRef = ".intent-cli/bugs/BUG-123.intent-submit.yaml",
            ReviewedExecutionUnit = "G41",
            ReviewRequestRef = ".intent-cli/reviews/G41.request.json",
            ReadyToReview = true
        };

        var roundTripped = BugIntentReviewArtifactYaml.Deserialize(BugIntentReviewArtifactYaml.Serialize(artifact));

        Assert.Equal(artifact, roundTripped);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_Throws()
    {
        var yaml = """
        bug_id: BUG-123
        intent_submit_ref: ".intent-cli/bugs/BUG-123.intent-submit.yaml"
        reviewed_execution_unit: null
        ready_to_review: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentReviewArtifactYaml.Deserialize(yaml));

        Assert.Contains("review_request_ref", exception.Message, StringComparison.Ordinal);
    }
}
