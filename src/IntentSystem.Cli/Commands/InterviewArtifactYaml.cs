using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class InterviewArtifactYaml
{
    internal sealed record ArtifactFile(string ArtifactPath, InterviewQueueItem Item);

    private static readonly string[] RequiredFields =
    [
        "artifact_kind",
        "domain_slug",
        "source_concept_ref",
        "question_id",
        "question_text",
        "reason",
        "affects",
        "blocking_or_nonblocking",
        "status",
        "return_to_intent_paths",
        "created_at",
        "answer"
    ];

    public static IReadOnlyList<ArtifactFile> Discover(string repoRoot, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var interviewsRoot = Path.Combine(
            repoRoot,
            ".intent-cli",
            "interviews",
            domain.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(interviewsRoot))
        {
            return [];
        }

        var items = new List<ArtifactFile>();
        foreach (var artifactPath in Directory.EnumerateFiles(interviewsRoot, "*.yaml", SearchOption.AllDirectories))
        {
            var item = Deserialize(File.ReadAllText(artifactPath));
            if (!string.Equals(item.DomainSlug, domain, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Interview artifact domain '{item.DomainSlug}' must match requested domain '{domain}'.");
            }

            items.Add(new ArtifactFile(artifactPath, item));
        }

        return items;
    }

    public static InterviewQueueItem Deserialize(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var values = ParseYaml(yaml);
        ValidateArtifactKind(values);
        ValidateRequiredFields(values);

        var status = ParseStatus(GetRequiredScalar(values, "status"));
        var answer = GetOptionalScalar(values, "answer");
        var answeredAt = GetOptionalDateTimeOffset(values, "answered_at");
        var recommendedUpdates = GetOptionalList(values, "recommended_updates");

        var item = new InterviewQueueItem
        {
            DomainSlug = GetRequiredScalar(values, "domain_slug"),
            SourceConceptRef = GetRequiredScalar(values, "source_concept_ref"),
            QuestionId = GetRequiredScalar(values, "question_id"),
            QuestionText = GetRequiredScalar(values, "question_text"),
            Reason = GetRequiredScalar(values, "reason"),
            Affects = GetRequiredList(values, "affects"),
            BlockingOrNonblocking = GetRequiredScalar(values, "blocking_or_nonblocking"),
            Status = status,
            ReturnToIntentPaths = GetRequiredList(values, "return_to_intent_paths"),
            CreatedAt = DateTimeOffset.Parse(GetRequiredScalar(values, "created_at")),
            Answer = answer,
            AnsweredAt = answeredAt,
            RecommendedUpdates = recommendedUpdates
        };

        ValidateStatusInvariant(item);
        return item;
    }

    public static string Serialize(InterviewQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateStatusInvariant(item);

        var lines = new List<string>
        {
            "artifact_kind: interview",
            $"domain_slug: {item.DomainSlug}",
            $"source_concept_ref: {Quote(item.SourceConceptRef)}",
            $"question_id: {item.QuestionId}",
            $"question_text: {Quote(item.QuestionText)}",
            $"reason: {Quote(item.Reason)}",
            "affects:"
        };

        lines.AddRange(item.Affects.Select(affect => $"  - {Quote(affect)}"));
        lines.Add($"blocking_or_nonblocking: {item.BlockingOrNonblocking}");
        lines.Add($"status: {FormatStatus(item.Status)}");
        lines.Add("return_to_intent_paths:");
        lines.AddRange(item.ReturnToIntentPaths.Select(path => $"  - {Quote(path)}"));
        lines.Add($"created_at: {Quote(item.CreatedAt.ToString("O"))}");
        lines.Add(item.Answer is null ? "answer: null" : $"answer: {Quote(item.Answer)}");

        if (item.AnsweredAt.HasValue)
        {
            lines.Add($"answered_at: {Quote(item.AnsweredAt.Value.ToString("O"))}");
        }

        if (item.RecommendedUpdates is not null)
        {
            lines.Add(item.RecommendedUpdates.Count == 0 ? "recommended_updates: []" : "recommended_updates:");
            if (item.RecommendedUpdates.Count > 0)
            {
                lines.AddRange(item.RecommendedUpdates.Select(update => $"  - {Quote(update)}"));
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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
                        $"Interview artifact YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[4..].TrimStart()));
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Interview artifact YAML field line is missing ':': '{line}'.");
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
                _ => ParseScalar(value)
            };
        }

        return values;
    }

    private static void ValidateArtifactKind(IReadOnlyDictionary<string, object?> values)
    {
        var artifactKind = GetRequiredScalar(values, "artifact_kind");
        if (!string.Equals(artifactKind, "interview", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Interview artifact YAML must use artifact_kind 'interview'.");
        }
    }

    private static void ValidateRequiredFields(IReadOnlyDictionary<string, object?> values)
    {
        foreach (var field in RequiredFields)
        {
            if (!values.ContainsKey(field))
            {
                throw new InvalidOperationException(
                    $"Interview artifact YAML must contain required field '{field}'.");
            }
        }
    }

    private static string GetRequiredScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new InvalidOperationException(
                $"Interview artifact YAML field '{key}' must be a scalar string.");
        }

        return text;
    }

    private static string? GetOptionalScalar(IReadOnlyDictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value as string : null;
    }

    private static DateTimeOffset? GetOptionalDateTimeOffset(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is not string text)
        {
            throw new InvalidOperationException(
                $"Interview artifact YAML field '{key}' must be a scalar string when present.");
        }

        return DateTimeOffset.Parse(text);
    }

    private static IReadOnlyList<string> GetRequiredList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException(
                $"Interview artifact YAML field '{key}' must be a list.");
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException(
                $"Interview artifact YAML field '{key}' must be a list.")
        };
    }

    private static IReadOnlyList<string>? GetOptionalList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException(
                $"Interview artifact YAML field '{key}' must be a list when present.")
        };
    }

    private static InterviewQueueItemStatus ParseStatus(string value)
    {
        return value switch
        {
            "open" => InterviewQueueItemStatus.Open,
            "answered" => InterviewQueueItemStatus.Answered,
            "applied" => InterviewQueueItemStatus.Applied,
            "cancelled" => InterviewQueueItemStatus.Cancelled,
            _ => throw new InvalidOperationException(
                $"Interview artifact YAML status '{value}' is not supported.")
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

    private static string FormatStatus(InterviewQueueItemStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
    }

    private static void ValidateStatusInvariant(InterviewQueueItem item)
    {
        var hasAnswer = item.Answer is not null;
        var hasAnsweredAt = item.AnsweredAt.HasValue;

        switch (item.Status)
        {
            case InterviewQueueItemStatus.Open when hasAnswer || hasAnsweredAt:
                throw new InvalidOperationException(
                    "Open interview artifacts must not contain answer metadata.");
            case InterviewQueueItemStatus.Answered or InterviewQueueItemStatus.Applied
                when !hasAnswer || !hasAnsweredAt:
                throw new InvalidOperationException(
                    $"Interview artifacts in status '{item.Status}' must contain answer and answered_at.");
        }
    }
}
