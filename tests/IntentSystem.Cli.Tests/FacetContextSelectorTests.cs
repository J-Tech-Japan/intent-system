using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G530: direct unit coverage for <see cref="FacetContextSelector"/> — the
/// shared node scan/group/scope logic behind both `context collect`'s
/// facet section and `packet draft`'s generated "Facet context" section.
/// </summary>
public sealed class FacetContextSelectorTests : IDisposable
{
    private readonly string domainRoot = Directory.CreateTempSubdirectory("facet-context-selector-tests-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(domainRoot))
        {
            Directory.Delete(domainRoot, recursive: true);
        }
    }

    private void WriteNode(string relativePath, IReadOnlyList<string> facets, string title = "Node")
    {
        var fullPath = Path.Combine(domainRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var facetsLine = facets.Count == 0 ? string.Empty : $"facets: [{string.Join(", ", facets)}]\n";
        File.WriteAllText(fullPath, $"---\n{facetsLine}---\n# {title}\nBody text.\n");
    }

    [Fact]
    public void Select_NoScopeNoFilter_ReturnsAllFourGroupsInCanonicalOrder()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        WriteNode("decisions/adr-1.md", ["decider"]);

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        Assert.True(selection.DomainHasAnyFacetNodes);
        Assert.Equal(4, selection.Groups.Count);
        Assert.Equal(
            new[] { "vocabulary", "invariant", "decider", "acceptance-property" },
            selection.Groups.Select(g => g.Facet));
    }

    [Fact]
    public void Select_NodeCarriesMultipleFacets_AppearsInEachMatchingGroup()
    {
        WriteNode("identity/mission.md", ["vocabulary", "invariant"]);

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        var vocabularyGroup = selection.Groups.Single(g => g.Facet == "vocabulary");
        var invariantGroup = selection.Groups.Single(g => g.Facet == "invariant");
        Assert.Single(vocabularyGroup.Nodes);
        Assert.Single(invariantGroup.Nodes);
        Assert.Equal(vocabularyGroup.Nodes[0].Id, invariantGroup.Nodes[0].Id);
        Assert.Equal(new[] { "vocabulary", "invariant" }, vocabularyGroup.Nodes[0].Facets);
    }

    [Fact]
    public void Select_FacetFilter_RestrictsToRequestedFacetsOnly_StillCanonicalOrder()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        WriteNode("decisions/adr-1.md", ["decider"]);
        WriteNode("means/flow.md", ["invariant"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: null, facetFilter: ["decider", "invariant"]);

        Assert.Equal(new[] { "invariant", "decider" }, selection.Groups.Select(g => g.Facet));
    }

    [Fact]
    public void Select_ScopeHintExactPathMatch_IncludesOnlyThatNode()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        WriteNode("decisions/adr-1.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["intents/intent-cli/identity/mission.md"], facetFilter: null);

        var vocabularyGroup = selection.Groups.Single(g => g.Facet == "vocabulary");
        var node = Assert.Single(vocabularyGroup.Nodes);
        Assert.Equal("identity/mission", node.Id);
    }

