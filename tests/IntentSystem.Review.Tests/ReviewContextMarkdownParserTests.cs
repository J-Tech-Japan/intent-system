using IntentSystem.Review.Models;

namespace IntentSystem.Review.Tests;

public sealed class ReviewContextMarkdownParserTests
{
    [Fact]
    public void Parse_GivenReviewContextMarkdown_ReadsDeterministicLists()
    {
        var parsed = ReviewContextMarkdownParser.Parse(CreateMarkdown(includeExpectedEvidence: true));

        Assert.Equal("G9", parsed.SourceExecutionUnit);
        Assert.Equal(["review request artifact is generated"], parsed.AcceptanceCriteria);
        Assert.Equal(
            ["input path stays under .intent-cli/issues/<execution-unit>/"],
            parsed.DeterministicReviewChecks);
        Assert.Equal(["dotnet test IntentSystem.sln"], parsed.ExpectedEvidence);
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
        # Review Context

        - **execution-unit**: `G9`

        ## Acceptance Criteria

        - review request artifact is generated
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => ReviewContextMarkdownParser.Parse(markdown));

        Assert.Contains("Deterministic Review Checks", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateMarkdown(bool includeExpectedEvidence)
    {
        return includeExpectedEvidence
            ? """
            # Review Context

            - **execution-unit**: `G9`

            ## Acceptance Criteria

            - review request artifact is generated

            ## Deterministic Review Checks

            - input path stays under .intent-cli/issues/<execution-unit>/

            ## Expected Evidence

            - dotnet test IntentSystem.sln
            """
            : """
            # Review Context

            - **execution-unit**: `G9`

            ## Acceptance Criteria

            - review request artifact is generated

            ## Deterministic Review Checks

            - input path stays under .intent-cli/issues/<execution-unit>/
            """;
    }
}
