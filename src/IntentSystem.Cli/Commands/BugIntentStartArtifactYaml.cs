namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentStartArtifact
{
    public required string BugId { get; init; }

    public required string IntentEnqueueRef { get; init; }

    public required string? AllocatedExecutionUnit { get; init; }

    public required string? WorktreePath { get; init; }

    public required string? BranchName { get; init; }

    public required bool ReadyToStart { get; init; }
}

internal static class BugIntentStartArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "intent_enqueue_ref",
        "allocated_execution_unit",
        "worktree_path",
        "branch_name",
        "ready_to_start"
    ];

    public static string Serialize(BugIntentStartArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"intent_enqueue_ref: {Quote(artifact.IntentEnqueueRef)}",
            $"allocated_execution_unit: {FormatNullableScalar(artifact.AllocatedExecutionUnit)}",
            $"worktree_path: {FormatNullableScalar(artifact.WorktreePath)}",
            $"branch_name: {FormatNullableScalar(artifact.BranchName)}",
            $"ready_to_start: {artifact.ReadyToStart.ToString().ToLowerInvariant()}"
        };

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugIntentStartArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugIntentStartArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            IntentEnqueueRef = GetRequiredScalar(values, "intent_enqueue_ref"),
            AllocatedExecutionUnit = GetNullableScalar(values, "allocated_execution_unit"),
            WorktreePath = GetNullableScalar(values, "worktree_path"),
            BranchName = GetNullableScalar(values, "branch_name"),
            ReadyToStart = GetRequiredBoolean(values, "ready_to_start")
        };
    }

    private static Dictionary<string, object?> ParseYaml(string yaml)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException($"Bug intent-start YAML field line is missing ':': '{line}'.");
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].TrimStart();
            values[key] = value switch
            {
                "null" => null,
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
                throw new InvalidOperationException($"Bug intent-start YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug intent-start YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static string? GetNullableScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-start YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            string text => text,
            _ => throw new InvalidOperationException($"Bug intent-start YAML field '{key}' must be a scalar string or null.")
        };
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug intent-start YAML field '{key}' must be a boolean.");
        }

        return booleanValue;
    }

    private static string FormatNullableScalar(string? value)
    {
        return value is null ? "null" : Quote(value);
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
