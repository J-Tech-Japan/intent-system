using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;

namespace IntentSystem.Review.Tests;

public sealed class ReviewRequestSerializerTests
{
    [Fact]
    public void Serialize_GivenRequest_WritesCurrentFieldShape()
    {
        var serialized = ReviewRequestSerializer.Serialize(CreateRequest());

        Assert.Contains("\"execution_unit\": \"G9\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"review_context_ref\": \".intent-cli/issues/G9/review-context.md\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"linked_pr\": \"https://github.com/J-Tech-Japan/intent-system/pull/44\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"deterministic_review_checks\": [", serialized, StringComparison.Ordinal);
        Assert.Contains("\"acceptance_criteria\": [", serialized, StringComparison.Ordinal);
        Assert.Contains("\"expected_evidence\": [", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "execution_unit": "G9",
          "review_context_ref": ".intent-cli/issues/G9/review-context.md",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/44",
          "deterministic_review_checks": [],
          "acceptance_criteria": []
        }
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => ReviewRequestSerializer.Deserialize(json));

        Assert.Contains("expected_evidence", exception.Message, StringComparison.Ordinal);
    }

    private static ReviewRequest CreateRequest()
    {
        return new ReviewRequest
        {
            ExecutionUnit = "G9",
            ReviewContextRef = ".intent-cli/issues/G9/review-context.md",
            LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/44",
            DeterministicReviewChecks = ["input paths stay canonical"],
            AcceptanceCriteria = ["review request artifact is generated"],
            ExpectedEvidence = ["dotnet test IntentSystem.sln"]
        };
    }
}
