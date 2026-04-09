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
            LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/58",
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
        review_request_ref: null
        ready_to_review: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentReviewArtifactYaml.Deserialize(yaml));

        Assert.Contains("linked_pr_url", exception.Message, StringComparison.Ordinal);
    }
}