    [Fact]
    public void Select_ScopeHintDirectoryPrefix_IncludesEveryNodeUnderIt()
    {
        WriteNode("means/a.md", ["decider"]);
        WriteNode("means/b.md", ["decider"]);
        WriteNode("identity/mission.md", ["decider"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["intents/intent-cli/means"], facetFilter: null);

        var deciderGroup = selection.Groups.Single(g => g.Facet == "decider");
        Assert.Equal(2, deciderGroup.Nodes.Count);
        Assert.All(deciderGroup.Nodes, node => Assert.StartsWith("means/", node.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void Select_ScopeHintDomainRelativeShortForm_AlsoMatches()
    {
        // The short "identity/mission" form (no "intents/<domain>/" prefix,
        // no ".md" extension) — as intent_references may realistically be
        // authored — must also overlap.
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["identity/mission"], facetFilter: null);

        var vocabularyGroup = selection.Groups.Single(g => g.Facet == "vocabulary");
        Assert.Single(vocabularyGroup.Nodes);
    }

    [Fact]
    public void Select_ScopeHintMatchingNothing_ReturnsEmptyGroupsButDomainStillHasFacetNodes()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["intents/intent-cli/unrelated"], facetFilter: null);

        Assert.True(selection.DomainHasAnyFacetNodes);
        Assert.All(selection.Groups, group => Assert.Empty(group.Nodes));
    }

    [Fact]
    public void Select_NoFacetAnnotatedNodesInDomain_DomainHasAnyFacetNodesIsFalse()
    {
        WriteNode("identity/mission.md", facets: []);

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        Assert.False(selection.DomainHasAnyFacetNodes);
        Assert.All(selection.Groups, group => Assert.Empty(group.Nodes));
    }

    [Fact]
    public void Select_MissingDomainDirectory_DegradesGracefullyToEmpty()
    {
        var missingRoot = Path.Combine(domainRoot, "does-not-exist");

        var selection = FacetContextSelector.Select(missingRoot, "intent-cli", scopeHints: null, facetFilter: null);

        Assert.False(selection.DomainHasAnyFacetNodes);
        Assert.All(selection.Groups, group => Assert.Empty(group.Nodes));
    }

    [Fact]
    public void Select_MalformedFacetsDeclaration_ExcludedFromEveryGroup()
    {
        var path = Path.Combine(domainRoot, "identity", "mission.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\nfacets: not-a-list\n---\n# Mission\n");

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        Assert.False(selection.DomainHasAnyFacetNodes);
        Assert.All(selection.Groups, group => Assert.Empty(group.Nodes));
    }

    [Fact]
    public void Select_UnknownFacetValueAlongsideValidOne_OnlyValidFacetBucketed()
    {
        WriteNode("identity/mission.md", ["vocabulary", "projection"]);

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        var vocabularyGroup = selection.Groups.Single(g => g.Facet == "vocabulary");
        var node = Assert.Single(vocabularyGroup.Nodes);
        Assert.DoesNotContain("projection", node.Facets);
        Assert.All(selection.Groups.Where(g => g.Facet != "vocabulary"), group => Assert.Empty(group.Nodes));
    }

    [Fact]
    public void Select_NodeSummary_IsFirstNonBlankLineAfterFrontmatterWithHashStripped()
    {
        WriteNode("identity/mission.md", ["vocabulary"], title: "Mission Statement");

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        var node = selection.Groups.Single(g => g.Facet == "vocabulary").Nodes.Single();
        Assert.Equal("Mission Statement", node.Summary);
    }

    [Fact]
    public void Select_NodePath_UsesIntentsDomainPrefixConvention()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        var node = selection.Groups.Single(g => g.Facet == "vocabulary").Nodes.Single();
        Assert.Equal("intents/intent-cli/identity/mission.md", node.Path);
    }

    // ── Review repair: symmetric, normalized scope overlap ──────────────

    [Fact]
    public void Select_ScopeHintShortFormWithMdExtension_Matches()
    {
        // The documented short domain-relative FILE form carries ".md";
        // the node's own id does not. Both must be recognized as the same
        // logical path.
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["identity/mission.md"], facetFilter: null);

        Assert.Single(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_ScopeHintAbsoluteFilesystemPath_ReducedToDomainRelativeForm()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        var absoluteHint = Path.Combine(domainRoot, "identity", "mission.md");

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: [absoluteHint], facetFilter: null);

        Assert.Single(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_ScopeHintAbsolutePathOutsideDomainRoot_NeverMatches()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        var outsideHint = Path.Combine(Path.GetTempPath(), "definitely-not-under-domain-root", "file.md");

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: [outsideHint], facetFilter: null);

