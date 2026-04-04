using System.Text.Json;
using IntentSystem.Review.Models;

namespace IntentSystem.Review.Serialization;

public static class ReviewRequestSerializer
{
    private static readonly string[] RequiredFields =
    [
        "execution_unit",
        "review_context_ref",
        "linked_pr",
        "deterministic_review_checks",
        "acceptance_criteria",
        "expected_evidence"
    ];

    public static string Serialize(ReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, ReviewJsonOptions.Indented);
    }

    public static ReviewRequest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement);

        return JsonSerializer.Deserialize<ReviewRequest>(json, ReviewJsonOptions.Compact)
            ?? throw new InvalidOperationException("Review request payload deserialized to null.");
    }

    private static void ValidateRequiredFields(JsonElement element)
    {
        foreach (var field in RequiredFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"Review request must contain required field '{field}'.");
            }
        }
    }
}
