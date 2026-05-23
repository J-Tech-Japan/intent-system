using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G389: tests for the ambiguous multi-packet contract detector. A normal
/// single-unit issue (even one whose Related Links cite many prior G-numbers)
/// must NOT be flagged; an issue declaring multiple primary execution-unit
/// identities (title or H1 headings) must be.
/// </summary>
public sealed class IssueContractAmbiguityAnalyzerTests
{
    [Fact]
    public void Analyze_SingleUnit_WithRelatedLinks_IsNotAmbiguous()
    {
        const string title = "G389 Enforce child worker terminal contracts";
        const string body =
            "# G389 Enforce child worker terminal contracts\n\n"
            + "## Goal\nMake completion enforce the contract.\n\n"
            + "## Related Links\n- G300 child worker commands.\n- G311 mandatory PR closing reference.\n- G388 branch resolution.\n";

        var result = IssueContractAmbiguityAnalyzer.Analyze(title, body);

        Assert.False(result.IsAmbiguous);
        Assert.Equal(new[] { "G389" }, result.PrimaryUnits);
        Assert.Equal(string.Empty, result.Diagnostic);
    }

    [Fact]
    public void Analyze_MultipleH1Units_IsAmbiguous()
    {
        const string title = "G390 + G391 bundle";
        const string body =
            "# G390 First packet\n\n## Goal\nA.\n\n"
            + "# G391 Second packet\n\n## Goal\nB.\n";

        var result = IssueContractAmbiguityAnalyzer.Analyze(title, body);

        Assert.True(result.IsAmbiguous);
        Assert.Contains("G390", result.PrimaryUnits);
        Assert.Contains("G391", result.PrimaryUnits);
        Assert.Contains("multiple execution-unit", result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_TitleUnitDiffersFromSingleH1_IsAmbiguous()
    {
        // Title says one unit, body H1 declares a different one — ambiguous.
        var result = IssueContractAmbiguityAnalyzer.Analyze(
            "G390 something",
            "# G391 different unit\n\n## Goal\nx.\n");

        Assert.True(result.IsAmbiguous);
        Assert.Equal(new[] { "G390", "G391" }, result.PrimaryUnits);
    }

    [Fact]
    public void Analyze_RelatedLinksWithManyGNumbers_StillSingleUnit()
    {
        // Many G-numbers as bullets must not trip the detector.
        const string body =
            "# G347 base branch policy propagation\n\n"
            + "## Related Links\n- G300\n- G305\n- G311\n- G346\n- G350\n- G362\n- G372\n- G388\n";

        var result = IssueContractAmbiguityAnalyzer.Analyze("G347 base branch policy", body);

        Assert.False(result.IsAmbiguous);
        Assert.Equal(new[] { "G347" }, result.PrimaryUnits);
    }

    [Fact]
    public void Analyze_NoUnitIdentity_IsNotAmbiguous()
    {
        var result = IssueContractAmbiguityAnalyzer.Analyze("Fix a typo", "# Fix a typo\n\nNo G-number here.\n");

        Assert.False(result.IsAmbiguous);
        Assert.Empty(result.PrimaryUnits);
    }
}
