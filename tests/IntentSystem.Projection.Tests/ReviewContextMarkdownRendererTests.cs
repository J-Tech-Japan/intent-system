using IntentSystem.Projection.Models;
using IntentSystem.Projection.Rendering;

namespace IntentSystem.Projection.Tests;

public sealed class ReviewContextMarkdownRendererTests
{
    [Fact]
    public void Render_GivenPacket_StartsWithReviewContextH1()
    {
        var markdown = ReviewContextMarkdownRenderer.Render(CreatePacket());

        Assert.StartsWith("# Review Context", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_IncludesExecutionUnitInHeader()
    {
        var markdown = ReviewContextMarkdownRenderer.Render(CreatePacket());

        Assert.Contains("**execution-unit**: `A2`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_IncludesAllSections()
    {
        var markdown = ReviewContextMarkdownRenderer.Render(CreatePacket());

        Assert.Contains("## Parent Intent Root", markdown, StringComparison.Ordinal);
        Assert.Contains("## Intent References", markdown, StringComparison.Ordinal);
        Assert.Contains("## Rules And Specs", markdown, StringComparison.Ordinal);
        Assert.Contains("## Acceptance Criteria", markdown, StringComparison.Ordinal);
        Assert.Contains("## Deterministic Review Checks", markdown, StringComparison.Ordinal);
        Assert.Contains("## Clarification Return Path", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_ListsAcceptanceCriteriaAsBullets()
    {
        var markdown = ReviewContextMarkdownRenderer.Render(CreatePacket());

        Assert.Contains("- generates implementation.md", markdown, StringComparison.Ordinal);
        Assert.Contains("- generates packet.yaml", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenSamePacket_ProducesIdenticalOutput()
    {
        var packet = CreatePacket();

        var first = ReviewContextMarkdownRenderer.Render(packet);
        var second = ReviewContextMarkdownRenderer.Render(packet);

        Assert.Equal(first, second);
    }

    private static ReviewContextPacket CreatePacket()
    {
        return new ReviewContextPacket
        {
            SourceExecutionUnit = "A2",
            ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md",
            IntentReferences = ["intents/intent-cli/intent-tree/00-map.md"],
            RulesAndSpecs = ["intents/rules/issue-projection-format.md"],
            AcceptanceCriteria = ["generates implementation.md", "generates packet.yaml"],
            DeterministicReviewChecks =
            [
                "packet generator does not carry queue policy",
                "artifact path is stable per execution unit"
            ],
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md"
        };
    }
}
