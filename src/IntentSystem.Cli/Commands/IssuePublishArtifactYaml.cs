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

    /// <summary>
    /// G307 canonical lifecycle state: one of
    /// <see cref="IssuePublishLifecycle.IssueCreated"/>,
    /// <see cref="IssuePublishLifecycle.Published"/>,
    /// <see cref="IssuePublishLifecycle.PrCreated"/>,
    /// <see cref="IssuePublishLifecycle.ClosedOut"/>. Optional for
    /// backward compatibility — artifacts produced before G307 are
    /// deserialized with <c>LifecycleState = null</c>; the lifecycle
    /// analyzer treats null as the implicit pre-G307 baseline equivalent
    /// to <c>issue-created</c>.
    /// </summary>
    public string? LifecycleState { get; init; }

    /// <summary>G307: PR number recorded when lifecycle reaches <c>pr-created</c>.</summary>
    public int? LinkedPrNumber { get; init; }

    /// <summary>G307: PR URL recorded when lifecycle reaches <c>pr-created</c>.</summary>
    public string? LinkedPrUrl { get; init; }

    /// <summary>G307: ISO-8601 UTC timestamp recorded when lifecycle reaches <c>closed-out</c>.</summary>
    public string? ClosedOutAt { get; init; }
}

/// <summary>G307: canonical lifecycle states for <see cref="IssuePublishArtifact.LifecycleState"/>.</summary>
internal static class IssuePublishLifecycle
{
    public const string IssueCreated = "issue-created";
    public const string Published = "published";
    public const string PrCreated = "pr-created";
    public const string ClosedOut = "closed-out";

    public static readonly IReadOnlyList<string> All = new[] { IssueCreated, Published, PrCreated, ClosedOut };

    public static bool IsKnown(string? state) =>
        !string.IsNullOrWhiteSpace(state) && All.Contains(state, StringComparer.Ordinal);

    public static int Rank(string? state) => state switch
    {
        IssueCreated => 0,
        Published => 1,
        PrCreated => 2,
        ClosedOut => 3,
        _ => 0  // null / unknown ⇒ implicit issue-created baseline
    };
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

        var lines = new List<string>
        {
            $"execution_unit: {artifact.ExecutionUnit}",
            $"publish_status: {artifact.PublishStatus}",
            $"packet_path: {Quote(artifact.PacketPath)}",
            $"issue_body_path: {Quote(artifact.IssueBodyPath)}",
            $"created_issue_number: {FormatNullableInteger(artifact.CreatedIssueNumber)}",
            $"created_issue_url: {FormatNullableScalar(artifact.CreatedIssueUrl)}",
            $"published_label_name: {FormatNullableScalar(artifact.PublishedLabelName)}"
        };

        // G307: emit the new lifecycle fields only when populated so artifacts
        // written by code paths that do not yet know about lifecycle remain
        // byte-stable until they upgrade.
        if (!string.IsNullOrEmpty(artifact.LifecycleState))
        {
            lines.Add($"lifecycle_state: {Quote(artifact.LifecycleState!)}");
        }
        if (artifact.LinkedPrNumber.HasValue)
        {
            lines.Add($"linked_pr_number: {FormatNullableInteger(artifact.LinkedPrNumber)}");
        }
        if (!string.IsNullOrEmpty(artifact.LinkedPrUrl))
        {
            lines.Add($"linked_pr_url: {FormatNullableScalar(artifact.LinkedPrUrl)}");
        }
        if (!string.IsNullOrEmpty(artifact.ClosedOutAt))
        {
            lines.Add($"closed_out_at: {Quote(artifact.ClosedOutAt!)}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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
            PublishedLabelName = GetNullableScalar(values, "published_label_name"),
            // G307 optional lifecycle fields — backward compatible with
            // artifacts written before this PR.
            LifecycleState = GetOptionalScalar(values, "lifecycle_state"),
            LinkedPrNumber = GetOptionalInteger(values, "linked_pr_number"),
            LinkedPrUrl = GetOptionalScalar(values, "linked_pr_url"),
            ClosedOutAt = GetOptionalScalar(values, "closed_out_at")
        };
    }

    private static string? GetOptionalScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }
        return value switch
        {
            null => null,
            string text => string.IsNullOrEmpty(text) ? null : text,
            _ => null
        };
    }

    private static int? GetOptionalInteger(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }
        return value switch
        {
            null => null,
            int n => n,
            _ => null
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
