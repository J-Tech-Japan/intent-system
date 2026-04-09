namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentReviewArtifact
{
    public required string BugId { get; init; }

    public required string IntentSubmitRef { get; init; }

    public required string? ReviewedExecutionUnit { get; init; }

    public required string? ReviewRequestRef { get; init; }

    public required string? LinkedPrUrl { get; init; }

    public required bool ReadyToReview { get; init; }
}

internal static class BugIntentReviewArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "intent_submit_ref",
        "reviewed_execution_unit",
        "review_request_ref",
        "linked_pr_url",
        "ready_to_review"
    ];

    public static string Serialize(BugIntentReviewArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"intent_submit_ref: {Quote(artifact.IntentSubmitRef)}",
            $"reviewed_execution_unit: {FormatNullableScalar(artifact.ReviewedExecutionUnit)}",
            $"review_request_ref: {FormatNullableScalar(artifact.ReviewRequestRef)}",
            $"linked_pr_url: {FormatNullableScalar(artifact.LinkedPrUrl)}",
            $"ready_to_review: {artifact.ReadyToReview.ToString().ToLowerInvariant()}"
        };

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugIntentReviewArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugIntentReviewArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            IntentSubmitRef = GetRequiredScalar(values, "intent_submit_ref"),
            ReviewedExecutionUnit = GetNullableScalar(values, "reviewed_execution_unit"),
            ReviewRequestRef = GetNullableScalar(values, "review_request_ref"),
            LinkedPrUrl = GetNullableScalar(values, "linked_pr_url"),
            ReadyToReview = GetRequiredBoolean(values, "ready_to_review")
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
                throw new InvalidOperationException($"Bug intent-review YAML field line is missing ':': '{line}'.");
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
                throw new InvalidOperationException($"Bug intent-review YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug intent-review YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static string? GetNullableScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-review YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            string text => text,
            _ => throw new InvalidOperationException($"Bug intent-review YAML field '{key}' must be a scalar string or null.")
        };
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug intent-review YAML field '{key}' must be a boolean.");
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