        Assert.Empty(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_ScopeHintDeeperThanNodePath_ReverseAncestorOverlapMatches()
    {
        // Symmetric overlap: a hint MORE SPECIFIC than a node's own path
        // (the node's segments are a prefix of the hint's) must also count
        // as overlap, not just the already-covered "hint is an ancestor
        // directory of the node" direction.
        WriteNode("means/flow.md", ["decider"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["intents/intent-cli/means/flow/deeper-anchor"], facetFilter: null);

        Assert.Single(selection.Groups.Single(g => g.Facet == "decider").Nodes);
    }

    [Fact]
    public void Select_MultipleScopeHints_UnionsMatchingNodesAcrossAllHints()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        WriteNode("decisions/adr-1.md", ["vocabulary"]);
        WriteNode("means/flow.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli",
            scopeHints: ["intents/intent-cli/identity", "intents/intent-cli/decisions"],
            facetFilter: null);

        var vocabularyGroup = selection.Groups.Single(g => g.Facet == "vocabulary");
        Assert.Equal(2, vocabularyGroup.Nodes.Count);
        Assert.DoesNotContain(vocabularyGroup.Nodes, n => n.Id == "means/flow");
    }

    [Fact]
    public void Select_ScopeHintWithBackslashSeparators_NormalizedAndMatches()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: [@"intents\intent-cli\identity\mission.md"], facetFilter: null);

        Assert.Single(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_ScopeHintWithParentTraversalSegment_RejectedNeverMatches()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["intents/intent-cli/identity/../identity/mission.md"], facetFilter: null);

        Assert.Empty(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_ScopeHintCaseMismatch_NeverMatches_CaseSensitivePinned()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["intents/intent-cli/IDENTITY/MISSION.md"], facetFilter: null);

        Assert.Empty(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_ScopeHintPrefixCollision_DoesNotMatchSimilarlyNamedSibling()
    {
        // "means" must never match "means-2/flow" via a bare string-prefix
        // check — only a whole path SEGMENT match counts.
        WriteNode("means-2/flow.md", ["decider"]);

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: ["intents/intent-cli/means"], facetFilter: null);

        Assert.Empty(selection.Groups.Single(g => g.Facet == "decider").Nodes);
    }

    // ── Review repair: malformed/unknown-value visibility ───────────────

