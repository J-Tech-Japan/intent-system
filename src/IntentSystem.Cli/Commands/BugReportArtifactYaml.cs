namespace IntentSystem.Cli.Commands;

internal sealed record BugReportArtifact
{
    public required string DomainSlug { get; init; }

    public required string BugId { get; init; }

    public required string Title { get; init; }

    public required string ReportSource { get; init; }

    public required string ProblemStatement { get; init; }

    public required IReadOnlyList<string> OriginalInstructionRefs { get; init; }

    public required IReadOnlyList<string> LinkedExecutionUnits { get; init; }

    public required IReadOnlyList<string> LinkedIssueRefs { get; init; }

    public required IReadOnlyList<string> LinkedPrRefs { get; init; }

    public required IReadOnlyList<string> LinkedReviewRefs { get; init; }
}

internal static class BugReportArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "domain_slug",
        "bug_id",
        "title",
        "report_source",
        "problem_statement",
        "original_instruction_refs",
        "linked_execution_units",
        "linked_issue_refs",
        "linked_pr_refs",
        "linked_review_refs"
    ];

    public static string Serialize(BugReportArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"domain_slug: {artifact.DomainSlug}",
            $"bug_id: {artifact.BugId}",
            $"title: {Quote(artifact.Title)}",
            $"report_source: {artifact.ReportSource}",
            $"problem_statement: {Quote(artifact.ProblemStatement)}"
        };

        AppendList(lines, "original_instruction_refs", artifact.OriginalInstructionRefs);
        AppendList(lines, "linked_execution_units", artifact.LinkedExecutionUnits);
        AppendList(lines, "linked_issue_refs", artifact.LinkedIssueRefs);
        AppendList(lines, "linked_pr_refs", artifact.LinkedPrRefs);
        AppendList(lines, "linked_review_refs", artifact.LinkedReviewRefs);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugReportArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugReportArtifact
        {
            DomainSlug = GetRequiredScalar(values, "domain_slug"),
            BugId = GetRequiredScalar(values, "bug_id"),
            Title = GetRequiredScalar(values, "title"),
            ReportSource = GetRequiredScalar(values, "report_source"),
            ProblemStatement = GetRequiredScalar(values, "problem_statement"),
            OriginalInstructionRefs = GetRequiredList(values, "original_instruction_refs"),
            LinkedExecutionUnits = GetRequiredList(values, "linked_execution_units"),
            LinkedIssueRefs = GetRequiredList(values, "linked_issue_refs"),
            LinkedPrRefs = GetRequiredList(values, "linked_pr_refs"),
            LinkedReviewRefs = GetRequiredList(values, "linked_review_refs")
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
                        $"Bug report YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException($"Bug report YAML field line is missing ':': '{line}'.");
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
                throw new InvalidOperationException($"Bug report YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug report YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug report YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException($"Bug report YAML field '{key}' must be a list.")
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
