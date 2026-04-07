namespace IntentSystem.Cli.Commands;

internal static class CurrentSourcesArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "domain_slug",
        "source_root",
        "selected_altitudes",
        "selected_issue_scope",
        "selected_pr_scope",
        "selected_paths",
        "source_refs",
        "sampling_notes",
        "gaps"
    ];

    public static string Serialize(CurrentSourcesArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"domain_slug: {artifact.DomainSlug}",
            $"source_root: {Quote(artifact.SourceRoot)}"
        };

        AppendList(lines, "selected_altitudes", artifact.SelectedAltitudes);
        lines.Add($"selected_issue_scope: {Quote(artifact.SelectedIssueScope)}");
        lines.Add($"selected_pr_scope: {Quote(artifact.SelectedPrScope)}");
        AppendList(lines, "selected_paths", artifact.SelectedPaths);
        AppendList(lines, "source_refs", artifact.SourceRefs);
        AppendList(lines, "sampling_notes", artifact.SamplingNotes);
        AppendList(lines, "gaps", artifact.Gaps);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static CurrentSourcesArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new CurrentSourcesArtifact
        {
            DomainSlug = GetRequiredScalar(values, "domain_slug"),
            SourceRoot = GetRequiredScalar(values, "source_root"),
            SelectedAltitudes = GetRequiredList(values, "selected_altitudes"),
            SelectedIssueScope = GetRequiredScalar(values, "selected_issue_scope"),
            SelectedPrScope = GetRequiredScalar(values, "selected_pr_scope"),
            SelectedPaths = GetRequiredList(values, "selected_paths"),
            SourceRefs = GetRequiredList(values, "source_refs"),
            SamplingNotes = GetRequiredList(values, "sampling_notes"),
            Gaps = GetRequiredList(values, "gaps")
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
                        $"Current sources YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Current sources YAML field line is missing ':': '{line}'.");
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
                    $"Current sources YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException(
                $"Current sources YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException(
                $"Current sources YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException(
                $"Current sources YAML field '{key}' must be a list.")
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
