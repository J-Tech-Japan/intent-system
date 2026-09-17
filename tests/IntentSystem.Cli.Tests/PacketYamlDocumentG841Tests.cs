using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>G841: <see cref="PacketYamlDocument"/> inventory-row coverage.</summary>
public sealed class PacketYamlDocumentG841Tests
{
    private static string BasePacket(string extra) =>
        """
        implementation_issue_packet:
          domain: intent-cli
          target_repo: J-Tech-Japan/intent-system

        """ + extra;

    [Fact]
    public void TryParse_InventoryRow1_DoubleQuotedHash_ParsesLiteral()
    {
        var yaml = BasePacket("  source_artifact: \"review of PR #1823\"\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("review of PR #1823", document!.Fields["implementation_issue_packet.source_artifact"]);
    }

    [Fact]
    public void TryParse_InventoryRow2_SingleQuotedHash_ParsesLiteral()
    {
        var yaml = BasePacket("  source_artifact: 'review of PR #1823'\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("review of PR #1823", document!.Fields["implementation_issue_packet.source_artifact"]);
    }

    [Fact]
    public void TryParse_InventoryRow3_DoubleQuotedEscape_ParsesInteriorQuote()
    {
        var yaml = BasePacket("  note: \"say \\\"hi\\\"\"\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("say \"hi\"", document!.Fields["implementation_issue_packet.note"]);
    }

    [Fact]
    public void TryParse_InventoryRow3_NewlineEscape_ParsesTwoLines()
    {
        var yaml = BasePacket("  note: \"a\\nb\"\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("a\nb", document!.Fields["implementation_issue_packet.note"]);
    }

    [Fact]
    public void TryParse_InventoryRow4_SingleQuotedDoubledEscape_Unescapes()
    {
        // G527 asserted the old reader's limitation; G841 removed it.
        var yaml = BasePacket("  title: 'it''s fine'\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("it's fine", document!.Fields["implementation_issue_packet.title"]);
    }

    [Fact]
    public void TryParse_InventoryRow5_BlockScalarNoColon_Parses()
    {
        var yaml = BasePacket("""
              notes: |
                A prose line with no colon.
            """);
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("A prose line with no colon.", document!.Fields["implementation_issue_packet.notes"]);
    }

    [Fact]
    public void TryParse_InventoryRow6_BlockScalarProse_DoesNotOverwriteDomain()
    {
        var yaml = BasePacket("""
              notes: |
                domain: sekiban-dcb-ts is what the prose says.
            """);
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("intent-cli", document!.Fields["implementation_issue_packet.domain"]);
        Assert.Equal("domain: sekiban-dcb-ts is what the prose says.", document!.Fields["implementation_issue_packet.notes"]);
    }

    [Fact]
    public void TryParse_InventoryRow7_QuotedHashNoSpace_KeepsHash()
    {
        var yaml = BasePacket("  tag: \"a#b\"\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("a#b", document!.Fields["implementation_issue_packet.tag"]);
    }

    [Fact]
    public void TryParse_InventoryRow8_QuotedHashWithSpace_KeepsValue()
    {
        var yaml = BasePacket("  tag: \"a #b\"\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("a #b", document!.Fields["implementation_issue_packet.tag"]);
    }

    [Fact]
    public void TryParse_InventoryRow10_TrailingComment_StripsComment()
    {
        var yaml = BasePacket("  domain_note: \"intent-cli\" # note\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("intent-cli", document!.Fields["implementation_issue_packet.domain_note"]);
    }

    [Fact]
    public void TryParse_InventoryRow11_LeadingHashInQuotes_KeepsHash()
    {
        var yaml = BasePacket("  tag: \"#tag here\"\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("#tag here", document!.Fields["implementation_issue_packet.tag"]);
    }

    [Fact]
    public void TryParse_InventoryRow12_EmptyScalar_RecordsNoFieldAndPreservesLaterPath()
    {
        var withEmpty = """
            implementation_issue_packet:
              domain: ""
              nested:
                key: value
            """;
        var withoutEmpty = """
            implementation_issue_packet:
              nested:
                key: value
            """;
        Assert.True(PacketYamlDocument.TryParse(withEmpty, out var withDoc, out _));
        Assert.True(PacketYamlDocument.TryParse(withoutEmpty, out var withoutDoc, out _));
        Assert.False(withDoc!.Fields.ContainsKey("implementation_issue_packet.domain"));
        Assert.Equal(withoutDoc!.Fields["implementation_issue_packet.nested.key"], withDoc.Fields["implementation_issue_packet.nested.key"]);
    }

    [Fact]
    public void TryParse_InventoryRow13_DuplicateKey_Fails()
    {
        var yaml = """
            implementation_issue_packet:
              domain: intent-cli
              domain: other-domain
            """;
        Assert.False(PacketYamlDocument.TryParse(yaml, out _, out var error));
        Assert.Contains("not valid YAML", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseWithLocation_DuplicateKey_ReportsExactLineAndColumn_G841D2()
    {
        var yaml = """
            implementation_issue_packet:
              domain: intent-cli
              domain: other-domain
            """;
        Assert.False(PacketYamlDocument.TryParseWithLocation(yaml, out _, out var error));
        Assert.Equal(3, error!.Line);
        Assert.Equal(3, error.Column);
    }

    [Fact]
    public void TryParseWithLocation_UnterminatedDoubleQuote_ReportsExactLineAndColumn_G841D2()
    {
        var yaml = """
            implementation_issue_packet:
              domain: intent-cli
              target_repo: "J-Tech-Creations/Zero4Racer
            """;
        Assert.False(PacketYamlDocument.TryParseWithLocation(yaml, out _, out var error));
        Assert.Equal(3, error!.Line);
        Assert.Equal(16, error.Column);
        Assert.DoesNotContain("is not valid YAML", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_InventoryRow_PlainScalarHashComment_YieldsA_G841D6()
    {
        var yaml = BasePacket("  tag: a #b\n");
        Assert.True(PacketYamlDocument.TryParse(yaml, out var document, out _));
        Assert.Equal("a", document!.Fields["implementation_issue_packet.tag"]);
    }
}
