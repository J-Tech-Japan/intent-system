using System.Text.Json;
using IntentSystem.Review.Models;

namespace IntentSystem.Review.Serialization;

public static class ReviewCommentArtifactSerializer
{
    private static readonly string[] RequiredFields =
    [
        "execution_unit",
        "review_request_ref",
        "linked_pr",
        "comment_ref",
        "body_path"
    ];

    public static string Serialize(ReviewCommentArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return JsonSerializer.Serialize(artifact, ReviewJsonOptions.Indented);
    }

    public static ReviewCommentArtifact Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement);

        return JsonSerializer.Deserialize<ReviewCommentArtifact>(json, ReviewJsonOptions.Compact)
            ?? throw new InvalidOperationException("Review comment artifact payload deserialized to null.");
    }

    private static void ValidateRequiredFields(JsonElement element)
    {
        foreach (var field in RequiredFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"Review comment artifact must contain required field '{field}'.");
            }
        }
    }
}
