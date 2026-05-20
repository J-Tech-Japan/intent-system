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

    [Fact]
    public void Parse_GivenMissingExecutionUnitSectionAndNoFallback_ThrowsInvalidOperationException()
    {
        // Backward compatibility: callers with no canonical fallback still
        // get the original "section is required" failure.
        var markdown = """
        # Deterministic Review Checks

        - review run resolves the execution unit
        """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => ReviewContextMarkdownParser.Parse(markdown));

        Assert.Contains("Execution Unit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_GivenMinimalReviewContextWithFallback_ResolvesExecutionUnitFromFallback()
    {
        // G373 / PR #844 shape: a newer minimal review-context.md omits the
        // legacy `# Execution Unit` section and relies on packet.yaml as the
        // canonical identity. With the caller-supplied fallback, parsing
        // resolves the unit instead of throwing.
        var markdown = """
        # Deterministic Review Checks

        - review run resolves the execution unit from packet.yaml
        """;

        var parsed = ReviewContextMarkdownParser.Parse(markdown, fallbackExecutionUnit: "G369");

        Assert.Equal("G369", parsed.SourceExecutionUnit);
        Assert.Equal(
            ["review run resolves the execution unit from packet.yaml"],
            parsed.DeterministicReviewChecks);
    }

    [Fact]
    public void Parse_GivenExecutionUnitSectionAndFallback_PrefersTheDeclaredSection()
    {
        // When the markdown DOES declare an execution unit, the declared
        // value wins over the fallback so the downstream queue-item match
        // guard can still detect a genuinely mismatched review-context.
        var markdown = """
        # Execution Unit

        `G9`

        # Deterministic Review Checks

        - declared section is authoritative when present
        """;

        var parsed = ReviewContextMarkdownParser.Parse(markdown, fallbackExecutionUnit: "G369");

        Assert.Equal("G9", parsed.SourceExecutionUnit);
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
