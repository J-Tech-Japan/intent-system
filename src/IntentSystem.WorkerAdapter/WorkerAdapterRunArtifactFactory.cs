using IntentSystem.WorkerAdapter.Models;
using IntentSystem.Workflow.Models;

namespace IntentSystem.WorkerAdapter;

public static class WorkerAdapterRunArtifactFactory
{
    public static WorkerAdapterResult CreateInitialResult(
        WorkerAdapterRequest request,
        WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(definition);

        var expectedWorkflowRef = $".intent-cli/workflows/{definition.ExecutionUnit}.yaml";
        if (!string.Equals(request.WorkflowDefinitionRef, expectedWorkflowRef, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Worker adapter request workflow definition ref '{request.WorkflowDefinitionRef}' must match execution unit '{definition.ExecutionUnit}'.");
        }

        var stepStatuses = new WorkerAdapterStepStatus[definition.Steps.Count];
        for (var index = 0; index < definition.Steps.Count; index++)
        {
            stepStatuses[index] = new WorkerAdapterStepStatus
            {
                Step = definition.Steps[index].Kind,
                Status = index == 0
                    ? WorkerAdapterStepState.Running
                    : WorkerAdapterStepState.Pending
            };
        }

        return new WorkerAdapterResult
        {
            RunStatus = WorkerAdapterRunStatus.Running,
            StepStatuses = stepStatuses,
            ReviewResult = new WorkerReviewResult
            {
                Disposition = WorkerReviewDisposition.Pending
            },
            ReviewCommentRefs = [],
            ClarificationRequests = [],
            ResultSummary = $"Workflow run artifact initialized for {definition.ExecutionUnit}.",
            RunLogRefs = [request.EventSink.SinkRef]
        };
    }
}
