using System.Text.Json;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.ConceptIntake.Serialization;

public static class InterviewQueueSerializer
{
    public static string Serialize(InterviewQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateStatusInvariant(item);
        return JsonSerializer.Serialize(item, ConceptIntakeJsonOptions.Indented);
    }

    public static InterviewQueueItem Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement);

        var item = JsonSerializer.Deserialize<InterviewQueueItem>(json, ConceptIntakeJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Interview queue item payload deserialized to null.");
        ValidateStatusInvariant(item);
        return item;
    }

    public static string SerializeAll(IReadOnlyList<InterviewQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            ValidateStatusInvariant(item);
        }

        return JsonSerializer.Serialize(items, ConceptIntakeJsonOptions.Indented);
    }

    public static IReadOnlyList<InterviewQueueItem> DeserializeAll(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Interview queue collection payload must be a JSON array.");
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            ValidateRequiredFields(element);
        }

        var items = JsonSerializer.Deserialize<InterviewQueueItem[]>(json, ConceptIntakeJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Interview queue collection payload deserialized to null.");
        foreach (var item in items)
        {
            ValidateStatusInvariant(item);
        }

        return items;
    }

    private static readonly string[] RequiredFields =
    [
        "question_id",
        "question_text",
        "reason",
        "affects",
        "blocking_or_nonblocking",
        "status",
        "domain_slug",
        "clarification_return_path"
    ];

    private static void ValidateRequiredFields(JsonElement element)
    {
        foreach (var field in RequiredFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"Interview queue item must contain required field '{field}'.");
            }
        }
    }

    private static void ValidateStatusInvariant(InterviewQueueItem item)
    {
        var hasAnswer = item.Answer is not null;
        var hasAnsweredAt = item.AnsweredAt.HasValue;

        switch (item.Status)
        {
            case InterviewQueueItemStatus.Open when hasAnswer || hasAnsweredAt:
                throw new InvalidOperationException(
                    "Open interview queue items must not contain answer metadata.");
            case InterviewQueueItemStatus.Answered or InterviewQueueItemStatus.Applied
                when !hasAnswer || !hasAnsweredAt:
                throw new InvalidOperationException(
                    $"Interview queue items in status '{item.Status}' must contain answer and answered_at.");
        }
    }
}
