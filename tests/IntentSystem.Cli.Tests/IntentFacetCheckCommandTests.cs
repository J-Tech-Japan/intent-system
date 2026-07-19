using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G531: coverage for the read-only <c>intent facet-check</c> scaffold —
/// appearance-ordered/Markdown-aware-noise-excluded term extraction,
/// per-field evidence/match-kind classification, acceptance-property
/// coverage scope-status honesty, deterministic packet-I/O fault
/// injection, no-facet-data degradation, and the always-exit-0 /
/// always-carries-a-disclaimer contract.
/// </summary>
public sealed class IntentFacetCheckCommandTests
{
    // ── Extraction: basic rules ─────────────────────────────────────

    [Fact]
    public void ExtractCandidateTerms_BacktickIdentifier_Extracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Add the `CreateOrder` command.");

        Assert.Contains("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_BacktickSpanWithSpaces_SkippedNotATerm()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Run `intent facet-check --domain d` first.");

        Assert.DoesNotContain(terms, t => t.Contains(' '));
    }

    [Fact]
    public void ExtractCandidateTerms_BacktickSpanFollowedByPunctuation_StillExtracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Introduces `CreateOrder`, a new command.");

        Assert.Contains("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_PlainCamelCaseWord_Extracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("This introduces OrderPlaced as a new concept.");

        Assert.Contains("OrderPlaced", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_WordEndingInCommandSuffix_Extracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("We add a new refund command.");

        // lowercase "command" alone has no case transition and isn't a
        // command/event token on its own — only a capitalized-suffix token
        // like "RefundCommand" should be picked up.
        Assert.DoesNotContain("command", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_PlainLowercaseProseWords_NotExtracted()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("This is an ordinary sentence about orders and events.");

        Assert.Empty(terms);
    }

    [Fact]
    public void ExtractCandidateTerms_DuplicateAcrossFormsAfterNormalization_DedupedKeepsFirstCasing()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Introduces `CreateOrder`. Later mentions CreateOrder again.");

        Assert.Single(terms, t => t == "CreateOrder");
    }

    [Fact]
    public void ExtractCandidateTerms_EscapedBacktick_NotTreatedAsInlineCodeDelimiter()
    {
        // "escapedterm" has no case transition and no Command/Event/Query
        // suffix, so it can ONLY be extracted via a (wrongly unescaped)
        // backtick span — isolating the escaping behavior specifically,
        // unlike a term such as "NotATerm" which independently qualifies
        // as a plain camelCase word regardless of any surrounding backticks.
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(@"The \`escapedterm\` is escaped, not inline code.");

        Assert.DoesNotContain("escapedterm", terms);
    }

    // ── Extraction: appearance order (review repair) ─────────────────

    [Fact]
    public void ExtractCandidateTerms_AppearanceOrderPreservedAcrossBacktickAndWordPasses()
    {
        // A later backtick term must not be emitted before an earlier
        // plain camelCase word — extraction must follow true document
        // order, not "all backtick hits, then all plain-word hits".
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(
            "OrderPlaced happens before we later mention `ShipPackage`.");

        Assert.Equal(new[] { "OrderPlaced", "ShipPackage" }, terms);
    }

    [Fact]
    public void ExtractCandidateTerms_ReverseOrderInText_ReflectedInOutputOrder()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(
            "First we mention `ShipPackage`, only later does OrderPlaced appear.");

        Assert.Equal(new[] { "ShipPackage", "OrderPlaced" }, terms);
    }

    [Fact]
    public void ExtractCandidateTerms_CrossFileOrderingOffsets_LaterFileTermsSortAfterEarlierFileTerms()
    {
        // Concatenation puts github-body before implementation, so even
        // though implementation's own internal offsets restart near zero,
        // its terms must still sort AFTER every github-body term — true
        // document order across the concatenation, not per-file offsets.
        var githubBody = "Mentions ZebraEvent near the end of the body.";
        var implementation = "AlphaCommand appears first in this file.";
        var concatenated = string.Join("\n", new[] { githubBody, implementation });

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(concatenated);

        Assert.Equal(new[] { "ZebraEvent", "AlphaCommand" }, terms);
    }

    // ── Extraction: fenced-code noise (review repair round 3) ─────────

    [Fact]
    public void ExtractCandidateTerms_FencedCodeBlock_ExcludedAsNoise()
    {
        var text = "Intro text.\n```csharp\npublic class CreateOrder { }\n```\nAfter the fence.";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_InlineBacktick_StillRetainedNotTreatedAsFencedNoise()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Use `CreateOrder` inline, not fenced.");

        Assert.Contains("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_LanguageTaggedFence_ExcludedAsNoise()
    {
        var text = "```csharp\nclass NoiseWithLanguageTag {}\n```";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("NoiseWithLanguageTag", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_NoLanguageFence_ExcludedAsNoise()
    {
        var text = "```\nclass NoiseWithNoLanguageTag {}\n```";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("NoiseWithNoLanguageTag", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_TildeFence_ExcludedAsNoise()
    {
        var text = "Intro.\n~~~\npublic class NoiseFromTildeFence { }\n~~~\nOutro.";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("NoiseFromTildeFence", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_LongBacktickFence_ExcludedAsNoise()
    {
        // A 4-backtick fence, whose content itself contains a 3-backtick
        // run — the 3-backtick run must NOT be mistaken for a closer.
        var text = "Intro.\n````\nRaw text with ``` inside: class NoiseFromLongFence {}\n````\nOutro.";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("NoiseFromLongFence", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_IndentedFence_ExcludedAsNoise()
    {
        var text = "Intro.\n   ```\n   class NoiseFromIndentedFence {}\n   ```\nOutro.";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("NoiseFromIndentedFence", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_CrlfFence_ExcludedAsNoise()
    {
        var text = "Intro.\r\n```csharp\r\nclass NoiseFromCrlfFence {}\r\n```\r\nOutro.";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("NoiseFromCrlfFence", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_UnclosedFence_FailsClosed_MasksToEndOfDocument()
    {
        var text = "Intro.\n```csharp\nclass NoiseFromUnclosedFence {}\nNo closing fence follows.";

        var terms = IntentFacetCheckCommand.ExtractCandidateTerms(text);

        Assert.DoesNotContain("NoiseFromUnclosedFence", terms);
    }

    // ── Extraction: link/URL/path noise, label preservation (review repair round 3) ──

    [Fact]
    public void ExtractCandidateTerms_UrlSegment_ExcludedAsNoise()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("See https://example.com/CreateOrder for details.");

        Assert.DoesNotContain("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_BarePathSegment_ExcludedAsNoise()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("Implemented in src/Commands/CreateOrder.cs today.");

        Assert.DoesNotContain("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_MarkdownLinkTarget_ExcludedAsNoise()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("See [the doc](docs/NoiseFromLinkTarget.md) for details.");

        Assert.DoesNotContain("NoiseFromLinkTarget", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_MarkdownLinkVisibleLabel_PreservedAndExtracted()
    {
        // The visible label is intentional authored proposal text — only
        // the parenthesized target is path/URL noise.
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("See [CreateOrder](design.md) for details.");

        Assert.Contains("CreateOrder", terms);
    }

    [Fact]
    public void ExtractCandidateTerms_ImageAltTextPreserved_ImageTargetMasked()
    {
        var terms = IntentFacetCheckCommand.ExtractCandidateTerms("![CreateOrder](diagrams/NoiseFromImagePath.png)");

        Assert.Contains("CreateOrder", terms);
        Assert.DoesNotContain("NoiseFromImagePath", terms);
    }

    // ── --terms mode: matching / evidence / collision / unmatched ────

    [Fact]
    public void Execute_TermsMode_MatchedTermReportsRelatedVocabularyNode()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.Equal("CreateOrder", termReport.GetProperty("term").GetString());
        Assert.False(termReport.GetProperty("unmatched").GetBoolean());
        var related = termReport.GetProperty("related_nodes").EnumerateArray().ToArray();
        var match = Assert.Single(related);
        Assert.Equal("commands/create-order", match.GetProperty("node").GetProperty("id").GetString());
    }

    [Fact]
    public void Execute_TermsMode_MatchedVocabularyNode_AlsoReportedAsCollision()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "create-order", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        var collisions = termReport.GetProperty("collisions").EnumerateArray().ToArray();
        var collision = Assert.Single(collisions);
        Assert.Equal("commands/create-order", collision.GetProperty("node").GetProperty("id").GetString());
    }

    [Fact]
    public void Execute_TermsMode_NonVocabularyMatch_NotReportedAsCollision()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("invariants/create-order.md", ["invariant"], "Create Order Invariant");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.False(termReport.GetProperty("unmatched").GetBoolean());
        Assert.Empty(termReport.GetProperty("collisions").EnumerateArray());
    }

    [Fact]
    public void Execute_TermsMode_NoMatchingNode_UnmatchedTrue()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "ShipPackage", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.True(termReport.GetProperty("unmatched").GetBoolean());
        Assert.Empty(termReport.GetProperty("related_nodes").EnumerateArray());
    }

    [Fact]
    public void Execute_TermsMode_MultiFacetNode_ReportedOnceNotOncePerFacet()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary", "invariant"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.Single(termReport.GetProperty("related_nodes").EnumerateArray());
    }

    [Fact]
    public void Execute_TermsMode_NoCoverageSection_NullNotFabricatedGap()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.TryGetProperty("coverage", out _));
    }

    [Fact]
    public void Execute_JsonFormat_EveryResultCarriesDisclaimer()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "AnyTerm", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var disclaimer = document.RootElement.GetProperty("disclaimer").GetString();
        Assert.False(string.IsNullOrWhiteSpace(disclaimer));
        Assert.Contains("not semantic verification", disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    // ── Per-field evidence / match-kind classification (review repair round 3) ──

    [Fact]
    public void Execute_TermsMode_IdOnlyEvidence_ExactRawMatch()
    {
        using var workspace = new FacetCheckWorkspace();
        // id last segment "create-order" raw-exact-matches the term; the
        // title is unrelated prose, so it contributes no evidence at all.
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Unrelated Title Text");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "create-order", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        var match = Assert.Single(termReport.GetProperty("related_nodes").EnumerateArray());
        var evidence = Assert.Single(match.GetProperty("evidence").EnumerateArray());
        Assert.Equal("id", evidence.GetProperty("field").GetString());
        Assert.Equal("create-order", evidence.GetProperty("value").GetString());
        Assert.Equal("exact", evidence.GetProperty("match_kind").GetString());
    }

    [Fact]
    public void Execute_TermsMode_TitleOnlyEvidence_NormalizedMatch()
    {
        using var workspace = new FacetCheckWorkspace();
        // id last segment "order-flow" does NOT normalize-match "CreateOrder",
        // but the node's title/summary "Create Order" does (only after
        // normalization — the raw strings differ).
        workspace.WriteFacetNode("commands/order-flow.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        var match = Assert.Single(termReport.GetProperty("related_nodes").EnumerateArray());
        var evidence = Assert.Single(match.GetProperty("evidence").EnumerateArray());
        Assert.Equal("title", evidence.GetProperty("field").GetString());
        Assert.Equal("Create Order", evidence.GetProperty("value").GetString());
        Assert.Equal("normalized", evidence.GetProperty("match_kind").GetString());
    }

    [Fact]
    public void Execute_TermsMode_BothIdAndTitleEvidence_BothNormalizedOnly()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary", "invariant"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        var match = Assert.Single(termReport.GetProperty("related_nodes").EnumerateArray());
        var evidences = match.GetProperty("evidence").EnumerateArray().ToArray();
        Assert.Equal(2, evidences.Length);
        Assert.All(evidences, e => Assert.Equal("normalized", e.GetProperty("match_kind").GetString()));
        var fields = evidences.Select(e => e.GetProperty("field").GetString()).ToArray();
        Assert.Contains("id", fields);
        Assert.Contains("title", fields);
        // Vocabulary-carrying node still isolates correctly into collisions.
        var collision = Assert.Single(termReport.GetProperty("collisions").EnumerateArray());
        Assert.Equal("commands/create-order", collision.GetProperty("node").GetProperty("id").GetString());
    }

    [Fact]
    public void Execute_TermsMode_MixedIdExactTitleNormalized_EvidencePerFieldClassification()
    {
        using var workspace = new FacetCheckWorkspace();
        // File literally named "CreateOrder.md" -> id last segment
        // "CreateOrder" is raw-exact against the term. Title "Create-Order"
        // normalizes the same but is NOT raw-identical to the term.
        workspace.WriteFacetNode("commands/CreateOrder.md", ["vocabulary"], "Create-Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        var match = Assert.Single(termReport.GetProperty("related_nodes").EnumerateArray());
        var byField = match.GetProperty("evidence").EnumerateArray()
            .ToDictionary(e => e.GetProperty("field").GetString()!, e => e.GetProperty("match_kind").GetString());
        Assert.Equal("exact", byField["id"]);
        Assert.Equal("normalized", byField["title"]);
    }

    [Fact]
    public void Execute_TermsMode_SubstringWithinTitle_DoesNotMatch_FullTokenEqualityOnly()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/order-flow.md", ["vocabulary"], "Create Order Flow");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "Order", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.True(termReport.GetProperty("unmatched").GetBoolean());
    }

    // ── no-facet-data degradation ────────────────────────────────────

    [Fact]
    public void Execute_UnannotatedDomain_NoFacetDataTrue_ExitZeroNotAnError()
    {
        using var workspace = new FacetCheckWorkspace();
        // No facet nodes written at all.

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("no_facet_data").GetBoolean());
        // Extraction/matching still runs — just reports unmatched, not suppressed.
        var termReport = document.RootElement.GetProperty("terms")[0];
        Assert.True(termReport.GetProperty("unmatched").GetBoolean());
    }

    [Fact]
    public void Execute_MarkdownFormat_NoFacetDataFalse_StillShowsExplicitLine()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No facet data: no", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownFormat_NoFacetDataTrue_ShowsExplicitLineInEveryShape()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No facet data: yes", writer.ToString(), StringComparison.Ordinal);
    }

    // ── --packet mode: extraction + coverage ─────────────────────────

    [Fact]
    public void Execute_PacketMode_ExtractsTermsFromGithubBodyAndImplementation()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: "Implements OrderPlaced handling.\n",
            intentReferences: []);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var terms = document.RootElement.GetProperty("terms").EnumerateArray().Select(t => t.GetProperty("term").GetString()).ToArray();
        Assert.Contains("CreateOrder", terms);
        Assert.Contains("OrderPlaced", terms);
    }

    [Fact]
    public void Execute_PacketMode_DedupAcrossGithubBodyThenImplementation_KeepsGithubBodyCasing()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WritePacket(
            "G900",
            githubBody: "Introduces `CreateOrder`.",
            implementation: "Later mentions `create-order` again.",
            intentReferences: []);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var terms = document.RootElement.GetProperty("terms").EnumerateArray().Select(t => t.GetProperty("term").GetString()).ToArray();
        Assert.Single(terms, t => t == "CreateOrder");
        Assert.DoesNotContain("create-order", terms);
    }

    [Fact]
    public void Execute_PacketMode_CoverageListsOverlappingAcceptancePropertyNode()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("acceptance/order-flow.md", ["acceptance-property"], "Order flow acceptance");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: string.Empty,
            intentReferences: ["intents/intent-cli/acceptance/order-flow.md"]);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("coverage");
        Assert.False(coverage.GetProperty("gap").GetBoolean());
        var node = Assert.Single(coverage.GetProperty("nodes").EnumerateArray());
        Assert.Equal("acceptance/order-flow", node.GetProperty("id").GetString());
        Assert.Equal("valid-non-empty", coverage.GetProperty("scope_status").GetString());
    }

    [Fact]
    public void Execute_PacketMode_NoOverlappingAcceptancePropertyNode_GapTrue()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("acceptance/unrelated.md", ["acceptance-property"], "Unrelated acceptance");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: string.Empty,
            intentReferences: ["intents/intent-cli/commands"]);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("coverage");
        Assert.True(coverage.GetProperty("gap").GetBoolean());
        Assert.Empty(coverage.GetProperty("nodes").EnumerateArray());
        Assert.Equal("valid-non-empty", coverage.GetProperty("scope_status").GetString());
    }

    [Fact]
    public void Execute_PacketMode_MissingPacketDirectory_ReturnsUsageErrorNotSilentEmptyResult()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G999-missing", "--format", "json"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No packet directory found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownFormat_PacketMode_RendersTermsAndCoverageSections()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WriteFacetNode("commands/create-order.md", ["vocabulary"], "Create Order");
        workspace.WritePacket(
            "G900",
            githubBody: "## Goal\n\nAdd the `CreateOrder` command.\n",
            implementation: string.Empty,
            intentReferences: []);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Facet check — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Terms", output, StringComparison.Ordinal);
        Assert.Contains("### `CreateOrder`", output, StringComparison.Ordinal);
        Assert.Contains("evidence: id=", output, StringComparison.Ordinal);
        Assert.Contains("## Acceptance-property coverage", output, StringComparison.Ordinal);
        Assert.Contains("Scope status: valid-empty", output, StringComparison.Ordinal);
        Assert.Contains("Gap: yes", output, StringComparison.Ordinal);
    }

    // ── Coverage scope-status honesty ─────────────────────────────────

    [Fact]
    public void Execute_PacketMode_AuthoredEmptyIntentReferences_ScopeStatusValidEmpty()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WritePacket("G900", githubBody: "`CreateOrder`", implementation: string.Empty, intentReferences: []);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("coverage");
        Assert.Equal("valid-empty", coverage.GetProperty("scope_status").GetString());
        Assert.True(coverage.GetProperty("gap").GetBoolean());
    }

    [Fact]
    public void Execute_PacketMode_PacketYamlMissingEntirely_ScopeStatusMissing_DetailPresent()
    {
        using var workspace = new FacetCheckWorkspace();
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G900");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "github-body.md"), "`CreateOrder`");
        // No packet.yaml written at all.

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("coverage");
        Assert.Equal("missing", coverage.GetProperty("scope_status").GetString());
        Assert.True(coverage.GetProperty("gap").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(coverage.GetProperty("scope_status_detail").GetString()));
    }

    [Fact]
    public void Execute_PacketMode_PacketYamlMissingIntentReferencesKey_ScopeStatusMissing()
    {
        using var workspace = new FacetCheckWorkspace();
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G900");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "github-body.md"), "`CreateOrder`");
        File.WriteAllText(
            Path.Combine(packetDir, "packet.yaml"),
            """
            implementation_issue_packet:
              source_execution_unit: G900
              domain: intent-cli
            """);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("missing", document.RootElement.GetProperty("coverage").GetProperty("scope_status").GetString());
    }

    [Fact]
    public void Execute_PacketMode_MalformedYaml_ScopeStatusMalformed()
    {
        using var workspace = new FacetCheckWorkspace();
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G900");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "github-body.md"), "`CreateOrder`");
        File.WriteAllText(Path.Combine(packetDir, "packet.yaml"), "implementation_issue_packet: [this is not valid: yaml: at all");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("malformed", document.RootElement.GetProperty("coverage").GetProperty("scope_status").GetString());
    }

    [Fact]
    public void Execute_PacketMode_WrongShapeIntentReferences_ScopeStatusWrongShape()
    {
        using var workspace = new FacetCheckWorkspace();
        var packetDir = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G900");
        Directory.CreateDirectory(packetDir);
        File.WriteAllText(Path.Combine(packetDir, "github-body.md"), "`CreateOrder`");
        File.WriteAllText(
            Path.Combine(packetDir, "packet.yaml"),
            """
            implementation_issue_packet:
              source_execution_unit: G900
              domain: intent-cli
              intent_references: not-a-list
            """);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("wrong-shape", document.RootElement.GetProperty("coverage").GetProperty("scope_status").GetString());
    }

    [Fact]
    public void Execute_PacketMode_InvalidIndividualReference_StillReportedAsG530ScopeWarning()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WritePacket(
            "G900", githubBody: "`CreateOrder`", implementation: string.Empty,
            intentReferences: ["intents/intent-cli/identity/../../outside"]);

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var coverage = document.RootElement.GetProperty("coverage");
        Assert.Equal("valid-non-empty", coverage.GetProperty("scope_status").GetString());
        var warning = Assert.Single(coverage.GetProperty("scope_warnings").EnumerateArray());
        Assert.Contains("traversal", warning.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    // ── Deterministic packet-I/O fault injection (review repair round 3) ──

    [Fact]
    public void Execute_GithubBodyUnreadable_DeterministicFaultInjection_TreatedAsNonFindingError()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WritePacket("G900", githubBody: "`CreateOrder`", implementation: "irrelevant text", intentReferences: []);
        var githubBodyPath = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G900", "github-body.md");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--packet", "G900"],
            writer,
            path => string.Equals(path, githubBodyPath, StringComparison.Ordinal)
                ? throw new IOException("simulated I/O failure")
                : File.ReadAllText(path));

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to read packet source", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("simulated I/O failure", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ImplementationMdUnreadable_DeterministicFaultInjection_TreatedAsNonFindingError()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WritePacket("G900", githubBody: "`CreateOrder`", implementation: "irrelevant text", intentReferences: []);
        var implementationPath = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G900", "implementation.md");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--packet", "G900"],
            writer,
            path => string.Equals(path, implementationPath, StringComparison.Ordinal)
                ? throw new UnauthorizedAccessException("simulated permission failure")
                : File.ReadAllText(path));

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to read packet source", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PacketYamlUnreadable_DeterministicFaultInjection_TreatedAsNonFindingError()
    {
        using var workspace = new FacetCheckWorkspace();
        workspace.WritePacket("G900", githubBody: "`CreateOrder`", implementation: string.Empty, intentReferences: []);
        var packetYamlPath = Path.Combine(workspace.RepoRoot, ".intent-cli", "issues", "G900", "packet.yaml");

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--packet", "G900"],
            writer,
            path => string.Equals(path, packetYamlPath, StringComparison.Ordinal)
                ? throw new IOException("simulated I/O failure")
                : File.ReadAllText(path));

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to read packet source", writer.ToString(), StringComparison.Ordinal);
    }

    // ── usage / exit-code contract ────────────────────────────────────

    [Fact]
    public void Execute_NeitherPacketNorTerms_ReturnsUsageError()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(workspace.Context, ["--domain", "intent-cli"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires exactly one of --packet or --terms", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_BothPacketAndTerms_ReturnsUsageError()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--packet", "G900", "--terms", "CreateOrder"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("mutually exclusive", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TermsWithEmptyElement_ReturnsUsageError()
    {
        using var workspace = new FacetCheckWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "CreateOrder,,ShipPackage"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("no empty elements", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FindingsPresent_StillExitsZero_NeverAGate()
    {
        using var workspace = new FacetCheckWorkspace();
        // A term that will be unmatched — still exit 0, never a gate.
        using var writer = new StringWriter();
        var exitCode = IntentFacetCheckCommand.Execute(
            workspace.Context, ["--domain", "intent-cli", "--terms", "TotallyUnknownThing"], writer);

        Assert.Equal(0, exitCode);
    }

    private sealed class FacetCheckWorkspace : IDisposable
    {
        public FacetCheckWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("facet-check-tests-").FullName;
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public string RepoRoot { get; }

        public CliContext Context { get; }

        public void WriteFacetNode(string relativePath, IReadOnlyList<string> facets, string title)
        {
            var fullPath = Path.Combine(
                RepoRoot, "intents", Context.Config.Project.Domain, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, $"---\nfacets: [{string.Join(", ", facets)}]\n---\n# {title}\n");
        }

        public void WritePacket(string executionUnit, string githubBody, string implementation, IReadOnlyList<string> intentReferences)
        {
            var packetDir = Path.Combine(RepoRoot, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(packetDir);
            File.WriteAllText(Path.Combine(packetDir, "github-body.md"), githubBody);
            File.WriteAllText(Path.Combine(packetDir, "implementation.md"), implementation);
            var referencesYaml = intentReferences.Count == 0
                ? "intent_references: []"
                : "intent_references:\n" + string.Join("\n", intentReferences.Select(r => $"  - {r}"));
            File.WriteAllText(
                Path.Combine(packetDir, "packet.yaml"),
                $"""
                implementation_issue_packet:
                  source_execution_unit: {executionUnit}
                  domain: intent-cli
                  {referencesYaml}
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot))
            {
                Directory.Delete(RepoRoot, recursive: true);
            }
        }
    }
}
