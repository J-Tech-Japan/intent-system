namespace IntentSystem.Cli.Commands;

internal sealed record IssuePublishArtifact
{
    public required string ExecutionUnit { get; init; }

    public required string PublishStatus { get; init; }

    public required string PacketPath { get; init; }

    public required string IssueBodyPath { get; init; }

    public required int? CreatedIssueNumber { get; init; }

    public required string? CreatedIssueUrl { get; init; }

    public required string? PublishedLabelName { get; init; }
}

internal static class IssuePublishArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "execution_unit",
        "publish_status",
        "packet_path",
        "issue_body_path",
        "created_issue_number",
        "created_issue_url",
        "published_label_name"
    ];

    public static string Serialize(IssuePublishArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return string.Join(
                   Environment.NewLine,
                   [
                       $"execution_unit: {artifact.ExecutionUnit}",
                       $"publish_status: {artifact.PublishStatus}",
                       $"packet_path: {Quote(artifact.PacketPath)}",
                       $"issue_body_path: {Quote(artifact.IssueBodyPath)}",
                       $"created_issue_number: {FormatNullableInteger(artifact.CreatedIssueNumber)}",
                       $"created_issue_url: {FormatNullableScalar(artifact.CreatedIssueUrl)}",
                       $"published_label_name: {FormatNullableScalar(artifact.PublishedLabelName)}"
                   ])
               + Environment.NewLine;
    }

    public static IssuePublishArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new IssuePublishArtifact
        {
            ExecutionUnit = GetRequiredScalar(values, "execution_unit"),
            PublishStatus = GetRequiredScalar(values, "publish_status"),
            PacketPath = GetRequiredScalar(values, "packet_path"),
            IssueBodyPath = GetRequiredScalar(values, "issue_body_path"),
            CreatedIssueNumber = GetNullableInteger(values, "created_issue_number"),
            CreatedIssueUrl = GetNullableScalar(values, "created_issue_url"),
            PublishedLabelName = GetNullableScalar(values, "published_label_name")
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
                throw new InvalidOperationException($"Issue publish artifact YAML field line is missing ':': '{line}'.");
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].TrimStart();
            values[key] = value switch
            {
                "null" => null,
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
                throw new InvalidOperationException($"Issue publish artifact YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Issue publish artifact YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static int? GetNullableInteger(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Issue publish artifact YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            int integerValue => integerValue,
            _ => throw new InvalidOperationException($"Issue publish artifact YAML field '{key}' must be an integer or null.")
        };
    }

    private static string? GetNullableScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Issue publish artifact YAML field '{key}' must be present.");
        }

        return value switch
        {
            null => null,
            string text => text,
            _ => throw new InvalidOperationException($"Issue publish artifact YAML field '{key}' must be a scalar string or null.")
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

    private static string FormatNullableInteger(int? value)
    {
        return value?.ToString() ?? "null";
    }

    private static string FormatNullableScalar(string? value)
    {
        return value is null ? "null" : Quote(value);
    }

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            + "\"";
    }
}
