namespace IntentSystem.Cli.Commands;

internal sealed record DeveloperConfirmationArtifact
{
    public required string DomainSlug { get; init; }

    public required string SourceBundleArtifactPath { get; init; }

    public required IReadOnlyList<string> ReconstructedArtifactPaths { get; init; }

    public required string ReviewArtifactPath { get; init; }

    public required string DecisionFilePath { get; init; }

    public required IReadOnlyList<string> ConfirmedItems { get; init; }

    public required IReadOnlyList<string> RejectedItems { get; init; }

    public required IReadOnlyList<string> ClarifyItems { get; init; }

    public required IReadOnlyList<string> DeferredItems { get; init; }

    public required IReadOnlyList<string> BlockedItems { get; init; }

    public required string DownstreamReadiness { get; init; }

    public required IReadOnlyList<string> ReturnToIntentPaths { get; init; }
}

internal static class DeveloperConfirmationArtifactYaml
{
    private static readonly string[] RequiredFields =
    [
        "domain_slug",
        "source_bundle_artifact_path",
        "reconstructed_artifact_paths",
        "review_artifact_path",
        "decision_file_path",
        "confirmed_items",
        "rejected_items",
        "clarify_items",
        "deferred_items",
        "blocked_items",
        "downstream_readiness",
        "return_to_intent_paths"
    ];

    public static string Serialize(DeveloperConfirmationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var lines = new List<string>
        {
            $"domain_slug: {artifact.DomainSlug}",
            $"source_bundle_artifact_path: {Quote(artifact.SourceBundleArtifactPath)}",
            $"review_artifact_path: {Quote(artifact.ReviewArtifactPath)}",
            $"decision_file_path: {Quote(artifact.DecisionFilePath)}",
            $"downstream_readiness: {artifact.DownstreamReadiness}"
        };

        AppendList(lines, "reconstructed_artifact_paths", artifact.ReconstructedArtifactPaths);
        AppendList(lines, "confirmed_items", artifact.ConfirmedItems);
        AppendList(lines, "rejected_items", artifact.RejectedItems);
        AppendList(lines, "clarify_items", artifact.ClarifyItems);
        AppendList(lines, "deferred_items", artifact.DeferredItems);
        AppendList(lines, "blocked_items", artifact.BlockedItems);
        AppendList(lines, "return_to_intent_paths", artifact.ReturnToIntentPaths);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static DeveloperConfirmationArtifact Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateRequiredFields(values);

        return new DeveloperConfirmationArtifact
        {
            DomainSlug = GetRequiredScalar(values, "domain_slug"),
            SourceBundleArtifactPath = GetRequiredScalar(values, "source_bundle_artifact_path"),
            ReconstructedArtifactPaths = GetRequiredList(values, "reconstructed_artifact_paths"),
            ReviewArtifactPath = GetRequiredScalar(values, "review_artifact_path"),
            DecisionFilePath = GetRequiredScalar(values, "decision_file_path"),
            ConfirmedItems = GetRequiredList(values, "confirmed_items"),
            RejectedItems = GetRequiredList(values, "rejected_items"),
            ClarifyItems = GetRequiredList(values, "clarify_items"),
            DeferredItems = GetRequiredList(values, "deferred_items"),
            BlockedItems = GetRequiredList(values, "blocked_items"),
            DownstreamReadiness = GetRequiredScalar(values, "downstream_readiness"),
            ReturnToIntentPaths = GetRequiredList(values, "return_to_intent_paths")
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
                    throw new InvalidOperationException($"Developer confirmation YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException($"Developer confirmation YAML field line is missing ':': '{line}'.");
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
                throw new InvalidOperationException($"Developer confirmation YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException($"Developer confirmation YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"Developer confirmation YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException($"Developer confirmation YAML field '{key}' must be a list.")
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
