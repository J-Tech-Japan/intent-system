using IntentSystem.Review.Models;

namespace IntentSystem.Review.Tests;

public sealed class ReviewContextMarkdownParserTests
{
    [Fact]
    public void Parse_GivenReviewContextMarkdown_ReadsDeterministicLists()
    {
        var parsed = ReviewContextMarkdownParser.Parse(CreateMarkdown(includeExpectedEvidence: true));

        Assert.Equal("G9", parsed.SourceExecutionUnit);
        Assert.Empty(parsed.AcceptanceCriteria);
        Assert.Equal(
            ["review run command が PR comment 投稿や closeout の責務へ広がっていない"],
            parsed.DeterministicReviewChecks);
        Assert.Equal(["dotnet test IntentSystem.sln", "review run command tests"], parsed.ExpectedEvidence);
    }

    [Fact]
    public void Parse_GivenMissingExpectedEvidenceSection_ReturnsEmptyList()
    {
        var parsed = ReviewContextMarkdownParser.Parse(CreateMarkdown(includeExpectedEvidence: false));

        Assert.Empty(parsed.ExpectedEvidence);
    }

    [Fact]
    public void Parse_GivenMissingRequiredSection_ThrowsInvalidOperationException()
    {
        var markdown = """
        # Execution Unit

        `G9`

        # Goal

        `intent-cli review run <execution-unit>` を working command にする。
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => ReviewContextMarkdownParser.Parse(markdown));

        Assert.Contains("Deterministic Review Checks", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateMarkdown(bool includeExpectedEvidence)
    {
        return includeExpectedEvidence
            ? """
            # Execution Unit

            `G9`

            # Goal

            `intent-cli review run <execution-unit>` を working command にする。

            # Parent References

            - [Intent CLI Surface](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/05-intent-cli-surface.md)

            # Deterministic Review Checks

            - review run command が PR comment 投稿や closeout の責務へ広がっていない

            # Expected Evidence

            - dotnet test IntentSystem.sln
            - review run command tests
            """
            : """
            # Execution Unit

            `G9`

            # Goal

            `intent-cli review run <execution-unit>` を working command にする。

            # Parent References

            - [Intent CLI Surface](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/05-intent-cli-surface.md)

            # Deterministic Review Checks

            - review run command が PR comment 投稿や closeout の責務へ広がっていない
            """;
    }
}
