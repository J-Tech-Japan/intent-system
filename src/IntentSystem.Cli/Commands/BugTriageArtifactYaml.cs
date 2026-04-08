namespace IntentSystem.Cli.Commands;

internal sealed record BugTriageArtifact
{
    public required string BugId { get; init; }

    public required string ReportRef { get; init; }

    public required string Classification { get; init; }

    public required string DownstreamAction { get; init; }

    public required bool ClarificationRequired { get; init; }

    public required IReadOnlyList<string> ClarificationReasons { get; init; }

    public required IReadOnlyList<string> OriginalInstructionRootRefs { get; init; }

    public required IReadOnlyList<string> LinkedReviewRefs { get; init; }

    public required IReadOnlyList<string> ResolvedExecutionUnits { get; init; }

    public required IReadOnlyList<string> ResolvedImplementationRefs { get; init; }

    public required IReadOnlyList<string> ResolvedReviewContextRefs { get; init; }

    public required IReadOnlyList<string> ResolvedPacketRefs { get; init; }

    public required IReadOnlyList<string> UnresolvedExecutionUnits { get; init; }

    public required IReadOnlyList<string> ImplementationRepairCandidates { get; init; }

    public required IReadOnlyList<string> IntentRepairCandidates { get; init; }
}

internal static class BugTriageArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "report_ref",
        "classification",
        "downstream_action",
        "clarification_required",
        "clarification_reasons",
        "original_instruction_root_refs",
        "linked_review_refs",
        "resolved_execution_units",
        "resolved_implementation_refs",
        "resolved_review_context_refs",
        "resolved_packet_refs",
        "unresolved_execution_units",
        "implementation_repair_candidates",
        "intent_repair_candidates"
    ];

    public static string Serialize(BugTriageArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"report_ref: {Quote(artifact.ReportRef)}",
            $"classification: {artifact.Classification}",
            $"downstream_action: {artifact.DownstreamAction}",
            $"clarification_required: {artifact.ClarificationRequired.ToString().ToLowerInvariant()}"
        };

        AppendList(lines, "clarification_reasons", artifact.ClarificationReasons);
        AppendList(lines, "original_instruction_root_refs", artifact.OriginalInstructionRootRefs);
        AppendList(lines, "linked_review_refs", artifact.LinkedReviewRefs);
        AppendList(lines, "resolved_execution_units", artifact.ResolvedExecutionUnits);
        AppendList(lines, "resolved_implementation_refs", artifact.ResolvedImplementationRefs);
        AppendList(lines, "resolved_review_context_refs", artifact.ResolvedReviewContextRefs);
        AppendList(lines, "resolved_packet_refs", artifact.ResolvedPacketRefs);
        AppendList(lines, "unresolved_execution_units", artifact.UnresolvedExecutionUnits);
        AppendList(lines, "implementation_repair_candidates", artifact.ImplementationRepairCandidates);
        AppendList(lines, "intent_repair_candidates", artifact.IntentRepairCandidates);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugTriageArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugTriageArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            ReportRef = GetRequiredScalar(values, "report_ref"),
            Classification = GetRequiredScalar(values, "classification"),
            DownstreamAction = GetRequiredScalar(values, "downstream_action"),
            ClarificationRequired = GetRequiredBoolean(values, "clarification_required"),
            ClarificationReasons = GetRequiredList(values, "clarification_reasons"),
            OriginalInstructionRootRefs = GetRequiredList(values, "original_instruction_root_refs"),
            LinkedReviewRefs = GetRequiredList(values, "linked_review_refs"),
            ResolvedExecutionUnits = GetRequiredList(values, "resolved_execution_units"),
            ResolvedImplementationRefs = GetRequiredList(values, "resolved_implementation_refs"),
            ResolvedReviewContextRefs = GetRequiredList(values, "resolved_review_context_refs"),
            ResolvedPacketRefs = GetRequiredList(values, "resolved_packet_refs"),
            UnresolvedExecutionUnits = GetRequiredList(values, "unresolved_execution_units"),
            ImplementationRepairCandidates = GetRequiredList(values, "implementation_repair_candidates"),
            IntentRepairCandidates = GetRequiredList(values, "intent_repair_candidates")
        };
    }

    private static void AppendList(List<string> lines, string label, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            lines.Add($"{label}: []");
            return;
        }

        lines.Add($"{label}:");
        lines.AddRange(values.Select(value => $"  - {Quote(value)}"));
    }

    private static Dictionary<string, object?> ParseYaml(string yaml)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        string? currentListKey = null;

        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (currentListKey is null
                    || !values.TryGetValue(currentListKey, out var listValue)
                    || listValue is not List<string> list)
                {
                    throw new InvalidOperationException(
                        $"Bug triage YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException($"Bug triage YAML field line is missing ':': '{line}'.");
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].TrimStart();

            if (value.Length == 0)
            {
                values[key] = new List<string>();
                currentListKey = key;
                continue;
            }

            currentListKey = null;
            values[key] = value switch
            {
                "[]" => Array.Empty<string>(),
                "true" => true,
                "false" => false,
                _ => ParseScalar(value)
            };
        }

        return values;
    }

    private static void ValidateRequiredFields(IReadOnlyDictionary<string, object?> values)
    {
        foreach (var field in RequiredFields)
        {
            if (!values.ContainsKey(field))
            {
                throw new InvalidOperationException($"Bug triage YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug triage YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug triage YAML field '{key}' must be a boolean.");
        }

        return booleanValue;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug triage YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException($"Bug triage YAML field '{key}' must be a list.")
        };
    }

    private static string ParseScalar(string value)
    {
        if (value.Length >= 2
            && value[0] == '"'
            && value[^1] == '"')
        {
            return value[1..^1]
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value;
    }

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
    }
}
