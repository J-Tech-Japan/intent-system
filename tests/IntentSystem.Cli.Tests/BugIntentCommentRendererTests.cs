using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentCommentRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugIntentCommentRenderer.WriteSummary(
            writer,
            new BugIntentCommentArtifact
            {
                BugId = "BUG-123",
                IntentReviewRef = ".intent-cli/bugs/BUG-123.intent-review.yaml",
                CommentedExecutionUnit = "G41",
                ReviewCommentRef = ".intent-cli/reviews/G41.comment.json",
                CommentRef = "https://github.com/J-Tech-Japan/intent-system/pull/58#issuecomment-1",
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/58",
                ReadyToComment = true
            },
            ".intent-cli/bugs/BUG-123.intent-comment.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug intent-comment artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.intent-comment.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Commented execution unit: G41", output, StringComparison.Ordinal);
        Assert.Contains("Review comment ref: .intent-cli/reviews/G41.comment.json", output, StringComparison.Ordinal);
        Assert.Contains("Comment ref: https://github.com/J-Tech-Japan/intent-system/pull/58#issuecomment-1", output, StringComparison.Ordinal);
        Assert.Contains("Linked PR URL: https://github.com/J-Tech-Japan/intent-system/pull/58", output, StringComparison.Ordinal);
        Assert.Contains("Ready to comment: true", output, StringComparison.Ordinal);
    }
}
