using System.Text.Json;
using IntentSystem.Drift.Models;

namespace IntentSystem.Drift.Serialization;

public static class DriftClassificationSerializer
{
    private static readonly string[] RequiredTopLevelFields =
    [
        "items"
    ];

    private static readonly string[] RequiredItemFields =
    [
        "execution_unit",
        "classification",
        "changed_canonical_refs"
    ];

    public static string Serialize(DriftClassificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, DriftJsonOptions.Indented);
    }

    public static DriftClassificationReport Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement, RequiredTopLevelFields, "Drift classification report");

        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            ValidateRequiredFields(item, RequiredItemFields, "Drift classification item");
        }

        return JsonSerializer.Deserialize<DriftClassificationReport>(json, DriftJsonOptions.Compact)
            ?? throw new InvalidOperationException("Drift classification report payload deserialized to null.");
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
