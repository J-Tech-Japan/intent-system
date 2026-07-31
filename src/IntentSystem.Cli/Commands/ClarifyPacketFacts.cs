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
/// The strict serializer is left untouched: publish-flow and review legitimately
/// demand a complete contract, and loosening it there would let an incomplete
/// packet through publication. This reader is scoped to what a clarification
/// record contains, and it is deliberately asymmetric about strictness:
///
/// - the source execution unit is REQUIRED, because it is the identity the
///   caller's queue item is checked against; guessing it would defeat the
///   mismatch guard that keeps a clarification from being filed against the
///   wrong unit;
/// - everything else is optional, because a scaffold has not filled it in yet
///   and a missing TODO is not a reason to refuse to record a blocking question;
/// - when the optional <c>review_context_packet</c> section IS present, its
///   values are surfaced so the caller can keep applying every cross-check it
///   applied before — an existing complete packet is validated exactly as
///   strictly as it always was.
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
    /// Reads <paramref name="yaml"/>. Throws <see cref="InvalidOperationException"/>
    /// only for the two things that genuinely block recording a clarification:
    /// YAML that does not parse at all, and a missing/blank
    /// <c>implementation_issue_packet.source_execution_unit</c>.
    /// </summary>
    public static ClarifyPacketFacts Read(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

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

        var reviewContext = GetMapping(root, "review_context_packet");

        return new ClarifyPacketFacts
        {
            SourceExecutionUnit = sourceExecutionUnit!,
            IssueTitle = GetScalar(implementation, "issue_title"),
            Goal = GetScalar(implementation, "goal"),
            TargetPart = GetScalar(implementation, "target_part"),
            IntentReferences = GetList(implementation, "intent_references"),
            HasReviewContextSection = reviewContext is not null,
            ReviewContextSourceExecutionUnit = reviewContext is null ? null : GetScalar(reviewContext, "source_execution_unit"),
            ReviewContextClarificationReturnPath = reviewContext is null ? null : GetScalar(reviewContext, "clarification_return_path"),
            ReviewContextIntentReferences = reviewContext is null ? null : GetList(reviewContext, "intent_references"),
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
