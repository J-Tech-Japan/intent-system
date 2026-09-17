using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G527/G841: quote-handling coverage now exercised through
/// <see cref="PacketYamlDocument"/> after retiring the regex reader.
/// </summary>
public sealed class PreparedPacketYamlScalarParserTests
{
    [Fact]
    public void Parse_DoubleQuotedValueWithApostrophe_ParsesAsLiteralContent()
    {
        var yaml = "placement_rationale: \"This is Sekiban's core boundary and it's the right place.\"\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal(
            "This is Sekiban's core boundary and it's the right place.",
            document!.Fields["placement_rationale"]);
    }

    [Fact]
    public void Parse_DoubleQuotedValueWithApostrophe_NestedKey_ParsesAsLiteralContent()
    {
        var yaml = "closeout_learning:\n  expected: \"Author's summary won't need rewording anymore.\"\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal(
            "Author's summary won't need rewording anymore.",
            document!.Fields["closeout_learning.expected"]);
    }

    [Fact]
    public void Parse_DoubleQuotedValueWithMultipleApostrophes_ParsesAsLiteralContent()
    {
        var yaml = "note: \"it's, don't, won't, can't\"\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("it's, don't, won't, can't", document!.Fields["note"]);
    }

    [Fact]
    public void Parse_UnquotedPlainScalarWithApostrophe_ParsesAsLiteralContent()
    {
        var yaml = "note: it's a plain unquoted value\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("it's a plain unquoted value", document!.Fields["note"]);
    }

    [Fact]
    public void Parse_SingleQuotedValueWithDoubledEscape_UnescapesToApostrophe()
    {
        // G527 asserted the old reader's limitation; G841 removed it.
        var yaml = "title: 'it''s single-quoted'\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("it's single-quoted", document!.Fields["title"]);
    }

    [Fact]
    public void Parse_SingleQuotedValueWithDoubleQuoteInside_ParsesAsLiteralContent()
    {
        var yaml = "title: 'she said \"hello\"'\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("she said \"hello\"", document!.Fields["title"]);
    }

    [Fact]
    public void Parse_UnquotedPlainScalar_StillParsesUnchanged()
    {
        var yaml = "target_repo: J-Tech-Creations/Zero4Racer\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("J-Tech-Creations/Zero4Racer", document!.Fields["target_repo"]);
    }

    [Fact]
    public void Parse_GenuinelyUnterminatedDoubleQuote_StillFailsWithLineNumber()
    {
        var yaml = "implementation_issue_packet:\n  source_execution_unit: Z4R-G3\n  target_repo: \"J-Tech-Creations/Zero4Racer\n";
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out var error));
        Assert.Contains("not valid YAML", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DoubleQuotedValueWithStrayInteriorDoubleQuote_ParsesWithEscape()
    {
        // G527 asserted the old reader's limitation; G841 removed it.
        var yaml = "title: \"say \\\"hi\\\"\"\n";
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("say \"hi\"", document!.Fields["title"]);
    }

    [Fact]
    public void Parse_SingleQuotedValueWithGenuinelyUnbalancedQuote_StillFails()
    {
        var yaml = "title: 'it's broken\n";
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out _));
    }
}
