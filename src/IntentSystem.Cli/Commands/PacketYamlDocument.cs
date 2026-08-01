using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G567: a whole-document parse of <c>packet.yaml</c>, flattened to the dotted
/// scalar map the seeding path already consumes.
///
/// G565 unified projection onto a real YAML parser. This is the same move one
/// surface further upstream, and on a MUTATION path: the queue-seed lane read
/// packet fields with <see cref="PreparedPacketYamlScalarParser"/>, a
/// line-and-regex reader that never parses the document, so a packet the schema
/// and projection surfaces both reject could still classify
/// <c>queue-seed-ready</c> and put a malformed unit into the queue. The failure
/// then surfaces at publish or preflight time, far from its cause.
///
/// The flattening reproduces the previous reader's OUTPUT shape exactly, so
/// well-formed packets seed byte-identically:
/// <list type="bullet">
///   <item>each scalar is recorded under its dotted path
///         (<c>implementation_issue_packet.target_repo</c>) AND, first-wins,
///         under its bare key (<c>target_repo</c>);</item>
///   <item>quoted scalars lose their quotes and plain scalars lose a trailing
///         <c>#</c> comment — the YAML parser does both natively;</item>
///   <item>a key with no inline value records nothing, exactly as before;</item>
///   <item>a FLOW sequence (<c>dependencies: [G1, G2]</c>) records its
///         bracketed text, which is what the previous reader captured and what
///         <c>ParsePacketArrayField</c> expects. A BLOCK sequence records
///         nothing — also as before. That second case is a pre-existing
///         data-loss bug (block-style <c>dependencies</c> are silently dropped
///         from the seed), deliberately preserved here because this unit is
///         about WHERE parsing happens, not about changing what a packet
///         means; it is listed for follow-up rather than fixed in passing.</item>
/// </list>
/// </summary>
internal sealed class PacketYamlDocument
{
    private PacketYamlDocument(IReadOnlyDictionary<string, string> fields)
    {
        Fields = fields;
    }

    /// <summary>Dotted-path scalar map, with bare-key aliases.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; }

    /// <summary>
    /// Parses <paramref name="yaml"/> as a whole document. Returns
    /// <see langword="false"/> with a named <paramref name="error"/> when the
    /// text is not a YAML mapping — the caller is expected to fail closed on
    /// that, never to fall back to a partial read.
    /// </summary>
    public static bool TryParse(string yaml, out PacketYamlDocument? document, out string error)
    {
        document = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(yaml))
        {
            error = "packet.yaml is empty.";
            return false;
        }

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode as YamlMappingNode;
        }
        // Same two-exception catch as the projection reader: YamlDotNet reports
        // most malformed documents as a YamlException, but some — an
        // unterminated flow sequence continuing across later lines — surface as
        // a bare InvalidOperationException from the node builder.
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            error = $"packet.yaml is not valid YAML: {exception.Message}";
            return false;
        }

        if (root is null)
        {
            error = "packet.yaml is empty or its top-level document is not a mapping.";
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(root, prefix: null, fields);
        document = new PacketYamlDocument(fields);
        return true;
    }

    private static void Flatten(YamlMappingNode mapping, string? prefix, Dictionary<string, string> fields)
    {
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key } || key.Length == 0)
            {
                continue;
            }

            var dottedPath = prefix is null ? key : $"{prefix}.{key}";

            switch (valueNode)
            {
                case YamlMappingNode nested:
                    Flatten(nested, dottedPath, fields);
                    break;

                case YamlSequenceNode { Style: YamlDotNet.Core.Events.SequenceStyle.Flow } flow:
                    Record(fields, dottedPath, key, RenderFlowSequence(flow));
                    break;

                case YamlScalarNode scalar when !string.IsNullOrEmpty(scalar.Value):
                    Record(fields, dottedPath, key, scalar.Value!);
                    break;

                // A block sequence, an empty scalar, or anything else: the
                // previous reader recorded nothing for these, and neither does
                // this one.
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Dotted path always wins; the bare-key alias is FIRST-WINS, matching the
    /// previous reader (<c>if (!fields.ContainsKey(key))</c>) so a nested key
    /// cannot shadow a top-level one that appeared earlier in the file.
    /// </summary>
    private static void Record(Dictionary<string, string> fields, string dottedPath, string key, string value)
    {
        fields[dottedPath] = value;
        if (!fields.ContainsKey(key))
        {
            fields[key] = value;
        }
    }

    private static string RenderFlowSequence(YamlSequenceNode sequence) =>
        "[" + string.Join(", ", sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value ?? string.Empty)) + "]";
}
