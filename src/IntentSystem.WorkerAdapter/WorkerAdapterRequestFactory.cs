using IntentSystem.WorkerAdapter.Models;

namespace IntentSystem.WorkerAdapter;

public static class WorkerAdapterRequestFactory
{
    public static WorkerAdapterRequest Create(string executionUnit, string workflowDefinitionRef, string targetWorktree)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWorktree);

        return new WorkerAdapterRequest
        {
            WorkflowDefinitionRef = workflowDefinitionRef,
            RunId = $"run-{executionUnit.Trim()}",
            TargetWorktree = targetWorktree,
            RuntimeEnv = new AdapterRuntimeEnvironment
            {
                Engine = "takt",
                Arguments = ["run", workflowDefinitionRef]
            },
            EventSink = new AdapterEventSink
            {
                SinkType = "jsonl-file",
                SinkRef = WorkerAdapterRunArtifactPathResolver.Resolve(executionUnit)
            }
        };
    }
}
