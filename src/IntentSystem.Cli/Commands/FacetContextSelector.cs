using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G530: shared, read-only selection of G529 semantic-facet nodes
/// (<c>vocabulary</c>, <c>invariant</c>, <c>decider</c>,
/// <c>acceptance-property</c>) for a domain's intent tree, used by both
/// <c>context collect</c> (a <c>--scope</c> hint list, narrowing by overlap)
/// and <c>packet draft</c> (the packet's own <c>intent_references</c> as the
/// scope hints) so the two surfaces can never disagree on what "overlaps".
/// Never mutates state; a missing/unreadable node is skipped rather than
/// failing the whole selection.
/// </summary>
internal static class FacetContextSelector
{
    /// <summary>
    /// Scans every <c>.md</c> file under <paramref name="domainRoot"/>
    /// (already resolved by the caller — <c>context collect</c> and
    /// <c>packet draft</c> each follow their own established parent-host /
    /// local-repo resolution), classifies each by its G529 <c>facets:</c>
    /// frontmatter, and groups the result into one entry per facet in the
    /// canonical order (<see cref="IntentNodeFacets.AllowedValues"/>).
    ///
    /// <paramref name="scopeHints"/> (paths, either the full displayed
    /// <c>intents/&lt;domain&gt;/...</c> form or the shorter domain-relative
    /// id form) narrow the result to nodes whose path equals a hint, sits
    /// under a hint treated as a directory prefix, or vice versa. A null or
    /// empty hint list applies no narrowing — every domain facet node is
    /// returned. <paramref name="facetFilter"/> (a subset of
    /// <see cref="IntentNodeFacets.AllowedValues"/>) restricts which facet
    /// groups are returned at all; null/empty returns all four.
    ///
    /// Only VALID facet values (per <see cref="IntentNodeFacets.IsAllowedValue"/>)
    /// are ever bucketed — an unknown value on an otherwise-Present node is
    /// silently excluded from the facet groups it doesn't belong to
    /// (validating and reporting unknown values is `lint-layout`'s job, not
    /// this consumption surface's). A node carrying more than one facet
    /// appears once in each matching group.
    /// </summary>
    public static FacetContextSelection Select(
        string domainRoot,
        string domain,
        IReadOnlyList<string>? scopeHints,
        IReadOnlyCollection<string>? facetFilter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var nodes = new List<(string RepoRelativePath, string DomainRelativeId, IReadOnlyList<string> Facets, string Summary)>();

        if (Directory.Exists(domainRoot))
        {
            foreach (var file in Directory
                         .EnumerateFiles(domainRoot, "*.md", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                var parsed = IntentNodeFacets.ParseFacets(content);
                if (parsed.Kind != FacetsParseKind.Present)
                {
                    continue;
                }

                var validFacets = parsed.Values.Where(IntentNodeFacets.IsAllowedValue).ToArray();
                if (validFacets.Length == 0)
                {
                    continue;
                }

                var domainRelativeId = ToDomainRelativeId(domainRoot, file);
                var repoRelativePath = $"intents/{domain}/{domainRelativeId}.md";
                var summary = ExtractSummary(content);
                nodes.Add((repoRelativePath, domainRelativeId, validFacets, summary));
            }
        }

        var domainHasAnyFacetNodes = nodes.Count > 0;

        var scoped = scopeHints is { Count: > 0 }
            ? nodes.Where(node => scopeHints.Any(hint => HintOverlapsNode(hint, node.RepoRelativePath, node.DomainRelativeId))).ToArray()
            : nodes.ToArray();

        var groups = new List<FacetContextGroup>();
        foreach (var facet in IntentNodeFacets.AllowedValues)
        {
            if (facetFilter is { Count: > 0 } && !facetFilter.Contains(facet, StringComparer.Ordinal))
            {
                continue;
            }

            var nodeRefs = scoped
                .Where(node => node.Facets.Contains(facet, StringComparer.Ordinal))
                .Select(node => new FacetContextNodeRef
                {
                    Id = node.DomainRelativeId,
                    Facets = node.Facets,
                    Summary = node.Summary,
                    Path = node.RepoRelativePath,
                })
                .ToArray();

            groups.Add(new FacetContextGroup { Facet = facet, Nodes = nodeRefs });
        }

        return new FacetContextSelection
        {
            Groups = groups,
            DomainHasAnyFacetNodes = domainHasAnyFacetNodes,
        };
    }

    /// <summary>The domain-relative id — path under the domain root, slashes normalized, without the `.md` extension (e.g. `identity/mission`).</summary>
    private static string ToDomainRelativeId(string domainRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(domainRoot, fullPath).Replace('\\', '/');
        return relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? relative[..^3]
            : relative;
    }

    private static string ExtractSummary(string content)
    {
        var body = IntentNodeFacets.TryExtractFrontmatterBlock(content, out _, out var bodyAfterFrontmatter)
            ? bodyAfterFrontmatter
            : content;
        var firstLine = body
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
        return firstLine.TrimStart('#').Trim();
    }

    private static bool HintOverlapsNode(string hint, string repoRelativePath, string domainRelativeId) =>
        HintMatchesPath(hint, repoRelativePath) || HintMatchesPath(hint, domainRelativeId);

    private static bool HintMatchesPath(string hint, string candidate)
    {
        var normalizedHint = hint.Replace('\\', '/').Trim().TrimEnd('/');
        if (normalizedHint.Length == 0)
        {
            return false;
        }

        var normalizedCandidate = candidate.Replace('\\', '/');
        return string.Equals(normalizedCandidate, normalizedHint, StringComparison.Ordinal)
            || normalizedCandidate.StartsWith(normalizedHint + "/", StringComparison.Ordinal);
    }
}

internal sealed record FacetContextSelection
{
    public required IReadOnlyList<FacetContextGroup> Groups { get; init; }

    /// <summary>
    /// True when the DOMAIN has at least one facet-annotated node, ignoring
    /// any <c>--scope</c>/<c>--facets</c> narrowing — used to distinguish
    /// "this domain has no facet nodes at all" (graceful-degradation note)
    /// from "this query's filter/scope happened to match nothing" (an
    /// ordinary empty result, not a degradation case).
    /// </summary>
    public required bool DomainHasAnyFacetNodes { get; init; }
}

internal sealed record FacetContextGroup
{
    [JsonPropertyName("facet")]
    public required string Facet { get; init; }

    [JsonPropertyName("nodes")]
    public required IReadOnlyList<FacetContextNodeRef> Nodes { get; init; }
}

internal sealed record FacetContextNodeRef
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("facets")]
    public required IReadOnlyList<string> Facets { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }
}
