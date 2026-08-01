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
    private PacketYamlDocument(
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sequences)
    {
        Fields = fields;
        Sequences = sequences;
    }

    /// <summary>A packet that is absent or empty: no fields, no sequences.</summary>
    public static readonly PacketYamlDocument Empty = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>Dotted-path scalar map, with bare-key aliases.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; }

    /// <summary>
    /// G568: dotted-path SEQUENCE map, with bare-key aliases — the same keying
    /// as <see cref="Fields"/>, and the reason a sequence no longer appears
    /// there at all.
    ///
    /// G567 kept a flow sequence as its bracketed TEXT and a block sequence as
    /// nothing, faithfully reproducing the reader it replaced. That was correct
    /// for a slice about where parsing happens, and it left real data loss in
    /// place: <c>dependencies:</c> written in block style never reached the
    /// queue at all. A dropped dependency is not cosmetic — dependency-aware
    /// selection reads the seeded list, so a dependent unit looks publish-ready
    /// while its root is still open, which is exactly what the ordering
    /// taxonomy exists to prevent.
    ///
    /// Both YAML styles now produce the SAME structured list, so how an author
    /// happened to write the sequence stops being a semantic difference.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Sequences { get; }

    /// <summary>
    /// First non-empty sequence among <paramref name="keys"/>, in caller order.
    /// Returns an empty list when none resolve — a packet that declares no
    /// dependencies legitimately produces an empty list, and absence is never
    /// guessed into content.
    /// </summary>
    public IReadOnlyList<string> LookupSequence(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (Sequences.TryGetValue(key, out var value) && value.Count > 0)
            {
                return value;
            }
        }

        return Array.Empty<string>();
    }

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
        var sequences = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        Flatten(root, prefix: null, fields, sequences);
        document = new PacketYamlDocument(fields, sequences);
        return true;
    }

    private static void Flatten(
        YamlMappingNode mapping,
        string? prefix,
        Dictionary<string, string> fields,
        Dictionary<string, IReadOnlyList<string>> sequences)
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
                    Flatten(nested, dottedPath, fields, sequences);
                    break;

                // G568: EVERY sequence — flow or block — becomes a structured
                // list under the same key. Style is a formatting choice, not a
                // semantic one, so the two must be indistinguishable downstream.
                case YamlSequenceNode sequence:
                    RecordSequence(sequences, dottedPath, key, ReadSequence(sequence));
                    break;

                case YamlScalarNode scalar when !string.IsNullOrEmpty(scalar.Value):
                    Record(fields, dottedPath, key, scalar.Value!);
                    break;

                // An empty scalar, or anything else: recorded as nothing, as
                // the previous reader did.
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

    /// <summary>Same first-wins alias rule as <see cref="Record"/>.</summary>
    private static void RecordSequence(
        Dictionary<string, IReadOnlyList<string>> sequences,
        string dottedPath,
        string key,
        IReadOnlyList<string> value)
    {
        sequences[dottedPath] = value;
        if (!sequences.ContainsKey(key))
        {
            sequences[key] = value;
        }
    }

    private static IReadOnlyList<string> ReadSequence(YamlSequenceNode sequence) =>
        sequence.Children
            .OfType<YamlScalarNode>()
            .Select(item => (item.Value ?? string.Empty).Trim())
            .Where(item => item.Length > 0)
            .ToArray();
}
