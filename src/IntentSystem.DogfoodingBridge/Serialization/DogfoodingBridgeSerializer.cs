using System.Text.Json;
using IntentSystem.DogfoodingBridge.Models;

namespace IntentSystem.DogfoodingBridge.Serialization;

public static class DogfoodingBridgeSerializer
{
    public static string Serialize(DogfoodingBridgeContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return JsonSerializer.Serialize(contract, DogfoodingBridgeJsonOptions.Indented);
    }

    public static DogfoodingBridgeContract Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredFields(document.RootElement);

        return JsonSerializer.Deserialize<DogfoodingBridgeContract>(json, DogfoodingBridgeJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Dogfooding bridge payload deserialized to null.");
    }

    private static readonly string[] RequiredFields =
    [
        "binding",
        "queue_input",
        "workflow_input",
        "return_routes"
    ];

    private static readonly string[] RequiredQueueInputFields =
    [
        "execution_unit",
        "packet_paths",
        "dependencies",
        "clarification_return_path",
        "worker_role",
        "review_role"
    ];

    private static readonly string[] RequiredWorkflowInputFields =
    [
        "execution_unit",
        "packet_paths",
        "dependency_snapshot",
        "worker_roles",
        "entry_conditions",
        "review_mode",
        "completion_action"
    ];

    private static readonly string[] RequiredReturnRouteFields =
    [
        "clarification_return_path",
        "interview_return_to_intent_paths"
    ];

    private static void ValidateRequiredFields(JsonElement root)
    {
        ValidateRequiredFields(root, RequiredFields, "Dogfooding bridge");
        ValidateRequiredFields(root.GetProperty("queue_input"), RequiredQueueInputFields, "Dogfooding bridge queue_input");
        ValidateRequiredFields(root.GetProperty("workflow_input"), RequiredWorkflowInputFields, "Dogfooding bridge workflow_input");
        ValidateRequiredFields(root.GetProperty("return_routes"), RequiredReturnRouteFields, "Dogfooding bridge return_routes");
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
