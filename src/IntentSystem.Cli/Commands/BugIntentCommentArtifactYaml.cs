namespace IntentSystem.Cli.Commands;

internal sealed record BugIntentCommentArtifact
{
    public required string BugId { get; init; }

    public required string IntentReviewRef { get; init; }

    public required string? CommentedExecutionUnit { get; init; }

    public required string? ReviewCommentRef { get; init; }

    public required string? CommentRef { get; init; }

    public required string? LinkedPrUrl { get; init; }

    public required bool ReadyToComment { get; init; }
}

internal static class BugIntentCommentArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "bug_id",
        "intent_review_ref",
        "commented_execution_unit",
        "review_comment_ref",
        "comment_ref",
        "linked_pr_url",
        "ready_to_comment"
    ];

    public static string Serialize(BugIntentCommentArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"bug_id: {artifact.BugId}",
            $"intent_review_ref: {Quote(artifact.IntentReviewRef)}",
            $"commented_execution_unit: {FormatNullableScalar(artifact.CommentedExecutionUnit)}",
            $"review_comment_ref: {FormatNullableScalar(artifact.ReviewCommentRef)}",
            $"comment_ref: {FormatNullableScalar(artifact.CommentRef)}",
            $"linked_pr_url: {FormatNullableScalar(artifact.LinkedPrUrl)}",
            $"ready_to_comment: {artifact.ReadyToComment.ToString().ToLowerInvariant()}"
        };

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static BugIntentCommentArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new BugIntentCommentArtifact
        {
            BugId = GetRequiredScalar(values, "bug_id"),
            IntentReviewRef = GetRequiredScalar(values, "intent_review_ref"),
            CommentedExecutionUnit = GetNullableScalar(values, "commented_execution_unit"),
            ReviewCommentRef = GetNullableScalar(values, "review_comment_ref"),
            CommentRef = GetNullableScalar(values, "comment_ref"),
            LinkedPrUrl = GetNullableScalar(values, "linked_pr_url"),
            ReadyToComment = GetRequiredBoolean(values, "ready_to_comment")
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
                throw new InvalidOperationException($"Bug intent-comment YAML field line is missing ':': '{line}'.");
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
                throw new InvalidOperationException($"Bug intent-comment YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Bug intent-comment YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static string? GetNullableScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Bug intent-comment YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            string text => text,
            _ => throw new InvalidOperationException($"Bug intent-comment YAML field '{key}' must be a scalar string or null.")
        };
    }

    private static bool GetRequiredBoolean(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not bool booleanValue)
        {
            throw new InvalidOperationException($"Bug intent-comment YAML field '{key}' must be a boolean.");
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
