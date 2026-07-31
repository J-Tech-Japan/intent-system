using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G564: the write-back OBLIGATION a packet declared, read from the packet's
/// own G461 metadata (<c>knowledge_updates.*.required</c> +
/// <c>closeout_learning.write_back_required</c>).
///
/// This is the declaration half of intent-tree co-evolution: the packet says
/// which knowledge has to be written back after the slice lands, and
/// <see cref="KnowledgeWriteBackRecord"/> says whether it was. Neither half
/// writes intent content — the tree is written by design, never by tooling.
///
/// Read tolerantly on ABSENCE and loudly on MALFORMEDNESS, for the same
/// reason <see cref="ClarifyPacketFacts"/> splits those cases: a packet that
/// never filled the optional G461 block in is a legacy/declined packet and
/// declares no obligation (<see cref="IsRequired"/> false); a packet whose
/// block is present but unparseable is evidence that cannot be trusted either
/// way, so it throws and the caller reports it with its path rather than
/// treating it as "nothing required".
/// </summary>
internal sealed record KnowledgeWriteBackDeclaration
{
    /// <summary>The four G461 <c>knowledge_updates</c> facets, in declaration order.</summary>
    private static readonly (string Facet, string PathsKey)[] KnowledgeUpdateFacets =
    [
        ("intent_tree", "target_paths"),
        ("adr", "target_paths"),
        ("diagram", "target_paths"),
        ("docs", "target_paths"),
    ];

    /// <summary>
    /// True when the packet declared at least one <c>required: true</c> facet
    /// or <c>closeout_learning.write_back_required: true</c>. A packet that
    /// declared nothing required never produces a pending item — declining is
    /// a legitimate answer, and this surface exists to detect broken promises,
    /// not to force every slice to touch the tree.
    /// </summary>
    public required bool IsRequired { get; init; }

    /// <summary>
    /// The facets that carry the obligation (<c>intent_tree</c>, <c>adr</c>,
    /// <c>diagram</c>, <c>docs</c>, <c>closeout_learning</c>) — named so a
    /// report can say WHAT was promised, not merely that something was.
    /// </summary>
    public required IReadOnlyList<string> RequiredFacets { get; init; }

    /// <summary>
    /// Declared target paths, de-duplicated across the required facets and
    /// <c>closeout_learning.write_back_targets</c>, in declaration order.
    /// Empty when the packet promised a write-back without naming where.
    /// </summary>
    public required IReadOnlyList<string> DeclaredTargets { get; init; }

    public static readonly KnowledgeWriteBackDeclaration None = new()
    {
        IsRequired = false,
        RequiredFacets = Array.Empty<string>(),
        DeclaredTargets = Array.Empty<string>(),
    };

    /// <summary>
    /// Reads the declaration from raw packet YAML. Throws
    /// <see cref="InvalidOperationException"/> when the YAML does not parse,
    /// when the root is not a mapping, or when a declared <c>required</c> /
    /// <c>write_back_required</c> value is present but is not a boolean — the
    /// three cases where the metadata exists but cannot be believed.
    /// </summary>
    public static KnowledgeWriteBackDeclaration Read(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new InvalidOperationException("Packet YAML is empty, so its knowledge write-back declaration cannot be read.");
        }

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode as YamlMappingNode;
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new InvalidOperationException($"Packet YAML could not be parsed: {exception.Message}");
        }

        if (root is null)
        {
            throw new InvalidOperationException("Packet YAML is empty or is not a mapping.");
        }

        var requiredFacets = new List<string>();
        var declaredTargets = new List<string>();

        var knowledgeUpdates = GetMapping(root, "knowledge_updates");
        if (knowledgeUpdates is not null)
        {
            foreach (var (facet, pathsKey) in KnowledgeUpdateFacets)
            {
                var facetNode = GetMapping(knowledgeUpdates, facet);
                if (facetNode is null)
                {
                    continue;
                }

                if (!ReadBoolean(facetNode, "required", $"knowledge_updates.{facet}.required"))
                {
                    continue;
                }

                requiredFacets.Add(facet);
                declaredTargets.AddRange(GetList(facetNode, pathsKey));
            }
        }

        var closeoutLearning = GetMapping(root, "closeout_learning");
        if (closeoutLearning is not null
            && ReadBoolean(closeoutLearning, "write_back_required", "closeout_learning.write_back_required"))
        {
            requiredFacets.Add("closeout_learning");
            declaredTargets.AddRange(GetList(closeoutLearning, "write_back_targets"));
        }

        return new KnowledgeWriteBackDeclaration
        {
            IsRequired = requiredFacets.Count > 0,
            RequiredFacets = requiredFacets,
            DeclaredTargets = declaredTargets.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>
    /// An absent flag means "not declared" (false). A PRESENT flag that is not
    /// a boolean is malformed metadata and throws: silently reading
    /// <c>required: yes-please</c> as false would turn a broken declaration
    /// into a clean bill of health, which is the exact failure this slice
    /// exists to stop.
    /// </summary>
    private static bool ReadBoolean(YamlMappingNode parent, string key, string path)
    {
        var raw = GetScalar(parent, key);
        if (raw is null || raw.Length == 0)
        {
            return false;
        }

        if (bool.TryParse(raw, out var value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Packet field '{path}' is '{raw}', which is not a boolean. A write-back obligation cannot be "
            + "established or ruled out from an unreadable declaration.");
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode parent, string key) =>
        parent.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node as YamlMappingNode : null;

    private static string? GetScalar(YamlMappingNode parent, string key) =>
        parent.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static IReadOnlyList<string> GetList(YamlMappingNode parent, string key) =>
        parent.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode sequence
            ? sequence.Children.OfType<YamlScalarNode>()
                .Select(scalar => (scalar.Value ?? string.Empty).Trim())
                .Where(value => value.Length > 0)
                .ToArray()
            : Array.Empty<string>();
}
