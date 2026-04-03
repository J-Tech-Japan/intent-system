using System.Text.Json;
using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Serialization;

public static class ProjectionSourceSerializer
{
    private static readonly string[] RequiredRowFields =
    [
        "source_execution_unit",
        "goal",
        "target_repo",
        "target_path",
        "target_part",
        "success_signal",
        "review_mode",
        "completion_action",
        "landing_policy"
    ];

    private static readonly string[] RequiredContextFields =
    [
        "issue_title",
        "issue_kind",
        "parent_intent_root",
        "clarification_return_path"
    ];

    public static string Serialize(ProjectionSourceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return JsonSerializer.Serialize(bundle, ProjectionJsonOptions.Indented);
    }

    public static ProjectionSourceBundle Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("row", out var rowElement))
        {
            throw new InvalidOperationException(
                "Projection source bundle must contain required field 'row'.");
        }

        if (!root.TryGetProperty("context", out var contextElement))
        {
            throw new InvalidOperationException(
                "Projection source bundle must contain required field 'context'.");
        }

        ValidateRequiredFields(rowElement, RequiredRowFields, "Projection source row");
        ValidateRequiredFields(contextElement, RequiredContextFields, "Projection source context");

        return JsonSerializer.Deserialize<ProjectionSourceBundle>(json, ProjectionJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Projection source bundle payload deserialized to null.");
    }

    private static void ValidateRequiredFields(
        JsonElement element,
        IReadOnlyList<string> requiredFields,
        string contractName)
    {
        foreach (var field in requiredFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"{contractName} must contain required field '{field}'.");
            }
        }
    }
}
