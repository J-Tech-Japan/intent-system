using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeConceptArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "domain_slug",
        "concept_source",
        "concept_text",
        "upstream_paths",
        "initial_goal",
        "constraints",
        "known_unknowns"
    ];

    public static string Serialize(ConceptIntakePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var lines = new List<string>
        {
            $"domain_slug: {packet.DomainSlug}",
            $"concept_source: {packet.ConceptSource}",
            $"concept_text: {Quote(packet.ConceptText)}"
        };

        AppendList(lines, "upstream_paths", packet.UpstreamPaths);
        lines.Add($"initial_goal: {Quote(packet.InitialGoal)}");
        AppendList(lines, "constraints", packet.Constraints);
        AppendList(lines, "known_unknowns", packet.KnownUnknowns);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static ConceptIntakePacket Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new ConceptIntakePacket
        {
            DomainSlug = GetRequiredScalar(values, "domain_slug"),
            ConceptSource = GetRequiredScalar(values, "concept_source"),
            ConceptText = GetRequiredScalar(values, "concept_text"),
            UpstreamPaths = GetRequiredList(values, "upstream_paths"),
            InitialGoal = GetRequiredScalar(values, "initial_goal"),
            Constraints = GetRequiredList(values, "constraints"),
            KnownUnknowns = GetRequiredList(values, "known_unknowns")
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
                        $"Intake concept YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Intake concept YAML field line is missing ':': '{line}'.");
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
                    $"Intake concept YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException(
                $"Intake concept YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException(
                $"Intake concept YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException(
                $"Intake concept YAML field '{key}' must be a list.")
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