    [Fact]
    public void Select_MalformedFacetsDeclaration_ProducesWarningWithPathAndReason()
    {
        var path = Path.Combine(domainRoot, "identity", "mission.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\nfacets: not-a-list\n---\n# Mission\n");

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        var warning = Assert.Single(selection.Warnings);
        Assert.Equal("intents/intent-cli/identity/mission.md", warning.Path);
        Assert.Contains("malformed", warning.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_UnknownFacetValue_ProducesWarning_NodeStillAppearsUnderItsValidFacets()
    {
        WriteNode("identity/mission.md", ["vocabulary", "projection"]);

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        var warning = Assert.Single(selection.Warnings);
        Assert.Equal("intents/intent-cli/identity/mission.md", warning.Path);
        Assert.Contains("projection", warning.Reason, StringComparison.Ordinal);
        Assert.Single(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_DomainWhereEveryDeclarationIsMalformedOrUnknownOnly_DistinguishableFromGenuinelyEmptyDomain()
    {
        // DomainHasAnyFacetNodes stays false (no VALID facet was ever
        // bucketed), but Warnings is non-empty — this is what lets a
        // consumer tell "excluded for a reason" apart from "never adopted
        // facets at all", which look identical without the warnings list.
        var malformedPath = Path.Combine(domainRoot, "identity", "mission.md");
        Directory.CreateDirectory(Path.GetDirectoryName(malformedPath)!);
        File.WriteAllText(malformedPath, "---\nfacets: not-a-list\n---\n# Mission\n");
        WriteNode("decisions/adr-1.md", ["projection"]); // unknown-only, nothing valid

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        Assert.False(selection.DomainHasAnyFacetNodes);
        Assert.Equal(2, selection.Warnings.Count);
        Assert.All(selection.Groups, group => Assert.Empty(group.Nodes));
    }

    // ── Review repair: rejected scope hints are never silent ────────────

    [Fact]
    public void Select_ScopeHintOutsideDomainRoot_ProducesScopeWarningNamingHintAndReason()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        var outsideHint = Path.Combine(Path.GetTempPath(), "definitely-not-under-domain-root", "file.md");

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: [outsideHint], facetFilter: null);

        var warning = Assert.Single(selection.ScopeWarnings);
        Assert.Equal(outsideHint, warning.Hint);
        Assert.Contains("outside the domain root", warning.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(selection.AllScopeHintsRejected);
    }

    [Fact]
    public void Select_ScopeHintWithTraversalSegment_ProducesScopeWarning()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        const string hint = "intents/intent-cli/identity/../identity/mission.md";

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: [hint], facetFilter: null);

        var warning = Assert.Single(selection.ScopeWarnings);
        Assert.Equal(hint, warning.Hint);
        Assert.Contains("traversal", warning.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(selection.AllScopeHintsRejected);
    }

    [Fact]
    public void Select_MixedSeparatorsWithTraversal_StillDetectedAndRejected()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        const string hint = @"intents\intent-cli\identity\..\..\etc";

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: [hint], facetFilter: null);

        var warning = Assert.Single(selection.ScopeWarnings);
        Assert.Equal(hint, warning.Hint);
        Assert.Contains("traversal", warning.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_AbsoluteHintWithTraversalSegment_RejectedNotSilentlyCanonicalized()
    {
        // Round-4 review repair: an ABSOLUTE hint containing ".." must be
        // rejected on its ORIGINAL text before Path.GetFullPath gets a
        // chance to canonicalize the traversal away — this hint resolves
        // right back to the real node's path, so if the traversal check ran
        // AFTER canonicalization (the prior bug) it would wrongly match.
        WriteNode("identity/mission.md", ["vocabulary"]);
        var hint = Path.Combine(domainRoot, "identity", "..", "identity", "mission.md");

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: [hint], facetFilter: null);

        var warning = Assert.Single(selection.ScopeWarnings);
        Assert.Equal(hint, warning.Hint);
        Assert.Contains("traversal", warning.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(selection.AllScopeHintsRejected);
        Assert.Empty(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_AbsoluteHintWithMixedSeparatorTraversal_RejectedNotSilentlyCanonicalized()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        var hint = Path.Combine(domainRoot, "identity") + @"\..\identity\mission.md";

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: [hint], facetFilter: null);

        var warning = Assert.Single(selection.ScopeWarnings);
        Assert.Equal(hint, warning.Hint);
        Assert.Contains("traversal", warning.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(selection.Groups.Single(g => g.Facet == "vocabulary").Nodes);
    }

    [Fact]
    public void Select_MixedValidAndInvalidScopeHints_UsesValidOnes_ReportsOnlyTheRejectedOnes()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        WriteNode("decisions/adr-1.md", ["vocabulary"]);
        const string invalidHint = "intents/intent-cli/identity/../../outside";

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli",
            scopeHints: ["intents/intent-cli/identity", invalidHint],
            facetFilter: null);

        var scopeWarning = Assert.Single(selection.ScopeWarnings);
        Assert.Equal(invalidHint, scopeWarning.Hint);
        Assert.False(selection.AllScopeHintsRejected);
        var vocabularyGroup = selection.Groups.Single(g => g.Facet == "vocabulary");
        var node = Assert.Single(vocabularyGroup.Nodes);
        Assert.Equal("identity/mission", node.Id);
    }

    [Fact]
    public void Select_AllScopeHintsInvalid_AllScopeHintsRejectedTrue_MatchesNothing()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);
        var outsideHint1 = Path.Combine(Path.GetTempPath(), "outside-one", "a.md");
        var outsideHint2 = Path.Combine(Path.GetTempPath(), "outside-two", "b.md");

        var selection = FacetContextSelector.Select(
            domainRoot, "intent-cli", scopeHints: [outsideHint1, outsideHint2], facetFilter: null);

        Assert.True(selection.AllScopeHintsRejected);
        Assert.Equal(2, selection.ScopeWarnings.Count);
        Assert.All(selection.Groups, group => Assert.Empty(group.Nodes));
    }

    [Fact]
    public void Select_NoScopeHintsPassed_NoScopeWarnings_NotAllRejected()
    {
        WriteNode("identity/mission.md", ["vocabulary"]);

        var selection = FacetContextSelector.Select(domainRoot, "intent-cli", scopeHints: null, facetFilter: null);

        Assert.Empty(selection.ScopeWarnings);
        Assert.False(selection.AllScopeHintsRejected);
    }
}
