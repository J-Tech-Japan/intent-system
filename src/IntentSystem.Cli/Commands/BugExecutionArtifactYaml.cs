namespace IntentSystem.Cli.Commands;

internal sealed record BugExecutionArtifact
{
    public required string BugId { get; init; }

    public required string ReportRef { get; init; }

    public required string TriageRef { get; init; }

    public required string DownstreamAction { get; init; }

    public required IReadOnlyList<string> ImplementationTaskCandidates { get; init; }

    public required IReadOnlyList<string> IntentTaskCandidates { get; init; }

    public required bool ClarificationRequired { get; init; }

    public required bool ReadyToLaunch { get; init; }
}

internal static class BugExecutionArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "report_ref",
        "triage_ref",
        "downstream_action",
        "implementation_task_candidates",
        "intent_task_candidates",
        "clarification_required",
        "ready_to_launch"
    ];

    public static string Serialize(BugExecutionArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"report_ref: {Quote(artifact.ReportRef)}",
            $"triage_ref: {Quote(artifact.TriageRef)}",
            $"downstream_action: {artifact.DownstreamAction}",
            $"clarification_required: {artifact.ClarificationRequired.ToString().ToLowerInvariant()}",
            $"ready_to_launch: {artifact.ReadyToLaunch.ToString().ToLowerInvariant()}"
        };

        AppendList(lines, "implementation_task_candidates", artifact.ImplementationTaskCandidates);
        AppendList(lines, "intent_task_candidates", artifact.IntentTaskCandidates);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugExecutionArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugExecutionArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            ReportRef = GetRequiredScalar(values, "report_ref"),
            TriageRef = GetRequiredScalar(values, "triage_ref"),
            DownstreamAction = GetRequiredScalar(values, "downstream_action"),
            ImplementationTaskCandidates = GetRequiredList(values, "implementation_task_candidates"),
            IntentTaskCandidates = GetRequiredList(values, "intent_task_candidates"),
            ClarificationRequired = GetRequiredBoolean(values, "clarification_required"),
            ReadyToLaunch = GetRequiredBoolean(values, "ready_to_launch")
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
                        $"Bug execution YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException($"Bug execution YAML field line is missing ':': '{line}'.");
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
                throw new InvalidOperationException($"Bug execution YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug execution YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug execution YAML field '{key}' must be a boolean.");
        }

        return booleanValue;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug execution YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException($"Bug execution YAML field '{key}' must be a list.")
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
