using System.Text.Json;
using IntentSystem.WorkerAdapter.Models;

namespace IntentSystem.WorkerAdapter.Serialization;

public static class WorkerAdapterSerializer
{
    public static string SerializeRequest(WorkerAdapterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, WorkerAdapterJsonOptions.Indented);
    }

    public static WorkerAdapterRequest DeserializeRequest(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequestFields(document.RootElement);

        return JsonSerializer.Deserialize<WorkerAdapterRequest>(json, WorkerAdapterJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Worker adapter request payload deserialized to null.");
    }

    public static string SerializeResult(WorkerAdapterResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(result, WorkerAdapterJsonOptions.Indented);
    }

    public static WorkerAdapterResult DeserializeResult(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateResultFields(document.RootElement);

        return JsonSerializer.Deserialize<WorkerAdapterResult>(json, WorkerAdapterJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Worker adapter result payload deserialized to null.");
    }

    private static readonly string[] RequiredRequestFields =
    [
        "workflow_definition_ref",
        "run_id",
        "target_worktree",
        "runtime_env",
        "event_sink"
    ];

    private static readonly string[] RequiredRuntimeEnvironmentFields =
    [
        "engine",
        "arguments"
    ];

    private static readonly string[] RequiredEventSinkFields =
    [
        "sink_type",
        "sink_ref"
    ];

    private static readonly string[] RequiredResultFields =
    [
        "run_status",
        "step_statuses",
        "review_result",
        "review_comment_refs",
        "clarification_requests",
        "result_summary",
        "run_log_refs"
    ];

    private static readonly string[] RequiredReviewResultFields =
    [
        "disposition"
    ];

    private static readonly string[] RequiredStepStatusFields =
    [
        "step",
        "status"
    ];

    private static void ValidateRequestFields(JsonElement element)
    {
        ValidateRequiredFields(element, RequiredRequestFields, "Worker adapter request");
        ValidateRequiredFields(
            element.GetProperty("runtime_env"),
            RequiredRuntimeEnvironmentFields,
            "Worker adapter runtime_env");
        ValidateRequiredFields(
            element.GetProperty("event_sink"),
            RequiredEventSinkFields,
            "Worker adapter event_sink");
    }

    private static void ValidateResultFields(JsonElement element)
    {
        ValidateRequiredFields(element, RequiredResultFields, "Worker adapter result");
        ValidateRequiredFields(
            element.GetProperty("review_result"),
            RequiredReviewResultFields,
            "Worker adapter review_result");

        foreach (var stepStatus in element.GetProperty("step_statuses").EnumerateArray())
        {
            ValidateRequiredFields(
                stepStatus,
                RequiredStepStatusFields,
                "Worker adapter step_statuses item");
        }
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
