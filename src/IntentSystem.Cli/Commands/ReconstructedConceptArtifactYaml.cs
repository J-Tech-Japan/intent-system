namespace IntentSystem.Cli.Commands;

internal static class ReconstructedConceptArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "domain_slug",
        "initial_goal",
        "candidate_intent_nodes",
        "candidate_user_context",
        "candidate_means",
        "candidate_rules",
        "candidate_specs",
        "candidate_execution_units",
        "confidence_by_altitude",
        "source_concept_refs"
    ];

    public static string Serialize(ReconstructedConceptArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"domain_slug: {artifact.DomainSlug}",
            $"initial_goal: {Quote(artifact.InitialGoal)}"
        };

        AppendList(lines, "candidate_intent_nodes", artifact.CandidateIntentNodes);
        AppendList(lines, "candidate_user_context", artifact.CandidateUserContext);
        AppendList(lines, "candidate_means", artifact.CandidateMeans);
        AppendList(lines, "candidate_rules", artifact.CandidateRules);
        AppendList(lines, "candidate_specs", artifact.CandidateSpecs);
        AppendList(lines, "candidate_execution_units", artifact.CandidateExecutionUnits);
        AppendList(lines, "confidence_by_altitude", artifact.ConfidenceByAltitude);
        AppendList(lines, "source_concept_refs", artifact.SourceConceptRefs);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static ReconstructedConceptArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new ReconstructedConceptArtifact
        {
            DomainSlug = GetRequiredScalar(values, "domain_slug"),
            InitialGoal = GetRequiredScalar(values, "initial_goal"),
            CandidateIntentNodes = GetRequiredList(values, "candidate_intent_nodes"),
            CandidateUserContext = GetRequiredList(values, "candidate_user_context"),
            CandidateMeans = GetRequiredList(values, "candidate_means"),
            CandidateRules = GetRequiredList(values, "candidate_rules"),
            CandidateSpecs = GetRequiredList(values, "candidate_specs"),
            CandidateExecutionUnits = GetRequiredList(values, "candidate_execution_units"),
            ConfidenceByAltitude = GetRequiredList(values, "confidence_by_altitude"),
            SourceConceptRefs = GetRequiredList(values, "source_concept_refs")
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
                        $"Reconstructed concept YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Reconstructed concept YAML field line is missing ':': '{line}'.");
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
                throw new InvalidOperationException(
                    $"Reconstructed concept YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException(
                $"Reconstructed concept YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException(
                $"Reconstructed concept YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException(
                $"Reconstructed concept YAML field '{key}' must be a list.")
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
