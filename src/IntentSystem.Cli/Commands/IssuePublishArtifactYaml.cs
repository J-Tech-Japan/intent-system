namespace IntentSystem.Cli.Commands;

internal sealed record IssuePublishArtifact
{
    public required string ExecutionUnit { get; init; }

    public required string PublishStatus { get; init; }

    public required string PacketPath { get; init; }

    public required string IssueBodyPath { get; init; }
}

internal static class IssuePublishArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "execution_unit",
        "publish_status",
        "packet_path",
        "issue_body_path"
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
                       $"issue_body_path: {Quote(artifact.IssueBodyPath)}"
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
            IssueBodyPath = GetRequiredScalar(values, "issue_body_path")
        };
    }

    private static Dictionary<string, string> ParseYaml(string yaml)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

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
            values[key] = ParseScalar(value);
        }

        return values;
    }

    private static void ValidateRequiredFields(IReadOnlyDictionary<string, string> values)
    {
        foreach (var field in RequiredFields)
        {
            if (!values.ContainsKey(field))
            {
                throw new InvalidOperationException($"Issue publish artifact YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Issue publish artifact YAML field '{key}' must be a scalar string.");
        }

        return value;
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
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            + "\"";
    }
}
