using System.Text.Json;
using IntentSystem.Workflow.Models;

namespace IntentSystem.Workflow.Serialization;

public static class WorkflowDefinitionSerializer
{
    public static string Serialize(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, WorkflowJsonOptions.Indented);
    }

    public static WorkflowDefinition Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement);

        return JsonSerializer.Deserialize<WorkflowDefinition>(json, WorkflowJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Workflow definition payload deserialized to null.");
    }

    private static readonly string[] RequiredFields =
    [
        "execution_unit",
        "packet_paths",
        "worker_roles",
        "dependency_snapshot",
        "entry_conditions",
        "steps",
        "success_signal",
        "review_mode",
        "completion_action"
    ];

    private static void ValidateRequiredFields(JsonElement element)
    {
        foreach (var field in RequiredFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"Workflow definition must contain required field '{field}'.");
            }
        }
    }
}
