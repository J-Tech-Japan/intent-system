using IntentSystem.Projection.Serialization;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G561: the few packet facts <c>clarify open</c> actually needs, read
/// tolerantly.
///
/// <c>clarify open</c> used to deserialize packet.yaml through the FULL
/// projection contract (<c>IntentSystem.Projection</c>'s
/// <c>ProjectionPacketSerializer</c>), which requires a
/// <c>review_context_packet</c> section plus twenty required
/// <c>implementation_issue_packet</c> fields. Packets produced by
/// <c>intent-cli packet draft</c> have neither: the scaffold carries
/// <c>implementation_issue_packet</c> / <c>intent_placement</c> /
/// <c>knowledge_updates</c> / <c>closeout_learning</c>, and review context lives
/// in <c>review-context.md</c> rather than in the packet. So every freshly
/// scaffolded packet was rejected — and the G552 design-decision flow, whose
/// whole point is to record a blocking question EARLY, was structurally
/// unavailable at exactly the moment it is needed.
///
/// The strict serializer is left untouched — publish-flow and review
/// legitimately demand a complete contract — and, review repair, it remains the
/// ONLY reader for any packet that DECLARES itself complete. Tolerance is not a
/// property of this reader; it is a property of the scaffold shape:
///
/// - a packet declaring a <c>review_context_packet</c> section is claiming to be
///   a complete projection packet, and is deserialized by
///   <c>ProjectionPacketSerializer</c> unchanged — same required fields, same
///   type checks, same validation order and messages, same failures. A
///   declared-but-broken packet fails exactly as loudly as it always did, and it
///   fails before any mutation;
/// - only a packet with NO such declaration takes the tolerant path below, and
///   there the source execution unit is still REQUIRED, because it is the
///   identity the caller's queue item is checked against; guessing it would
///   defeat the mismatch guard that keeps a clarification from being filed
///   against the wrong unit;
/// - everything else on that path is optional, because a scaffold has not filled
///   it in yet and an unfilled TODO is not a reason to refuse to record a
///   blocking question.
/// </summary>
internal sealed record ClarifyPacketFacts
{
    public required string SourceExecutionUnit { get; init; }

    public string? IssueTitle { get; init; }

    public string? Goal { get; init; }

    public string? TargetPart { get; init; }

    public required IReadOnlyList<string> IntentReferences { get; init; }

    /// <summary>True when the packet carries the full projection review-context section.</summary>
    public required bool HasReviewContextSection { get; init; }

    public string? ReviewContextSourceExecutionUnit { get; init; }

    public string? ReviewContextClarificationReturnPath { get; init; }

    public IReadOnlyList<string>? ReviewContextIntentReferences { get; init; }

    /// <summary>
    /// Reads <paramref name="yaml"/>, routing on ONE question: does the packet
    /// DECLARE a <c>review_context_packet</c> section?
    ///
    /// A packet that declares it is claiming to be a complete projection
    /// packet, so it is deserialized by the unchanged
    /// <see cref="ProjectionPacketSerializer"/> — same required fields, same
    /// type and ordering checks, same validation sequence, same messages, same
    /// failures. Tolerance must never be applied to a packet that says it is
    /// complete: a declared-but-broken packet has to fail exactly as loudly as
    /// it did before, and it fails before any mutation.
    ///
    /// Only a packet with NO such declaration takes the tolerant path — that
    /// is the `packet draft` scaffold, which never claimed completeness.
    ///
    /// The routing decision is made by scanning for the top-level section line
    /// itself, deliberately WITHOUT parsing: a complete packet must reach the
    /// strict serializer through the same bytes it always did, so no second
    /// parser can reject a packet the strict one would have accepted.
    /// </summary>
    public static ClarifyPacketFacts Read(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        if (DeclaresReviewContextSection(yaml))
        {
            var contract = ProjectionPacketSerializer.Deserialize(yaml);
            return new ClarifyPacketFacts
            {
                SourceExecutionUnit = contract.ImplementationIssuePacket.SourceExecutionUnit,
                IssueTitle = contract.ImplementationIssuePacket.IssueTitle,
                Goal = contract.ImplementationIssuePacket.Goal,
                TargetPart = contract.ImplementationIssuePacket.TargetPart,
                IntentReferences = contract.ImplementationIssuePacket.IntentReferences,
                HasReviewContextSection = true,
                ReviewContextSourceExecutionUnit = contract.ReviewContextPacket.SourceExecutionUnit,
                ReviewContextClarificationReturnPath = contract.ReviewContextPacket.ClarificationReturnPath,
                ReviewContextIntentReferences = contract.ReviewContextPacket.IntentReferences,
            };
        }

        return ReadScaffold(yaml);
    }

    /// <summary>
    /// True when a top-level <c>review_context_packet:</c> section line is
    /// present, using the strict parser's own rule for a section header (a line
    /// that starts at column 0). Presence is judged on the DECLARATION, never on
    /// what the value turns out to be — a section declared with a scalar or a
    /// sequence body is present-and-wrong, and must reach the strict serializer
    /// to fail there rather than being mistaken for an absent section and
    /// silently tolerated.
    /// </summary>
    private static bool DeclaresReviewContextSection(string yaml) =>
        yaml.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Any(line => line.Length > 0
                && !char.IsWhiteSpace(line[0])
                && line.StartsWith("review_context_packet:", StringComparison.Ordinal));

    /// <summary>
    /// The tolerant path, for a packet that never declared a review-context
    /// section. Throws <see cref="InvalidOperationException"/> only for the two
    /// things that genuinely block recording a clarification: YAML that does not
    /// parse at all, and a missing/blank
    /// <c>implementation_issue_packet.source_execution_unit</c>.
    /// </summary>
    private static ClarifyPacketFacts ReadScaffold(string yaml)
    {
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
            throw new InvalidOperationException(
                $"Projection packet YAML could not be parsed: {exception.Message}");
        }

        if (root is null)
        {
            throw new InvalidOperationException("Projection packet YAML is empty or is not a mapping.");
        }

        var implementation = GetMapping(root, "implementation_issue_packet")
            ?? throw new InvalidOperationException(
                "Projection packet YAML must contain an 'implementation_issue_packet' section — "
                + "clarify open reads the execution-unit identity from it.");

        var sourceExecutionUnit = GetScalar(implementation, "source_execution_unit");
        if (string.IsNullOrWhiteSpace(sourceExecutionUnit))
        {
            throw new InvalidOperationException(
                "Projection packet YAML field 'implementation_issue_packet.source_execution_unit' is required — "
                + "it is the identity a clarification is filed against and must never be guessed.");
        }

        // No review-context values here by construction: this path only runs
        // for a packet that declared no such section, so there is nothing to
        // read and nothing to conflate.
        return new ClarifyPacketFacts
        {
            SourceExecutionUnit = sourceExecutionUnit!,
            IssueTitle = GetScalar(implementation, "issue_title"),
            Goal = GetScalar(implementation, "goal"),
            TargetPart = GetScalar(implementation, "target_part"),
            IntentReferences = GetList(implementation, "intent_references"),
            HasReviewContextSection = false,
        };
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
                .Select(scalar => scalar.Value ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToArray()
            : Array.Empty<string>();
}
