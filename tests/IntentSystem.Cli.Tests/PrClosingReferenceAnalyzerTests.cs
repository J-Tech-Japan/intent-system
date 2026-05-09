using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class PrClosingReferenceAnalyzerTests
{
    private const string Repo = "J-Tech-Japan/intent-system";

    [Theory]
    [InlineData("Closes #725")]
    [InlineData("closes #725")]
    [InlineData("CLOSES #725")]
    [InlineData("Fix #725")]
    [InlineData("fixes #725")]
    [InlineData("Fixed #725")]
    [InlineData("Resolve #725")]
    [InlineData("resolves #725")]
    [InlineData("Resolved #725")]
    [InlineData("Close #725")]
    [InlineData("closed #725")]
    public void Analyze_RecognizedKeyword_ReturnsValid(string body)
    {
        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationValid, result.Classification);
        Assert.True(result.Ok);
        Assert.Single(result.References);
        Assert.Equal(725, result.References[0].IssueNumber);
        Assert.Empty(result.Remediation);
    }

    [Fact]
    public void Analyze_KeywordWithColonSeparator_ReturnsValid()
    {
        // GitHub also recognizes `Closes: #725` with a colon.
        var result = PrClosingReferenceAnalyzer.Analyze("Closes: #725", sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationValid, result.Classification);
    }

    [Fact]
    public void Analyze_KeywordEmbeddedInBody_ReturnsValid()
    {
        var body = """
            ## Summary
            - Implement G311 closing-reference validator.

            ## Test plan
            - [x] Unit tests pass.

            Closes #725
            """;

        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationValid, result.Classification);
    }

    [Fact]
    public void Analyze_BareIssueLink_DoesNotCount_ReturnsMissing()
    {
        // "see #725" or just "#725" is NOT a closing reference per GitHub's rules.
        var body = """
            ## Summary
            - Refactor only; see #725 for context.
            """;

        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationMissing, result.Classification);
        Assert.False(result.Ok);
        Assert.Empty(result.References);
        Assert.Contains(result.Remediation, step => step.Contains("Closes #725", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_EmptyBody_ReturnsMissing()
    {
        var result = PrClosingReferenceAnalyzer.Analyze(string.Empty, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationMissing, result.Classification);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Analyze_NullBody_ReturnsMissing()
    {
        var result = PrClosingReferenceAnalyzer.Analyze(prBody: null, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationMissing, result.Classification);
    }

    [Fact]
    public void Analyze_WrongIssue_ReturnsWrongIssueClassification()
    {
        var result = PrClosingReferenceAnalyzer.Analyze("Closes #999", sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationWrongIssue, result.Classification);
        Assert.False(result.Ok);
        Assert.Single(result.References);
        Assert.Equal(999, result.References[0].IssueNumber);
        Assert.Contains(result.Remediation, s => s.Contains("Closes #725", StringComparison.Ordinal));
        Assert.Contains("#999", result.Summary, StringComparison.Ordinal);
        Assert.Contains("#725", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_MultipleDistinctClosingReferences_ReturnsMultipleIssues()
    {
        var body = "Closes #725. Also fixes #800.";
        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationMultipleIssues, result.Classification);
        Assert.False(result.Ok);
        Assert.Equal(2, result.References.Count);
        // Refuses to silently pick one even when one of them happens to match the source.
        Assert.Contains(result.Remediation, s => s.Contains("Refuse", StringComparison.OrdinalIgnoreCase) || s.Contains("refuse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DuplicateSameNumberKeywords_StillValid()
    {
        // Two textual mentions of the same source issue — GitHub treats this
        // as a single closing reference, so we should not classify as ambiguous.
        var body = "Closes #725. (Also see fixes #725 in tests.)";
        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationValid, result.Classification);
        Assert.True(result.References.Count >= 1);
    }

    [Fact]
    public void Analyze_CrossRepoSameOwnerRepo_ReturnsValid()
    {
        var body = "Closes J-Tech-Japan/intent-system#725";
        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationValid, result.Classification);
    }

    [Fact]
    public void Analyze_CrossRepoDifferentRepo_IsExcluded_ReturnsMissing()
    {
        // Closing references targeting a different repo do NOT close issues
        // in this repo on GitHub, so they must not satisfy the contract.
        var body = "Closes other-org/other-repo#725";
        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationMissing, result.Classification);
    }

    [Fact]
    public void Analyze_KeywordWithoutHash_DoesNotMatch()
    {
        // "fix the bug" should not match — only `<keyword> #N` does.
        var body = "fix the test for issue 725";
        var result = PrClosingReferenceAnalyzer.Analyze(body, sourceIssueNumber: 725, repo: Repo);

        Assert.Equal(PrClosingReferenceAnalyzer.ClassificationMissing, result.Classification);
    }

    [Fact]
    public void Analyze_RejectsZeroOrNegativeSourceIssue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PrClosingReferenceAnalyzer.Analyze("Closes #1", sourceIssueNumber: 0, repo: Repo));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PrClosingReferenceAnalyzer.Analyze("Closes #1", sourceIssueNumber: -1, repo: Repo));
    }

    [Fact]
    public void Analyze_RejectsEmptyRepo()
    {
        Assert.Throws<ArgumentException>(() =>
            PrClosingReferenceAnalyzer.Analyze("Closes #1", sourceIssueNumber: 1, repo: ""));
    }
}
