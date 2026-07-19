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
}
