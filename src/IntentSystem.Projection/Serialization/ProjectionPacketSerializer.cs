using IntentSystem.Projection.Models;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Projection.Serialization;

public static class ProjectionPacketSerializer
{
    private static readonly string[] RequiredImplementationFields =
    [
        "issue_title",
        "issue_kind",
        "source_execution_unit",
        "goal",
        "in_scope",
        "out_of_scope",
        "target_repo",
        "target_path",
        "target_part",
        "dependencies",
        "technical_baseline",
        "project_local_guide",
        "intent_baseline",
        "intent_references",
        "rules_and_specs",
        "acceptance_criteria",
        "verification_evidence",
        "review_mode",
        "completion_action",
        "landing_policy"
    ];

    private static readonly string[] RequiredReviewContextFields =
    [
        "source_execution_unit",
        "parent_intent_root",
        "intent_references",
        "rules_and_specs",
        "acceptance_criteria",
        "deterministic_review_checks",
        "clarification_return_path"
    ];

    public static ProjectionPacketContract Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var sections = ParseSections(yaml);
        var implementationValues = GetRequiredSection(sections, "implementation_issue_packet");
        var reviewContextValues = GetRequiredSection(sections, "review_context_packet");

        ValidateRequiredFields(implementationValues, RequiredImplementationFields, "Implementation issue packet");
        ValidateRequiredFields(reviewContextValues, RequiredReviewContextFields, "Review context packet");

        var implementationPacket = new ImplementationIssuePacket
        {
            IssueTitle = GetRequiredScalar(implementationValues, "issue_title"),
            IssueKind = ParseIssueKind(GetRequiredScalar(implementationValues, "issue_kind")),
            SourceExecutionUnit = GetRequiredScalar(implementationValues, "source_execution_unit"),
            Goal = GetRequiredScalar(implementationValues, "goal"),
            InScope = GetRequiredList(implementationValues, "in_scope"),
            OutOfScope = GetRequiredList(implementationValues, "out_of_scope"),
            TargetRepo = GetRequiredScalar(implementationValues, "target_repo"),
            TargetPath = GetRequiredScalar(implementationValues, "target_path"),
            TargetPart = GetRequiredScalar(implementationValues, "target_part"),
            Dependencies = GetRequiredList(implementationValues, "dependencies"),
            TechnicalBaseline = GetRequiredList(implementationValues, "technical_baseline"),
            ProjectLocalGuide = GetRequiredList(implementationValues, "project_local_guide"),
            IntentBaseline = GetRequiredList(implementationValues, "intent_baseline"),
            IntentReferences = GetRequiredList(implementationValues, "intent_references"),
            RulesAndSpecs = GetRequiredList(implementationValues, "rules_and_specs"),
            AcceptanceCriteria = GetRequiredList(implementationValues, "acceptance_criteria"),
            VerificationEvidence = GetRequiredList(implementationValues, "verification_evidence"),
            ReviewMode = GetRequiredScalar(implementationValues, "review_mode"),
            CompletionAction = GetRequiredScalar(implementationValues, "completion_action"),
            LandingPolicy = GetRequiredScalar(implementationValues, "landing_policy")
        };

        var reviewContextPacket = new ReviewContextPacket
        {
            SourceExecutionUnit = GetRequiredScalar(reviewContextValues, "source_execution_unit"),
            ParentIntentRoot = GetRequiredScalar(reviewContextValues, "parent_intent_root"),
            IntentReferences = GetRequiredList(reviewContextValues, "intent_references"),
            RulesAndSpecs = GetRequiredList(reviewContextValues, "rules_and_specs"),
            AcceptanceCriteria = GetRequiredList(reviewContextValues, "acceptance_criteria"),
            DeterministicReviewChecks = GetRequiredList(reviewContextValues, "deterministic_review_checks"),
            ClarificationReturnPath = GetRequiredScalar(reviewContextValues, "clarification_return_path")
        };

        if (!string.Equals(
                implementationPacket.SourceExecutionUnit,
                reviewContextPacket.SourceExecutionUnit,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Implementation issue packet execution unit must match review context packet execution unit.");
        }

        return new ProjectionPacketContract
        {
            ImplementationIssuePacket = implementationPacket,
            ReviewContextPacket = reviewContextPacket
        };
    }

