using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G527: focused coverage for <see cref="PreparedPacketYamlScalarParser"/>'s
/// quote-handling — apostrophes inside double-quoted values must parse as
/// literal content (standard YAML behavior), while genuinely unbalanced
/// quotes still fail closed with a line number and quoting guidance.
/// </summary>
public sealed class PreparedPacketYamlScalarParserTests
{
    [Fact]
    public void Parse_DoubleQuotedValueWithApostrophe_ParsesAsLiteralContent()
    {
        // 2026-07-10 field incident regression: a correctly double-quoted
        // value containing an apostrophe must not be rejected.
        var fields = PreparedPacketYamlScalarParser.Parse(
            "placement_rationale: \"This is Sekiban's core boundary and it's the right place.\"\n");

        Assert.Equal(
            "This is Sekiban's core boundary and it's the right place.",
            fields["placement_rationale"]);
    }

    [Fact]
    public void Parse_DoubleQuotedValueWithApostrophe_NestedKey_ParsesAsLiteralContent()
    {
        // The second 2026-07-10 refusal was on a nested key
        // (closeout_learning.expected).
        var fields = PreparedPacketYamlScalarParser.Parse(
            "closeout_learning:\n  expected: \"Author's summary won't need rewording anymore.\"\n");

        Assert.Equal(
            "Author's summary won't need rewording anymore.",
            fields["closeout_learning.expected"]);
    }

    [Fact]
    public void Parse_DoubleQuotedValueWithMultipleApostrophes_ParsesAsLiteralContent()
    {
        var fields = PreparedPacketYamlScalarParser.Parse(
            "note: \"it's, don't, won't, can't\"\n");

        Assert.Equal("it's, don't, won't, can't", fields["note"]);
    }

    [Fact]
    public void Parse_UnquotedPlainScalarWithApostrophe_ParsesAsLiteralContent()
    {
        // Standard YAML gives quote characters no special meaning inside an
        // unquoted plain scalar — an apostrophe here must not trigger the
        // single-quote balance check either.
        var fields = PreparedPacketYamlScalarParser.Parse(
            "note: it's a plain unquoted value\n");

        Assert.Equal("it's a plain unquoted value", fields["note"]);
    }

    [Fact]
    public void Parse_SingleQuotedValueWithDoubledEscape_DoesNotThrow()
    {
        // Preserved behavior: single-quoted values using the YAML ''
        // doubled-single-quote escape convention must keep parsing (this
        // lightweight reader does not unescape '' to ' — it only strips the
        // outer delimiters — so the doubled quote survives in the output,
        // matching pre-G527 behavior).
        var fields = PreparedPacketYamlScalarParser.Parse(
            "title: 'it''s single-quoted'\n");

        Assert.Equal("it''s single-quoted", fields["title"]);
    }

    [Fact]
    public void Parse_SingleQuotedValueWithDoubleQuoteInside_ParsesAsLiteralContent()
    {
        // Symmetry: a double quote inside a single-quoted scalar is literal
        // content and must never trigger the double-quote balance check.
        var fields = PreparedPacketYamlScalarParser.Parse(
            "title: 'she said \"hello\"'\n");

        Assert.Equal("she said \"hello\"", fields["title"]);
    }

    [Fact]
    public void Parse_UnquotedPlainScalar_StillParsesUnchanged()
    {
        var fields = PreparedPacketYamlScalarParser.Parse(
            "target_repo: J-Tech-Creations/Zero4Racer\n");

        Assert.Equal("J-Tech-Creations/Zero4Racer", fields["target_repo"]);
    }

    [Fact]
    public void Parse_GenuinelyUnterminatedDoubleQuote_StillThrowsWithLineNumberAndGuidance()
    {
        // 2026-07-10 incident's ORIGINAL shape must still fail: a
        // double-quoted value with no closing quote at all.
        var exception = Assert.Throws<FormatException>(() =>
            PreparedPacketYamlScalarParser.Parse(
                "implementation_issue_packet:\n  source_execution_unit: Z4R-G3\n  target_repo: \"J-Tech-Creations/Zero4Racer\n"));

        Assert.Contains("line 3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unbalanced double quote", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DoubleQuotedValueWithStrayInteriorDoubleQuote_StillThrows()
    {
        // A genuinely ambiguous double-quoted scalar (an interior
        // unescaped double quote before the true end) must still fail
        // closed — this lightweight reader has no `\"` escape support.
        var exception = Assert.Throws<FormatException>(() =>
            PreparedPacketYamlScalarParser.Parse(
                "title: \"broken \"quote\" here\"\n"));

        Assert.Contains("unbalanced double quote", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_SingleQuotedValueWithGenuinelyUnbalancedQuote_StillThrows()
    {
        var exception = Assert.Throws<FormatException>(() =>
            PreparedPacketYamlScalarParser.Parse(
                "title: 'it's broken\n"));

        Assert.Contains("unbalanced single quote", exception.Message, StringComparison.Ordinal);
    }
}
