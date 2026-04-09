namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentEnqueueArtifact
{
    public required string BugId { get; init; }

    public required string IntentIssueRef { get; init; }

    public required string? AllocatedExecutionUnit { get; init; }

    public required string? LinkedIssueUrl { get; init; }

    public required int? LinkedIssueNumber { get; init; }

    public required IReadOnlyList<string> PacketPaths { get; init; }

    public required bool ReadyToEnqueue { get; init; }
}

internal static class BugIntentEnqueueArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "intent_issue_ref",
        "allocated_execution_unit",
        "linked_issue_url",
        "linked_issue_number",
        "packet_paths",
        "ready_to_enqueue"
    ];

    public static string Serialize(BugIntentEnqueueArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"intent_issue_ref: {Quote(artifact.IntentIssueRef)}",
            $"allocated_execution_unit: {FormatNullableScalar(artifact.AllocatedExecutionUnit)}",
            $"linked_issue_url: {FormatNullableScalar(artifact.LinkedIssueUrl)}",
            $"linked_issue_number: {FormatNullableInteger(artifact.LinkedIssueNumber)}",
            $"ready_to_enqueue: {artifact.ReadyToEnqueue.ToString().ToLowerInvariant()}"
        };

        AppendList(lines, "packet_paths", artifact.PacketPaths);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugIntentEnqueueArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugIntentEnqueueArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            IntentIssueRef = GetRequiredScalar(values, "intent_issue_ref"),
            AllocatedExecutionUnit = GetNullableScalar(values, "allocated_execution_unit"),
            LinkedIssueUrl = GetNullableScalar(values, "linked_issue_url"),
            LinkedIssueNumber = GetNullableInteger(values, "linked_issue_number"),
            PacketPaths = GetRequiredList(values, "packet_paths"),
            ReadyToEnqueue = GetRequiredBoolean(values, "ready_to_enqueue")
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
                        $"Bug intent-enqueue YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException($"Bug intent-enqueue YAML field line is missing ':': '{line}'.");
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
                "null" => null,
                "true" => true,
                "false" => false,
                _ when int.TryParse(value, out var integerValue) => integerValue,
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
                throw new InvalidOperationException($"Bug intent-enqueue YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static string? GetNullableScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            string text => text,
            _ => throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be a scalar string or null.")
        };
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be a boolean.");
        }

        return booleanValue;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be a list.")
        };
    }

    private static int? GetNullableInteger(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            int integerValue => integerValue,
            _ => throw new InvalidOperationException($"Bug intent-enqueue YAML field '{key}' must be an integer or null.")
        };
    }

    private static string FormatNullableScalar(string? value)
    {
        return value is null ? "null" : Quote(value);
    }

    private static string FormatNullableInteger(int? value)
    {
        return value?.ToString() ?? "null";
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
