using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G529 rereview repair: direct unit coverage for
/// <see cref="IntentNodeFacets.ParseFacets"/> — the real YAML-list parser
/// (block + flow forms, quoted scalars, comments, multiline, duplicates,
/// malformed detection) that replaced the original single-line-flow-only
/// regex, which silently treated every other valid YAML shape as absent.
/// </summary>
public sealed class IntentNodeFacetsTests
{
    // ── Absent ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseFacets_NoFrontmatterBlock_IsAbsent()
    {
        var result = IntentNodeFacets.ParseFacets("# Mission\n\nNo frontmatter at all.\n");

        Assert.Equal(FacetsParseKind.Absent, result.Kind);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void ParseFacets_FrontmatterWithNoFacetsKey_IsAbsent()
    {
        var result = IntentNodeFacets.ParseFacets("---\nintent_id: G1\n---\n# Node\n");

        Assert.Equal(FacetsParseKind.Absent, result.Kind);
    }

    // ── Flow form ────────────────────────────────────────────────────────

    [Fact]
    public void ParseFacets_FlowForm_SingleLine_Parses()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [vocabulary, invariant]\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_FlowForm_MultiLine_Parses()
    {
        var content = "---\nfacets: [\n  vocabulary,\n  invariant\n]\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_FlowForm_QuotedScalars_DoubleAndSingle_Parses()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [\"vocabulary\", 'invariant']\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_FlowForm_InlineCommentAfterList_Parses()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [vocabulary, invariant]  # the two vocabulary facets\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_FlowForm_ExtraWhitespaceAndTrailingComma_Parses()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets:   [  vocabulary ,   invariant ,  ]\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_FlowForm_EmptyList_ParsesAsPresentWithNoValues()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: []\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void ParseFacets_FlowForm_DuplicateValues_DedupedPreservingFirstSeenOrder()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [invariant, vocabulary, invariant]\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "invariant", "vocabulary" }, result.Values);
    }

    // ── Block form ───────────────────────────────────────────────────────

    [Fact]
    public void ParseFacets_BlockForm_Parses()
    {
        var content = "---\nfacets:\n  - vocabulary\n  - invariant\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_BlockForm_QuotedItems_Parses()
    {
        var content = "---\nfacets:\n  - \"vocabulary\"\n  - 'invariant'\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_BlockForm_InlineCommentsPerItem_Parses()
    {
        var content = "---\nfacets:\n  - vocabulary  # the glossary facet\n  - invariant  # consistency boundary\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_BlockForm_BlankLineBetweenItems_Parses()
    {
        var content = "---\nfacets:\n  - vocabulary\n\n  - invariant\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "invariant" }, result.Values);
    }

    [Fact]
    public void ParseFacets_BlockForm_DuplicateValues_DedupedPreservingFirstSeenOrder()
    {
        var content = "---\nfacets:\n  - decider\n  - vocabulary\n  - decider\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "decider", "vocabulary" }, result.Values);
    }

    [Fact]
    public void ParseFacets_BlockForm_OtherFrontmatterFieldsAfterBlock_AreIgnoredAsBlockBoundary()
    {
        var content = "---\nfacets:\n  - vocabulary\nintent_id: G1\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary" }, result.Values);
    }

    // ── Malformed: must never be silently treated as absent ────────────────

    [Fact]
    public void ParseFacets_BareScalar_IsMalformedNotAbsent()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: projection\n---\n");

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
        Assert.NotNull(result.MalformedReason);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void ParseFacets_UnterminatedFlowList_IsMalformed()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [vocabulary, invariant\n---\n");

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
    }

    [Fact]
    public void ParseFacets_UnbalancedQuoteInFlowList_IsMalformed()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [\"vocabulary]\n---\n");

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
    }

    [Fact]
    public void ParseFacets_UnbalancedQuoteInBlockItem_IsMalformed()
    {
        var content = "---\nfacets:\n  - \"vocabulary\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
    }

    [Fact]
    public void ParseFacets_TabIndentedFacetsKey_IsNotRecognizedAsTopLevel_IsAbsent()
    {
        var result = IntentNodeFacets.ParseFacets("---\n\tfacets: [vocabulary]\n---\n");

        // A tab-indented "facets:" is not column-0, so it is not recognized
        // as the top-level key at all — absent, not malformed. This case
        // instead pins that a tab appearing WITHIN the recognized facets
        // line/block is rejected; see the block-form variant below for the
        // block-list indentation case.
        Assert.Equal(FacetsParseKind.Absent, result.Kind);
    }

    [Fact]
    public void ParseFacets_TabInBlockItemIndentation_IsMalformed()
    {
        var content = "---\nfacets:\n\t- vocabulary\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
    }

    [Fact]
    public void ParseFacets_FacetsKeyWithNoValueAndNoBlockItems_IsMalformed()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets:\nintent_id: G1\n---\n");

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
    }

    [Fact]
    public void ParseFacets_BlockFormWithNonListItemLine_IsMalformed()
    {
        var content = "---\nfacets:\n  vocabulary\n---\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
    }

    // ── Unknown-value validation stays a separate concern from parsing ─────

    [Fact]
    public void ParseFacets_UnknownValue_StillParsesAsPresent_ValidationIsCallersJob()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [vocabulary, projection]\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary", "projection" }, result.Values);
        Assert.True(IntentNodeFacets.IsAllowedValue("vocabulary"));
        Assert.False(IntentNodeFacets.IsAllowedValue("projection"));
    }
}
