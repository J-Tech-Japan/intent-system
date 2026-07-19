using IntentSystem.Projection.Models;

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

    private static Dictionary<string, Dictionary<string, object>> ParseSections(string yaml)
    {
        var sections = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        Dictionary<string, object>? currentSection = null;
        string? currentListKey = null;

        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                if (!line.EndsWith(':'))
                {
                    throw new InvalidOperationException(
                        $"Projection packet YAML contains invalid section header '{line}'.");
                }

                var sectionName = line[..^1];
                currentSection = new Dictionary<string, object>(StringComparer.Ordinal);
                sections[sectionName] = currentSection;
                currentListKey = null;
                continue;
            }

            if (currentSection is null)
            {
                throw new InvalidOperationException(
                    "Projection packet YAML must declare a section before its fields.");
            }

            // G534 review repair: a YAML block-sequence item may be
            // indented at the SAME column as its parent key (the common
            // convention, e.g. "  intent_references:\n  - foo") or nested
            // one level deeper (this project's own renderer convention,
            // "    - foo") — both are valid YAML. Detect a list item by
            // its CONTENT ("- " once leading spaces are stripped), not by
            // a fixed column count, so either convention — and any mix of
            // the two across different fields in the same file — parses
            // identically. Previously only the 4-space form was accepted;
            // a 2-space list item fell through to the generic field-line
            // branch below and failed with "field line is missing ':'"
            // (no colon in "- foo"), rejecting every hand-authored packet
            // using the more common convention.
            var listItemCandidate = line.TrimStart(' ');
            if (listItemCandidate.StartsWith("- ", StringComparison.Ordinal) || listItemCandidate == "-")
            {
                if (currentListKey is null
                    || !currentSection.TryGetValue(currentListKey, out var listValue)
                    || listValue is not List<string> list)
                {
                    throw new InvalidOperationException(
                        $"Projection packet YAML contains list item without a list field: '{line.Trim()}'.");
                }

                var itemText = listItemCandidate.Length > 1 ? listItemCandidate[2..] : string.Empty;
                list.Add(ParseScalar(itemText));
                continue;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Projection packet YAML contains invalid indentation: '{line}'.");
            }

            var trimmed = line[2..];
            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Projection packet YAML field line is missing ':': '{trimmed}'.");
            }

            var key = trimmed[..separatorIndex];
            var value = trimmed[(separatorIndex + 1)..].TrimStart();

            if (value.Length == 0)
            {
                currentSection[key] = new List<string>();
                currentListKey = key;
                continue;
            }

            currentListKey = null;

            if (value == "[]")
            {
                currentSection[key] = Array.Empty<string>();
                continue;
            }

            currentSection[key] = ParseScalar(value);
        }

        return sections;
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

    private static string ParseScalar(string value)
    {
        if (value.Length >= 2
            && value[0] == '"'
            && value[^1] == '"')
        {
            return Unescape(value[1..^1]);
        }

        return value;
    }

    private static string Unescape(string value)
    {
        var chars = new List<char>(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current != '\\')
            {
                chars.Add(current);
                continue;
            }

            if (index == value.Length - 1)
            {
                throw new InvalidOperationException("Projection packet YAML contains an invalid escape sequence.");
            }

            index++;
            chars.Add(value[index] switch
            {
                '\\' => '\\',
                '"' => '"',
                'n' => '\n',
                'r' => '\r',
                _ => throw new InvalidOperationException(
                    "Projection packet YAML contains an unsupported escape sequence.")
            });
        }

        return new string(chars.ToArray());
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
