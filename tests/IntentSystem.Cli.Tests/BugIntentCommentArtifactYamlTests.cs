using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentCommentArtifactYamlTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenArtifact_RoundTripsDeterministically()
    {
        var artifact = new BugIntentCommentArtifact
        {
            BugId = "BUG-123",
            IntentReviewRef = ".intent-cli/bugs/BUG-123.intent-review.yaml",
            CommentedExecutionUnit = "G41",
            ReviewCommentRef = ".intent-cli/reviews/G41.comment.json",
            CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/58#issuecomment-1",
            LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/58",
            ReadyToComment = true
        };

        var roundTripped = BugIntentCommentArtifactYaml.Deserialize(BugIntentCommentArtifactYaml.Serialize(artifact));

        Assert.Equal(artifact, roundTripped);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_Throws()
    {
        var yaml = """
        bug_id: BUG-123
        intent_review_ref: ".intent-cli/bugs/BUG-123.intent-review.yaml"
        commented_execution_unit: null
        review_comment_ref: null
        comment_ref: null
        ready_to_comment: false
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => BugIntentCommentArtifactYaml.Deserialize(yaml));

        Assert.Contains("linked_pr_url", exception.Message, StringComparison.Ordinal);
    }
}
