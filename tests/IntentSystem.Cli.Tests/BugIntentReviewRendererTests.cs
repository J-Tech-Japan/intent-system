using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentReviewRendererTests
{
    [Fact]
    public void WriteSummary_GivenArtifact_RendersDeterministicSummary()
    {
        using var writer = new StringWriter();

        BugIntentReviewRenderer.WriteSummary(
            writer,
            new BugIntentReviewArtifact
            {
                BugId = "BUG-123",
                IntentSubmitRef = ".intent-cli/bugs/BUG-123.intent-submit.yaml",
                ReviewedExecutionUnit = "G41",
                ReviewRequestRef = ".intent-cli/reviews/G41.request.json",
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/58",
                ReadyToReview = true
            },
            ".intent-cli/bugs/BUG-123.intent-review.yaml");

        var output = writer.ToString();
        Assert.Contains("Bug intent-review artifact generated for 'BUG-123'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: .intent-cli/bugs/BUG-123.intent-review.yaml", output, StringComparison.Ordinal);
        Assert.Contains("Reviewed execution unit: G41", output, StringComparison.Ordinal);
        Assert.Contains("Review request ref: .intent-cli/reviews/G41.request.json", output, StringComparison.Ordinal);
        Assert.Contains("Linked PR URL: https://github.com/J-Tech-Japan/intent-system/pull/58", output, StringComparison.Ordinal);
        Assert.Contains("Ready to review: true", output, StringComparison.Ordinal);
    }
}
