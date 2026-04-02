using System.Text.Json;
using IntentSystem.Clarify.Models;

namespace IntentSystem.Clarify.Serialization;

public static class ClarificationSerializer
{
    public static string Serialize(ClarificationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateStateInvariant(item);
        return JsonSerializer.Serialize(item, ClarifyJsonOptions.Indented);
    }

    public static ClarificationItem Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateArtifactKind(document.RootElement);
        ValidateRequiredContractFields(document.RootElement);

        var item = JsonSerializer.Deserialize<ClarificationItem>(json, ClarifyJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Clarification payload deserialized to null.");
        ValidateStateInvariant(item);
        return item;
    }

    public static string SerializeAll(IReadOnlyList<ClarificationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            ValidateStateInvariant(item);
        }

        return JsonSerializer.Serialize(items, ClarifyJsonOptions.Indented);
    }

    public static IReadOnlyList<ClarificationItem> DeserializeAll(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Clarification collection payload must be a JSON array.");
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            ValidateArtifactKind(element);
        }

        var items = JsonSerializer.Deserialize<ClarificationItem[]>(json, ClarifyJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Clarification collection payload deserialized to null.");
        foreach (var item in items)
        {
            ValidateStateInvariant(item);
        }

        return items;
    }

    private static void ValidateArtifactKind(JsonElement element)
    {
        if (!element.TryGetProperty("artifact_kind", out var artifactKindElement))
        {
            throw new InvalidOperationException(
                "Clarification payload must contain artifact_kind.");
        }

        var artifactKind = artifactKindElement.GetString();
        if (!string.Equals(
            artifactKind,
            ClarificationConstants.ArtifactKind,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Clarification payload must use artifact_kind '{ClarificationConstants.ArtifactKind}'.");
        }
    }

    private static readonly string[] RequiredContractFields =
    [
        "clarification_source",
        "question_id",
        "question_text",
        "reason",
        "affected_intents",
        "affected_execution_units",
        "blocking_or_nonblocking",
        "clarification_return_path"
    ];

    private static void ValidateRequiredContractFields(JsonElement element)
    {
        foreach (var field in RequiredContractFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"Clarification payload must contain required contract field '{field}'.");
            }
        }
    }

    private static void ValidateStateInvariant(ClarificationItem item)
    {
        var hasAnswer = item.Answer is not null;
        var hasAnsweredAt = item.AnsweredAt.HasValue;

        switch (item.State)
        {
            case ClarificationState.Open when hasAnswer || hasAnsweredAt:
                throw new InvalidOperationException(
                    "Open clarification items must not contain answer metadata.");
            case ClarificationState.Answered or ClarificationState.Applied
                when !hasAnswer || !hasAnsweredAt:
                throw new InvalidOperationException(
                    $"Clarification items in state '{item.State}' must contain answer and answered_at.");
        }
    }
}
