using System.Text.Json;
using IntentSystem.DomainBinding.Models;

namespace IntentSystem.DomainBinding.Serialization;

public static class DomainBindingSerializer
{
    public static string SerializeSource(DomainExecutionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return JsonSerializer.Serialize(source, DomainBindingJsonOptions.Indented);
    }

    public static DomainExecutionSource DeserializeSource(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement, RequiredSourceFields, "Domain execution source");

        return JsonSerializer.Deserialize<DomainExecutionSource>(json, DomainBindingJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Domain execution source payload deserialized to null.");
    }

    public static string SerializeProjectionReadySlice(ProjectionReadySlice projectionReady)
    {
        ArgumentNullException.ThrowIfNull(projectionReady);
        return JsonSerializer.Serialize(projectionReady, DomainBindingJsonOptions.Indented);
    }

    public static ProjectionReadySlice DeserializeProjectionReadySlice(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement, RequiredProjectionReadyFields, "Projection-ready slice");

        return JsonSerializer.Deserialize<ProjectionReadySlice>(json, DomainBindingJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Projection-ready slice payload deserialized to null.");
    }

    private static readonly string[] RequiredSourceFields =
    [
        "source_kind",
        "dogfooding_track",
        "execution_unit",
        "goal",
        "target_repo",
        "target_path",
        "target_part",
        "dependencies",
        "success_signal",
        "review_mode",
        "completion_action",
        "landing_policy",
        "embedded_canonical_summary"
    ];

    private static readonly string[] RequiredProjectionReadyFields =
    [
        "execution_unit",
        "goal",
        "target_repo",
        "target_path",
        "target_part",
        "dependencies",
        "success_signal",
        "review_mode",
        "completion_action",
        "landing_policy",
        "dogfooding_track",
        "embedded_canonical_summary"
    ];

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
