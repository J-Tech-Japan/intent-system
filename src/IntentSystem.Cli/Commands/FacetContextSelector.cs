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
    /// <paramref name="scopeHints"/> narrow the result to nodes overlapping
    /// at least one hint (see <see cref="NormalizeHintSegments"/> for every
    /// accepted hint form and <see cref="SegmentsOverlap"/> for the
    /// symmetric overlap rule). A null or empty hint list applies no
    /// narrowing — every domain facet node is returned. <paramref
    /// name="facetFilter"/> (a subset of <see cref="IntentNodeFacets.AllowedValues"/>)
    /// restricts which facet groups are returned at all; null/empty returns
    /// all four.
    ///
    /// Only VALID facet values (per <see cref="IntentNodeFacets.IsAllowedValue"/>)
    /// are ever bucketed. Unlike the first G530 round, neither a malformed
    /// <c>facets:</c> declaration nor an unknown facet value is silent: both
    /// produce a <see cref="FacetContextWarning"/> (path + reason) so an
    /// agent can tell "genuinely no facets here" apart from "facets were
    /// present but excluded" — <see cref="FacetContextSelection.DomainHasAnyFacetNodes"/>
    /// reflects only nodes that contributed at least one VALID facet: a
    /// domain whose every facets: declaration is malformed or entirely
    /// unknown-valued still reports <c>false</c>, but the accompanying
    /// warnings explain why, rather than looking identical to a domain that
    /// never adopted facets at all. A node carrying more than one facet
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
        var warnings = new List<FacetContextWarning>();

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

                var domainRelativeId = ToDomainRelativeId(domainRoot, file);
                var repoRelativePath = $"intents/{domain}/{domainRelativeId}.md";

                var parsed = IntentNodeFacets.ParseFacets(content);
                if (parsed.Kind == FacetsParseKind.Malformed)
                {
                    warnings.Add(new FacetContextWarning
                    {
                        Path = repoRelativePath,
                        Reason = $"malformed 'facets:' declaration excluded from facet context: {parsed.MalformedReason}",
                    });
                    continue;
                }

                if (parsed.Kind != FacetsParseKind.Present)
                {
                    continue;
                }

                var validFacets = parsed.Values.Where(IntentNodeFacets.IsAllowedValue).ToArray();
                var unknownFacets = parsed.Values.Where(value => !IntentNodeFacets.IsAllowedValue(value)).ToArray();
                if (unknownFacets.Length > 0)
                {
                    warnings.Add(new FacetContextWarning
                    {
                        Path = repoRelativePath,
                        Reason =
                            $"unknown facet value(s) ignored: {string.Join(", ", unknownFacets)} "
                            + $"(closed set: {string.Join(", ", IntentNodeFacets.AllowedValues)})",
                    });
                }

                if (validFacets.Length == 0)
                {
                    continue;
                }

                var summary = ExtractSummary(content);
                nodes.Add((repoRelativePath, domainRelativeId, validFacets, summary));
            }
        }

        var domainHasAnyFacetNodes = nodes.Count > 0;

        // G530 review repair: a hint that NormalizeHintSegments rejects
        // (outside the domain root, a `..` traversal, or otherwise
        // unresolvable) still safely matches nothing — but that must never
        // look identical to a valid scope that simply matched no nodes.
        // Every rejected hint is captured here, verbatim, with the reason.
        var scopeWarnings = new List<FacetScopeWarning>();
        var normalizedHints = new List<IReadOnlyList<string>>();
        foreach (var hint in scopeHints ?? Array.Empty<string>())
        {
            var segments = NormalizeHintSegments(hint, domain, domainRoot, out var rejectionReason);
            if (segments is null)
            {
                scopeWarnings.Add(new FacetScopeWarning { Hint = hint, Reason = rejectionReason! });
                continue;
            }
            normalizedHints.Add(segments);
        }

        // Narrowing is requested whenever the caller passed a scope-hint
        // LIST AT ALL — including an EXPLICITLY EMPTY one (e.g. a packet
        // that declares zero `intent_references`, which must scope to
        // NOTHING, not to "no scoping requested" — a fresh, unreferenced
        // packet is not entitled to the whole domain's facet nodes). Only
        // a null scopeHints (the caller genuinely omitted --scope) applies
        // no narrowing at all. Within a requested-but-empty-or-fully-
        // invalid hint set, every node fails to match — matches nothing,
        // never falls back to "show everything".
        var scoped = scopeHints is not null
            ? nodes.Where(node => normalizedHints.Any(hint => SegmentsOverlap(hint, node.DomainRelativeId.Split('/')))).ToArray()
            : nodes.ToArray();

        // "All requested hints were rejected" is only true, by definition,
        // when the caller requested at least one hint AND every one of
        // them ended up in scopeWarnings (none survived into
        // normalizedHints) — computed rather than tracked separately so it
        // can never drift out of sync with the two lists above.
        var allScopeHintsRejected = scopeHints is { Count: > 0 } && normalizedHints.Count == 0;

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
            Warnings = warnings,
            ScopeWarnings = scopeWarnings,
            AllScopeHintsRejected = allScopeHintsRejected,
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

    /// <summary>
    /// Normalizes a scope hint into a domain-relative segment list, or
    /// <see langword="null"/> (with <paramref name="rejectionReason"/> set)
    /// when the hint is empty/whitespace, resolves outside
    /// <paramref name="domainRoot"/> entirely, or attempts to escape the
    /// domain via a <c>..</c> segment. G530 review repair: a rejection is
    /// NEVER silent — the caller (<see cref="Select"/>) surfaces every
    /// rejected hint, verbatim, with this reason, as a
    /// <see cref="FacetScopeWarning"/>, so "--scope matched nothing because
    /// every hint was invalid" is never indistinguishable from "--scope
    /// matched nothing because no node happened to overlap a genuinely
    /// valid hint". Accepted hint forms — all reduced to the SAME canonical
    /// segment list a node's own domain-relative id already uses, so
    /// comparison is always apples-to-apples:
    /// <list type="bullet">
    /// <item>an absolute filesystem path under <paramref name="domainRoot"/>
    ///   (reduced by subtracting the domain root, exactly like a node's own
    ///   path is derived);</item>
    /// <item>the repo-relative <c>intents/&lt;domain&gt;/...</c> form (the
    ///   prefix is stripped);</item>
    /// <item>the short domain-relative id form with no prefix (e.g.
    ///   <c>identity/mission</c>);</item>
    /// <item>any of the above WITH a trailing <c>.md</c> (stripped, so the
    ///   documented short file form <c>identity/mission.md</c> matches the
    ///   extension-less node id <c>identity/mission</c>).</item>
    /// </list>
    /// A leading/trailing <c>/</c> and any <c>.</c> (no-op) segment are
    /// tolerated and dropped; a <c>..</c> segment is rejected outright
    /// (returns null) rather than silently resolved, since it could
    /// otherwise walk a hint outside the domain it claims to scope.
    /// Comparison is case-sensitive (<see cref="StringComparison.Ordinal"/>)
    /// throughout, matching every other path-handling surface in this
    /// codebase (case-sensitive filesystem semantics) — pinned explicitly
    /// here since path overlap has no other natural default.
    /// </summary>
    private static IReadOnlyList<string>? NormalizeHintSegments(string hint, string domain, string domainRoot, out string? rejectionReason)
    {
        rejectionReason = null;

        if (string.IsNullOrWhiteSpace(hint))
        {
            rejectionReason = "hint is empty or whitespace-only";
            return null;
        }

        var normalized = hint.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(normalized))
        {
            string fullHintPath;
            string fullDomainRoot;
            try
            {
                fullHintPath = Path.GetFullPath(normalized).Replace('\\', '/');
                fullDomainRoot = Path.GetFullPath(domainRoot).Replace('\\', '/').TrimEnd('/');
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                rejectionReason = $"could not be resolved as a filesystem path: {exception.Message}";
                return null;
            }

            if (string.Equals(fullHintPath, fullDomainRoot, StringComparison.Ordinal))
            {
                normalized = string.Empty;
            }
            else if (fullHintPath.StartsWith(fullDomainRoot + "/", StringComparison.Ordinal))
            {
                normalized = fullHintPath[(fullDomainRoot.Length + 1)..];
            }
            else
            {
                // outside the domain root entirely — never matches
                rejectionReason = $"resolves to '{fullHintPath}', which is outside the domain root '{fullDomainRoot}'";
                return null;
            }
        }
        else
        {
            var domainPrefix = $"intents/{domain}/";
            if (normalized.StartsWith(domainPrefix, StringComparison.Ordinal))
            {
                normalized = normalized[domainPrefix.Length..];
            }
            else if (string.Equals(normalized.TrimEnd('/'), $"intents/{domain}", StringComparison.Ordinal))
            {
                normalized = string.Empty;
            }
            // else: already the short domain-relative form — used as-is.
        }

        normalized = normalized.Trim('/');
        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3];
        }

        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                // reject traversal outside the domain — never silently resolved
                rejectionReason = "contains a '..' traversal segment, which is rejected rather than resolved";
                return null;
            }
            segments.Add(segment);
        }

        return segments;
    }

    /// <summary>
    /// Symmetric segment-boundary overlap: true when either segment list is
    /// a whole-segment prefix of the other (covers an exact match, a hint
    /// naming an ancestor directory of a node, AND the reverse — a node
    /// whose own segments are a prefix of a more specific hint). An empty
    /// segment list (a hint that normalized to the domain root itself) is a
    /// prefix of everything, so it matches every node — a deliberate "scope
    /// to the whole domain" hint, not a bug. Never a bare string-prefix
    /// comparison, so a hint like <c>means</c> can never spuriously match a
    /// node <c>means-2/flow</c> ("means" is not a whole segment of
    /// "means-2").
    /// </summary>
    private static bool SegmentsOverlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var shorter = a.Count <= b.Count ? a : b;
        var longer = a.Count <= b.Count ? b : a;
        for (var i = 0; i < shorter.Count; i++)
        {
            if (!string.Equals(shorter[i], longer[i], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}

internal sealed record FacetContextSelection
{
    public required IReadOnlyList<FacetContextGroup> Groups { get; init; }

    /// <summary>
    /// True when the DOMAIN has at least one node contributing a VALID
    /// facet, ignoring any <c>--scope</c>/<c>--facets</c> narrowing — used
    /// to distinguish "this domain has no usable facet nodes at all"
    /// (graceful-degradation note) from "this query's filter/scope happened
    /// to match nothing" (an ordinary empty result, not a degradation
    /// case). A domain whose only <c>facets:</c> declarations are malformed
    /// or entirely unknown-valued is still <see langword="false"/> here —
    /// check <see cref="Warnings"/> to tell that apart from a domain that
    /// never adopted facets at all.
    /// </summary>
    public required bool DomainHasAnyFacetNodes { get; init; }

    /// <summary>
    /// G530 review repair: every node excluded from the facet groups
    /// because its own <c>facets:</c> declaration was malformed, plus every
    /// node whose Present declaration carried at least one unknown value
    /// (that node may still appear in <see cref="Groups"/> under its OTHER
    /// valid facets — the warning only concerns the unknown value itself).
    /// Never silent: both JSON and Markdown renderers surface this list so
    /// an agent can tell omitted-with-a-reason apart from never-existed.
    /// </summary>
    public required IReadOnlyList<FacetContextWarning> Warnings { get; init; }

    /// <summary>
    /// G530 review repair: every requested <c>--scope</c>/<c>intent_references</c>
    /// hint that <see cref="FacetContextSelector.NormalizeHintSegments"/>
    /// rejected (outside the domain root, a <c>..</c> traversal, or
    /// otherwise unresolvable), verbatim, with the reason. A rejected hint
    /// still safely contributes zero matches — but never silently, so
    /// "matched nothing because every hint was invalid" is distinguishable
    /// from "matched nothing because a valid hint just didn't overlap any
    /// node".
    /// </summary>
    public required IReadOnlyList<FacetScopeWarning> ScopeWarnings { get; init; }

    /// <summary>
    /// True only when the caller requested at least one scope hint AND
    /// every single one of them was rejected (none survived into the
    /// actual narrowing) — lets a renderer say so explicitly rather than
    /// leaving the reader to infer it by counting <see cref="ScopeWarnings"/>.
    /// </summary>
    public required bool AllScopeHintsRejected { get; init; }
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

internal sealed record FacetContextWarning
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

internal sealed record FacetScopeWarning
{
    [JsonPropertyName("hint")]
    public required string Hint { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}
