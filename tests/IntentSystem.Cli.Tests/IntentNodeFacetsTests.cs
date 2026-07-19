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
    public void ParseFacets_TabIndentedFacetsKey_IsMalformedNeverAbsent()
    {
        // Rereview repair: a tab-indented "facets:" is not column-0, so it
        // is not recognized as the strict top-level key — but it clearly
        // LOOKS like an attempted facets declaration, so it must never be
        // silently swallowed as "no facets here" (Absent). It is Malformed,
        // which lint reports as a nonzero-exit error.
        var result = IntentNodeFacets.ParseFacets("---\n\tfacets: [vocabulary]\n---\n");

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
        Assert.Contains("tab", result.MalformedReason, StringComparison.OrdinalIgnoreCase);
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

    // ── Rereview repair: escape-aware scanning, trailing junk, exact delimiters ──

    [Fact]
    public void ParseFacets_DoubleQuotedScalar_EscapedQuote_DecodedCorrectly_NotSplitEarly()
    {
        // The escaped quote must not be treated as the closing delimiter —
        // if scanning were not escape-aware, this would either split the
        // value early or fail to find the list's closing bracket at all.
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [\"vocabulary\\\"quoted\", invariant]\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(2, result.Values.Count);
        Assert.Equal("vocabulary\"quoted", result.Values[0]);
        Assert.Equal("invariant", result.Values[1]);
    }

    [Fact]
    public void ParseFacets_DoubleQuotedScalar_EscapedBackslash_DecodedCorrectly()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [\"back\\\\slash\"]\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "back\\slash" }, result.Values);
    }

    [Fact]
    public void ParseFacets_SingleQuotedScalar_DoubledQuoteEscape_DecodedToOneLiteralQuote()
    {
        // YAML single-quote scalars escape an embedded quote by doubling
        // it ('') — not backslash-escaping. "it''s" decodes to "it's".
        var result = IntentNodeFacets.ParseFacets("---\nfacets: ['it''s vocabulary']\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "it's vocabulary" }, result.Values);
    }

    [Fact]
    public void ParseFacets_QuotedScalar_ContainingHashAndComma_TreatedAsLiteralContentNotDelimiters()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [\"a, b # not a comment\", invariant]\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(2, result.Values.Count);
        Assert.Equal("a, b # not a comment", result.Values[0]);
        Assert.Equal("invariant", result.Values[1]);
    }

    [Fact]
    public void ParseFacets_FlowForm_NonCommentTrailingContentAfterClosingBracket_IsMalformed()
    {
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [vocabulary] trailing-junk\n---\n");

        Assert.Equal(FacetsParseKind.Malformed, result.Kind);
    }

    [Fact]
    public void ParseFacets_FlowForm_CommentAfterClosingBracket_StillParses()
    {
        // A genuine comment (not other content) after the closing bracket
        // is valid YAML and must still parse — distinguishing this from
        // the trailing-junk case above.
        var result = IntentNodeFacets.ParseFacets("---\nfacets: [vocabulary] # trailing comment is fine\n---\n");

        Assert.Equal(FacetsParseKind.Present, result.Kind);
        Assert.Equal(new[] { "vocabulary" }, result.Values);
    }

    [Fact]
    public void ParseFacets_OpeningDelimiter_NearMissNotExactDashes_IsAbsent()
    {
        // "---junk" is not a valid frontmatter opening delimiter — the file
        // has no recognized frontmatter block at all, so this is Absent
        // (not a false-positive empty frontmatter match).
        var result = IntentNodeFacets.ParseFacets("---junk\nfacets: [vocabulary]\n---\n# Title\n");

        Assert.Equal(FacetsParseKind.Absent, result.Kind);
    }

    [Fact]
    public void ParseFacets_ClosingDelimiter_NearMissNotExactDashes_TreatedAsUnterminatedFrontmatter()
    {
        // "---junk" mid-file must never be mistaken for the closing "---"
        // delimiter (the prior IndexOf("\n---")-based implementation would
        // have accepted it). With no exact closing delimiter anywhere, the
        // file has no recognized frontmatter block at all — Absent, not a
        // corrupted partial parse.
        var content = "---\nfacets: [vocabulary]\n---junk\nmore text\n";

        var result = IntentNodeFacets.ParseFacets(content);

        Assert.Equal(FacetsParseKind.Absent, result.Kind);
    }

    [Fact]
    public void TryExtractFrontmatterBlock_ExactClosingDelimiter_NotConfusedByNearMissEarlier()
    {
        // A "---junk" line must be skipped when searching for the closing
        // delimiter; the real, exact "---" further down is what closes it.
        var content = "---\nfacets: [vocabulary]\n---junk\n---\n# Title\n";

        var found = IntentNodeFacets.TryExtractFrontmatterBlock(content, out var frontmatter, out var body);

        Assert.True(found);
        Assert.Contains("---junk", frontmatter, StringComparison.Ordinal);
        Assert.Equal("# Title", body.Trim());
    }

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