    /// <summary>
    /// G565: reads the packet through YamlDotNet — the SAME YAML
    /// implementation the packet schema surfaces (<c>packet draft</c>,
    /// <c>queue-seed-from-packet</c>, <c>clarify open</c>, the facet checks) use
    /// to read the same file.
    ///
    /// This replaces a hand-rolled line reader whose acceptance was an
    /// approximation of YAML, so every legal construct it failed to anticipate
    /// became a projection-only failure. The field report is the shape of the
    /// whole class: a packet whose title contained an em-dash and a quoted
    /// <c>": "</c> was authored and validated happily by the packet surfaces,
    /// then rejected by projection with "contains invalid section header"
    /// (<c>clarify open SKS-G837</c>, 2026-07-31, v0.6.2). G534 had already
    /// patched one such gap (block-sequence indentation) and G561 another
    /// (required-section rejection); parsing YAML with a YAML parser removes
    /// the source rather than the next symptom.
    ///
    /// The projection CONTRACT is unchanged — the section/field requirements,
    /// their validation order, and their messages all still come from the code
    /// below this method. Only the question "what is valid YAML" moves, and it
    /// moves to the same answer the rest of the toolchain already gives.
    /// </summary>
    private static Dictionary<string, Dictionary<string, object>> ParseSections(string yaml)
    {
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode as YamlMappingNode;
        }
        // YamlDotNet reports most malformed documents as a YamlException, but
        // some — a flow sequence left unterminated across following lines, for
        // one — surface as a bare InvalidOperationException from the node
        // builder. Both are "this file is not YAML", and a caller must not have
        // to tell a parse failure apart from a contract violation by exception
        // type, so both are wrapped with the same diagnostic.
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Projection packet YAML could not be parsed: {exception.Message}");
        }

        if (root is null)
        {
            throw new InvalidOperationException("Projection packet YAML is empty or is not a mapping.");
        }

        var sections = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var (keyNode, valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } sectionName })
            {
                continue;
            }

            // A top-level entry that is not a mapping is not a section. It is
            // not an error either — the packet carries optional non-section
            // metadata, and only the sections this contract REQUIRES are
            // enforced, by GetRequiredSection below.
            if (valueNode is YamlMappingNode sectionNode)
            {
                sections[sectionName] = ReadSection(sectionNode);
            }
        }

        return sections;
    }

    /// <summary>
    /// G565: maps one section's entries onto the value shapes the rest of this
    /// contract already expects — a scalar becomes <see cref="string"/>, a
    /// sequence becomes <c>List&lt;string&gt;</c>, and everything else (an
    /// empty value, a nested mapping) becomes an empty list.
    ///
    /// That last case preserves the previous reader's behaviour exactly: it
    /// turned a valueless <c>key:</c> into an empty list, so a REQUIRED SCALAR
    /// left empty failed with "must be a scalar string" rather than passing as
    /// an empty string. Keeping the mapping means the same packet produces the
    /// same diagnostic it did before.
    /// </summary>
    private static Dictionary<string, object> ReadSection(YamlMappingNode section)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var (keyNode, valueNode) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key })
            {
                continue;
            }

            values[key] = valueNode switch
            {
                // A valueless `key:` is an empty PLAIN scalar. The previous
                // reader turned it into an empty list, so a required scalar
                // left blank failed with "must be a scalar string" — keep that,
                // or a packet with a blank title would start passing. An
                // explicitly quoted `key: ""` is a real empty string and stays
                // a scalar, exactly as before.
                YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } plain
                    when string.IsNullOrEmpty(plain.Value) => new List<string>(),
                YamlScalarNode scalar => scalar.Value!,
                YamlSequenceNode sequence => sequence.Children
                    .OfType<YamlScalarNode>()
                    .Select(item => item.Value ?? string.Empty)
                    .ToList(),
                _ => new List<string>(),
            };
        }

        return values;
    }

    private static Dictionary<string, object> GetRequiredSection(
        IReadOnlyDictionary<string, Dictionary<string, object>> sections,
        string sectionName)
    {
        if (!sections.TryGetValue(sectionName, out var sectionValues))
        {
            throw new InvalidOperationException(
                $"Projection packet YAML must contain required section '{sectionName}'.");
        }

        return sectionValues;
    }

    private static void ValidateRequiredFields(
        IReadOnlyDictionary<string, object> values,
        IReadOnlyList<string> requiredFields,
        string contractName)
    {
        foreach (var field in requiredFields)
        {
            if (!values.ContainsKey(field))
            {
                throw new InvalidOperationException(
                    $"{contractName} must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var value)
            || value is not string textValue)
        {
            throw new InvalidOperationException(
                $"Projection packet YAML field '{key}' must be a scalar string.");
        }

        return textValue;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException(
                $"Projection packet YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException(
                $"Projection packet YAML field '{key}' must be a list.")
        };
    }

    private static IssueKind ParseIssueKind(string issueKind)
    {
        return issueKind switch
        {
            "feature" => IssueKind.Feature,
            "bugfix" => IssueKind.Bugfix,
            "boundary-fix" => IssueKind.BoundaryFix,
            "verification" => IssueKind.Verification,
            "refactor" => IssueKind.Refactor,
            "clarification-followup" => IssueKind.ClarificationFollowup,
            _ => throw new InvalidOperationException(
                $"Projection packet YAML contains unsupported issue kind '{issueKind}'.")
        };
    }
}
