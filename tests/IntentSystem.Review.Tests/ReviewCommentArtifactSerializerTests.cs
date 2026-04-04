using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;

namespace IntentSystem.Review.Tests;

public sealed class ReviewCommentArtifactSerializerTests
{
    [Fact]
    public void Serialize_GivenArtifact_WritesCurrentFieldShape()
    {
        var serialized = ReviewCommentArtifactSerializer.Serialize(CreateArtifact());

        Assert.Contains("\"execution_unit\": \"G10\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"review_request_ref\": \".intent-cli/reviews/G10.request.json\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"linked_pr\": \"https://github.com/J-Tech-Japan/intent-system/pull/46\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"comment_ref\": \"https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"body_path\": \"/tmp/G10-comment.md\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "execution_unit": "G10",
          "review_request_ref": ".intent-cli/reviews/G10.request.json",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/46",
          "comment_ref": "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1"
        }
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => ReviewCommentArtifactSerializer.Deserialize(json));

        Assert.Contains("body_path", exception.Message, StringComparison.Ordinal);
    }

    private static ReviewCommentArtifact CreateArtifact()
    {
        return new ReviewCommentArtifact
        {
            ExecutionUnit = "G10",
            ReviewRequestRef = ".intent-cli/reviews/G10.request.json",
            LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/46",
            CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1",
            BodyPath = "/tmp/G10-comment.md"
        };
    }
}
