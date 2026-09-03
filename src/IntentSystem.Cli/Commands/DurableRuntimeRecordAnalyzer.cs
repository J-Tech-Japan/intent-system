using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G791: validates forward-only host runtime records outside queue-state and
/// runs.jsonl. JSONL ledgers are accepted only when existing non-empty lines
/// remain byte-identical and every newly appended line is JSON. The measured
/// supervision emission policy is accepted only as a structurally valid
/// generated policy record; all other durable paths still route to review or
/// unsafe lanes in DurableStatePreflightAnalyzer.
/// </summary>
internal static class DurableRuntimeRecordAnalyzer
{
    public const string ClassificationAppendOnly = "append-only";
    public const string ClassificationValidPolicy = "valid-policy";
    public const string ClassificationNeedsOperatorReview = "needs-operator-review";
    public const string ClassificationInvalid = "invalid";

    public static bool IsCandidate(string path) =>
        IsAppendOnlyJsonlPath(path) || IsEmissionPolicyPath(path);

    public static DurableRuntimeRecordDelta Analyze(string path, string headContent, string workingContent)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(headContent);
        ArgumentNullException.ThrowIfNull(workingContent);

        var normalized = DurableHostStatePathClassifier.NormalizePath(path);
        if (IsAppendOnlyJsonlPath(normalized))
        {
            return AnalyzeAppendOnlyJsonl(normalized, headContent, workingContent);
        }
        if (IsEmissionPolicyPath(normalized))
        {
            return AnalyzeEmissionPolicy(normalized, workingContent);
        }
        return NeedsReview($"'{normalized}' is not a recognized append-only runtime record or generated supervision policy.");
    }

    private static DurableRuntimeRecordDelta AnalyzeAppendOnlyJsonl(string path, string headContent, string workingContent)
    {
        var headLines = SplitNonEmptyLines(headContent);
        var workingLines = SplitNonEmptyLines(workingContent);
        if (workingLines.Count < headLines.Count)
        {
            return NeedsReview($"'{path}' shrank from {headLines.Count} to {workingLines.Count} records; refuse append-only auto-commit.");
        }

        for (var index = 0; index < headLines.Count; index++)
        {
            if (!string.Equals(headLines[index], workingLines[index], StringComparison.Ordinal))
            {
                return NeedsReview($"'{path}' record {index + 1} was modified in place; refuse append-only auto-commit.");
            }
        }

        var appended = workingLines.Skip(headLines.Count).ToArray();
        if (appended.Length == 0)
        {
            return NeedsReview($"'{path}' is dirty without a new appended record; refuse auto-commit.");
        }

        for (var index = 0; index < appended.Length; index++)
        {
            try
            {
                using var _ = JsonDocument.Parse(appended[index]);
            }
            catch (JsonException exception)
            {
                return Invalid($"'{path}' appended record {headLines.Count + index + 1} is not valid JSON: {exception.Message}");
            }
        }

        return new DurableRuntimeRecordDelta
        {
            Classification = ClassificationAppendOnly,
            Summary = $"'{path}' is append-only with {appended.Length} new runtime record(s).",
            AppendedRecordCount = appended.Length,
        };
    }

    private static DurableRuntimeRecordDelta AnalyzeEmissionPolicy(string path, string workingContent)
    {
        try
        {
            using var document = JsonDocument.Parse(workingContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !HasNonEmptyString(document.RootElement, "domain")
                || !HasNonEmptyString(document.RootElement, "team")
                || !HasPositiveInt(document.RootElement, "full_cadence_seconds")
                || !HasPositiveInt(document.RootElement, "repeat_backoff_seconds")
                || !HasPositiveInt(document.RootElement, "debounce_consecutive_observations")
                || !HasNonEmptyString(document.RootElement, "recorded_at"))
            {
                return Invalid($"'{path}' is not a complete generated supervision emission policy.");
            }
        }
        catch (JsonException exception)
        {
            return Invalid($"'{path}' is not valid JSON: {exception.Message}");
        }

        return new DurableRuntimeRecordDelta
        {
            Classification = ClassificationValidPolicy,
            Summary = $"'{path}' is a valid generated supervision emission policy record.",
            AppendedRecordCount = 0,
        };
    }

    private static bool IsAppendOnlyJsonlPath(string path) =>
        path.EndsWith(".jsonl", StringComparison.Ordinal)
        && (path.StartsWith(".intent-cli/continuation-chains/", StringComparison.Ordinal)
            || path.StartsWith(".intent-cli/events/", StringComparison.Ordinal)
            || path.StartsWith(".intent-cli/notify/", StringComparison.Ordinal)
            || path.StartsWith(".intent-cli/supervision/", StringComparison.Ordinal));

    private static bool IsEmissionPolicyPath(string path) =>
        path.StartsWith(".intent-cli/supervision/", StringComparison.Ordinal)
        && path.EndsWith("/emission-policy.json", StringComparison.Ordinal);

    private static IReadOnlyList<string> SplitNonEmptyLines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

    private static bool HasNonEmptyString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(property.GetString());

    private static bool HasPositiveInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
        && value > 0;

    private static DurableRuntimeRecordDelta NeedsReview(string summary) => new()
    {
        Classification = ClassificationNeedsOperatorReview,
        Summary = summary,
        AppendedRecordCount = 0,
    };

    private static DurableRuntimeRecordDelta Invalid(string summary) => new()
    {
        Classification = ClassificationInvalid,
        Summary = summary,
        AppendedRecordCount = 0,
    };
}

internal sealed record DurableRuntimeRecordDelta
{
    public required string Classification { get; init; }
    public required string Summary { get; init; }
    public required int AppendedRecordCount { get; init; }
}
