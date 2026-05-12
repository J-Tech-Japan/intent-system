using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G328: tests for the pure <see cref="NextSlicePacketProvenanceReader"/>.
/// Covers the role / host / source-pr parse, the default-design
/// fallback for pre-G328 packets, and the silent-recovery behavior
/// when the role value is unknown or the block is malformed.
/// </summary>
public sealed class NextSlicePacketProvenanceTests
{
    [Fact]
    public void ReadFromText_GivenDesignProvenance_ReturnsDesignRoleAndHost()
    {
        var provenance = NextSlicePacketProvenanceReader.ReadFromText(
            """
            implementation_issue_packet:
              source_execution_unit: G328
            provenance:
              created_by_role: design
              created_by_host: MyIntentHost
            """);

        Assert.Equal("design", provenance.CreatedByRole);
        Assert.Equal("MyIntentHost", provenance.CreatedByHost);
        Assert.Null(provenance.SourceCloseoutPr);
        Assert.Equal("packet.yaml", provenance.ProvenanceSource);
    }

    [Fact]
    public void ReadFromText_GivenReviewRuntimeProvenance_ReturnsRoleAndSourcePr()
    {
        var provenance = NextSlicePacketProvenanceReader.ReadFromText(
            """
            provenance:
              created_by_role: review-runtime
              created_by_host: review-runtime-intent-system
              source_closeout_pr: 758
            """);

        Assert.Equal("review-runtime", provenance.CreatedByRole);
        Assert.Equal("review-runtime-intent-system", provenance.CreatedByHost);
        Assert.Equal(758, provenance.SourceCloseoutPr);
        Assert.Equal("packet.yaml", provenance.ProvenanceSource);
    }

    [Fact]
    public void ReadFromText_GivenNoProvenanceBlock_ReturnsDefaultDesignFallback()
    {
        // G328: pre-G328 packets do not record provenance. The reader
        // returns a `default-design` record so the candidate JSON
        // is never null and existing publish lanes keep working.
        var provenance = NextSlicePacketProvenanceReader.ReadFromText(
            """
            implementation_issue_packet:
              source_execution_unit: G244
              target_repo: J-Tech-Japan/intent-system
            """);

        Assert.Equal("design", provenance.CreatedByRole);
        Assert.Null(provenance.CreatedByHost);
        Assert.Null(provenance.SourceCloseoutPr);
        Assert.Equal("default-design", provenance.ProvenanceSource);
    }

    [Fact]
    public void ReadFromText_GivenNullOrWhitespace_ReturnsDefaultDesign()
    {
        var fromNull = NextSlicePacketProvenanceReader.ReadFromText(null);
        var fromBlank = NextSlicePacketProvenanceReader.ReadFromText("   \n\t");

        Assert.Equal("design", fromNull.CreatedByRole);
        Assert.Equal("default-design", fromNull.ProvenanceSource);
        Assert.Equal("design", fromBlank.CreatedByRole);
        Assert.Equal("default-design", fromBlank.ProvenanceSource);
    }

    [Theory]
    [InlineData("robot")]
    [InlineData("Design")] // case-sensitive — we don't accept aliases
    [InlineData("")]
    public void ReadFromText_GivenUnknownRoleValue_FallsBackToDefaultDesign(string role)
    {
        // G328: the reader never throws on bad data; an unrecognised
        // role triggers the default-design fallback so a malformed
        // packet doesn't take next-slice planning offline.
        var provenance = NextSlicePacketProvenanceReader.ReadFromText(
            $"""
            provenance:
              created_by_role: {role}
              created_by_host: x
            """);

        Assert.Equal("design", provenance.CreatedByRole);
        Assert.Equal("default-design", provenance.ProvenanceSource);
    }

    [Fact]
    public void ReadFromText_GivenQuotedValues_StripsQuotes()
    {
        var provenance = NextSlicePacketProvenanceReader.ReadFromText(
            """
            provenance:
              created_by_role: "review-runtime"
              created_by_host: 'review-runtime-intent-system'
              source_closeout_pr: 758
            """);

        Assert.Equal("review-runtime", provenance.CreatedByRole);
        Assert.Equal("review-runtime-intent-system", provenance.CreatedByHost);
        Assert.Equal(758, provenance.SourceCloseoutPr);
    }

    [Fact]
    public void ReadFromText_GivenInvalidSourcePrNumber_LeavesPrFieldNull()
    {
        var provenance = NextSlicePacketProvenanceReader.ReadFromText(
            """
            provenance:
              created_by_role: review-runtime
              source_closeout_pr: not-a-number
            """);

        Assert.Equal("review-runtime", provenance.CreatedByRole);
        Assert.Null(provenance.SourceCloseoutPr);
    }

    [Fact]
    public void ReadFromText_StopsAtNextTopLevelKey()
    {
        // G328: the provenance parser must stop walking the file when
        // it dedents back to a top-level key so it doesn't pick up
        // keys from a sibling block as part of provenance.
        var provenance = NextSlicePacketProvenanceReader.ReadFromText(
            """
            provenance:
              created_by_role: review-runtime
              created_by_host: x
            implementation_issue_packet:
              source_closeout_pr: 999
            """);

        Assert.Equal("review-runtime", provenance.CreatedByRole);
        Assert.Equal("x", provenance.CreatedByHost);
        // The `source_closeout_pr` inside `implementation_issue_packet`
        // must NOT be picked up.
        Assert.Null(provenance.SourceCloseoutPr);
    }

    [Fact]
    public void Read_GivenDirectoryWithoutPacketYaml_ReturnsDefaultDesign()
    {
        using var temp = new TempDirectory();
        var provenance = NextSlicePacketProvenanceReader.Read(temp.Path);
        Assert.Equal("design", provenance.CreatedByRole);
        Assert.Equal("default-design", provenance.ProvenanceSource);
    }

    [Fact]
    public void Read_GivenDirectoryWithPacketYaml_ReadsProvenance()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "packet.yaml"),
            """
            provenance:
              created_by_role: review-runtime
              created_by_host: x
            """);
        var provenance = NextSlicePacketProvenanceReader.Read(temp.Path);
        Assert.Equal("review-runtime", provenance.CreatedByRole);
        Assert.Equal("x", provenance.CreatedByHost);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            Directory.CreateTempSubdirectory("g328-provenance-tests-").FullName;

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
