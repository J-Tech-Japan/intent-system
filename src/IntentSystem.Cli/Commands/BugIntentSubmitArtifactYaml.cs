namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentSubmitArtifact
{
    public required string BugId { get; init; }

    public required string IntentStartRef { get; init; }

    public required string? SubmittedExecutionUnit { get; init; }

    public required string? LinkedPrUrl { get; init; }

    public required int? LinkedPrNumber { get; init; }

    public required bool ReadyToSubmit { get; init; }
}

internal static class BugIntentSubmitArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "intent_start_ref",
        "submitted_execution_unit",
        "linked_pr_url",
        "linked_pr_number",
        "ready_to_submit"
    ];

    public static string Serialize(BugIntentSubmitArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"intent_start_ref: {Quote(artifact.IntentStartRef)}",
            $"submitted_execution_unit: {FormatNullableScalar(artifact.SubmittedExecutionUnit)}",
            $"linked_pr_url: {FormatNullableScalar(artifact.LinkedPrUrl)}",
            $"linked_pr_number: {FormatNullableInteger(artifact.LinkedPrNumber)}",
            $"ready_to_submit: {artifact.ReadyToSubmit.ToString().ToLowerInvariant()}"
        };

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugIntentSubmitArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugIntentSubmitArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            IntentStartRef = GetRequiredScalar(values, "intent_start_ref"),
            SubmittedExecutionUnit = GetNullableScalar(values, "submitted_execution_unit"),
            LinkedPrUrl = GetNullableScalar(values, "linked_pr_url"),
            LinkedPrNumber = GetNullableInteger(values, "linked_pr_number"),
            ReadyToSubmit = GetRequiredBoolean(values, "ready_to_submit")
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
                throw new InvalidOperationException($"Bug intent-submit YAML field line is missing ':': '{line}'.");
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].TrimStart();
            values[key] = value switch
            {
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
                throw new InvalidOperationException($"Bug intent-submit YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug intent-submit YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static string? GetNullableScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-submit YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            string text => text,
            _ => throw new InvalidOperationException($"Bug intent-submit YAML field '{key}' must be a scalar string or null.")
        };
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug intent-submit YAML field '{key}' must be a boolean.");
        }

        return booleanValue;
    }

    private static int? GetNullableInteger(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-submit YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            int integerValue => integerValue,
            _ => throw new InvalidOperationException($"Bug intent-submit YAML field '{key}' must be an integer or null.")
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
