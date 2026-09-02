using System.Globalization;

namespace IntentSystem.Cli.Commands;

internal sealed record BugImplementationRepairArtifact
{
    public required string BugId { get; init; }

    public required string ExecutionRef { get; init; }

    public required IReadOnlyList<string> ImplementationTaskCandidates { get; init; }

    public required IReadOnlyList<string> ImplementationRepairTargets { get; init; }

    public required string SuggestedIssueTitle { get; init; }

    public required string SuggestedGoal { get; init; }

    public required bool ReadyToIssueCut { get; init; }

    public string? RepairExecutionUnit { get; init; }

    public int? RepairIssueNumber { get; init; }

    public string? RepairIssueUrl { get; init; }

    public string? RecordedBy { get; init; }

    public string? Note { get; init; }

    public DateTimeOffset? RecordedAt { get; init; }
}

internal static class BugImplementationRepairArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "execution_ref",
        "implementation_task_candidates",
        "implementation_repair_targets",
        "suggested_issue_title",
        "suggested_goal",
        "ready_to_issue_cut"
    ];

    public static string Serialize(BugImplementationRepairArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"execution_ref: {Quote(artifact.ExecutionRef)}",
            $"suggested_issue_title: {Quote(artifact.SuggestedIssueTitle)}",
            $"suggested_goal: {Quote(artifact.SuggestedGoal)}",
            $"ready_to_issue_cut: {artifact.ReadyToIssueCut.ToString().ToLowerInvariant()}"
        };

        AppendList(lines, "implementation_task_candidates", artifact.ImplementationTaskCandidates);
        AppendList(lines, "implementation_repair_targets", artifact.ImplementationRepairTargets);
        AppendOptionalScalar(lines, "repair_execution_unit", artifact.RepairExecutionUnit);
        AppendOptionalInteger(lines, "repair_issue_number", artifact.RepairIssueNumber);
        AppendOptionalScalar(lines, "repair_issue_url", artifact.RepairIssueUrl);
        AppendOptionalScalar(lines, "recorded_by", artifact.RecordedBy);
        AppendOptionalScalar(lines, "note", artifact.Note);
        AppendOptionalScalar(
            lines,
            "recorded_at",
            artifact.RecordedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugImplementationRepairArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugImplementationRepairArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            ExecutionRef = GetRequiredScalar(values, "execution_ref"),
            ImplementationTaskCandidates = GetRequiredList(values, "implementation_task_candidates"),
            ImplementationRepairTargets = GetRequiredList(values, "implementation_repair_targets"),
            SuggestedIssueTitle = GetRequiredScalar(values, "suggested_issue_title"),
            SuggestedGoal = GetRequiredScalar(values, "suggested_goal"),
            ReadyToIssueCut = GetRequiredBoolean(values, "ready_to_issue_cut"),
            RepairExecutionUnit = GetOptionalScalar(values, "repair_execution_unit"),
            RepairIssueNumber = GetOptionalPositiveInteger(values, "repair_issue_number"),
            RepairIssueUrl = GetOptionalScalar(values, "repair_issue_url"),
            RecordedBy = GetOptionalScalar(values, "recorded_by"),
            Note = GetOptionalScalar(values, "note"),
            RecordedAt = GetOptionalDateTimeOffset(values, "recorded_at")
        };
    }

    private static void AppendOptionalScalar(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {Quote(value)}");
        }
    }

    private static void AppendOptionalInteger(List<string> lines, string label, int? value)
    {
        if (value is not null)
        {
            lines.Add($"{label}: {value.Value.ToString(CultureInfo.InvariantCulture)}");
        }
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
                        $"Bug implementation-repair YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException($"Bug implementation-repair YAML field line is missing ':': '{line}'.");
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
                throw new InvalidOperationException($"Bug implementation-repair YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug implementation-repair YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug implementation-repair YAML field '{key}' must be a boolean.");
        }

        return booleanValue;
    }

    private static string? GetOptionalScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Bug implementation-repair YAML field '{key}' must be a non-empty scalar string when present.");
        }

        return text;
    }

    private static int? GetOptionalPositiveInteger(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is not string text
            || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            throw new InvalidOperationException($"Bug implementation-repair YAML field '{key}' must be a positive integer when present.");
        }

        return parsed;
    }

    private static DateTimeOffset? GetOptionalDateTimeOffset(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is not string text
            || !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new InvalidOperationException($"Bug implementation-repair YAML field '{key}' must be an ISO-8601 timestamp when present.");
        }

        return parsed;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug implementation-repair YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException($"Bug implementation-repair YAML field '{key}' must be a list.")
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
